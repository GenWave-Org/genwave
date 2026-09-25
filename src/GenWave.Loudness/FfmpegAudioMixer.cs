using System.Globalization;
using System.Text;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenWave.Loudness;

/// <summary>
/// Renders the final safe-segment artifact in exactly one ffmpeg invocation (SPEC F27.2 / F27.4 /
/// F27.5): voice-only is re-muxed with embedded RIFF INFO tags; with a bed, the bed is cue-trimmed,
/// crossfade-looped or trimmed to cover the voice plus lead-in/tail-out pad, attenuated relative to
/// the voice, and mixed in after the voice's lead-in delay. One invocation in every path keeps audio
/// rendering in exactly one place.
///
/// Tag embedding is two-step (SPEC F27.2): ffmpeg's <c>-metadata</c> writes the RIFF INFO artist/title
/// chunks that generic tools (ffprobe et al.) read, but ffmpeg's wav muxer has no way to write a chunk
/// TagLibSharp reads as <c>Tag.Performers</c> — the media library's tags re-enrich path reads exactly
/// that. So once ffmpeg produces the file, a short TagLibSharp pass stamps the artist onto
/// <c>Tag.Performers</c> (a real ID3v2 TPE1 frame) so a <c>fields=tags</c> re-enrich round-trips the
/// brand instead of leaving artist NULL.
///
/// Config-free by design: every value arrives via <see cref="AudioMixRequest"/>; the mixer never
/// reads <c>Station:Safe:*</c> or the database itself (callers resolve those).
///
/// On failure (missing/unreadable input, ffmpeg non-zero exit) this throws
/// <see cref="InvalidOperationException"/> and deletes any partially-written output file.
/// </summary>
public sealed class FfmpegAudioMixer(ILoudnessAnalyzer loudnessAnalyzer, ILogger<FfmpegAudioMixer>? logger = null) : IAudioMixer
{
    readonly ILogger<FfmpegAudioMixer> logger = logger ?? NullLogger<FfmpegAudioMixer>.Instance;

    // The bed branch is resampled to this rate before crossfade-looping so the timing is deterministic
    // regardless of the bed file's native sample rate.
    const int BedProcessingSampleRate = 44100;

    // gh-#855 — the crossfade at each loop-repeat join; shrunk for short clips (BuildBedFilterGraph)
    // so it never exceeds a single copy's own length.
    const double BedLoopCrossfadeSeconds = 0.5;

    // SPEC F196.3 — the bed's gain (relative to the voice/target, before mixing) is clamped to this
    // range; a clamp always logs WARN with the computed (pre-clamp) value.
    internal const double MinBedGainDb = -40.0;
    internal const double MaxBedGainDb = 12.0;

    public async Task MixAsync(AudioMixRequest request, CancellationToken ct)
    {
        try
        {
            if (request.Bed is null)
                await RunVoiceOnlyAsync(request, ct);
            else
                await RunWithBedAsync(request, request.Bed, ct);

            EmbedPerformerTag(request.OutputPath, request.Tags.Artist);
        }
        catch (Exception)
        {
            DeletePartialOutput(request.OutputPath);
            throw;
        }
    }

    /// <summary>Re-muxes the voice clip alone into <see cref="AudioMixRequest.OutputPath"/> with tags embedded.</summary>
    static async Task RunVoiceOnlyAsync(AudioMixRequest request, CancellationToken ct)
    {
        var args = new List<string>
        {
            "-nostdin", "-y", "-hide_banner", "-loglevel", "error",
            "-i", request.VoicePath,
            "-map", "0:a",
            "-c:a", "pcm_s16le",
        };
        AddTagArgs(args, request.Tags);
        args.Add("--");   // end-of-options: OutputPath may start with '-' once operator input drives it
        args.Add(request.OutputPath);

        await FfmpegProcess.RunFfmpegAsync(args, ct);
    }

    /// <summary>
    /// Mixes the cue-trimmed, crossfade-looped/trimmed, ducked bed under the voice (delayed by the
    /// lead-in pad) in a single filter_complex pass.
    /// </summary>
    async Task RunWithBedAsync(AudioMixRequest request, BedSpec bed, CancellationToken ct)
    {
        // gh-#746 — BedDuckDb is "dB UNDER THE VOICE", so the bed's gain is relative to what the voice
        // and the bed actually measure, never a flat volume= on the raw bed file. The assembled voice
        // is always measured here (it is freshly mixed, never a catalog row); the bed's loudness is the
        // catalog's own when the caller carried it on the BedSpec, measured here otherwise.
        var voiceLoudness = await loudnessAnalyzer.AnalyzeAsync(request.VoicePath, ct);
        var bedLufs = bed.IntegratedLufs ?? await MeasureBedLufsAsync(bed.Path, ct);
        var bedGainDb = ResolveAndLogBedGainDb(bed, request.BedDuckDb, request.TargetLufs, voiceLoudness, bedLufs);

        var voiceDurationSec = await FfmpegProcess.ProbeDurationSecondsAsync(request.VoicePath, ct);
        var totalDurationSec = voiceDurationSec + (2 * request.BedPadSeconds);

        var cueInSec = bed.CueInSec ?? 0.0;
        var cueOutSec = bed.CueOutSec ?? await FfmpegProcess.ProbeDurationSecondsAsync(bed.Path, ct);
        var bedSegmentDurationSec = cueOutSec - cueInSec;
        if (bedSegmentDurationSec <= 0.0)
            throw new InvalidOperationException(
                $"Bed cue points for '{bed.Path}' produce a non-positive segment " +
                $"({cueInSec}s to {cueOutSec}s).");

        var delayMs = (long)Math.Round(request.BedPadSeconds * 1000.0, MidpointRounding.AwayFromZero);

        var filter = BuildBedFilterGraph(request, bedGainDb, cueInSec, cueOutSec, totalDurationSec, delayMs);

        var args = new List<string>
        {
            "-nostdin", "-y", "-hide_banner", "-loglevel", "error",
            "-i", request.VoicePath,
            "-i", bed.Path,
            "-filter_complex", filter,
            "-map", "[out]",
            "-c:a", "pcm_s16le",
        };
        AddTagArgs(args, request.Tags);
        args.Add("--");   // end-of-options: OutputPath may start with '-' once operator input drives it
        args.Add(request.OutputPath);

        await FfmpegProcess.RunFfmpegAsync(args, ct);
    }

    /// <summary>Measures the bed file's own integrated loudness for the duck calculation.</summary>
    async Task<double?> MeasureBedLufsAsync(string bedPath, CancellationToken ct)
    {
        var measured = await loudnessAnalyzer.AnalyzeAsync(bedPath, ct);
        return measured.Measurable ? measured.IntegratedLufs : null;
    }

    /// <summary>
    /// The UNCLAMPED gain applied to the bed branch: <c>reference + duck − bed</c>, where
    /// <c>reference</c> is the voice's own integrated loudness (gh-#746 — the bed lands exactly
    /// <paramref name="duckDb"/> dB under the voice actually rendered, never a flat <c>volume=</c> on
    /// the raw bed file) when the voice is measurable, or <paramref name="targetLufs"/> — the
    /// station's own loudness target — otherwise (SPEC F196.1; a Kokoro render that came out
    /// unmeasurable still gets a sane reference to duck under, rather than losing the "relative to
    /// something" property gh-#746 fixed). When <paramref name="bedLufs"/> is unmeasurable (<c>null</c>
    /// or non-finite — a bed the analyzer cannot gate) there is nothing to be relative to on the bed
    /// side either, and the duck falls back to the pre-gh-#746 absolute reading: a flat
    /// <paramref name="duckDb"/> on the raw bed (SPEC F196.2). Clamping (SPEC F196.3) happens one level
    /// up, in <see cref="ClampBedGainDb"/> / <see cref="ResolveAndLogBedGainDb"/> — this method stays
    /// pure and side-effect-free so a fact can pin the exact pre-clamp number.
    /// </summary>
    internal static double ResolveBedGainDb(double duckDb, double targetLufs, Core.Domain.Loudness voice, double? bedLufs)
    {
        var reference = voice.Measurable ? voice.IntegratedLufs : targetLufs;
        return bedLufs is double measuredBed && double.IsFinite(measuredBed)
            ? reference + duckDb - measuredBed
            : duckDb;
    }

    /// <summary>SPEC F196.3 — clamps a computed bed gain into [<see cref="MinBedGainDb"/>, <see cref="MaxBedGainDb"/>].</summary>
    internal static double ClampBedGainDb(double computedDb) => Math.Clamp(computedDb, MinBedGainDb, MaxBedGainDb);

    /// <summary>
    /// The seam <see cref="RunWithBedAsync"/> calls on the deploy path (no PR-tier fact pins that
    /// call site — it is ffmpeg-tier; the container smoke covers it): resolves the bed gain via <see cref="ResolveBedGainDb"/>, clamps it via
    /// <see cref="ClampBedGainDb"/>, and logs the two WARNs SPEC F196.2/F196.3 require — once each,
    /// per render, never inside a loop. No ffmpeg/file I/O here, so specs can call this directly with a
    /// fake <see cref="ILoudnessAnalyzer"/>'s output and a <see cref="Loudness"/> literal.
    /// </summary>
    internal double ResolveAndLogBedGainDb(BedSpec bed, double duckDb, double targetLufs, Core.Domain.Loudness voice, double? bedLufs)
    {
        // The WARN fires on a null measurement only; ResolveBedGainDb's fallback also covers a
        // non-finite one (NaN/±∞), which the analyzer never emits today, so that path stays silent.
        if (bedLufs is null)
            logger.LogWarning(
                "Bed {BedPath} has no measured loudness; ducking by {DuckDb} dB alone (F196.2)",
                bed.Path, duckDb);

        var computedDb = ResolveBedGainDb(duckDb, targetLufs, voice, bedLufs);
        var appliedDb = ClampBedGainDb(computedDb);
        if (appliedDb != computedDb)
            logger.LogWarning(
                "Bed gain {ComputedDb} dB for {BedPath} clamped to {AppliedDb} dB (F196.3)",
                computedDb, bed.Path, appliedDb);

        return appliedDb;
    }

    /// <summary>
    /// SPEC F168.3/.4; gh-#855 — pure filter_complex graph: cue-trim, crossfade-loop to cover
    /// <paramref name="totalDurationSec"/> (no hard splice), duck, fade, delay-and-mix. Static/pure
    /// (like <see cref="BuildFadeSuffix"/>) so <c>InternalsVisibleTo</c> specs pin the emitted string
    /// without a real ffmpeg binary.
    /// </summary>
    internal static string BuildBedFilterGraph(
        AudioMixRequest request, double bedGainDb, double cueInSec, double cueOutSec, double totalDurationSec,
        long delayMs)
    {
        var segmentSec = cueOutSec - cueInSec;
        // gh-#855 — crossfade shrinks for a clip too short to hold a full BedLoopCrossfadeSeconds
        // join, so no fade window ever exceeds a single copy's own length.
        var crossfadeSec = Math.Min(BedLoopCrossfadeSeconds, segmentSec / 4.0);
        var copies = segmentSec >= totalDurationSec
            ? 1
            : (int)Math.Ceiling((totalDurationSec - crossfadeSec) / (segmentSec - crossfadeSec));

        var cueTrim =
            $"[1:a]atrim=start={Fmt(cueInSec)}:end={Fmt(cueOutSec)},asetpts=PTS-STARTPTS," +
            $"aformat=sample_rates={BedProcessingSampleRate}:channel_layouts=stereo[cue];";
        var loopChain = copies == 1 ? "" : BuildCrossfadeLoopChain(copies, segmentSec, crossfadeSec);
        var loopedLabel = copies == 1 ? "cue" : "looped";

        return
            cueTrim + loopChain +
            $"[{loopedLabel}]atrim=start=0:end={Fmt(totalDurationSec)},asetpts=PTS-STARTPTS," +
            $"volume={Fmt(bedGainDb)}dB{BuildFadeSuffix(totalDurationSec, request.BedFadeSeconds)}[bed];" +
            $"[0:a]aformat=sample_rates={BedProcessingSampleRate}:channel_layouts=stereo," +
            $"adelay=delays={delayMs}:all=1[voice];" +
            "[bed][voice]amix=inputs=2:duration=first:dropout_transition=0:normalize=0[out]";
    }

    /// <summary>
    /// gh-#855 — overlaps <paramref name="copies"/> taps of <c>[cue]</c> by <paramref name="crossfadeSec"/>
    /// via equal-power <c>afade</c> in/out + <c>adelay</c> + <c>amix</c> (normalize=0, so the overlap
    /// stays full-scale, not halved) — not <c>acrossfade</c>: it corrupts length over <c>asplit</c> taps
    /// on ffmpeg 7.1. Only called when <paramref name="copies"/> &gt; 1.
    /// </summary>
    static string BuildCrossfadeLoopChain(int copies, double copySec, double crossfadeSec)
    {
        var stepSec = copySec - crossfadeSec;
        var taps = string.Concat(Enumerable.Range(0, copies).Select(i => $"[s{i}]"));
        var chain = new StringBuilder($"[cue]asplit={copies}{taps};");

        var mixLabels = new List<string>(copies);
        for (var i = 0; i < copies; i++)
        {
            var fades = new List<string>();
            if (i > 0)
                fades.Add($"afade=t=in:st=0:d={Fmt(crossfadeSec)}:curve=qsin");
            if (i < copies - 1)
                fades.Add($"afade=t=out:st={Fmt(stepSec)}:d={Fmt(crossfadeSec)}:curve=qsin");

            var delayMs = (long)Math.Round(i * stepSec * 1000.0, MidpointRounding.AwayFromZero);
            if (delayMs > 0)
                fades.Add($"adelay=delays={delayMs}:all=1");

            var label = $"c{i}";
            mixLabels.Add(label);
            chain.Append($"[s{i}]{string.Join(',', fades)}[{label}];");
        }

        chain.Append(string.Concat(mixLabels.Select(l => $"[{l}]")));
        chain.Append($"amix=inputs={copies}:duration=longest:dropout_transition=0:normalize=0[looped];");
        return chain.ToString();
    }

    /// <summary>
    /// SPEC F168.4; STORY-403; PLAN T416 — the bed's own trailing <c>afade=t=out</c> filter suffix,
    /// appended to the bed chain right after its duck <c>volume</c> stage (so the fade rides on top of
    /// the already-ducked level, never fights it). Empty when <paramref name="bedFadeSeconds"/> is
    /// zero or negative (the "no fade" default — <see cref="AudioMixRequest.BedFadeSeconds"/>'s own
    /// remarks) so a caller that never varies it changes the rendered filter string not at all.
    ///
    /// <para>
    /// <b>Clamped to the bed's own total duration (never a negative fade start).</b> A configured fade
    /// longer than <paramref name="totalDurationSec"/> itself — a short spot, a large
    /// <c>Station:Ads:BedFadeMs</c> — would otherwise push <c>st=</c> negative, which ffmpeg's
    /// <c>afade</c> filter rejects outright; clamping the fade DURATION to the bed's own length instead
    /// makes the fade start at 0 and cover the whole bed, the honest "fade what you've got" behavior
    /// rather than a render failure over a configuration knob.
    /// </para>
    ///
    /// <para>
    /// Extracted as a pure, internal, static function (no ffmpeg process, no I/O) specifically so it is
    /// unit-testable without a real ffmpeg binary — <c>GenWave.Tts.Tests</c> exercises it directly via
    /// this project's own <c>InternalsVisibleTo</c> grant (csproj remarks), the SAME reason
    /// <see cref="Fmt"/>'s own G17 formatting is reused rather than re-implemented here.
    /// </para>
    /// </summary>
    internal static string BuildFadeSuffix(double totalDurationSec, double bedFadeSeconds)
    {
        if (bedFadeSeconds <= 0.0)
            return "";

        var fadeDuration = Math.Min(bedFadeSeconds, totalDurationSec);
        var fadeStart = totalDurationSec - fadeDuration;
        return $",afade=t=out:st={Fmt(fadeStart)}:d={Fmt(fadeDuration)}";
    }

    static void AddTagArgs(List<string> args, AudioTags tags)
    {
        args.Add("-metadata");
        args.Add($"artist={tags.Artist}");
        args.Add("-metadata");
        args.Add($"title={tags.Title}");
    }

    /// <summary>
    /// Stamps <paramref name="artist"/> onto the rendered wav's <c>Tag.Performers</c> (SPEC F27.2).
    /// ffmpeg's <c>-metadata artist=</c> only reaches the RIFF INFO "IART" chunk, which TagLibSharp
    /// surfaces as <c>Tag.AlbumArtists</c>, not <c>Tag.Performers</c> — the property the media
    /// library's enricher reads. Setting <c>Tag.Performers</c> here writes a real ID3v2 TPE1 frame
    /// (plus the RIFF "ISTR" chunk) so a tags-only re-enrich rehydrates the artist instead of leaving
    /// it NULL. MovieId/DivX are container tag types TagLibSharp always instantiates for a wav on open
    /// (irrelevant to an audio-only file) — removed before saving so they never reach disk.
    /// </summary>
    static void EmbedPerformerTag(string outputPath, string artist)
    {
        using var file = TagLib.File.Create(outputPath);
        file.Tag.Performers = new[] { artist };
        file.RemoveTags(TagLib.TagTypes.MovieId | TagLib.TagTypes.DivX);
        file.Save();
    }

    /// <summary>Best-effort cleanup of a partially-written output file after a failed mix.</summary>
    static void DeletePartialOutput(string outputPath)
    {
        try
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
        catch (IOException)
        {
            // The original failure is what the caller needs to see; a locked/undeletable partial
            // file is a secondary concern best-effort cleaned up here and not worth masking it.
        }
    }

    static string Fmt(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
}

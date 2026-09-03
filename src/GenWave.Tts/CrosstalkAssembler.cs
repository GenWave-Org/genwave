namespace GenWave.Tts;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Logging;
using GenWave.Loudness;

/// <summary>
/// Renders every line of a validated <see cref="GenWave.Core.Domain.CrosstalkAiredScript"/> through the one TTS funnel — each
/// with ITS speaker's own <see cref="TtsRenderContext"/> (SPEC F127.5) — then mixes the per-line
/// renders into a single audio asset (SPEC F127.6) via one argv-only ffmpeg invocation, measures it
/// exactly like any other segment, and caches the result. Lives beside <see cref="CrosstalkScriptWriter"/>
/// (ARCHITECTURE.md "Crosstalk (F127…)") — that class turns a completion into a validated script;
/// this class turns a validated script into one playable asset. Casting (T285's <c>CrosstalkPlanner</c>)
/// and vending (T286/T287) are LATER tasks this class never touches: <see cref="AssembleAsync"/> has
/// no caller yet.
///
/// <para>
/// <b>Both voices or nobody (SPEC F127.5).</b> Any single line whose render fails (the F99
/// right-voice bar — a null/throw per <see cref="ITtsSynthesizer"/>'s own fault contract) discards
/// the WHOLE exchange: every line file rendered so far is deleted, one Information line names the
/// speaker/line/cause, and the ffmpeg mix never runs. There is no single-voice salvage, mirroring
/// <see cref="CrosstalkScriptWriter"/>'s own F127.4 posture one stage upstream.
/// </para>
///
/// <para>
/// <b>Per-speaker context, the resolver seam (SPEC F127.5).</b> Each line's <see cref="TtsRenderContext"/>
/// carries THAT line's speaker's own resolved pronunciation rules (<see cref="PronunciationRuleResolver.ResolveForRender"/>,
/// the SAME seam <c>TtsSegmentSource</c>/<c>SafeSegmentAuthor</c> already call — never a hand-rolled
/// merge) and speaking pace (<see cref="TtsPace.Clamp"/> over that speaker's own
/// <c>PersonaCard.Voice.Pace</c>) — never the ambient active-persona caches
/// (<see cref="ActivePersonaPronunciationRulesCache"/>/<see cref="ActivePersonaPaceCache"/>) TtsSegmentSource
/// reads: this render's two speakers are two EXPLICITLY cast cards on <see cref="CrosstalkAssemblyRequest"/>,
/// not whichever persona happens to be on air, mirroring <see cref="CrosstalkScriptWriter"/>'s own
/// "never resolves who is on either side of the booth, always receives already-cast cards" posture.
/// Calling <see cref="PronunciationRuleResolver.ResolveForRender"/> directly here is the seam itself,
/// not a bypass of it — law L8 (ARCHITECTURE.md "Architecture governance") forbids reaching for the
/// underlying merge primitives from OUTSIDE <c>GenWave.Tts</c>; this class lives inside it, same as
/// <c>TtsSegmentSource</c>/<c>SafeSegmentAuthor</c>, so L8 has nothing to enforce at this call site —
/// it is the boundary it protects, not a gate on the boundary's own interior.
/// </para>
///
/// <para>
/// <b>Assembly (SPEC F127.6).</b> One ffmpeg invocation positions every line's render on a shared
/// timeline via <c>adelay</c> (start times from <see cref="CrosstalkTimeline"/>: an ordinary line
/// starts after the previous line's own jittered gap, an interjection starts before the previous
/// line's tail) then sums them with <c>amix</c> — the same delay-then-mix shape
/// <c>GenWave.Loudness.FfmpegAudioMixer</c> already uses for voice-under-bed, generalized from two
/// inputs to N. <c>amix</c> over the concat demuxer is deliberate: an interjection must genuinely
/// overlap two voices in the mix, and concat has no way to overlap two inputs at all. A trailing
/// <c>alimiter</c> stage on the mix (SPEC F127.6/T284 review) supplies the headroom two full-scale
/// voices genuinely overlapping (an interjection's whole point) can otherwise need before the result
/// is quantized to <c>pcm_s16le</c> — see <see cref="MixAsync"/>'s own remarks.
/// </para>
///
/// <para>
/// <b>The ceiling (SPEC F127.6).</b> The duration estimate a script's own generation only ever
/// models spoken characters — the inter-line gaps (and any interjection overlap) are structurally
/// unmodelled there. This class measures the ASSEMBLED asset's real container duration (ffprobe,
/// argv-only, the same process discipline as every ffmpeg/ffprobe call in this codebase) and
/// discards — deleting the mixed asset — when it runs past 1.5x
/// <see cref="CrosstalkOptions.DurationTargetSeconds"/>, logging both the estimate and the measured
/// actual. That ffprobe read is STRICTLY the ceiling gate's own input — the result's own
/// <see cref="CrosstalkAssemblyResult.Assembled.DurationMs"/> is a separate, cue-derived measurement
/// (see <see cref="MeasureAssembledAsync"/>'s own remarks); the two are never the same number.
/// </para>
///
/// <para>
/// Registered as a DI singleton with NO eager I/O in its constructor (Story125's zero-I/O invariant)
/// — every dependency here is itself a cheap seam (a synthesizer, providers, options monitors, a
/// logger); constructing this class never touches the network or the filesystem.
/// </para>
/// </summary>
public sealed class CrosstalkAssembler(
    ITtsSynthesizer synthesizer,
    PronunciationRuleProvider pronunciations,
    ILoudnessAnalyzer loudnessAnalyzer,
    ICueAnalyzer cueAnalyzer,
    IAudioMixer mixer,
    IOptionsMonitor<TtsOptions> ttsOptions,
    IOptionsMonitor<CrosstalkOptions> crosstalkOptions,
    ILogger<CrosstalkAssembler> logger)
{
    /// <summary>How far past <see cref="CrosstalkOptions.DurationTargetSeconds"/> the ASSEMBLED
    /// asset's real, measured duration may run before this class discards it (SPEC F127.6) — the
    /// estimate a script's own generation applies is chars-only and structurally cannot see the
    /// inter-line gaps this class itself introduces, so the ceiling carries headroom the estimate
    /// never gets.</summary>
    internal const double CeilingMultiplier = 1.5;

    /// <summary>Subdirectory under <see cref="TtsOptions.CacheRoot"/> the assembled asset is cached
    /// into — mirrors <c>TtsSegmentSource</c>'s own "blurbs" subdirectory convention, kept separate
    /// from both the evergreen station cache and the ordinary blurb cache since a crosstalk asset's
    /// own lifecycle (single-use, retired at air — SPEC F127.7, a LATER task's concern) is distinct
    /// from either.</summary>
    const string CrosstalkDirName = "crosstalk";

    /// <summary>The crosstalk cache directory under <paramref name="options"/>'s own
    /// <see cref="TtsOptions.CacheRoot"/> (the T284/T285-recorded rider for T286 — see <c>CrosstalkStockWorker</c>'s
    /// startup purge) — the SAME path <see cref="AssembleAsync"/> itself writes into, composed the one
    /// place that knows <see cref="CrosstalkDirName"/> so a purge (or any other future caller) can
    /// never target the wrong directory even if that constant's own value ever changes.</summary>
    public static string ResolveCacheDir(TtsOptions options) => Path.Combine(options.CacheRoot, CrosstalkDirName);

    /// <summary>Sample rate every per-line input is normalized to before <c>adelay</c>/<c>amix</c> —
    /// mirrors <c>FfmpegAudioMixer.BedProcessingSampleRate</c>'s own reasoning: a shared rate makes
    /// the mix deterministic regardless of what rate any one engine happens to render at.</summary>
    const int MixSampleRate = 44100;

    public async Task<CrosstalkAssemblyResult> AssembleAsync(CrosstalkAssemblyRequest request, CancellationToken ct)
    {
        var script = request.Script;

        // Fail fast (T284 review F7): CrosstalkAssemblyRequest is a public, unvalidated record — a
        // caller bypassing CrosstalkScriptWriter's own 3-8 line invariant could hand this a
        // one-line (or zero-line) script, which MixAsync's own delay math (every start derives from
        // the line BEFORE it) cannot make sense of at all.
        if (script.Lines.Count < 2)
        {
            throw new ArgumentException(
                $"A crosstalk exchange needs at least 2 lines to render and mix; got {script.Lines.Count}.",
                nameof(request));
        }

        var cfg = ttsOptions.CurrentValue;
        var outputDir = ResolveCacheDir(cfg);
        Directory.CreateDirectory(outputDir);
        var outputPath = Path.Combine(outputDir, $"{Guid.NewGuid():N}.{cfg.Format}");

        var lineFiles = new List<string>(script.Lines.Count);
        try
        {
            var renderLines = script.Lines
                .Select(line => (line.Speaker.ToString(), BuildCardContext(line, request.HostCard, request.NeighborCard)))
                .ToList();
            var renderFailure = await RenderLinesAsync(renderLines, lineFiles, "Crosstalk exchange", ct);
            if (renderFailure is not null)
                return renderFailure;

            var isInterjections = script.Lines.Select(line => line.IsInterjection).ToArray();
            await MixAsync(lineFiles, isInterjections, CrosstalkTimeline.ComputeSeed(script), outputPath, ct);

            var estimatedSeconds = script.Lines.Sum(line => line.Text.Length) / CrosstalkScriptParser.CharsPerSecond;
            var actualSeconds = await FfmpegProcess.ProbeDurationSecondsAsync(outputPath, ct);
            var targetSeconds = crosstalkOptions.CurrentValue.DurationTargetSeconds;

            if (CeilingViolationReason(actualSeconds, estimatedSeconds, targetSeconds) is { } ceilingReason)
            {
                DeleteIfExists(outputPath);
                return Discard(ceilingReason, lineFiles, "Crosstalk exchange");
            }

            var (loudness, cue, durationMs) = await MeasureAssembledAsync(outputPath, ct);
            DeleteAll(lineFiles);

            return new CrosstalkAssemblyResult.Assembled(outputPath, loudness, cue, durationMs);
        }
        catch (Exception)
        {
            // T284 review F3: mirrors FfmpegAudioMixer.MixAsync's own idiom one project over
            // (GenWave.Loudness) — ANY exception past this point leaves neither the per-line renders
            // nor a partially-written mixed asset behind. Deliberately not split into a separate
            // OperationCanceledException arm: cancellation is itself an Exception, so the identical
            // cleanup applies whether ct fired mid-mix/mid-measure or a dependency genuinely threw —
            // the sibling's own posture, not a narrower one. (A per-line synth failure is handled
            // inside RenderLinesAsync as a business discard, not an exception, and never reaches here.)
            DeleteAll(lineFiles);
            DeleteIfExists(outputPath);
            throw;
        }
    }

    /// <summary>
    /// The widened sibling of <see cref="AssembleAsync"/> (SPEC F161.2, F161.3; STORY-391; PLAN T401):
    /// a 1-3 voice cast (bare <see cref="VoiceSpec"/>s, never persona cards) instead of a fixed
    /// two-persona-card crosstalk exchange, a caller-supplied duration ceiling instead of
    /// <see cref="CrosstalkOptions.DurationTargetSeconds"/>, and an optional bed mixed in via the
    /// existing <see cref="GenWave.Core.Abstractions.IAudioMixer"/> seam. Reuses every timeline/mix/
    /// measure internal <see cref="AssembleAsync"/> itself uses (<see cref="RenderLinesAsync"/>,
    /// <see cref="MixAsync"/>, <see cref="MeasureAssembledAsync"/>, <see cref="Discard"/>) — widened,
    /// never forked; <see cref="AssembleAsync"/>'s own two-voice path is untouched by this method's
    /// existence.
    ///
    /// <para>
    /// <b>Pipeline:</b> render every line (F99's "the whole cast or nobody" — any one line's render
    /// failure discards everything rendered so far); mix the cast onto one shared timeline exactly
    /// like a crosstalk exchange (<c>adelay</c>/<c>amix</c>/<c>alimiter</c>, ad copy never carries an
    /// interjection so every transition is an "ordinary line" gap); pass that raw mix through
    /// <see cref="mixer"/> ONCE MORE — even with no <see cref="CastAssemblyRequest.Bed"/> — so brand
    /// tags land in the file exactly like <c>SafeSegmentAuthor</c>'s own "voice-only still goes
    /// through the mixer once" posture (tag-embedding stays in exactly one place, F161.3); measure the
    /// ceiling against the FINAL (bed-included) artifact's real, ffprobe'd duration — never the
    /// pre-bed raw mix, since <see cref="CastAssemblyRequest.BedPadSeconds"/> padding can itself push
    /// the final artifact past the ceiling; measure loudness/cue on that same final artifact.
    /// </para>
    /// </summary>
    public async Task<CrosstalkAssemblyResult> AssembleCastAsync(CastAssemblyRequest request, CancellationToken ct)
    {
        ValidateCastRequest(request);

        var cfg = ttsOptions.CurrentValue;
        Directory.CreateDirectory(request.OutputDirectory);
        var rawMixPath = Path.Combine(request.OutputDirectory, $"{Guid.NewGuid():N}.raw.{cfg.Format}");
        var finalPath = Path.Combine(request.OutputDirectory, $"{Guid.NewGuid():N}.{cfg.Format}");

        var castByTag = request.Cast.ToDictionary(member => member.Tag, member => member.Voice, StringComparer.Ordinal);
        var renderLines = request.Lines
            .Select(line => (line.Tag, BuildCastContext(line, castByTag[line.Tag])))
            .ToList();

        var lineFiles = new List<string>(request.Lines.Count);
        try
        {
            var renderFailure = await RenderLinesAsync(renderLines, lineFiles, "Cast segment", ct);
            if (renderFailure is not null)
                return renderFailure;

            // Ad copy never overlaps two voices (see CastLine's own remarks) — every transition is
            // an "ordinary line" gap, the same shape MixAsync already computes for crosstalk's own
            // non-interjecting lines.
            var isInterjections = new bool[lineFiles.Count];
            await MixAsync(lineFiles, isInterjections, CrosstalkTimeline.ComputeSeed(request.Lines), rawMixPath, ct);

            await mixer.MixAsync(
                new AudioMixRequest(rawMixPath, request.Bed, request.Tags, request.BedDuckDb, request.BedPadSeconds, finalPath),
                ct);
            DeleteIfExists(rawMixPath);

            var actualSeconds = await FfmpegProcess.ProbeDurationSecondsAsync(finalPath, ct);
            if (CastCeilingViolationReason(actualSeconds, request.CeilingSeconds) is { } ceilingReason)
            {
                DeleteIfExists(finalPath);
                return Discard(ceilingReason, lineFiles, "Cast segment");
            }

            var (loudness, cue, durationMs) = await MeasureAssembledAsync(finalPath, ct);
            DeleteAll(lineFiles);

            return new CrosstalkAssemblyResult.Assembled(finalPath, loudness, cue, durationMs);
        }
        catch (Exception)
        {
            // Mirrors AssembleAsync's own identical posture (see its remarks) — ANY exception past
            // this point leaves no per-line render, no raw pre-bed mix, and no final artifact behind.
            DeleteAll(lineFiles);
            DeleteIfExists(rawMixPath);
            DeleteIfExists(finalPath);
            throw;
        }
    }

    /// <summary>
    /// Fail-fast validation for <see cref="AssembleCastAsync"/> (the <see cref="AssembleAsync"/>
    /// T284 review F7 precedent: <see cref="CastAssemblyRequest"/> is a public, unvalidated record) —
    /// before any render, mix, or ffmpeg call. SPEC F160.3's own format rule already guarantees 1-3
    /// distinct tags with an accepted script; this is the defensive backstop for a caller that hands
    /// this method a request bypassing that rule.
    /// </summary>
    static void ValidateCastRequest(CastAssemblyRequest request)
    {
        if (request.Lines.Count < 1)
        {
            throw new ArgumentException(
                "A voice-cast assembly needs at least 1 line to render.", nameof(request));
        }

        if (request.Cast.Count is < 1 or > 3)
        {
            throw new ArgumentException(
                $"A voice cast must carry 1-3 members; got {request.Cast.Count}.", nameof(request));
        }

        var castTags = request.Cast.Select(member => member.Tag).ToList();
        if (castTags.Distinct(StringComparer.Ordinal).Count() != castTags.Count)
            throw new ArgumentException("Cast tags must be distinct.", nameof(request));

        var castTagSet = new HashSet<string>(castTags, StringComparer.Ordinal);
        var missing = request.Lines.FirstOrDefault(line => !castTagSet.Contains(line.Tag));
        if (missing is not null)
        {
            throw new ArgumentException(
                $"Line tag \"{missing.Tag}\" has no matching cast member.", nameof(request));
        }
    }

    /// <summary>
    /// <see langword="null"/> when <paramref name="actualSeconds"/> is within
    /// <paramref name="ceilingSeconds"/>; otherwise the one discard reason naming the ceiling
    /// (SPEC F161.2's own "over-ceiling → discard + failed, fail_reason names the ceiling" — the
    /// <see cref="CeilingViolationReason"/> sibling above's own text, without a global
    /// <see cref="CrosstalkOptions"/> target to also report — <paramref name="ceilingSeconds"/> here
    /// is already the caller's whole, final number).
    /// </summary>
    static string? CastCeilingViolationReason(double actualSeconds, double ceilingSeconds)
    {
        if (actualSeconds <= ceilingSeconds)
            return null;

        return
            $"assembled duration {actualSeconds:F1}s exceeds the {ceilingSeconds:F1}s ceiling " +
            "— the estimate lied";
    }

    /// <summary>
    /// Renders every entry of <paramref name="lines"/> in order, appending each successful render's
    /// path to <paramref name="lineFiles"/> (T284 review F10 — split out of <see cref="AssembleAsync"/>
    /// so that method's own three concerns — render, ceiling, measure — read as three paragraphs, not
    /// one). Shared by BOTH the two-voice crosstalk path (SPEC F127.5's "both voices or nobody") and
    /// the widened voice-cast path (SPEC F161.2, STORY-391, PLAN T401 — "the whole cast or nobody",
    /// the identical posture one level up) — reused, not forked: each caller resolves its own
    /// per-line <see cref="TtsRenderContext"/> up front (<see cref="BuildCardContext"/> for a cast
    /// card, <see cref="BuildCastContext"/> for a bare <see cref="VoiceSpec"/>) and hands this method
    /// only the already-resolved (label, context) pairs it needs to actually render and discard on
    /// failure. Returns a <see cref="CrosstalkAssemblyResult.Discarded"/> the instant any one line
    /// fails the F99 right-voice bar; returns <see langword="null"/> once every line has rendered.
    /// </summary>
    async Task<CrosstalkAssemblyResult.Discarded?> RenderLinesAsync(
        IReadOnlyList<(string Label, TtsRenderContext Context)> lines, List<string> lineFiles, string subject,
        CancellationToken ct)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var (label, context) = lines[i];

            string linePath;
            try
            {
                linePath = await synthesizer.SynthesizeAsync(context, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // SPEC F127.5/F161.2: F99's right-voice bar failing on ANY one line discards the
                // WHOLE exchange — no single-voice salvage. Every line rendered so far is cleaned up
                // before returning. The render exception itself rides along into the discard log
                // (T284 review F8) so a 503, a timeout, and an outright bad-voice rejection each
                // leave a distinguishable trace, not just this one collapsed type name.
                return Discard(
                    $"{label} line {i + 1} of {lines.Count} failed to render ({ex.GetType().Name})",
                    lineFiles, subject, ex);
            }

            lineFiles.Add(linePath);
        }

        return null;
    }

    /// <summary>
    /// Resolves a crosstalk line's <see cref="TtsRenderContext"/> from its cast card (SPEC F127.5) —
    /// exactly the logic <see cref="RenderLinesAsync"/> used to inline before it was widened to share
    /// with the cast path (see that method's own remarks); byte-identical behavior, just relocated so
    /// it can be built once, up front, for every line before rendering starts.
    /// </summary>
    TtsRenderContext BuildCardContext(CrosstalkAiredLine line, PersonaCard hostCard, PersonaCard neighborCard)
    {
        var card = line.Speaker == CrosstalkSpeaker.Host ? hostCard : neighborCard;

        // SPEC F127.5: this line's OWN speaker's resolved rules/pace — the resolver seam (see the
        // class remarks for law L8's actual scope), never the ambient active-persona caches (see
        // class remarks).
        var contextRules = PronunciationRuleResolver.ResolveForRender(pronunciations.Current, ToCardRules(card));
        return new TtsRenderContext(line.Text, card.Voice.VoiceId, SegmentKind.Crosstalk)
        {
            Rules = contextRules,
            Pace = TtsPace.Clamp(card.Voice.Pace),
        };
    }

    /// <summary>
    /// Resolves a cast line's <see cref="TtsRenderContext"/> from its bare <see cref="VoiceSpec"/>
    /// (SPEC F161.2's own rider, PLAN T401 design note). A cast render has no persona card at all —
    /// ad voices are actors, not the station's DJs — so pronunciation rules resolve from the STATION
    /// layer alone, empty card layer, no candidates: <c>SafeSegmentAuthor</c>'s own precedent one
    /// project-internal seam over (see that class's remarks), never <see cref="BuildCardContext"/>'s
    /// card-cast merge, which has no card here to merge from.
    ///
    /// <para>
    /// <see cref="TtsRenderContext.IsAudition"/> = <see langword="true"/>, mirroring
    /// <c>SafeSegmentAuthor</c>'s own posture, not crosstalk's: this render produces a REPLAYED
    /// artifact (an ad spot airs many times after one render), not an on-air moment in itself — the
    /// same "authoring is not airing" reasoning <see cref="TtsRenderContext.IsAudition"/>'s own
    /// remarks state, extended here from safe segments to cast segments. Crosstalk's own
    /// <see cref="BuildCardContext"/> never sets it because a crosstalk exchange airs exactly once,
    /// immediately — its render genuinely IS the on-air moment.
    /// </para>
    /// </summary>
    TtsRenderContext BuildCastContext(CastLine line, VoiceSpec voice)
    {
        var contextRules = PronunciationRuleResolver.ResolveForRender(
            pronunciations.Current, cardRules: [], candidates: null);
        return new TtsRenderContext(line.Text, voice.VoiceId, SegmentKind.Ad)
        {
            Rules = contextRules,
            Pace = TtsPace.Clamp(voice.Pace),
            IsAudition = true,
        };
    }

    /// <summary>
    /// <see langword="null"/> when <paramref name="actualSeconds"/> is within SPEC F127.6's ceiling
    /// (1.5x <paramref name="targetSeconds"/>); otherwise the one discard reason naming both the
    /// measured actual and the generation-time estimate (T284 review F10 split, see
    /// <see cref="AssembleAsync"/>'s own remarks).
    /// </summary>
    static string? CeilingViolationReason(double actualSeconds, double estimatedSeconds, int targetSeconds)
    {
        var ceilingSeconds = targetSeconds * CeilingMultiplier;
        if (actualSeconds <= ceilingSeconds)
            return null;

        return
            $"assembled duration {actualSeconds:F1}s exceeds {ceilingSeconds:F1}s (1.5x the " +
            $"{targetSeconds}s {nameof(CrosstalkOptions.DurationTargetSeconds)} target; " +
            $"estimated {estimatedSeconds:F1}s) — the estimate lied";
    }

    /// <summary>
    /// Loudness/cue/duration for the finished mix at <paramref name="outputPath"/> (T284 review F10
    /// split, see <see cref="AssembleAsync"/>'s own remarks). <c>DurationMs</c> mirrors the house
    /// shape <c>TtsSegmentSource</c>/<c>SafeSegmentAuthor</c> already use (<c>SafeSegmentAuthor.BuildInsert</c>'s
    /// own remarks, T284 review F4) — derived from the cue analyzer's OWN <see cref="CuePoints.CueOutSec"/>
    /// when cue analysis succeeds, and <see langword="null"/> when it does not (cue analysis never
    /// gates readiness — see <see cref="MeasureCueAsync"/>'s own remarks). Never the ffprobe
    /// container-duration reading <see cref="AssembleAsync"/> takes for its own ceiling check —
    /// that measurement stays scoped to the ceiling gate alone (see the class remarks).
    /// </summary>
    async Task<(Loudness Loudness, CuePoints? Cue, int? DurationMs)> MeasureAssembledAsync(string outputPath, CancellationToken ct)
    {
        var loudness = await loudnessAnalyzer.AnalyzeAsync(outputPath, ct);
        var cue = await MeasureCueAsync(outputPath, ct);
        var durationMs = cue is not null
            ? (int?)Math.Round(cue.CueOutSec * 1000.0, MidpointRounding.AwayFromZero)
            : null;

        return (loudness, cue, durationMs);
    }

    /// <summary>
    /// The one discard path every failure funnels through (mirrors <see cref="CrosstalkScriptWriter.Discard"/>'s
    /// own shape one stage upstream) — deletes every per-line file rendered so far and logs exactly
    /// one Information line (never WARN — a discard here is discipline, not an outage, the same
    /// F127.4 posture this stage's own upstream sibling already carries). <paramref name="cause"/>,
    /// when the discard was itself triggered by a caught exception (a line's own render failure),
    /// rides along as the log entry's exception object (T284 review F8) — SPEC F127.5's "both voices
    /// or nobody" discard would otherwise read identically whether the underlying fault was a 503, a
    /// timeout, or an outright bad-voice rejection; the ceiling-exceeded discard has no such cause
    /// and passes none. <paramref name="subject"/> is REQUIRED (T401 review F10 — no silent default to
    /// drift out of sync): every <see cref="AssembleAsync"/> call site names "Crosstalk exchange"
    /// explicitly, so <see cref="AssembleCastAsync"/>'s own calls (SPEC F161.2, PLAN T401) can name
    /// what actually discarded ("Cast segment") instead of misreporting an ad render as a two-persona
    /// exchange it never was.
    /// </summary>
    CrosstalkAssemblyResult.Discarded Discard(
        string reason, IReadOnlyList<string> lineFiles, string subject, Exception? cause = null)
    {
        DeleteAll(lineFiles);
        if (cause is not null)
            logger.LogInformation(cause, "{Subject} discarded: {Reason}", subject, LogSanitize.Strip(reason));
        else
            logger.LogInformation("{Subject} discarded: {Reason}", subject, LogSanitize.Strip(reason));

        return new CrosstalkAssemblyResult.Discarded(reason);
    }

    /// <summary>Converts a cast speaker's card pronunciation rules into the <c>GenWave.Tts</c>-local
    /// shape <see cref="PronunciationRuleResolver.ResolveForRender"/>'s <c>cardRules</c> parameter
    /// expects — mirrors <see cref="ActivePersonaPronunciationRulesCache"/>'s own private
    /// <c>ToTtsRule</c> conversion one seam over (that cache resolves the AMBIENT active persona's
    /// card; this resolves an EXPLICITLY cast one, so the two never share a call site to consolidate
    /// into).</summary>
    static IReadOnlyList<PronunciationRule> ToCardRules(PersonaCard card) =>
        card.Pronunciations is { Count: > 0 } rules
            ? [.. rules.Select(rule => new PronunciationRule(rule.Pattern, rule.Word, rule.Ipa))]
            : [];

    async Task<CuePoints?> MeasureCueAsync(string path, CancellationToken ct)
    {
        try
        {
            return await cueAnalyzer.AnalyzeAsync(path, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Cue analysis never gates readiness, mirroring TtsSegmentSource/SafeSegmentAuthor's own
            // identical posture — the asset still airs, just without trim points (and, per T284
            // review F4, without a measured DurationMs either — see MeasureAssembledAsync's remarks).
            logger.LogWarning(ex, "Crosstalk cue analysis failed for {Path}", path);
            return null;
        }
    }

    /// <summary>
    /// Positions every line's render on a shared timeline (<c>adelay</c>) and sums them
    /// (<c>amix</c>) in ONE ffmpeg invocation (SPEC F127.6) — see the class remarks for why
    /// <c>amix</c>, not concat. Start times come from <see cref="CrosstalkTimeline"/>, fed by
    /// each line's OWN rendered duration (probed here, not estimated) so gaps/overlaps land against
    /// the real audio, not a guess.
    ///
    /// <para>
    /// A trailing <c>alimiter</c> stage sits between the mix and the output map (T284 review F6):
    /// <c>amix</c>'s own <c>normalize=0</c> is deliberate (SPEC F127.6 never wants every line ducked
    /// the instant a second voice is present — only an interjection's own overlap should genuinely
    /// sum two full-scale voices), but that same overlap can therefore exceed 0 dBFS for the ~0.35s
    /// two voices are truly concurrent, baked in as hard clipping the instant the result quantizes to
    /// <c>pcm_s16le</c> — before loudness is ever measured. <c>alimiter</c> (default settings: a
    /// 0 dBFS true-peak ceiling, a few milliseconds' attack) is a limiter, not a duck: it only
    /// engages during that brief overlap, leaving every ordinary, non-overlapping line's own level
    /// untouched.
    /// </para>
    /// </summary>
    static async Task MixAsync(
        IReadOnlyList<string> lineFiles, IReadOnlyList<bool> isInterjections, int seed, string outputPath, CancellationToken ct)
    {
        var gaps = CrosstalkTimeline.ComputeGapsSeconds(lineFiles.Count - 1, seed);
        var starts = new double[lineFiles.Count];
        for (var i = 1; i < lineFiles.Count; i++)
        {
            var previousDuration = await FfmpegProcess.ProbeDurationSecondsAsync(lineFiles[i - 1], ct);
            var previousEnd = starts[i - 1] + previousDuration;
            starts[i] = CrosstalkTimeline.ComputeLineStartSeconds(previousEnd, isInterjections[i], gaps[i - 1]);
        }

        var filterStages = new List<string>();
        var mixLabels = new List<string>();
        for (var i = 0; i < lineFiles.Count; i++)
        {
            var delayMs = (long)Math.Round(starts[i] * 1000.0, MidpointRounding.AwayFromZero);
            var label = $"l{i}";
            filterStages.Add(
                $"[{i}:a]aformat=sample_rates={MixSampleRate}:channel_layouts=stereo,adelay=delays={delayMs}:all=1[{label}]");
            mixLabels.Add($"[{label}]");
        }

        filterStages.Add(
            $"{string.Join("", mixLabels)}amix=inputs={lineFiles.Count}:duration=longest:dropout_transition=0:normalize=0[mixed]");
        filterStages.Add("[mixed]alimiter[out]");

        var args = new List<string> { "-nostdin", "-y", "-hide_banner", "-loglevel", "error" };
        foreach (var lineFile in lineFiles)
        {
            args.Add("-i");
            args.Add(lineFile);
        }

        args.Add("-filter_complex");
        args.Add(string.Join(";", filterStages));
        args.Add("-map");
        args.Add("[out]");
        args.Add("-c:a");
        args.Add("pcm_s16le");
        args.Add("--");   // end-of-options: OutputPath under an operator-influenced CacheRoot could start with '-'
        args.Add(outputPath);

        await FfmpegProcess.RunFfmpegAsync(args, ct);
    }

    static void DeleteAll(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
            DeleteIfExists(path);
    }

    static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup — mirrors SafeSegmentAuthor/TtsSegmentSource's own identical
            // precedent: a locked/undeletable file is a secondary concern, never worth masking the
            // real outcome this call is already returning.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup — see the IOException arm's own remarks.
        }
    }
}

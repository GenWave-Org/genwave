namespace GenWave.Tts;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Logging;
using LoudnessMeasurement = GenWave.Core.Domain.Loudness;

/// <summary>
/// Composes the safe-segment authoring pipeline end to end (SPEC F27.1–F27.5, STORY-078): synthesize
/// the voice clip, render the final artifact through <see cref="IAudioMixer"/> (voice-only still goes
/// through the mixer once — P2's design keeps tag-embedding in exactly one place), measure the FINAL
/// artifact with the same analyzers the enricher uses, and insert a ready row via
/// <see cref="IAuthoredCatalogWriter"/>. Both shipped triggers — the <c>POST /api/safe-segments</c>
/// endpoint (P6) and the boot seed (P7) — call <see cref="AuthorAsync"/>: one code path, one failure
/// surface.
///
/// All-or-nothing (F27.1): any failure after a file exists on disk deletes every file this attempt
/// wrote — the raw synth clip AND the mixed artifact — before returning a typed failure. Kokoro being
/// unreachable, an insert failure (including the FK violation documented on
/// <see cref="IAuthoredCatalogWriter.InsertAuthoredAsync"/>), or any other stage's fault is caught here
/// and reported via <see cref="SafeSegmentAuthorResult"/> — never left to throw across this seam (the
/// P4 reviewer forward-note: the raw exception never escapes as this service's failure surface).
///
/// The artifact filename is always a fresh GUID under <see cref="SafeSegmentRequest.AuthoredRoot"/> —
/// never derived from <see cref="SafeSegmentRequest.Title"/> or <see cref="SafeSegmentRequest.Text"/>,
/// so path traversal via operator-supplied text is structurally impossible (the P4 reviewer
/// forward-note on path safety).
///
/// Config-free across the Host boundary, mirroring <see cref="IAudioMixer"/> one layer down:
/// <c>Station:Safe:*</c>, <c>Station:Voice</c>, and <c>Station:Name</c> bind to <c>StationOptions</c>
/// in <c>GenWave.Host</c>, which depends on this project — not the other way around — so
/// <see cref="SafeSegmentRequest"/> carries every station-resolved value in from the caller. The one
/// exception is <see cref="TtsOptions.Format"/> (the artifact's file extension): that IS this
/// project's own config, ctor-injected exactly as <see cref="TtsSegmentSource"/> already does.
///
/// <see cref="SafeSegmentRequest.Text"/> may carry the literal token <c>{StationName}</c> — this is
/// the SOLE place it is expanded, to <see cref="SafeSegmentRequest.StationName"/> (SPEC F29.1–F29.2,
/// STORY-095). Both triggers used to resolve this token independently (only the boot seed did, the
/// endpoint never did at all — the hole gitea-#184 shipped through); centralizing it here means every future
/// caller of <see cref="AuthorAsync"/> gets expansion for free with no per-caller opt-in.
/// </summary>
public sealed class SafeSegmentAuthor(
    ITtsSynthesizer synthesizer,
    IAudioMixer mixer,
    ILoudnessAnalyzer loudnessAnalyzer,
    ICueAnalyzer cueAnalyzer,
    IEnergyAnalyzer energyAnalyzer,
    IAuthoredCatalogWriter catalogWriter,
    IOptions<TtsOptions> ttsOptions,
    ILogger<SafeSegmentAuthor> logger) : ISafeSegmentAuthor
{
    public const string DefaultTitle = "Please Stand By";

    readonly TtsOptions cfg = ttsOptions.Value;

    public async Task<SafeSegmentAuthorResult> AuthorAsync(SafeSegmentRequest request, CancellationToken ct)
    {
        var text = request.Text.Replace("{StationName}", request.StationName, StringComparison.Ordinal);
        var voice = request.Voice ?? request.DefaultVoice;
        var tags = new AudioTags(request.StationName, request.Title ?? DefaultTitle);

        string synthPath;
        try
        {
            synthPath = await synthesizer.SynthesizeAsync(text, voice, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Safe-segment synthesis failed (voice {Voice})", LogSanitize.Strip(voice));
            return SafeSegmentAuthorResult.Failure(SafeSegmentFailureReason.SynthesisFailed, ex.Message);
        }

        // Creating AuthoredRoot belongs to the same failure surface as the mix stage that writes
        // into it next — an unwritable/invalid root can never produce the mixed artifact, so a
        // failure here is reported as MixFailed rather than escaping as a raw framework exception.
        try
        {
            Directory.CreateDirectory(request.AuthoredRoot);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Safe-segment authored root not writable: {AuthoredRoot}", request.AuthoredRoot);
            DeleteIfExists(synthPath);
            return SafeSegmentAuthorResult.Failure(SafeSegmentFailureReason.MixFailed, "authored root not writable");
        }

        var artifactPath = Path.Combine(request.AuthoredRoot, $"{Guid.NewGuid():N}.{cfg.Format}");

        try
        {
            return await RenderAndInsertAsync(request, tags, synthPath, artifactPath, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            DeleteIfExists(synthPath);
            DeleteIfExists(artifactPath);
            throw;
        }
    }

    async Task<SafeSegmentAuthorResult> RenderAndInsertAsync(
        SafeSegmentRequest request, AudioTags tags, string synthPath, string artifactPath, CancellationToken ct)
    {
        try
        {
            await mixer.MixAsync(
                new AudioMixRequest(synthPath, request.Bed, tags, request.BedDuckDb, request.BedPadSeconds, artifactPath),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Safe-segment mix failed for {ArtifactPath}", artifactPath);
            DeleteIfExists(synthPath);
            return SafeSegmentAuthorResult.Failure(SafeSegmentFailureReason.MixFailed, ex.Message);
        }

        LoudnessMeasurement loudness;
        try
        {
            loudness = await loudnessAnalyzer.AnalyzeAsync(artifactPath, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Safe-segment loudness measurement failed for {ArtifactPath}", artifactPath);
            DeleteIfExists(synthPath);
            DeleteIfExists(artifactPath);
            return SafeSegmentAuthorResult.Failure(SafeSegmentFailureReason.MeasurementFailed, ex.Message);
        }

        // Cue/energy never gate readiness (F13.3 / F17.4 discipline) — failures here are logged and
        // treated as NULL, exactly like the enricher, never as an authoring failure.
        var cue = await MeasureCueAsync(artifactPath, ct);
        var energy = await MeasureEnergyAsync(artifactPath, cue, ct);
        var insert = BuildInsert(request.LibraryId, artifactPath, cfg.Format, tags, loudness, cue, energy);

        try
        {
            var mediaId = await catalogWriter.InsertAuthoredAsync(insert, ct);
            DeleteIfExists(synthPath);
            return SafeSegmentAuthorResult.Success(mediaId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Safe-segment catalog insert failed for {ArtifactPath}", artifactPath);
            DeleteIfExists(synthPath);
            DeleteIfExists(artifactPath);
            return SafeSegmentAuthorResult.Failure(SafeSegmentFailureReason.InsertFailed, ex.Message);
        }
    }

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
            logger.LogWarning(ex, "Safe-segment cue analysis failed for {Path}; cue will be NULL", path);
            return null;
        }
    }

    async Task<EnergyPoints?> MeasureEnergyAsync(string path, CuePoints? cue, CancellationToken ct)
    {
        try
        {
            return await energyAnalyzer.AnalyzeAsync(path, cue?.CueInSec, cue?.CueOutSec, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Safe-segment energy analysis failed for {Path}; energy will be NULL", path);
            return null;
        }
    }

    /// <summary>
    /// DurationMs/SampleRate/Channels/BitrateKbps are best-effort (F27.1 allows NULL on the row shape).
    /// Duration is taken from the cue-analyzer's CueOutSec when available — free, since cue analysis
    /// already ran — rather than paying for a dedicated ffprobe call; the others are left NULL rather
    /// than adding a new probing dependency to this project for a nice-to-have.
    /// </summary>
    static AuthoredMediaInsert BuildInsert(
        long libraryId, string artifactPath, string format, AudioTags tags,
        LoudnessMeasurement loudness, CuePoints? cue, EnergyPoints? energy)
    {
        var info = new FileInfo(artifactPath);
        var durationMs = cue is not null
            ? (int?)Math.Round(cue.CueOutSec * 1000.0, MidpointRounding.AwayFromZero)
            : null;

        return new AuthoredMediaInsert(
            Path: artifactPath,
            Format: format,
            LibraryId: libraryId,
            SizeBytes: info.Length,
            Mtime: info.LastWriteTimeUtc,
            Tags: tags,
            Loudness: loudness,
            Cue: cue,
            Energy: energy,
            DurationMs: durationMs,
            SampleRate: null,
            Channels: null,
            BitrateKbps: null);
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
            // Best-effort cleanup; the reported failure is what the caller needs to see (mirrors
            // FfmpegAudioMixer.DeletePartialOutput's own precedent).
        }
    }
}

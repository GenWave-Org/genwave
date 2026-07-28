using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;
using LoudnessMeasurement = GenWave.Core.Domain.Loudness;

namespace GenWave.MediaLibrary.Enrich;

/// <summary>
/// One enrichment pass over a file (PRD §8): loudness via FFmpeg ebur128, cue-point detection via
/// silence analysis, energy analysis over the cue-trimmed windows (STORY-033), BPM analysis over the
/// same cue-trimmed window (SPEC F46.3), plus the technical audio properties and the normalized tags
/// via TagLibSharp (which reads both in one managed call and normalizes across MP3/ID3 and
/// FLAC/Vorbis, so genre/artist are consistent for future criteria queries). Pure of any DB concern —
/// it returns an <see cref="EnrichmentResult"/> the repository writes atomically. Idempotent:
/// re-enriching a file yields the same result.
///
/// Cue analysis failure (SPEC F13.3): an exception from <see cref="ICueAnalyzer"/> is caught,
/// logged at WARN, and does not block the row from becoming <c>ready</c> — loudness still gates that
/// transition. <c>cue_analyzed_at</c> is always written to mark "we tried".
///
/// Energy analysis failure (STORY-033): an exception from <see cref="IEnergyAnalyzer"/> is treated
/// identically — caught, logged at WARN, energy columns persist NULL, but the row still reaches
/// <c>ready</c> provided loudness succeeded. <c>energy_analyzed_at</c> is always written.
///
/// BPM analysis failure (SPEC F46.1/F46.3): an exception from <see cref="IBpmAnalyzer"/> is treated
/// identically to cue/energy — caught, logged at WARN, <c>bpm</c> persists NULL, but the row still
/// reaches <c>ready</c> provided loudness succeeded. <c>bpm_analyzed_at</c> is always written, whether
/// the analyzer throws or simply returns <c>null</c> (indeterminate tempo) — attempted-none-found.
///
/// Advisory/explicit tag (SPEC F95.3, STORY-251, PLAN T112): read alongside the other normalized
/// tags via the same TagLib open — never a second file open. Honors the real-world ITUNESADVISORY
/// convention: an ID3v2 TXXX user-text frame (MP3) or a Vorbis comment field (FLAC/Ogg), value "1"
/// = explicit, "2" = clean (a positive clean rating is itself information the tag pass stamps),
/// "0"/absent/unparseable = a miss (stays <see langword="null"/>, never stamped).
/// </summary>
sealed class Enricher(
    ILoudnessAnalyzer loudness,
    ICueAnalyzer cueAnalyzer,
    IEnergyAnalyzer energyAnalyzer,
    IBpmAnalyzer bpmAnalyzer,
    ILogger<Enricher> log)
{
    public async Task<EnrichmentResult> EnrichAsync(string path, CancellationToken ct)
    {
        var measured = await loudness.AnalyzeAsync(path, ct);   // ffmpeg ebur128 (subprocess)
        ct.ThrowIfCancellationRequested();

        CuePoints? cuePoints = null;
        try
        {
            cuePoints = await cueAnalyzer.AnalyzeAsync(path, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Cue analysis failed for {Path}; cue columns will be NULL", path);
        }

        EnergyPoints? energyPoints = null;
        try
        {
            energyPoints = await energyAnalyzer.AnalyzeAsync(path, cuePoints?.CueInSec, cuePoints?.CueOutSec, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Energy analysis failed for {Path}; energy columns will be NULL", path);
        }

        double? bpm = null;
        try
        {
            bpm = await bpmAnalyzer.AnalyzeAsync(path, cuePoints?.CueInSec, cuePoints?.CueOutSec, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "BPM analysis failed for {Path}; bpm will be NULL", path);
        }

        return ReadTags(path, measured, cuePoints, energyPoints, bpm);
    }

    static EnrichmentResult ReadTags(
        string path,
        LoudnessMeasurement loudnessMeasurement,
        CuePoints? cuePoints,
        EnergyPoints? energyPoints,
        double? bpm)
    {
        using var file = TagLib.File.Create(path);
        var props = file.Properties;
        var tag = file.Tag;

        int? durationMs = props is not null && props.Duration > TimeSpan.Zero
            ? (int)props.Duration.TotalMilliseconds
            : null;
        int? sampleRate = props is { AudioSampleRate: > 0 } ? props.AudioSampleRate : null;
        short? channels = props is { AudioChannels: > 0 } ? (short)props.AudioChannels : null;
        int? bitrateKbps = props is { AudioBitrate: > 0 } ? props.AudioBitrate : null;

        return new EnrichmentResult(
            DurationMs:       durationMs,
            SampleRate:       sampleRate,
            Channels:         channels,
            BitrateKbps:      bitrateKbps,
            Title:            NullIfBlank(tag.Title),
            Artist:           NullIfBlank(tag.JoinedPerformers),
            Album:            NullIfBlank(tag.Album),
            AlbumArtist:      NullIfBlank(tag.JoinedAlbumArtists),
            Genre:            NullIfBlank(tag.JoinedGenres),
            TrackNo:          tag.Track > 0 ? (int)tag.Track : null,
            Year:             tag.Year > 0 ? (int)tag.Year : null,
            Explicit:         TryReadAdvisoryTag(file),
            IntegratedLufs:   loudnessMeasurement.IntegratedLufs,
            TruePeakDbtp:     loudnessMeasurement.TruePeakDbtp,
            Measurable:       loudnessMeasurement.Measurable,
            CueInSec:         cuePoints?.CueInSec,
            CueOutSec:        cuePoints?.CueOutSec,
            CueAnalyzedAt:    DateTime.UtcNow,
            IntroEnergy:      energyPoints?.IntroEnergy,
            OutroEnergy:      energyPoints?.OutroEnergy,
            EnergyAnalyzedAt: DateTime.UtcNow,
            Bpm:              bpm,
            BpmAnalyzedAt:    DateTime.UtcNow);
    }

    static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    // The real-world advisory-flag convention (SPEC F95.3): iTunes/Picard/beets all key it
    // "ITUNESADVISORY", carried as an ID3v2 TXXX user-text frame or a Vorbis comment field.
    const string AdvisoryTagKey = "ITUNESADVISORY";

    /// <summary>
    /// Reads the ITUNESADVISORY advisory flag from whichever tag container the file actually has —
    /// ID3v2 (MP3) checked first, then Xiph/Vorbis comment (FLAC/Ogg). Matched case-insensitively:
    /// ffmpeg's ID3v2 writer round-trips a TXXX description's casing verbatim (taggers in the wild
    /// disagree on it), while TagLib's own Xiph writer always upper-cases field names regardless of
    /// how they were set. Returns <see langword="null"/> when neither container carries the tag, or
    /// when it holds neither "1" nor "2" — a miss the caller must never stamp.
    /// </summary>
    static bool? TryReadAdvisoryTag(TagLib.File file)
    {
        if (file.GetTag(TagLib.TagTypes.Id3v2, false) is TagLib.Id3v2.Tag id3v2)
        {
            foreach (var frame in id3v2.GetFrames<TagLib.Id3v2.UserTextInformationFrame>())
            {
                if (string.Equals(frame.Description, AdvisoryTagKey, StringComparison.OrdinalIgnoreCase))
                    return ParseAdvisoryValue(frame.Text.FirstOrDefault());
            }
        }

        if (file.GetTag(TagLib.TagTypes.Xiph, false) is TagLib.Ogg.XiphComment xiph)
        {
            var values = xiph.GetField(AdvisoryTagKey);
            if (values.Length > 0)
                return ParseAdvisoryValue(values[0]);
        }

        return null;
    }

    /// <summary>
    /// "1" = explicit, "2" = clean (F95.3 — a positive clean rating is information too, not a
    /// miss). "0", blank, absent, or anything else unparseable is a miss: stays
    /// <see langword="null"/>, never stamped.
    /// </summary>
    static bool? ParseAdvisoryValue(string? raw) => raw?.Trim() switch
    {
        "1" => true,
        "2" => false,
        _   => null,
    };
}

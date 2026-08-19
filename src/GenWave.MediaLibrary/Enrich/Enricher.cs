using GenWave.Core.Abstractions;
using GenWave.MediaLibrary.Catalog;
using LoudnessMeasurement = GenWave.Core.Domain.Loudness;

namespace GenWave.MediaLibrary.Enrich;

/// <summary>
/// The first-pass enrichment fast pass (SPEC F135.1, STORY-341): loudness via FFmpeg ebur128, plus
/// the technical audio properties and the normalized tags via TagLibSharp (which reads both in one
/// managed call and normalizes across MP3/ID3 and FLAC/Vorbis, so genre/artist are consistent for
/// future criteria queries) — one ffmpeg pass and one tag read, nothing more. The existing atomic
/// write (<see cref="Catalog.MediaRepository.WriteEnrichmentAsync"/>) flips the row to <c>ready</c>
/// with cue/energy/BPM columns left NULL; the second-tier backfill lanes in
/// <see cref="EnrichmentService"/> sweep them in afterward by finding the row through those very
/// NULLs — no new queue, worker, or schema. Text tags pass through <see cref="TagText.Normalize"/> —
/// the single entity-decode seam (gh-#257): entity-encoded tags some export pipelines write
/// (<c>Paul &amp;amp; Manuel</c>) are decoded exactly once HERE, so every downstream consumer
/// (annotate, now-playing, play-history, both UIs) stays a pass-through.
/// Pure of any DB concern —
/// it returns an <see cref="EnrichmentResult"/> the repository writes atomically. Idempotent:
/// re-enriching a file yields the same result.
///
/// Loudness failure (SPEC F135.1/AC5): an exception from <see cref="ILoudnessAnalyzer"/> is left
/// uncaught — no local catch-and-log, unlike the pre-F135 cue/energy/bpm calls this class used to
/// make. It propagates to <see cref="EnrichmentService.EnrichOneAsync"/>, which logs it and marks the
/// row <c>failed</c>. The fast pass narrows the work, never the failure contract.
///
/// Advisory/explicit tag (SPEC F95.3, STORY-251, PLAN T112): read alongside the other normalized
/// tags via the same TagLib open — never a second file open. Honors the real-world ITUNESADVISORY
/// convention: an ID3v2 TXXX user-text frame (MP3) or a Vorbis comment field (FLAC/Ogg), value "1"
/// = explicit, "2" = clean (a positive clean rating is itself information the tag pass stamps),
/// "0"/absent/unparseable = a miss (stays <see langword="null"/>, never stamped).
/// </summary>
sealed class Enricher(ILoudnessAnalyzer loudness)
{
    public async Task<EnrichmentResult> EnrichAsync(string path, CancellationToken ct)
    {
        var measured = await loudness.AnalyzeAsync(path, ct);   // ffmpeg ebur128 (subprocess)
        ct.ThrowIfCancellationRequested();

        return ReadTags(path, measured);
    }

    static EnrichmentResult ReadTags(string path, LoudnessMeasurement loudnessMeasurement)
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
            Title:            TagText.Normalize(tag.Title),
            Artist:           TagText.Normalize(tag.JoinedPerformers),
            Album:            TagText.Normalize(tag.Album),
            AlbumArtist:      TagText.Normalize(tag.JoinedAlbumArtists),
            Genre:            TagText.Normalize(tag.JoinedGenres),
            TrackNo:          tag.Track > 0 ? (int)tag.Track : null,
            Year:             tag.Year > 0 ? (int)tag.Year : null,
            Explicit:         TryReadAdvisoryTag(file),
            IntegratedLufs:   loudnessMeasurement.IntegratedLufs,
            TruePeakDbtp:     loudnessMeasurement.TruePeakDbtp,
            Measurable:       loudnessMeasurement.Measurable,
            // Cue/energy/BPM stay NULL — including the *_analyzed_at columns (SPEC F135.1) — so the
            // existing second-tier backfill lanes find this row by those very NULLs and sweep it in.
            CueInSec:         null,
            CueOutSec:        null,
            CueAnalyzedAt:    null,
            IntroEnergy:      null,
            OutroEnergy:      null,
            EnergyAnalyzedAt: null,
            Bpm:              null,
            BpmAnalyzedAt:    null);
    }

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

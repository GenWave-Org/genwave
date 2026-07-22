namespace GenWave.Core.Abstractions;

/// <summary>
/// Assigns mood tags to a track from the fixed <c>GenWave.Core.Domain.MoodVocabulary</c> via the
/// configured LLM endpoint (SPEC F85.2-F85.4, STORY-216, T72) — the offline, second-tier enrichment
/// counterpart to <see cref="IYearLookup"/>. This is the committed seam
/// <c>GenWave.MediaLibrary.Enrich.EnrichmentService</c>'s mood-tag backfill pass consumes; only one
/// implementation ships today (<c>GenWave.MediaLibrary.Mood.OllamaMoodTagger</c>, an OpenAI-compatible
/// chat-completions client), mirroring where <see cref="IYearLookup"/>/<c>MusicBrainzYearLookup</c>
/// sit relative to each other.
///
/// Never throws past this boundary for an ordinary miss (F85.4): returns an empty list whenever the
/// round trip completes but produces fewer than one in-vocabulary survivor after constrained-output
/// parsing — the legal "no confident tag" outcome, same shape as <see cref="IYearLookup"/>'s nullable
/// return. An implementation MAY also implement an optional, MediaLibrary-internal diagnostics seam
/// (mirroring <c>GenWave.MediaLibrary.YearLookup.IYearLookupDiagnostics</c>) so a caller can
/// distinguish that legal miss from an endpoint-level failure (timeout, connect failure, non-2xx,
/// malformed body) — deliberately never folded into THIS committed contract, which stays as narrow
/// as <see cref="IYearLookup"/>'s own.
/// </summary>
public interface IMoodTagger
{
    /// <summary>
    /// Returns up to <c>MoodVocabulary.MaxMoodsPerTrack</c> in-vocabulary mood terms for the track
    /// described by <paramref name="artist"/>/<paramref name="title"/>/<paramref name="genre"/> — any
    /// of which may be null/blank; the tagger builds the best prompt context it can from whatever is
    /// present. An empty result is a legal miss (F85.4), never an exception.
    /// </summary>
    Task<IReadOnlyList<string>> TagAsync(string? artist, string? title, string? genre, CancellationToken ct);
}

namespace GenWave.MediaLibrary.Mood;

/// <summary>
/// Optional out-of-band diagnostic an <see cref="GenWave.Core.Abstractions.IMoodTagger"/>
/// implementation MAY also implement to distinguish "zero in-vocabulary survivors" (a legal miss per
/// the Core contract, F85.4) from "the call could not complete an HTTP round trip" (an endpoint-level
/// outage) — WITHOUT widening the committed Core seam, whose <c>TagAsync</c> returns an empty list
/// either way. Mirrors <c>GenWave.MediaLibrary.YearLookup.IYearLookupDiagnostics</c> exactly (SPEC
/// F76.2's own precedent, applied to moods).
///
/// <see cref="Enrich.EnrichmentService"/>'s mood-tag backfill pattern-matches for this interface
/// after every attempt so a genuine miss stamps the re-claim gate (<c>mood_tag_missed_at</c>) while a
/// failed round trip leaves the row eligible for the very next tick — a test double proving only
/// "no survivors" simply doesn't implement it.
/// </summary>
public interface IMoodTaggerDiagnostics
{
    /// <summary>
    /// <see langword="true"/> when the most recent <c>TagAsync</c> call could not complete an HTTP
    /// round trip (timeout, connect failure, non-2xx status, malformed response body) —
    /// <see langword="false"/> when a response was successfully received and parsed, regardless of
    /// whether it produced any in-vocabulary survivor.
    /// </summary>
    bool LastCallFailed { get; }
}

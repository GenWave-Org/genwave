namespace GenWave.MediaLibrary.ExplicitClassification;

/// <summary>
/// Optional out-of-band diagnostic an <see cref="GenWave.Core.Abstractions.IExplicitClassifier"/>
/// implementation MAY also implement to distinguish "the model answered but it wasn't a confident
/// yes/no" (a legal miss per the Core contract, F95.3) from "the call could not complete an HTTP
/// round trip" (an endpoint-level outage) — WITHOUT widening the committed Core seam, whose
/// <c>ClassifyAsync</c> returns <see langword="null"/> either way. Mirrors
/// <c>GenWave.MediaLibrary.Mood.IMoodTaggerDiagnostics</c> /
/// <c>GenWave.MediaLibrary.YearLookup.IYearLookupDiagnostics</c> exactly.
///
/// <see cref="Enrich.EnrichmentService"/>'s explicit-classification backfill pattern-matches for
/// this interface after every attempt so a genuine "unknown" miss stamps the re-claim gate
/// (<c>explicit_llm_missed_at</c>) while a failed round trip leaves the row eligible for the very
/// next tick — a test double proving only "no verdict" simply doesn't implement it.
/// </summary>
public interface IExplicitClassifierDiagnostics
{
    /// <summary>
    /// <see langword="true"/> when the most recent <c>ClassifyAsync</c> call could not complete an
    /// HTTP round trip (timeout, connect failure, non-2xx status, malformed response body) —
    /// <see langword="false"/> when a response was successfully received and parsed, regardless of
    /// whether it produced a confident yes/no verdict.
    /// </summary>
    bool LastCallFailed { get; }
}

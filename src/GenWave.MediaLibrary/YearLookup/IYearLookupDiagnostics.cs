namespace GenWave.MediaLibrary.YearLookup;

/// <summary>
/// Optional out-of-band diagnostic an <see cref="GenWave.Core.Abstractions.IYearLookup"/>
/// implementation MAY also implement to distinguish "no confident match" (a legal outcome per the
/// Core contract, F48.2) from "the call could not complete an HTTP round trip" (an endpoint-level
/// outage, F48.5) — WITHOUT widening the committed Core seam, whose <c>TryLookupAsync</c> returns a
/// nullable year either way and must not change shape (X5 house rule).
///
/// <see cref="Enrich.EnrichmentService"/>'s backfill pattern-matches for this interface after every
/// attempt in a batch so it can WARN once per tick when the endpoint itself is unreachable, rather
/// than once per row (SPEC F48.5). A test double proving only "no confident match" simply doesn't
/// implement it — the service treats an <see cref="GenWave.Core.Abstractions.IYearLookup"/> that
/// isn't also an <see cref="IYearLookupDiagnostics"/> as carrying no failure signal, never as a
/// failure.
/// </summary>
public interface IYearLookupDiagnostics
{
    /// <summary>
    /// <see langword="true"/> when the most recent <c>TryLookupAsync</c> call could not complete an
    /// HTTP round trip (timeout, connect failure, non-2xx status, malformed response body) —
    /// <see langword="false"/> when a response was successfully received and parsed, regardless of
    /// whether it produced a confident match.
    /// </summary>
    bool LastCallFailed { get; }
}

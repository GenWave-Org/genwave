namespace GenWave.Core.Domain;

/// <summary>
/// How much a <see cref="PatterDurationEstimate"/> should be trusted (gh-#253) — the honest tier
/// the estimate came from, carried alongside the number so a consumer (the gh-#254 boundary-fit
/// selector) can widen its tolerance as confidence drops rather than treating every estimate as
/// gospel. Ordered best-first so "worst of several" is a simple max.
/// </summary>
public enum PatterEstimateConfidence
{
    /// <summary>
    /// The estimate IS a measured duration of the exact audio that will air (a render-ahead /
    /// cache-stable hit) — F66.1's cue-derived <c>DurationMs</c>, never fabricated.
    /// </summary>
    Exact,

    /// <summary>
    /// A rolling average over real measured durations of previously aired patter for the same
    /// persona × kind — cheap and self-improving, but the next airing's copy is still unwritten.
    /// </summary>
    Historical,

    /// <summary>
    /// Cold fallback: a chars-per-second heuristic with no measured samples behind it (or too few
    /// to trust), bounded by the live <c>Llm:MaxCopyChars</c> worst case.
    /// </summary>
    Heuristic,
}

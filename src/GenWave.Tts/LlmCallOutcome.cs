namespace GenWave.Tts;

/// <summary>
/// Outcome of a single completed LLM call as captured by <see cref="LlmCallRing"/> (SPEC F73.1,
/// STORY-196, T41) — distinct from (and narrower than) <see cref="LlmAttemptOutcome"/>, which only
/// ever sees on-air <see cref="LlmCopyWriter.WriteAsync"/> attempts and collapses every miss to one
/// "Failed" bucket. The ring inspector's whole point is a finer-grained debug lens, so
/// <see cref="Timeout"/> is split out from a generic <see cref="Failed"/>.
/// </summary>
public enum LlmCallOutcome
{
    /// <summary>
    /// The completions endpoint returned 2xx. <see cref="LlmCallRecord.Response"/> carries the RAW
    /// reply exactly as received — BEFORE <c>LlmCopyWriter.CleanCopy</c> hygiene — so a call whose
    /// text was later rejected for being empty or over-length after cleanup still shows up here as
    /// Ok with a telling raw response, never silently reclassified as a failure.
    /// </summary>
    Ok,

    /// <summary>
    /// A non-2xx status, a connect failure, a malformed endpoint URI, or bad JSON — any completions
    /// fault other than this call's own timeout budget elapsing. <see cref="LlmCallRecord.StatusDetail"/>
    /// carries the HTTP status or exception type name.
    /// </summary>
    Failed,

    /// <summary>
    /// This call's own <c>Llm:TimeoutSeconds</c> budget elapsed before a response arrived. Split out
    /// from <see cref="Failed"/> because it is the one outcome an operator can address by raising
    /// <c>Llm:TimeoutSeconds</c> rather than investigating the endpoint itself.
    /// </summary>
    Timeout,
}

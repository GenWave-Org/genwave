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

    /// <summary>
    /// The completions endpoint returned 2xx, but the cleaned reply exceeded <c>Llm:MaxCopyChars</c>
    /// and was salvaged by cutting at the last complete sentence that fit (SPEC F123.2-F123.4,
    /// STORY-319, PLAN T263) — split out from <see cref="Ok"/> because this ring is exactly the
    /// diagnostic surface gh-#277's over-length-copy investigation leans on: an operator watching the
    /// inspector should be able to spot which persona/kind trims often without re-deriving it by
    /// comparing <see cref="LlmCallRecord.Response"/> lengths by hand. Discipline, not an outage — the
    /// segment still airs the trimmed copy (see <see cref="LlmCopyWriter.CleanCopy"/>'s own remarks).
    /// </summary>
    Trimmed,

    /// <summary>
    /// The completions endpoint returned 2xx, but the reply failed CONTENT validation — a
    /// <see cref="CrosstalkScriptWriter"/>-only outcome (SPEC F127.4, PLAN T282): the script didn't
    /// parse (wrong line count, an unrecognized speaker tag, broken alternation), a line failed
    /// hygiene or its per-line budget, or the estimated spoken duration exceeded the configured
    /// target. Split out from <see cref="Failed"/> the same way <see cref="Trimmed"/> split out from
    /// <see cref="Ok"/> above — a validation reject is a content-quality decision this project made
    /// on a genuinely successful HTTP call, never a transport/endpoint fault. Unlike
    /// <see cref="Trimmed"/>, there is no salvage here: F127.4 is skip-only, no trim, no template
    /// rung — <see cref="LlmCallRecord.StatusDetail"/> carries the discard reason.
    /// </summary>
    Rejected,
}

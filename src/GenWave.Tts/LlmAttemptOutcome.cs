namespace GenWave.Tts;

/// <summary>Outcome of a single <see cref="LlmCopyWriter"/> completion attempt (SPEC F34.8).</summary>
public enum LlmAttemptOutcome
{
    /// <summary>
    /// The completion returned usable copy that passed hygiene — an exact fit under
    /// <c>Llm:MaxCopyChars</c>, or a sentence-boundary salvage of an over-length reply (SPEC
    /// F123.2-F123.4, STORY-319, PLAN T263). A trim is a SUCCESS at this coarse Ok/Failed grain (it
    /// airs) and must never feed <see cref="LlmCopyStatusHolder.ConsecutiveFailureCount"/>'s F72
    /// degradation walk-down — <see cref="LlmCallRing"/>'s own finer-grained
    /// <see cref="LlmCallOutcome.Trimmed"/> is where the salvage stays visible as its own outcome,
    /// for the <c>/api/llm-calls</c> debug lens alone.
    /// </summary>
    Ok,

    /// <summary>Any miss — timeout, non-2xx, connect failure, empty/over-length copy.</summary>
    Failed,
}

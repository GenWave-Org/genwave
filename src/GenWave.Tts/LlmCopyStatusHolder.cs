namespace GenWave.Tts;

/// <summary>
/// In-memory record of the most recent <see cref="LlmCopyWriter"/> completion attempt (SPEC F34.8).
/// Singleton, process-lifetime only — no persistence and no active health-polling; <c>GET
/// /api/status</c> (STORY-125) reads whatever the last render produced. A disabled writer and the
/// templated kinds (StationId/TimeDate) never call <see cref="Record"/> — this holder reflects LLM
/// attempts only.
///
/// <see cref="ConsecutiveFailureCount"/> (SPEC F69.2, STORY-188) is what
/// <see cref="DegradationController"/> reads for the auto-drop side of the mode state machine — the
/// real-call-outcome half of "use real call outcomes for drops, probes for raises". It mirrors
/// <see cref="DependencyHealthVerdict.ConsecutiveFailureCount"/>'s exact reset/increment shape, and
/// inherits the same "never counts a disabled writer" guarantee <see cref="Record"/> already carried
/// for free: an empty <c>Llm:Endpoint</c> short-circuits <see cref="LlmCopyWriter.WriteAsync"/>
/// before it ever reaches <see cref="Record"/>, so this count can never mistake "LLM disabled by
/// design" for a failure streak.
/// </summary>
public sealed class LlmCopyStatusHolder
{
    readonly object gate = new();
    LlmAttemptStatus? last;
    int consecutiveFailureCount;

    /// <summary>Records the outcome of a completion attempt.</summary>
    public void Record(LlmAttemptOutcome outcome, DateTimeOffset attemptedAt)
    {
        lock (gate)
        {
            last = new LlmAttemptStatus(outcome, attemptedAt);
            consecutiveFailureCount = outcome == LlmAttemptOutcome.Ok ? 0 : consecutiveFailureCount + 1;
        }
    }

    /// <summary>The most recent attempt, or null if the writer has never attempted a completion.</summary>
    public LlmAttemptStatus? Last
    {
        get
        {
            lock (gate)
            {
                return last;
            }
        }
    }

    /// <summary>
    /// Consecutive <see cref="LlmAttemptOutcome.Failed"/> records in a row, reset to 0 by the next
    /// <see cref="LlmAttemptOutcome.Ok"/>. 0 until the first attempt is recorded.
    /// </summary>
    public int ConsecutiveFailureCount
    {
        get
        {
            lock (gate)
            {
                return consecutiveFailureCount;
            }
        }
    }
}

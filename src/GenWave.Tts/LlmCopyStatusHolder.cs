namespace GenWave.Tts;

/// <summary>
/// In-memory record of the most recent <see cref="LlmCopyWriter"/> completion attempt (SPEC F34.8).
/// Singleton, process-lifetime only — no persistence and no active health-polling; <c>GET
/// /api/status</c> (STORY-125) reads whatever the last render produced. A disabled writer and the
/// templated kinds (StationId/TimeDate) never call <see cref="Record"/> — this holder reflects LLM
/// attempts only.
/// </summary>
public sealed class LlmCopyStatusHolder
{
    readonly object gate = new();
    LlmAttemptStatus? last;

    /// <summary>Records the outcome of a completion attempt.</summary>
    public void Record(LlmAttemptOutcome outcome, DateTimeOffset attemptedAt)
    {
        lock (gate)
        {
            last = new LlmAttemptStatus(outcome, attemptedAt);
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
}

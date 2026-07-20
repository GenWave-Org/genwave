namespace GenWave.Tts;

/// <summary>
/// A single dependency's cached health snapshot (SPEC F70.2, STORY-187). <see cref="Reason"/> is
/// null exactly when <see cref="Healthy"/> is true — never an empty string, never populated for a
/// healthy verdict. <see cref="ConsecutiveFailureCount"/> resets to 0 on the next healthy verdict
/// and otherwise increments on every unhealthy verdict in a row; F69.2's mode-transition
/// thresholds (STORY-188, the very next task) read this count rather than re-deriving it from
/// verdict history, so it is carried here now even though nothing in this task reads it yet.
/// </summary>
public sealed record DependencyHealthVerdict(
    string DependencyName,
    bool Healthy,
    DateTimeOffset CheckedAt,
    string? Reason,
    int ConsecutiveFailureCount);

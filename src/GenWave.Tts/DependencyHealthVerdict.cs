namespace GenWave.Tts;

/// <summary>
/// A single dependency's cached health snapshot (SPEC F70.2, STORY-187). <see cref="Reason"/> is
/// null exactly when <see cref="Healthy"/> is true — never an empty string, never populated for a
/// healthy verdict. <see cref="ConsecutiveFailureCount"/> resets to 0 on the next successful probe
/// and otherwise increments on every failed probe in a row; F69.2's mode-transition thresholds
/// (STORY-188) read this count rather than re-deriving it from verdict history.
/// <para>
/// <see cref="Healthy"/> and <see cref="ConsecutiveFailureCount"/> are NOT redundant, and since
/// F70.2 AC5 (gh-#125) they can legitimately disagree: the count is the raw observation, while
/// <see cref="Healthy"/> is the debounced conclusion after
/// <c>DependencyHealth:UnhealthyThreshold</c> is applied. A single failed probe under a threshold
/// of 2 therefore reads <c>Healthy: true, ConsecutiveFailureCount: 1</c> — "one probe missed, we
/// do not yet believe it is down". Read <see cref="Healthy"/> to make a routing decision; read the
/// count only to reason about how a verdict was reached.
/// </para>
/// </summary>
public sealed record DependencyHealthVerdict(
    string DependencyName,
    bool Healthy,
    DateTimeOffset CheckedAt,
    string? Reason,
    int ConsecutiveFailureCount);

namespace GenWave.Core.Abstractions;

/// <summary>
/// The public listener-count seam (gitea-#10) — a live read of how many listeners are currently
/// tuned in, sourced from Icecast's admin stats. Null means "unknown": unconfigured, unreachable,
/// or unparsable — never fabricated, and never surfaced as an error to a caller. Gitea-#10's
/// remaining scope (a periodic analytics poller / event-sink publication of this count over time)
/// is NOT this interface's job — this seam only answers "what is the count right now."
/// </summary>
public interface IListenerStatsSource
{
    /// <summary>The current listener count, or null when it cannot be determined right now.</summary>
    Task<int?> GetListenerCountAsync(CancellationToken ct);
}

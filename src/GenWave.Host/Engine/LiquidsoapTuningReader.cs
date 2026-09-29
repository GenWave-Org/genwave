using GenWave.Host.Options;

namespace GenWave.Host.Engine;

/// <summary>
/// Telnet <see cref="IEngineTuningReader"/> (SPEC F213.5) — sends <c>gw_tuning</c> over
/// <see cref="LiquidsoapTelnet"/> and returns the raw reply line.
/// </summary>
sealed class LiquidsoapTuningReader(LiquidsoapOptions options, TimeSpan? readTimeout = null) : IEngineTuningReader
{
    /// <summary>
    /// Default budget for a single <c>gw_tuning</c> round trip before this reader gives up and
    /// throws <see cref="TimeoutException"/> — a few seconds, well under the health-probe's 30 s
    /// cadence (SPEC F213.5, T602), so a socket that accepts the connection but never replies costs
    /// at most one missed probe tick, never a stalled probe. Overridable via the constructor's
    /// <c>readTimeout</c> so a spec can prove the timeout path without paying the real budget
    /// (PLAN T601 review F2); production callers all take the default.
    /// </summary>
    static readonly TimeSpan DefaultReadTimeout = TimeSpan.FromSeconds(5);

    readonly TimeSpan readTimeout = readTimeout ?? DefaultReadTimeout;

    const string Command = "gw_tuning";

    public async Task<string> ReadAsync(CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(readTimeout);
        try
        {
            return await LiquidsoapTelnet.SendAsync(options.Host, options.Port, Command, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // timeoutCts fired, not the caller's own ct — surface a specific, named failure rather
            // than a bare OperationCanceledException the caller might mistake for its own cancel.
            throw new TimeoutException($"{Command}: no reply within {readTimeout}.");
        }
    }
}

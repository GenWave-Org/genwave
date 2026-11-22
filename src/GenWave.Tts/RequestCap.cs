namespace GenWave.Tts;

/// <summary>
/// One request's own time cap, run on the client's <see cref="TimeProvider"/> so a fake clock can drive it.
/// <see cref="Token"/> fires when the cap passes or when the caller's token does.
/// </summary>
sealed class RequestCap : IDisposable
{
    readonly CancellationTokenSource cap;
    readonly CancellationTokenSource linked;

    public RequestCap(TimeSpan after, TimeProvider time, CancellationToken caller)
    {
        After = after;
        cap = new CancellationTokenSource(after, time);
        linked = CancellationTokenSource.CreateLinkedTokenSource(caller, cap.Token);
    }

    /// <summary>How long the request was allowed.</summary>
    public TimeSpan After { get; }

    /// <summary>True once the cap itself has passed, whether or not the caller's token fired as well.</summary>
    public bool TimedOut => cap.IsCancellationRequested;

    public CancellationToken Token => linked.Token;

    public void Dispose()
    {
        linked.Dispose();
        cap.Dispose();
    }
}

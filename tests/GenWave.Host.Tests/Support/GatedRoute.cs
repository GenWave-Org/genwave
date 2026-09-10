namespace GenWave.Host.Tests.Support;

/// <summary>
/// A single <see cref="AdScriptCompletionsRouter"/> route that proves at-most-one-caller-in-flight
/// rather than merely answering (STORY-422 AC1, PLAN T441) — every request routed here increments a
/// live counter on entry, records the running high-water mark into <see cref="MaxConcurrency"/>, then
/// blocks on <see cref="Release"/> before answering, so an Arc dispatching two jobs at the SAME
/// gated sponsor can observe whether the writer itself ever saw them overlap, independent of whatever
/// the station's own on-air signal reports.
/// </summary>
internal sealed class GatedRoute
{
    readonly Lock gate = new();
    readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    int inFlight;

    /// <summary>How many completion requests have entered this route so far, whether or not they have
    /// been released yet.</summary>
    public int RequestsSeen { get; private set; }

    /// <summary>The highest number of requests this route ever held inside it AT ONCE, between entry
    /// and <see cref="Release"/> — one iff no two callers this route ever answered were ever in flight
    /// at the same instant.</summary>
    public int MaxConcurrency { get; private set; }

    public async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request)
    {
        lock (gate)
        {
            RequestsSeen++;
            inFlight++;
            MaxConcurrency = Math.Max(MaxConcurrency, inFlight);
        }

        await release.Task;

        lock (gate)
        {
            inFlight--;
        }

        return AdScriptCompletionsRouter.WellFormedResponse();
    }

    /// <summary>Releases every request currently held (and every future one, since this route answers
    /// once for all callers) — an Arc calls this once it has captured whatever concurrency it needed
    /// to observe.</summary>
    public void Release() => release.TrySetResult();
}

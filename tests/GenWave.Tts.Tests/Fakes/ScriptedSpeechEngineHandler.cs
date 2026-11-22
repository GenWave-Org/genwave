namespace GenWave.Tts.Tests.Fakes;

using System.Net;

/// <summary>
/// A single-threaded speech engine as the voice tile sees it: while a render is held open it answers no
/// <c>OPTIONS</c> ping (the ping times out); otherwise it answers. Renders and pings are counted per host.
/// </summary>
public sealed class ScriptedSpeechEngineHandler : HttpMessageHandler
{
    readonly Lock gate = new();
    readonly Dictionary<string, int> renders = new(StringComparer.Ordinal);
    readonly Dictionary<string, int> pings = new(StringComparer.Ordinal);
    readonly Dictionary<string, TaskCompletionSource<HttpStatusCode>> held = new(StringComparer.Ordinal);
    readonly HashSet<string> timeOutNext = new(StringComparer.Ordinal);
    readonly HashSet<string> busyHosts = new(StringComparer.Ordinal);
    readonly Dictionary<string, TaskCompletionSource> started = new(StringComparer.Ordinal);

    public int Renders(string host) => Count(renders, host);

    public int Pings(string host) => Count(pings, host);

    /// <summary>Makes the next render on <paramref name="host"/> wait until <see cref="Finish"/>; the engine is busy meanwhile.</summary>
    public Task HoldNextRender(string host)
    {
        var begun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            held[host] = new TaskCompletionSource<HttpStatusCode>(TaskCreationOptions.RunContinuationsAsynchronously);
            started[host] = begun;
        }

        return begun.Task;
    }

    /// <summary>Makes the next render on <paramref name="host"/> end as an HttpClient timeout (the caller's own token stays live).</summary>
    public void TimeOutNextRender(string host)
    {
        lock (gate)
            timeOutNext.Add(host);
    }

    /// <summary>Ends the held render on <paramref name="host"/> with <paramref name="status"/>.</summary>
    public void Finish(string host, HttpStatusCode status)
    {
        lock (gate)
        {
            if (held.TryGetValue(host, out var hold))
                hold.TrySetResult(status);
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var host = request.RequestUri?.Host ?? string.Empty;
        if (request.Method == HttpMethod.Options)
        {
            Bump(pings, host);
            bool busy;
            lock (gate)
                busy = busyHosts.Contains(host);
            if (busy)
                throw new TaskCanceledException("The ping timed out: the engine is rendering.");
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        Bump(renders, host);
        bool timeOut;
        lock (gate)
            timeOut = timeOutNext.Remove(host);
        if (timeOut)
            throw new TaskCanceledException("The request timed out.", new TimeoutException());

        TaskCompletionSource<HttpStatusCode>? pending;
        TaskCompletionSource? begun;
        lock (gate)
        {
            held.TryGetValue(host, out pending);
            started.Remove(host, out begun);
            if (pending is not null)
                busyHosts.Add(host);
        }

        var status = HttpStatusCode.OK;
        if (pending is not null)
        {
            begun?.TrySetResult();
            try
            {
                status = await pending.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                lock (gate)
                {
                    busyHosts.Remove(host);
                    held.Remove(host);
                }
            }
        }

        return new HttpResponseMessage(status) { Content = new ByteArrayContent([1, 2, 3, 4]) };
    }

    void Bump(Dictionary<string, int> counts, string host)
    {
        lock (gate)
            counts[host] = counts.GetValueOrDefault(host) + 1;
    }

    int Count(Dictionary<string, int> counts, string host)
    {
        lock (gate)
            return counts.GetValueOrDefault(host);
    }
}

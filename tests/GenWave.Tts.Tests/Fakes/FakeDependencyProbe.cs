namespace GenWave.Tts.Tests.Fakes;

using GenWave.Tts;

/// <summary>
/// Controllable <see cref="IDependencyProbe"/> test double (STORY-187): counts invocations so
/// specs can assert cadence and read-purity without any real network call, and can simulate a
/// hanging dependency (honors <see cref="ProbeAsync"/>'s own cancellation token) to exercise the
/// per-probe timeout path.
/// <para>
/// <see cref="Hang"/> is settable mid-run (gh-#125) so a spec can drive a fail → recover → fail
/// sequence against one live prober — the flap shape the F70.2 AC5 debounce exists to absorb.
/// </para>
/// </summary>
public sealed class FakeDependencyProbe(string dependencyName, bool healthy, bool hang = false) : IDependencyProbe
{
    int callCount;

    public string DependencyName => dependencyName;

    /// <summary>
    /// Whether the next probe hangs until its own timeout fires. Written from a spec thread while
    /// a <c>RunAsync</c> loop reads it, hence <see cref="Volatile"/> on both sides.
    /// </summary>
    public bool Hang
    {
        get => Volatile.Read(ref hang);
        set => Volatile.Write(ref hang, value);
    }

    /// <summary>Interlocked: <c>RunAsync</c> increments this off the spec's thread.</summary>
    public int CallCount => Volatile.Read(ref callCount);

    public async Task<bool> ProbeAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref callCount);
        if (Hang)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
        return healthy;
    }
}

namespace GenWave.Tts.Tests.Fakes;

using GenWave.Tts;

/// <summary>
/// Controllable <see cref="IDependencyProbe"/> test double (STORY-187): counts invocations so
/// specs can assert cadence and read-purity without any real network call, and can simulate a
/// hanging dependency (honors <see cref="ProbeAsync"/>'s own cancellation token) to exercise the
/// per-probe timeout path.
/// </summary>
public sealed class FakeDependencyProbe(string dependencyName, bool healthy, bool hang = false) : IDependencyProbe
{
    public string DependencyName => dependencyName;

    public int CallCount { get; private set; }

    public async Task<bool> ProbeAsync(CancellationToken ct)
    {
        CallCount++;
        if (hang)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
        return healthy;
    }
}

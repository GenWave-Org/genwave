namespace GenWave.Tts.Tests.Fakes;

using GenWave.Tts;

/// <summary>
/// Controllable <see cref="IDependencyHealth"/> double (STORY-188) — no probe ever runs; a spec
/// sets exactly the cached verdict <see cref="DegradationController"/> should see for a given
/// dependency name.
/// </summary>
public sealed class FakeDependencyHealth : IDependencyHealth
{
    readonly Dictionary<string, DependencyHealthVerdict> verdicts = new(StringComparer.Ordinal);

    public void Set(DependencyHealthVerdict verdict) => verdicts[verdict.DependencyName] = verdict;

    public DependencyHealthVerdict? GetVerdict(string dependencyName) =>
        verdicts.GetValueOrDefault(dependencyName);
}

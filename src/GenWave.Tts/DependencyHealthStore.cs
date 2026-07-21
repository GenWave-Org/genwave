namespace GenWave.Tts;

using System.Collections.Concurrent;

/// <summary>
/// The single in-memory verdict cache (SPEC F70.2, STORY-187). <see cref="DependencyHealthProber"/>
/// writes one verdict per dependency per probe cycle via <see cref="Record"/>; every other caller
/// reads through <see cref="IDependencyHealth"/>. Registered as a singleton and exposed under
/// both surfaces from the one instance — mirrors how <c>NormalizingTtsSynthesizer</c> and
/// <c>LlmCopyWriter</c> are registered concretely once and exposed under every interface they
/// implement (<see cref="TtsServiceCollectionExtensions"/>).
/// </summary>
public sealed class DependencyHealthStore : IDependencyHealth
{
    readonly ConcurrentDictionary<string, DependencyHealthVerdict> verdicts = new(StringComparer.Ordinal);

    public DependencyHealthVerdict? GetVerdict(string dependencyName) =>
        verdicts.GetValueOrDefault(dependencyName);

    /// <summary>
    /// Records the outcome of one probe for <paramref name="dependencyName"/>.
    /// <paramref name="reason"/> must be null exactly when <paramref name="healthy"/> is true.
    /// <see cref="DependencyHealthVerdict.ConsecutiveFailureCount"/> resets to 0 on a healthy
    /// verdict and increments on every unhealthy verdict in a row.
    /// </summary>
    public void Record(string dependencyName, bool healthy, string? reason)
    {
        verdicts.AddOrUpdate(
            dependencyName,
            addValueFactory: _ => new DependencyHealthVerdict(
                dependencyName, healthy, DateTimeOffset.UtcNow, reason, healthy ? 0 : 1),
            updateValueFactory: (_, prior) => new DependencyHealthVerdict(
                dependencyName, healthy, DateTimeOffset.UtcNow, reason,
                healthy ? 0 : prior.ConsecutiveFailureCount + 1));
    }
}

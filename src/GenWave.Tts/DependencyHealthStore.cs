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
    /// probe and increments on every failed probe in a row.
    /// <para>
    /// <paramref name="unhealthyThreshold"/> debounces the published verdict (SPEC F70.2 AC5,
    /// gh-#125): the stored <see cref="DependencyHealthVerdict.Healthy"/> stays true until this
    /// many probes have failed <em>in a row</em>. It defaults to 1 — flip on the first failure,
    /// the original F70.2 behavior — so every caller that does not opt in is unchanged.
    /// </para>
    /// <para>
    /// The two properties deliberately answer different questions, and only this method knows
    /// both: <see cref="DependencyHealthVerdict.ConsecutiveFailureCount"/> is the raw observation
    /// ("how many probes in a row have failed"), while
    /// <see cref="DependencyHealthVerdict.Healthy"/> is the debounced conclusion ("do we believe
    /// it is down"). Keeping the count raw is what lets the threshold be re-read live on the very
    /// next probe without the counter needing to be rebased. The
    /// "<see cref="DependencyHealthVerdict.Reason"/> is null exactly when
    /// <see cref="DependencyHealthVerdict.Healthy"/> is true" invariant is preserved: a
    /// sub-threshold failure publishes a healthy verdict and therefore drops its reason (the
    /// prober logs it instead).
    /// </para>
    /// </summary>
    /// <returns>
    /// The verdict as stored, so a caller that must react to the debounced outcome (the prober,
    /// choosing a log level for a failure that has not yet flipped the verdict) reads it from the
    /// same atomic update rather than racing a second <see cref="GetVerdict"/> against a
    /// concurrent probe. This mirrors <see cref="ConcurrentDictionary{TKey,TValue}.AddOrUpdate"/>
    /// itself, which is likewise a mutation that hands back the resulting value.
    /// </returns>
    public DependencyHealthVerdict Record(
        string dependencyName, bool healthy, string? reason, int unhealthyThreshold = 1)
    {
        var threshold = Math.Max(1, unhealthyThreshold);

        return verdicts.AddOrUpdate(
            dependencyName,
            addValueFactory: _ => Build(healthy ? 0 : 1),
            updateValueFactory: (_, prior) => Build(healthy ? 0 : prior.ConsecutiveFailureCount + 1));

        DependencyHealthVerdict Build(int failures)
        {
            var publishedHealthy = failures < threshold;
            return new DependencyHealthVerdict(
                dependencyName,
                publishedHealthy,
                DateTimeOffset.UtcNow,
                publishedHealthy ? null : reason,
                failures);
        }
    }
}

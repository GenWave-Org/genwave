namespace GenWave.Tts;

/// <summary>
/// One snapshot of the dependency-probe tuning knobs (SPEC F70.2 AC1/AC3/AC5), read fresh by
/// <see cref="DependencyHealthProber"/> on every cycle rather than frozen at boot — so an operator
/// edit through the settings API reaches the very next probe with no api restart (gh-#125).
/// <para>
/// This lives in GenWave.Tts, not the Host, deliberately: the prober must not take a dependency on
/// <c>GenWave.Host.Options.DependencyHealthOptions</c> (nor on <c>IOptionsMonitor</c>), so the Host
/// supplies a <c>Func&lt;DependencyProbeCadence&gt;</c> that closes over its own monitor and the
/// prober stays a plain, host-free unit under test.
/// </para>
/// </summary>
/// <param name="Interval">Time between probe cycles.</param>
/// <param name="PerProbeTimeout">Budget for a single probe before it counts as a timeout.</param>
/// <param name="UnhealthyThreshold">
/// How many probes must fail in a row before the cached verdict flips unhealthy. 1 restores the
/// original flip-on-first-failure behavior; the shipped default is 2.
/// </param>
public sealed record DependencyProbeCadence(
    TimeSpan Interval,
    TimeSpan PerProbeTimeout,
    int UnhealthyThreshold);

namespace GenWave.Host.Engine;

/// <summary>
/// The engine-settings verdict check (SPEC F213.5–F213.8, STORY-488, PLAN T602): reads
/// <see cref="IEngineTuningReader"/>, computes the verdict against the SAME effective configuration
/// <c>/internal/engine-config</c> serves (<see cref="EngineTuningKeys.ReadEffective"/>), publishes it
/// to <see cref="EngineSettingsStatus"/>, and logs the transition. Owns its own cadence loop
/// (<see cref="RunAsync"/>) — mirroring <see cref="GenWave.Tts.DependencyHealthProber"/>'s own split
/// from its BackgroundService shell (<c>EngineSettingsCheckService</c> is a thin timer/try-catch
/// wrapper one seam over, exactly like <c>DependencyHealthProbeService</c>) — so the cadence/never-
/// throws contract is unit-testable directly, without spinning a host (the aspnetcore-patterns house
/// rule).
/// <para>
/// Never throws except the caller's own cancellation: <see cref="EngineSettingsVerdict.ComputeAsync"/>
/// already maps every reader failure to <see cref="EngineSettingsState.Unknown"/>, so there is
/// nothing left here that can fault the host's probe loop (SPEC F213.6).
/// </para>
/// </summary>
sealed class EngineSettingsCheck(
    IEngineTuningReader reader,
    IConfiguration configuration,
    EngineSettingsStatus status,
    TimeProvider timeProvider,
    ILogger<EngineSettingsCheck> logger)
{
    /// <summary>
    /// The state this check last logged a transition FROM. <see langword="null"/> before the first
    /// non-Unknown verdict. <see cref="EngineSettingsState.Unknown"/> is deliberately never stored
    /// here (see <see cref="RunOnceAsync"/>) — an Unknown tick is a no-op for transition-logging
    /// purposes, not a state of its own.
    /// </summary>
    EngineSettingsState? previous;

    /// <summary>
    /// Probes once immediately — so a verdict exists as soon as possible after boot rather than
    /// only after the first full interval elapses (AC11) — then again on the cadence
    /// <paramref name="interval"/> reports, until <paramref name="ct"/> is cancelled.
    /// <para>
    /// <paramref name="interval"/> is a delegate, not a value, so it is re-read once per cycle and a
    /// live <c>DependencyHealth:ProbeIntervalSeconds</c> edit governs the very next tick with no api
    /// restart (mirrors <see cref="GenWave.Tts.DependencyHealthProber.RunAsync"/>'s own delegate
    /// cadence). The timer is retuned via <see cref="PeriodicTimer.Period"/> AFTER each cycle, so a
    /// changed interval never disturbs the tick that just completed.
    /// </para>
    /// <para>
    /// The <see cref="PeriodicTimer"/> is constructed against the injected <see cref="TimeProvider"/>
    /// (never <see cref="TimeProvider.System"/> read directly), so a spec can drive this loop
    /// cycle-by-cycle with a <c>FakeTimeProvider</c> instead of racing wall-clock sleeps against it —
    /// the SAME reliable hook <see cref="GenWave.Tts.DependencyHealthProber.RunAsync"/> already
    /// proves out.
    /// </para>
    /// </summary>
    public async Task RunAsync(Func<TimeSpan> interval, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval(), timeProvider);
        do
        {
            await RunOnceAsync(ct).ConfigureAwait(false);
            timer.Period = interval();
        }
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
    }

    /// <summary>
    /// Runs one probe cycle: read, compute, publish, log. Exposed separately from
    /// <see cref="RunAsync"/> so a test (or a caller with its own cadence) can drive exactly N cycles
    /// without waiting on real or faked time.
    /// </summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        var effective = EngineTuningKeys.ReadEffective(configuration);
        var verdict = await EngineSettingsVerdict.ComputeAsync(reader, effective, ct).ConfigureAwait(false);

        status.Set(verdict);
        LogTransition(verdict);
    }

    /// <summary>
    /// Logs exactly one WARN on entering <see cref="EngineSettingsState.RestartNeeded"/> (naming the
    /// differing keys and the fix, AC7) and exactly one INFO on returning to
    /// <see cref="EngineSettingsState.InSync"/> (AC8) — nothing on <see cref="EngineSettingsState.Unknown"/>
    /// (AC9's sad path) and nothing on a repeat tick that stays in the same state.
    /// <para>
    /// Two edge cases the spec leaves open, decided here:
    /// </para>
    /// <list type="bullet">
    /// <item>RestartNeeded → RestartNeeded with a DIFFERENT set of differing keys does NOT re-warn.
    /// The fix is identical either way (<c>docker compose restart engine</c>), and re-announcing an
    /// outage that never resolved on every keyset change would be exactly the noisy-repeat pattern
    /// <see cref="GenWave.Tts.DependencyHealthProber"/> was fixed (gh-#338) to avoid — the current
    /// differing keys are always visible on <c>/api/status</c> regardless, so nothing is lost by
    /// staying silent.</item>
    /// <item>RestartNeeded → Unknown → RestartNeeded (a flapping engine) does NOT re-warn either. An
    /// Unknown tick never updates <see cref="previous"/>, so from this method's point of view the
    /// flap back to RestartNeeded is indistinguishable from having stayed there the whole time — the
    /// least noisy reading of a transient probe failure sandwiched between two identical verdicts.</item>
    /// </list>
    /// </summary>
    void LogTransition(EngineSettingsVerdict verdict)
    {
        if (verdict.State == EngineSettingsState.Unknown)
        {
            // A no-op for transition purposes: previous is left exactly as it was (see the flapping
            // decision above), and nothing new is logged (AC9).
            return;
        }

        if (verdict.State == EngineSettingsState.RestartNeeded && previous != EngineSettingsState.RestartNeeded)
        {
            logger.LogWarning(
                "Engine settings out of sync with configuration: {Keys} — run `docker compose restart engine` to apply",
                string.Join(", ", verdict.Differs));
        }
        else if (verdict.State == EngineSettingsState.InSync && previous == EngineSettingsState.RestartNeeded)
        {
            logger.LogInformation("Engine settings back in sync with configuration");
        }

        previous = verdict.State;
    }
}

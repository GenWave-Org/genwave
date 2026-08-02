// gh-#338 — the unhealthy-verdict warning re-fired on every probe cycle instead of on the flip.
//
// BDD specification — xUnit. Drives DependencyHealthProber.RunCycleAsync directly, one cycle at a
// time, so every fact here is about WHICH cycle logged WHAT — no timers, no clock, no waiting.
//
// The field symptom (Pi 5, v2.9.0, --pinned --piper-only): kokoro is profile-disabled by the
// overlay, so its probe can never succeed, and the api logged
//
//     warn: kokoro health probe failed (Name or service not known (kokoro:8880))
//           — 29 consecutive failures, cached verdict is now unhealthy
//           [full stack trace]
//
// every 30s, forever. The verdict had flipped at failure 2; failure 29 changed nothing, and the
// wording claimed a transition each time. At the F70.2 default cadence that is ~2,880 warnings a
// day, each carrying an identical stack trace, for a condition reported once and permanent by
// construction.

using GenWave.Tts;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureProbeWarningIsEdgeTriggered
{
    const string Dep = "kokoro";

    /// <summary>The field failure: DNS has no such host, so the probe throws rather than timing out.</summary>
    static Exception Nxdomain() => new HttpRequestException("Name or service not known (kokoro:8880)");

    static DependencyProbeCadence Cadence(int threshold) =>
        new(TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(5), UnhealthyThreshold: threshold);

    sealed class FlippableProbe(string name) : IDependencyProbe
    {
        public string DependencyName => name;

        /// <summary>Non-null makes the probe throw — a connect failure, not a "false" declaration.</summary>
        public Exception? Fault { get; set; }

        /// <summary>Used only when <see cref="Fault"/> is null. False = "not configured" (F34.2).</summary>
        public bool Healthy { get; set; } = true;

        public Task<bool> ProbeAsync(CancellationToken ct) =>
            Fault is not null ? Task.FromException<bool>(Fault) : Task.FromResult(Healthy);
    }

    sealed record LogLine(LogLevel Level, string Message, Exception? Exception);

    sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogLine> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Lines.Add(new LogLine(level, formatter(state, exception), exception));
    }

    sealed record Harness(FlippableProbe Probe, DependencyHealthStore Store,
                          DependencyHealthProber Prober, RecordingLogger<DependencyHealthProber> Log)
    {
        public async Task CyclesAsync(int count, int threshold = 2)
        {
            for (var i = 0; i < count; i++)
                await Prober.RunCycleAsync(Cadence(threshold), CancellationToken.None);
        }

        public IReadOnlyList<LogLine> At(LogLevel level) => Log.Lines.Where(l => l.Level == level).ToList();
    }

    static Harness NewHarness()
    {
        var probe = new FlippableProbe(Dep);
        var store = new DependencyHealthStore();
        var log = new RecordingLogger<DependencyHealthProber>();
        return new Harness(probe, store, new DependencyHealthProber([probe], store, log), log);
    }

    // ---------------------------------------------------------------------
    // A permanently absent dependency is reported once, not forever
    // ---------------------------------------------------------------------

    public static class ScenarioADependencyThatCanNeverRecover
    {
        [Fact]
        public static async Task Thirty_cycles_of_the_same_failure_produce_exactly_one_warning()
        {
            // gh-#338 itself, at the scale that surfaced it. Thirty cycles is roughly the 29
            // failures the Pi logged before anyone looked.
            var h = NewHarness();
            h.Probe.Fault = Nxdomain();

            await h.CyclesAsync(30);

            Assert.Single(h.At(LogLevel.Warning));
        }

        [Fact]
        public static async Task The_one_warning_is_the_cycle_that_actually_flipped_the_verdict()
        {
            // Threshold 2: cycle 1 is sub-threshold (verdict still healthy), cycle 2 flips it.
            var h = NewHarness();
            h.Probe.Fault = Nxdomain();

            await h.CyclesAsync(1);
            Assert.Empty(h.At(LogLevel.Warning));
            Assert.True(h.Store.GetVerdict(Dep)!.Healthy);

            await h.CyclesAsync(1);
            Assert.Single(h.At(LogLevel.Warning));
            Assert.False(h.Store.GetVerdict(Dep)!.Healthy);
        }

        [Fact]
        public static async Task Repeat_failures_after_the_flip_log_at_debug()
        {
            var h = NewHarness();
            h.Probe.Fault = Nxdomain();

            await h.CyclesAsync(10);

            // 1 sub-threshold + 8 post-flip repeats. Debug does not reach the fleet log sink, so
            // this is what takes 2,880 warnings/day down to one.
            Assert.Equal(9, h.At(LogLevel.Debug).Count);
        }

        [Fact]
        public static async Task The_post_flip_repeats_carry_no_stack_trace()
        {
            // The trace is identical on every cycle and the reason string already names the cause,
            // so the repeats drop it. The WARNING keeps it — that is the one report of the
            // incident — and so does the sub-threshold Debug, which is the first observation of a
            // real problem and is bounded at threshold-1 occurrences per outage (default: one).
            // Only the unbounded stream is stripped.
            var h = NewHarness();
            h.Probe.Fault = Nxdomain();

            await h.CyclesAsync(10);

            Assert.NotNull(Assert.Single(h.At(LogLevel.Warning)).Exception);

            var repeats = h.At(LogLevel.Debug)
                .Where(l => l.Message.Contains("still failing", StringComparison.Ordinal))
                .ToList();
            Assert.Equal(8, repeats.Count);
            Assert.All(repeats, line => Assert.Null(line.Exception));
        }
    }

    // ---------------------------------------------------------------------
    // The implementation trap: suppression keys on the EDGE, not on the state
    // ---------------------------------------------------------------------

    public static class ScenarioADependencyThatDropsRecoversAndDropsAgain
    {
        [Fact]
        public static async Task The_second_outage_warns_again()
        {
            // The whole reason this suppression cannot key on "is the verdict unhealthy": a real
            // dependency that drops, comes back and drops again must page twice. Keying on state
            // instead of the edge would silently swallow the second outage.
            var h = NewHarness();

            h.Probe.Fault = Nxdomain();
            await h.CyclesAsync(2);
            Assert.Single(h.At(LogLevel.Warning));

            h.Probe.Fault = null;
            await h.CyclesAsync(1);

            h.Probe.Fault = Nxdomain();
            await h.CyclesAsync(2);

            Assert.Equal(2, h.At(LogLevel.Warning).Count);
        }

        [Fact]
        public static async Task Recovery_is_announced_once_at_information()
        {
            // The up-edge was previously logged nowhere at all: an operator saw "kokoro unhealthy"
            // and never learned it came back. One line, on the transition only.
            var h = NewHarness();

            h.Probe.Fault = Nxdomain();
            await h.CyclesAsync(2);

            h.Probe.Fault = null;
            await h.CyclesAsync(5);

            var recovered = Assert.Single(h.At(LogLevel.Information));
            Assert.Contains("recovered", recovered.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public static async Task A_healthy_dependency_never_logs_anything()
        {
            var h = NewHarness();

            await h.CyclesAsync(20);

            Assert.Empty(h.Log.Lines);
        }
    }

    // ---------------------------------------------------------------------
    // "Not configured" is a declaration, not a fault
    // ---------------------------------------------------------------------

    public static class ScenarioADependencyDisabledByDesign
    {
        [Fact]
        public static async Task A_probe_returning_false_never_warns()
        {
            // An empty Llm:Endpoint (SPEC F34.2) is the documented disabled state and repeats
            // identically forever — exactly the shape that must not reach the warning stream.
            var h = NewHarness();
            h.Probe.Healthy = false;

            await h.CyclesAsync(20);

            Assert.Empty(h.At(LogLevel.Warning));
        }

        [Fact]
        public static async Task Becoming_configured_does_not_announce_a_recovery_nobody_was_told_about()
        {
            // The recovery line must be paired with a warning that actually fired. A lone
            // "recovered" for an outage never reported reads as noise at best and as a phantom
            // incident at worst.
            var h = NewHarness();
            h.Probe.Healthy = false;
            await h.CyclesAsync(5);

            h.Probe.Healthy = true;
            await h.CyclesAsync(5);

            Assert.Empty(h.At(LogLevel.Information));
        }
    }

    // ---------------------------------------------------------------------
    // The edge survives a threshold changed mid-outage
    // ---------------------------------------------------------------------

    public static class ScenarioTheThresholdIsRetunedWhileTheDependencyIsDown
    {
        [Fact]
        public static async Task Lowering_the_threshold_mid_outage_still_warns_on_the_flip()
        {
            // Why the edge is tracked rather than derived from ConsecutiveFailureCount ==
            // Threshold: the threshold is re-read live every cycle (gh-#125), so an operator
            // lowering it during an outage flips the verdict at a count already PAST the new
            // threshold. Count-vs-threshold arithmetic logs that flip at Debug and loses it.
            var h = NewHarness();
            h.Probe.Fault = Nxdomain();

            // Four failures under a threshold of 10 — verdict stays healthy, nothing warned.
            await h.CyclesAsync(4, threshold: 10);
            Assert.Empty(h.At(LogLevel.Warning));
            Assert.True(h.Store.GetVerdict(Dep)!.Healthy);

            // Operator drops it to 2. The next probe flips the verdict at count 5 — well past 2.
            await h.CyclesAsync(1, threshold: 2);

            Assert.False(h.Store.GetVerdict(Dep)!.Healthy);
            Assert.Single(h.At(LogLevel.Warning));
        }
    }
}

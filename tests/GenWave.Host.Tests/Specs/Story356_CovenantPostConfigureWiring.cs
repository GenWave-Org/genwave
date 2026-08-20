// STORY-356 — The boundary covenant holds by construction (SPEC F142 · PLAN T327) — the Host-side
// wiring half. The pure math/spec half lives in
// Orchestration.Tests/Specs/Story356_BoundaryCadenceCovenant.cs.
//
// BDD specification — xUnit. T327 review round-3 FAIL-3: zero Host-side coverage existed for
// BoundaryCadenceCovenantPostConfigure (IPostConfigureOptions<StationOptions>) — this file is that
// coverage. Mirrors Story321_TimeAnnouncementBudgetSecondsValidation.cs's own
// ValidOptions()/BuildStationOptionsValidator() direct-construction idiom: no
// WebApplicationFactory, no DI container — PostConfigure and Validate are invoked directly against
// the SAME StationOptions instance, in the SAME order the framework itself runs them (PostConfigure
// runs before every IValidateOptions<StationOptions>, including StationOptionsValidator — see
// BoundaryCadenceCovenantPostConfigure's own remarks for why).

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Host.Options;

namespace GenWave.Host.Tests.Specs;

public static class FeatureCovenantPostConfigureWiring
{
    /// <summary>A minimally-valid StationOptions instance for direct PostConfigure/Validate construction.</summary>
    static StationOptions ValidOptions() => new()
    {
        Id    = "s1",
        Name  = "GenWave",
        Voice = "af_heart",
        Scope = new StationScopeOptions { LibraryIds = [1L] },
    };

    // 3s mirrors PlayoutFeederService.PullInterval, the value Program.cs actually wires in — pinned
    // here as a literal (not a cross-namespace read) so this file exercises the same construction
    // shape production code does without reaching into GenWave.Host.Playout for it.
    static BoundaryCadenceCovenantPostConfigure BuildPostConfigure(ILogger<BoundaryCadenceCovenantPostConfigure> logger) =>
        new(logger, TimeSpan.FromSeconds(3));

    static StationOptionsValidator BuildStationOptionsValidator() =>
        new(NullLogger<StationOptionsValidator>.Instance);

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public sealed class ScenarioANegativeLookaheadPassesThroughThenFailsValidation
    {
        [Fact]
        public void PostConfigureLeavesItByteUnchangedThenValidateFailsOnTheSameInstance()
        {
            var options = ValidOptions();
            options.BoundaryBias.LookaheadMinutes = -1;

            BuildPostConfigure(NullLogger<BoundaryCadenceCovenantPostConfigure>.Instance)
                .PostConfigure(null, options);

            // Asserted in THIS order because the framework runs them in this order: PostConfigure
            // first, on every OptionsFactory<StationOptions>.Create call, before ANY
            // IValidateOptions<StationOptions> (see BoundaryCadenceCovenantPostConfigure's own
            // remarks). A negative LookaheadMinutes is StationOptionsValidator's own error to raise
            // — PostConfigure's own guard must leave it byte-unchanged so Validate still gets the
            // chance to reject it below, rather than silently repairing the very misconfiguration
            // that guard exists to catch.
            Assert.Equal(-1, options.BoundaryBias.LookaheadMinutes);

            var result = BuildStationOptionsValidator().Validate(null, options);

            Assert.True(result.Failed);
            Assert.Contains(
                "Station:BoundaryBias:LookaheadMinutes must be non-negative",
                result.FailureMessage ?? string.Empty,
                StringComparison.Ordinal);
        }
    }

    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioTheShippedDefaultRoundTripsUnchanged
    {
        [Fact]
        public void PostConfigureLeavesTheDefaultUnchangedAndLogsNothing()
        {
            var options = ValidOptions();
            Assert.Equal(10, options.BoundaryBias.LookaheadMinutes);   // StationBoundaryBiasOptions' own shipped default

            var logger = new CapturingLogger<BoundaryCadenceCovenantPostConfigure>();
            BuildPostConfigure(logger).PostConfigure(null, options);

            // 15s SignOffLeadTime + 3s pull gap = 18s required, ceiled to the knob's own one-minute
            // grain: 60s. The shipped 10-minute default comfortably covers that — a covenant no-op
            // (BoundaryCadenceCovenant's own remarks) — so nothing here should move, and nothing
            // should log.
            Assert.Equal(10, options.BoundaryBias.LookaheadMinutes);
            Assert.Empty(logger.Entries);
        }
    }
}

/// <summary>
/// Minimal <see cref="ILogger{T}"/> that collects every logged message for assertion — this file's
/// "no log line emitted" fact needs a POSITIVE assertion (zero entries), which
/// <see cref="NullLogger{T}"/> alone can't give since it discards without recording. Test-scope only.
/// </summary>
file sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add((logLevel, formatter(state, exception)));
    }
}

// STORY-442 — Time-budget facts run on a fake clock (gh-#723 · SPEC F181.3 · PLAN T482/T483)
//
// BDD specification — xUnit. AC3/AC4: the Story243 production chain (FeatureDjsHandOffAudibly's
// harness, opened to this assembly) with the per-unit render budget and the fake TTS's RenderDelay
// BOTH riding chain.Time — the outcome is decided by due order (budget due at +10 ms vs render due at
// +200 ms, or render due at +5 ms vs budget at +10 ms), never by which wall-clock timer happens to
// fire first on a cold runner. A wall-clock ceiling on the whole scenario proves no real waiting.
// AC1/AC2 (no hand-rolled fake, the package is referenced) are pins in GenWave.Architecture.Tests
// (Story442_FakeClockPins.cs); AC5/AC6 are GenWave.Ads.Tests' (Story442_AdReaskBudgetOnAFakeClock.cs).
//
// GREEN as of T483: FakeTtsSegmentSource.RenderDelay now takes the same TimeProvider as
// Orchestrator's per-unit render budget (both ride chain.Time, wired in
// FeatureDjsHandOffAudibly.BuildProductionChain) — the race between them is decided purely by fake
// clock due order, never by which wall-clock timer happens to fire first on a cold runner.

using System.Diagnostics;
using GenWave.Core.Domain;
using static GenWave.Orchestration.Tests.Specs.FeatureDjsHandOffAudibly;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureHandoffBudgetOnAFakeClock
{
    static readonly TimeSpan WallClockCeiling = TimeSpan.FromMilliseconds(500);

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheRenderIsDueFirstSoBothPiecesAir : IAsyncLifetime
    {
        List<MediaItem> items = [];
        TimeSpan elapsed;

        public async Task InitializeAsync()
        {
            var renderBudget = TimeSpan.FromMilliseconds(10);
            var renderDelay = TimeSpan.FromMilliseconds(5); // due before the 10 ms budget
            var chain = BuildProductionChain(
                TwoDjStore(), TwoDjSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10),
                renderBudget: renderBudget);
            chain.Tts.RenderDelay = renderDelay;

            var clock = Stopwatch.StartNew();
            items = await PullUnitsRacingTheRenderBudgetAsync(
                chain.Orchestrator, chain.Time, PullStep, PullCount, renderDelay, renderBudget);
            elapsed = clock.Elapsed;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSignOffAirs() => Assert.Contains(items, IsSignOff);

        [Fact]
        public void TheSignOnAirs() => Assert.Contains(items, IsSignOn);

        [Fact]
        public void NoWallClockWaitHappened() => Assert.True(elapsed < WallClockCeiling, $"took {elapsed}");
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the budget is due first
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheBudgetIsDueFirstSoBothPiecesDrop : IAsyncLifetime
    {
        List<MediaItem> items = [];
        TimeSpan elapsed;

        public async Task InitializeAsync()
        {
            var renderBudget = TimeSpan.FromMilliseconds(10);
            var renderDelay = TimeSpan.FromMilliseconds(200); // due long after the 10 ms budget
            var chain = BuildProductionChain(
                TwoDjStore(), TwoDjSchedule(), JustBeforeNoon, TimeSpan.FromMinutes(10),
                renderBudget: renderBudget);
            chain.Tts.RenderDelay = renderDelay;

            var clock = Stopwatch.StartNew();
            items = await PullUnitsRacingTheRenderBudgetAsync(
                chain.Orchestrator, chain.Time, PullStep, PullCount, renderDelay, renderBudget);
            elapsed = clock.Elapsed;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSignOffIsDropped() => Assert.DoesNotContain(items, IsSignOff);

        [Fact]
        public void TheSignOnIsDropped() => Assert.DoesNotContain(items, IsSignOn);

        [Fact]
        public void NoWallClockWaitHappened() => Assert.True(elapsed < WallClockCeiling, $"took {elapsed}");
    }
}

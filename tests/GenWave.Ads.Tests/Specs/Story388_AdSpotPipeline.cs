// STORY-388 — An ad airs every N units, from whichever source answers first (F158.2, F158.5 · PLAN
// T396)
//
// BDD specification — xUnit, pure unit tests over fakes (no live stack). AC3 (first non-null wins,
// floor last) and AC6 (a throwing source is skipped) were originally seeded as pending facts in
// GenWave.Orchestration.Tests/Specs/Story388_AdCadenceAndPipeline.cs — MOVED here at T396 because the
// pipeline they exercise (AdSpotPipeline) lives in GenWave.Ads, which GenWave.Orchestration.Tests
// does not (and should not) reference; the story tag travels with them. AC4 (anti-repeat) lives
// beside LibraryAdSpotSource instead — see Story388_LibraryAdSpotSource.cs — since the ring itself is
// that class's own state, not the pipeline's. What stays behind in Orchestration.Tests
// (ScenarioTheCadenceTriggersAVend, the zero/empty-pipeline sad paths) is PLAN T397's: the
// Orchestrator cadence wiring this pipeline plugs into.
//
// The locator jail (PLAN T390 review carry-forward 2) is new at T396 — no prior pending stub existed
// for it.

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Ads.Tests.Fakes;
using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Specs;

public static class FeatureAdSpotPipeline
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static readonly AdSpotLocatorRoots DefaultRoots = new("/media", "/authored");

    static MediaItem Spot(string id, string locator = "/authored/ads/spot.wav") =>
        new(id, locator, $"Spot {id}", new Loudness(-14.0, -1.0, true));

    static AdSpotPipeline BuildPipeline(IEnumerable<GenWave.Core.Abstractions.IAdSpotSource> sources, AdSpotLocatorRoots? roots = null) =>
        new(sources, roots ?? DefaultRoots, NullLogger<AdSpotPipeline>.Instance);

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioThePipelineOrder
    {
        [Fact]
        public async Task FirstNonNullWinsInRegistrationOrder()
        {
            // Source A (null), source B (spot), floor C (spot): B's spot vends — F158.2 AC3.
            var sourceA = new FakeAdSpotSource { Answer = null };
            var sourceB = new FakeAdSpotSource { Answer = Spot("b") };
            var floorC = new FakeAdSpotSource { Answer = Spot("c") };
            var pipeline = BuildPipeline([sourceA, sourceB, floorC]);

            var result = await pipeline.GetNextSpotAsync(CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal("b", result.MediaId);
            Assert.Equal(1, sourceA.CallCount);
            Assert.Equal(1, sourceB.CallCount);
            Assert.Equal(0, floorC.CallCount); // Never reached — B already answered.
        }

        [Fact]
        public async Task TheLibraryFloorAnswersWhenEveryPluginIsNull()
        {
            var plugin = new FakeAdSpotSource { Answer = null };
            var floor = new FakeAdSpotSource { Answer = Spot("floor") };
            var pipeline = BuildPipeline([plugin, floor]);

            var result = await pipeline.GetNextSpotAsync(CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal("floor", result.MediaId);
        }
    }

    public sealed class ScenarioTheLocatorJail
    {
        [Fact]
        public async Task ALocatorUnderTheAuthoredRootIsAdmitted()
        {
            var floor = new FakeAdSpotSource { Answer = Spot("floor", "/authored/ads/x.wav") };
            var pipeline = BuildPipeline([floor]);

            var result = await pipeline.GetNextSpotAsync(CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal("floor", result.MediaId);
        }

        [Fact]
        public async Task ALocatorUnderTheMediaRootIsAdmitted()
        {
            var floor = new FakeAdSpotSource { Answer = Spot("floor", "/media/some/track.flac") };
            var pipeline = BuildPipeline([floor]);

            var result = await pipeline.GetNextSpotAsync(CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal("floor", result.MediaId);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioQuietAndFailingSources
    {
        [Fact]
        public async Task AThrowingSourceIsWarnSkippedAndTheFloorStillAnswers()
        {
            var throwing = new FakeAdSpotSource { ThrowOnNextCall = new InvalidOperationException("boom") };
            var floor = new FakeAdSpotSource { Answer = Spot("floor") };
            var pipeline = BuildPipeline([throwing, floor]);

            var result = await pipeline.GetNextSpotAsync(CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal("floor", result.MediaId);
        }

        [Fact]
        public async Task EveryNullSourceAnswersNull()
        {
            var a = new FakeAdSpotSource { Answer = null };
            var b = new FakeAdSpotSource { Answer = null };
            var pipeline = BuildPipeline([a, b]);

            var result = await pipeline.GetNextSpotAsync(CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task ALocatorOutsideBothRootsIsSkippedAndTheFloorStillAnswers()
        {
            // PLAN T390 review carry-forward 2: a plugin source is full-trust, but the jail is cheap
            //   defense-in-depth — a locator escaping BOTH configured roots (e.g. /etc/passwd, or a
            //   traversal attempt collapsing out via Path.GetFullPath) never reaches the caller.
            var escaping = new FakeAdSpotSource { Answer = Spot("escaping", "/etc/passwd") };
            var traversal = new FakeAdSpotSource { Answer = Spot("traversal", "/media/../etc/shadow") };
            var floor = new FakeAdSpotSource { Answer = Spot("floor") };
            var pipeline = BuildPipeline([escaping, traversal, floor]);

            var result = await pipeline.GetNextSpotAsync(CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal("floor", result.MediaId);
        }
    }
}

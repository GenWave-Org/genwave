// gh-#865 — a swap by the stale pass (gh-#854) stamps the release it ran on onto the spot, and writes
// one booth-log line, so a changed take always has a visible reason. Drives the REAL AdSpotWorker
// through AdSpotWorkerHarness (the Gh854_StaleReRenderWorker.cs precedent).

namespace GenWave.Ads.Tests.Specs;

using GenWave.Ads.Tests.Support;
using GenWave.Core.Domain;

public static class Gh865FeatureAnAutomaticReRenderIsNarrated
{
    static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    static Task Tick(AdSpotWorkerHarness.Harness harness) =>
        harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

    static void SeedStaleSpot(AdSpotWorkerHarness.Harness harness, bool oldMediaEligible = true)
    {
        harness.Store.AddSpot(1, AdState.Ready, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddHours(-1), renderVersion: 0);
        harness.AdminLookup.Add(500, AdSpotWorkerHarness.MakeMediaRow(500, eligible: oldMediaEligible), harness.AdsLibraryId);
    }

    public sealed class ScenarioASwapLands : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);

        public async Task InitializeAsync()
        {
            SeedStaleSpot(harness);
            await Tick(harness);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        AdSpot Spot => harness.Store.Spots.Single(s => s.Id == 1);

        [Fact]
        public void TheSpotRecordsTheReleaseItRanOn()
            => Assert.Equal(AdSpotWorkerHarness.HarnessAppVersion.Value, Spot.AutoRerenderedOnVersion);

        [Fact]
        public void TheSpotRecordsWhenItRan()
            => Assert.NotNull(Spot.AutoRerenderedAt);

        [Fact]
        public void OneBoothLineIsWritten()
            => Assert.Equal(AdSpotWorker.AutoRerenderBoothKind, Assert.Single(harness.BoothLog.Calls).Kind);

        [Fact]
        public void TheLineNamesTheSpotTheSponsorAndTheRelease()
            => Assert.Equal(
                "Ad \"Acme spot\" for Acme was re-rendered automatically on v9.9.9. The new take replaces the old one.",
                Assert.Single(harness.BoothLog.Calls).Summary);
    }

    public sealed class ScenarioTheSwapIsDeclined : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);

        public async Task InitializeAsync()
        {
            // Operator-disabled old media: the stale pass never swaps, so there is nothing to narrate.
            SeedStaleSpot(harness, oldMediaEligible: false);
            await Tick(harness);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoBoothLineIsWritten()
            => Assert.Empty(harness.BoothLog.Calls);

        [Fact]
        public void TheSpotCarriesNoAutoReRenderNote()
            => Assert.Null(harness.Store.Spots.Single(s => s.Id == 1).AutoRerenderedOnVersion);
    }

    public sealed class ScenarioTheBoothLogIsDown : IAsyncLifetime
    {
        readonly AdSpotWorkerHarness.Harness harness = AdSpotWorkerHarness.Build(Now);
        Exception? tickError;

        public async Task InitializeAsync()
        {
            SeedStaleSpot(harness);
            harness.BoothLog.ThrowOnAppend = true;
            tickError = await Record.ExceptionAsync(() => Tick(harness));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheTickStillCompletes()
            => Assert.Null(tickError);

        [Fact]
        public void TheSwapStillStands()
            => Assert.NotEqual(500L, harness.Store.Spots.Single(s => s.Id == 1).MediaId);
    }
}

public static class Gh865FeatureAppVersionReadsLikeARelease
{
    public sealed class ScenarioTheStampIsNormalised
    {
        [Theory]
        [InlineData("v5.13.1", "v5.13.1")]
        [InlineData("5.13.1", "v5.13.1")]
        [InlineData("v5.13.1+e36dce6", "v5.13.1")]
        [InlineData("1.0.0+e36dce6458df97fe", "v1.0.0")]
        [InlineData("0.0.0-dev", "v0.0.0-dev")]
        [InlineData("", "unknown")]
        [InlineData(null, "unknown")]
        public void TheStampReadsLikeATag(string? informational, string expected)
            => Assert.Equal(expected, AdAppVersion.From(informational).Value);
    }
}

public static class Gh865FeatureTheMarkerColumnsAreMirroredInFreshInit
{
    public sealed class ScenarioBothColumnsMatchDb49
    {
        [Theory]
        [InlineData("auto_rerendered_at timestamptz null")]
        [InlineData("auto_rerendered_on_version text null")]
        public void Db49AndDb06CarryTheIdenticalColumnDefinition(string columnText)
        {
            var repoRoot = RepoRootLocator.Find(AppContext.BaseDirectory);
            Assert.Contains(columnText, File.ReadAllText(Path.Combine(repoRoot, "db", "49-ad-spot-auto-rerender-marker-migration.sh")));
            Assert.Contains(columnText, File.ReadAllText(Path.Combine(repoRoot, "db", "06-station-settings-migration.sh")));
        }
    }
}

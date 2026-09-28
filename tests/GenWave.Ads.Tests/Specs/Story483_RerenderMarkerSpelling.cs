// STORY-483 — One version, read once, shown one way (gh-#9 + gh-#868 · SPEC F211.2 · PLAN T589)
//
// BDD specification — xUnit. Seam: AdSpotWorker through AdSpotWorkerHarness. PLAN T589 replaced
// AdAppVersion with IAppVersion.

namespace GenWave.Ads.Tests.Specs;

using GenWave.Ads.Tests.Support;
using GenWave.Core;
using GenWave.Core.Domain;

public static class FeatureRerenderMarkerSpelling
{
    public sealed class ScenarioAStaleSpotIsReRendered : IAsyncLifetime
    {
        static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

        // Given: IAppVersion from "5.13.2+abc1234"; one stale spot; the worker's stale pass swaps its take
        readonly AdSpotWorkerHarness.Harness harness =
            AdSpotWorkerHarness.Build(Now, appVersion: AppVersion.From("5.13.2+abc1234"));

        public async Task InitializeAsync()
        {
            harness.Store.AddSpot(1, AdState.Ready, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddHours(-1), renderVersion: 0);
            harness.AdminLookup.Add(500, AdSpotWorkerHarness.MakeMediaRow(500, eligible: true), harness.AdsLibraryId);
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>AC7 — the auto re-render marker reads "v5.13.2" (unchanged spelling)</summary>
        [Fact]
        public void MarkerReadsV5132()
            => Assert.Equal("v5.13.2", harness.Store.Spots.Single(s => s.Id == 1).AutoRerenderedOnVersion);
    }
}

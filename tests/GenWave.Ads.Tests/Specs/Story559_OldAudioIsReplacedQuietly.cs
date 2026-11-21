// STORY-559 — Old audio is replaced quietly, the ad half (SPEC F219.22 · PLAN T763)
//
// The seeder half lives in GenWave.Host.Tests/Specs/Story559_OldAudioIsReplacedQuietly.cs. Here the REAL
// AdSpotWorker runs its production tick against spots made before a switch.

namespace GenWave.Ads.Tests.Specs;

using GenWave.Ads.Tests.Support;
using GenWave.Core.Domain;

public static class FeatureOldAdsAreReplacedQuietly
{
    static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    const string NovaMap = "{\"kokoro\":\"af_nova\",\"chatterbox\":\"nova\"}";

    // AC2 — 3 stale ads after a switch, one idle tick
    public sealed class ScenarioOnePerIdleTick : IAsyncLifetime
    {
        static readonly Dictionary<string, string?> Settings = new() { ["Station:Voice"] = NovaMap };

        readonly AdSpotWorkerHarness.Harness onKokoro = AdSpotWorkerHarness.Build(Now, Settings);

        readonly AdSpotWorkerHarness.Harness onChatterbox =
            AdSpotWorkerHarness.Build(Now, Settings, engine: "chatterbox");

        public async Task InitializeAsync()
        {
            foreach (var id in new[] { 1, 2, 3 })
            {
                onChatterbox.Store.AddSpot(
                    id, AdState.Ready, mediaId: 500 + id, stateChangedAt: Now.UtcDateTime.AddHours(-id),
                    renderVersion: onKokoro.CurrentMarker);
                onChatterbox.AdminLookup.Add(
                    500 + id, AdSpotWorkerHarness.MakeMediaRow(500 + id, eligible: true), onChatterbox.AdsLibraryId);
            }

            await onChatterbox.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void OneAdIsReRenderedPerTick() =>
            Assert.Single(onChatterbox.Store.Spots, spot => spot.RenderVersion == onChatterbox.CurrentMarker);
    }
}

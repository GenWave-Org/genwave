// gh-#156 — PlayoutFeederService.StopAsync was not idempotent: host shutdown can reach a stop
// more than once (supervisor stop + host teardown), and when a starved runner pushed the first
// call's WaitAsync past ShutdownTimeout, the second call CancelAsync'd the already-disposed
// linked source — ObjectDisposedException out of WebApplicationFactory.DisposeAsync, reddening
// an otherwise-green CI run (first observed on the README-only #138 merge).
//
// BDD specification — xUnit. The IHostedService contract pin: stop-after-stop and
// stop-before-start both complete as no-ops, never throw.

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Playout;
using GenWave.Host.Playout;
using GenWave.Host.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenWave.Host.Tests.Specs;

public static class FeaturePlayoutFeederServiceIdempotentStop
{
    static PlayoutFeederService BuildService() =>
        new(
            new Station(1, "Test Station", "localhost", "engine", "icecast",
                new CadenceConfig(), DateTimeOffset.UnixEpoch),
            new PlayoutFeeder(
                new FakeLiquidsoapControl(),
                new NothingProvider(),
                new FakeRotationSettingsProvider(new RotationSettings())),
            new FakeStationIdentityProvider(new StationIdentity("1", "Test Station", "af_heart")),
            NullLogger<PlayoutFeederService>.Instance);

    /// <summary>Yields nothing — the feeder loop just idles until cancelled.</summary>
    sealed class NothingProvider : INextItemProvider
    {
        public Task<MediaItem?> GetNextAsync(PlayoutContext ctx, CancellationToken ct)
            => Task.FromResult<MediaItem?>(null);
    }

    public sealed class ScenarioHostStopsTwice
    {
        [Fact]
        public async Task SecondStopCompletesWithoutThrowing()
        {
            var service = BuildService();
            await service.StartAsync(CancellationToken.None);
            await service.StopAsync(CancellationToken.None);

            // Pre-fix this threw ObjectDisposedException: the first stop's finally disposed
            // the linked source but left the field set, so re-entry cancelled a disposed CTS.
            await service.StopAsync(CancellationToken.None);
        }
    }

    public sealed class ScenarioStopBeforeStart
    {
        [Fact]
        public async Task StopWithoutStartIsANoOp()
        {
            var service = BuildService();
            await service.StopAsync(CancellationToken.None);
        }
    }
}

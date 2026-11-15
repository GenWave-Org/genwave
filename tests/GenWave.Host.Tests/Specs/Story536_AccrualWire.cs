// STORY-536 — the DJ remembers its week: Host wiring (SPEC F218.15 · PLAN T703)
//
// Boots the real composition root (Program.cs, no DB) with only IEpisodeStore faked, then proves the
// accrual consumer is on the ONE event bus, reads the real schedule resolver, and drains to the store.

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Host.Playout;
using GenWave.Memory;
using GenWave.Orchestration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace GenWave.Host.Tests.Specs;

public static class FeatureTheDjMemoryAccrualIsWired
{
    public sealed class ScenarioTheCompositionRoot
    {
        [Fact]
        public async Task TheOnAirSnapshotIsTheSchedulesOwnResolver()
        {
            await using var factory = new AccrualWireFactory(new RecordingEpisodeStore());
            Assert.Same(
                factory.Services.GetRequiredService<CachingScheduleResolver>(),
                factory.Services.GetRequiredService<IOnAirSnapshotSource>());
        }

        [Fact]
        public async Task TheAccrualSinkIsASingletonWithItsDrainRegistered()
        {
            await using var factory = new AccrualWireFactory(new RecordingEpisodeStore());
            Assert.Same(
                factory.Services.GetRequiredService<EpisodeAccrualSink>(),
                factory.Services.GetRequiredService<EpisodeAccrualSink>());
            Assert.Single(factory.Services.GetServices<IHostedService>().OfType<EpisodeAccrualDrainService>());
        }

        // When a crosstalk airs on the host's one event bus and the drain runs, the store gets both episodes;
        // stopping the host flushes what is queued.
        [Fact]
        public async Task ACrosstalkPublishedOnTheBusIsWrittenAndFlushedOnStop()
        {
            var store = new RecordingEpisodeStore();
            await using var factory = new AccrualWireFactory(store);
            var drain = factory.Services.GetServices<IHostedService>().OfType<EpisodeAccrualDrainService>().Single();
            // The factory's host has already started the drain (Services builds and starts the host).

            var script = new CrosstalkAiredScript([new CrosstalkAiredLine(CrosstalkSpeaker.Host, "Hello there.", false)])
            {
                Host = new CrosstalkParticipant(7, "Dusty"),
                Neighbor = new CrosstalkParticipant(9, "Flip"),
            };
            factory.Services.GetRequiredService<IStationEventSink>().Publish(new TrackAired(
                "xt:1", "Dusty and Flip", "Dusty", 0, DateTimeOffset.UtcNow, 30_000, SegmentKind: SegmentKind.Crosstalk)
            {
                CrosstalkScript = script,
            });

            await drain.StopAsync(CancellationToken.None);

            Assert.Equal([7L, 9L], store.PersonaIds.Order());
        }

        // The shutdown token fires while a write is stuck on a down store: StopAsync must return quietly and
        // still cancel the drain's own token so the stuck write ends.
        [Fact]
        public async Task AShutdownTimeoutWhileAWriteIsStuckStillCancelsTheWrite()
        {
            var store = new BlockedEpisodeStore();
            await using var factory = new AccrualWireFactory(store);
            var drain = factory.Services.GetServices<IHostedService>().OfType<EpisodeAccrualDrainService>().Single();
            var script = new CrosstalkAiredScript([new CrosstalkAiredLine(CrosstalkSpeaker.Host, "Hello there.", false)])
            {
                Host = new CrosstalkParticipant(7, "Dusty"),
                Neighbor = new CrosstalkParticipant(9, "Flip"),
            };
            factory.Services.GetRequiredService<IStationEventSink>().Publish(new TrackAired(
                "xt:1", "Dusty and Flip", "Dusty", 0, DateTimeOffset.UtcNow, 30_000, SegmentKind: SegmentKind.Crosstalk)
            {
                CrosstalkScript = script,
            });
            await store.WriteStarted.WaitAsync(TimeSpan.FromSeconds(10));

            using var shutdownTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await drain.StopAsync(shutdownTimeout.Token);

            await store.WriteCancelled.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}

file sealed class RecordingEpisodeStore : IEpisodeStore
{
    readonly List<long> personaIds = [];

    public IReadOnlyList<long> PersonaIds
    {
        get { lock (personaIds) return [.. personaIds]; }
    }

    public Task AppendAsync(NewEpisode episode, CancellationToken ct)
    {
        lock (personaIds) personaIds.Add(episode.PersonaId);
        return Task.CompletedTask;
    }

    public Task AppendSharedAsync(NewMemoryEvent happening, IReadOnlyList<NewEpisode> episodes, CancellationToken ct)
    {
        lock (personaIds) personaIds.AddRange(episodes.Select(e => e.PersonaId));
        return Task.CompletedTask;
    }
}

file sealed class BlockedEpisodeStore : IEpisodeStore
{
    readonly TaskCompletionSource writeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly TaskCompletionSource writeCancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WriteStarted => writeStarted.Task;
    public Task WriteCancelled => writeCancelled.Task;

    public Task AppendAsync(NewEpisode episode, CancellationToken ct) => BlockUntilCancelledAsync(ct);

    public Task AppendSharedAsync(NewMemoryEvent happening, IReadOnlyList<NewEpisode> episodes, CancellationToken ct)
        => BlockUntilCancelledAsync(ct);

    async Task BlockUntilCancelledAsync(CancellationToken ct)
    {
        writeStarted.TrySetResult();
        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        finally
        {
            writeCancelled.TrySetResult();
        }
    }
}

file sealed class AccrualWireFactory(IEpisodeStore store) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("ConnectionStrings:Station", "");
        builder.UseSetting("Admin:Password", "test-password-story536-accrual");
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");

        builder.ConfigureTestServices(services =>
        {
            // Keep only the accrual drain; no engine, feeder or other background reach.
            var drain = services.First(d => d.ServiceType == typeof(IHostedService)
                && d.ImplementationType == typeof(EpisodeAccrualDrainService));
            services.RemoveAll<IHostedService>();
            services.Add(drain);
            services.RemoveAll<IEpisodeStore>();
            services.AddSingleton(store);
        });
    }
}

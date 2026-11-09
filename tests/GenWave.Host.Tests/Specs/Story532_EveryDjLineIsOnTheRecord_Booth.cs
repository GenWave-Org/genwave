// STORY-532 — Every DJ line is on the record (gh-#293 gh-#920 · SPEC F218 · PLAN T684 T685)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-08); the named task
// activates its facts, watches them fail, then makes them pass.
//
// Spans three files; see tests/GenWave.Tts.Tests/Specs/Story532_EveryDjLineIsOnTheRecord_Copy.cs for the map.

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Host.Playout;
using GenWave.Host.Tests.Fakes;
using GenWave.MediaLibrary.Station;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GenWave.Host.Tests.Specs;

/// <summary>
/// Drives one <see cref="SegmentGenerated"/> through the real booth-log write path (public
/// <c>AddBoothLog</c> DI surface, only the appender faked) — the Story217 harness idiom.
/// </summary>
file static class PatterAiredHarness
{
    public static async Task<BoothLogAppendRequest> DriveAsync(SegmentGenerated evt)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
        services.AddBoothLog("Host=nowhere;Database=test", new ConfigurationBuilder().Build());

        var appender = new FakeBoothLogAppender();
        services.AddSingleton<IBoothLogAppender>(appender);

        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().ToList();
        foreach (var service in hosted)
            await service.StartAsync(CancellationToken.None);

        provider.GetRequiredService<IBoothLogEventConsumer>().Publish(evt);
        await appender.WaitForCallsAsync(1, TimeSpan.FromSeconds(5));

        foreach (var service in hosted)
            await service.StopAsync(CancellationToken.None);

        return Assert.Single(appender.Calls);
    }
}

/// <summary>
/// Boots the real composition root (Program.cs, no DB) with only <see cref="IUtteranceStore"/>
/// faked, starts the real <c>UtteranceAirDrainService</c>, and publishes through the container's
/// real <see cref="IStationEventSink"/> — the same bus the feeder's TrackAired travels.
/// </summary>
file sealed class AirTimeHarness : IAsyncDisposable
{
    readonly AirTimeFactory factory;
    readonly IHostedService drain;
    readonly RecordingUtteranceStore store;

    AirTimeHarness(AirTimeFactory factory, IHostedService drain, RecordingUtteranceStore store)
    {
        this.factory = factory;
        this.drain = drain;
        this.store = store;
    }

    /// <param name="startDrain">False leaves the drain loop stopped, so a queue's contents stay observable.</param>
    public static async Task<AirTimeHarness> StartAsync(bool startDrain = true)
    {
        var store = new RecordingUtteranceStore();
        var factory = new AirTimeFactory(store);
        var drain = factory.Services.GetServices<IHostedService>()
            .OfType<UtteranceAirDrainService>().Single();
        if (startDrain) await drain.StartAsync(CancellationToken.None);
        return new AirTimeHarness(factory, drain, store);
    }

    public void Publish(StationEvent evt) => factory.Services.GetRequiredService<IStationEventSink>().Publish(evt);

    /// <summary>One line rendered and aired, drain stopped so the queues stay observable.</summary>
    public static async Task<AirTimeHarness> StartWithOneAiredLineAsync()
    {
        var run = await StartAsync(startDrain: false);
        run.Publish(new SegmentGenerated("tts:abc", "LeadIn", "af_heart") { UtteranceId = 42 });
        run.Publish(new TrackAired("tts:abc", null, null, 0.0, DateTimeOffset.UtcNow, 4_000, SegmentKind: SegmentKind.LeadIn));
        return run;
    }

    public T Resolve<T>() where T : notnull => factory.Services.GetRequiredService<T>();

    public Task<long?> NextStampAsync(TimeSpan wait) => store.NextStampAsync(wait);

    public async ValueTask DisposeAsync()
    {
        await drain.StopAsync(CancellationToken.None); // a no-op when never started
        await factory.DisposeAsync();
    }
}

file sealed class AirTimeFactory(IUtteranceStore store) : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("ConnectionStrings:Station", "");
        builder.UseSetting("Admin:Password", "test-password-story532-airtime");
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");

        builder.ConfigureTestServices(services =>
        {
            // Keep only the air-time drain loop; no engine, feeder or other background reach.
            var drain = services.First(d => d.ServiceType == typeof(IHostedService)
                && d.ImplementationType == typeof(UtteranceAirDrainService));
            services.RemoveAll<IHostedService>();
            services.Add(drain);
            services.AddSingleton(store);
        });
    }
}

file sealed class RecordingUtteranceStore : IUtteranceStore
{
    readonly System.Threading.Channels.Channel<long> stamps = System.Threading.Channels.Channel.CreateUnbounded<long>();

    public async Task<long?> NextStampAsync(TimeSpan wait)
    {
        using var cts = new CancellationTokenSource(wait);
        try
        {
            return await stamps.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    public Task MarkAiredAsync(long id, CancellationToken ct)
    {
        stamps.Writer.TryWrite(id);
        return Task.CompletedTask;
    }

    public Task<long> RecordAsync(NewUtterance utterance, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<UtteranceEntry>> RecentAsync(long personaId, int take, CancellationToken ct) => throw new NotSupportedException();
    public Task<int> SweepAsync(CancellationToken ct) => throw new NotSupportedException();
}

public static class FeatureEveryDjLineIsOnTheRecordBooth
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC3 — the booth feed quotes it
    // entry point: BoothLogWriter via the event bus (SegmentGenerated)
    public sealed class ScenarioTheBoothFeedQuotesIt
    {
        // Given a SegmentGenerated event carrying PersonaId, ShowId and UtteranceId
        // When  BoothLogWriter publishes it

        static readonly string Copy = new string('a', 60) + new string('b', 60);

        static Task<BoothLogAppendRequest> DriveAsync() =>
            PatterAiredHarness.DriveAsync(new SegmentGenerated("tts:abc", "LeadIn", "af_heart")
            {
                PersonaId = 7,
                ShowId = 3,
                UtteranceId = 99,
                Text = Copy,
            });

        [Fact]
        public async Task ThePatterAiredRowStampsPersonaId() => Assert.Equal(7, (await DriveAsync()).PersonaId);

        [Fact]
        public async Task ThePatterAiredRowStampsShowId() => Assert.Equal(3, (await DriveAsync()).ShowId);

        [Fact]
        public async Task ItsSummaryEndsWithTheCopySFirst80CharactersInQuotes() =>
            Assert.EndsWith($"\"{Copy[..80]}\"", (await DriveAsync()).Summary, StringComparison.Ordinal);
    }

    // AC3 edge — the 80-character cut never splits a surrogate pair
    public sealed class ScenarioTheCutFallsInsideASurrogatePair
    {
        // Given copy whose 80th character is the high half of a surrogate pair
        // When  BoothLogWriter publishes it

        static readonly string Copy = new string('a', 79) + "\U0001F3B5" + "tail";

        [Fact]
        public async Task ItsQuoteStopsBeforeTheSplitPair()
        {
            var row = await PatterAiredHarness.DriveAsync(new SegmentGenerated("tts:abc", "LeadIn", "af_heart") { Text = Copy });

            Assert.EndsWith($"\"{new string('a', 79)}\"", row.Summary, StringComparison.Ordinal);
        }
    }

    // AC4 — air time is marked
    // entry point: engine metadata callback (gw_tts="true" file started)
    public sealed class ScenarioAirTimeIsMarked
    {
        // Given a stored utterance whose file the engine then starts
        // When  the engine reports a gw_tts="true" file started

        [Fact]
        public async Task ThatUtteranceSAiredAtIsSet()
        {
            await using var run = await AirTimeHarness.StartAsync();
            run.Publish(new SegmentGenerated("tts:abc", "LeadIn", "af_heart") { UtteranceId = 42 });
            run.Publish(new TrackAired("tts:abc", null, null, 0.0, DateTimeOffset.UtcNow, 4_000, SegmentKind: SegmentKind.LeadIn));

            Assert.Equal(42L, await run.NextStampAsync(TimeSpan.FromSeconds(5)));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC6 — dropped renders never air
    public sealed class ScenarioDroppedRendersNeverAir
    {
        // Given a stored utterance whose render is later dropped
        // When  no engine event arrives

        [Fact]
        public async Task AiredAtStaysNull()
        {
            await using var run = await AirTimeHarness.StartAsync();
            run.Publish(new SegmentGenerated("tts:dropped", "LeadIn", "af_heart") { UtteranceId = 43 });
            // A different file airs; the dropped render's file never does.
            run.Publish(new TrackAired("tts:other", null, null, 0.0, DateTimeOffset.UtcNow, 4_000, SegmentKind: SegmentKind.LeadIn));
            // Channel order is FIFO: had the dropped render's 43 been queued, it would come out
            // before 44. No fixed sleep; 5 s is only the ceiling for 44 to arrive.
            run.Publish(new SegmentGenerated("tts:aired", "LeadIn", "af_heart") { UtteranceId = 44 });
            run.Publish(new TrackAired("tts:aired", null, null, 0.0, DateTimeOffset.UtcNow, 4_000, SegmentKind: SegmentKind.LeadIn));

            Assert.Equal(44L, await run.NextStampAsync(TimeSpan.FromSeconds(5)));
        }
    }

    // ---------------------------------------------------------------------
    // Support: the utterance queue shares nothing with other registrations
    // ---------------------------------------------------------------------
    public sealed class ScenarioTheUtteranceQueueHijacksNoOtherQueue
    {
        // The enrichment queue (Channel<long>) and the listener-request queue
        // (ChannelReader<long>/ChannelWriter<long>) are resolved by closed generic type, so a
        // bare long queue here would take them over. An aired line must reach neither.

        [Fact]
        public async Task TheEnrichmentQueueReceivesNothing()
        {
            await using var run = await AirTimeHarness.StartWithOneAiredLineAsync();

            Assert.False(run.Resolve<System.Threading.Channels.Channel<long>>().Reader.TryRead(out _));
        }

        [Fact]
        public async Task TheListenerRequestQueueReceivesNothing()
        {
            await using var run = await AirTimeHarness.StartWithOneAiredLineAsync();

            Assert.False(run.Resolve<System.Threading.Channels.ChannelReader<long>>().TryRead(out _));
        }
    }

    // ---------------------------------------------------------------------
    // A line the feeder proves aired between two polls still gets its stamp
    // ---------------------------------------------------------------------
    public sealed class ScenarioAProvenLateLineIsStamped
    {
        // Given a stored utterance whose short file aired and departed between two polls
        // When  the feeder proves it aired from a later observed sibling

        [Fact]
        public async Task AProvenLateLineStillGetsItsStamp()
        {
            await using var run = await AirTimeHarness.StartAsync();
            run.Publish(new SegmentGenerated("tts:late", "LeadIn", "af_heart") { UtteranceId = 45 });
            run.Publish(new TrackProvenAired("tts:late", DateTimeOffset.UtcNow));

            Assert.Equal(45L, await run.NextStampAsync(TimeSpan.FromSeconds(5)));
        }
    }

    // ---------------------------------------------------------------------
    // Support: a re-rendered line that aired once survives eviction pressure
    // ---------------------------------------------------------------------
    public sealed class ScenarioAReRenderAfterAnAiringSurvivesEviction
    {
        readonly System.Threading.Channels.Channel<UtteranceAirSignal> queue;
        readonly UtteranceAirEventSink sink;

        // Given a cached line that rendered and aired once, then was re-rendered (new utterance)
        //       as the second-oldest of a batch that overflows the pending bound by one
        public ScenarioAReRenderAfterAnAiringSurvivesEviction()
        {
            queue = System.Threading.Channels.Channel.CreateUnbounded<UtteranceAirSignal>();
            sink = new UtteranceAirEventSink(queue.Writer, Microsoft.Extensions.Logging.Abstractions.NullLogger<UtteranceAirEventSink>.Instance);

            sink.Publish(new SegmentGenerated("tts:x", "LeadIn", "af_heart") { UtteranceId = 1 });
            Air("tts:x");
            queue.Reader.TryRead(out _);
            sink.Publish(new SegmentGenerated("tts:f0", "LeadIn", "af_heart") { UtteranceId = 1_000 });
            sink.Publish(new SegmentGenerated("tts:x", "LeadIn", "af_heart") { UtteranceId = 2 });
            for (var i = 1; i < UtteranceAirEventSink.MaxPending; i++)
                sink.Publish(new SegmentGenerated($"tts:f{i}", "LeadIn", "af_heart") { UtteranceId = 1_000 + i });
            while (queue.Reader.TryRead(out _)) { }
        }

        // When  the re-rendered line airs

        [Fact]
        public void TheReRenderedLineStillGetsItsStamp()
        {
            Air("tts:x");

            Assert.True(queue.Reader.TryRead(out var signal));
            Assert.Equal(2L, signal.UtteranceId);
        }

        void Air(string mediaId) =>
            sink.Publish(new TrackAired(mediaId, null, null, 0.0, DateTimeOffset.UtcNow, 4_000, SegmentKind: SegmentKind.LeadIn));
    }
}

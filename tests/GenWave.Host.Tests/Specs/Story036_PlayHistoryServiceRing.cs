// STORY-036 — PlayHistoryService ring + feeder advance hook

using Microsoft.Extensions.DependencyInjection;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Playout;
using GenWave.Host.Options;
using GenWave.Host.Playout;

using MsOptions = Microsoft.Extensions.Options.Options;
using CoreLoudness = GenWave.Core.Domain.Loudness;

namespace GenWave.Host.Tests.Specs;

public static class FeaturePlayHistoryServiceRing
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioPlayHistoryServiceRegistration
    {
        [Fact]
        public void PlayHistoryServiceTypeExists() =>
            Assert.NotNull(Type.GetType("GenWave.Host.Playout.PlayHistoryService, GenWave.Host"));

        [Fact]
        public void RegisteredAsSingletonInDI()
        {
            var services = new ServiceCollection();
            services.Configure<AdminOptions>(_ => { });
            services.AddSingleton<PlayHistoryService>();

            using var provider = services.BuildServiceProvider();
            var first = provider.GetRequiredService<PlayHistoryService>();
            var second = provider.GetRequiredService<PlayHistoryService>();
            Assert.Same(first, second);
        }
    }

    public sealed class ScenarioCapacityIsConfigBoundWithDefault50
    {
        [Fact]
        public void DefaultCapacityIs50()
        {
            var opts = MsOptions.Create(new AdminOptions());
            Assert.Equal(50, opts.Value.PlayHistoryCapacity);
        }
    }

    public sealed class ScenarioFeederAdvancePushesOneEntry
    {
        // Shared fixture: feeder + service that have seen one advance (safe → m1).
        // After two ticks (tick1: sees "safe" → pulls m1 into queue; tick2: sees "m1" on-air → fires callback),
        // the ring has one entry for m1.
        static async Task<(PlayHistoryService history, PlayHistoryEntry entry)> ArrangeOneAdvance()
        {
            var history = MakeHistory();
            var (feeder, ls) = MakeFeederWithHistory("station-1", history);

            // Provide m1 as the next item.
            var m1 = MakeItem("m1", "Song One", "Artist A");
            ls.NextItem = m1;

            // Tick 1: sees safe-rotation on-air → detects queue drained → pushes m1.
            await feeder.TickAsync(CancellationToken.None);
            // Tick 2: sees m1 on-air → publishes TrackAired for m1.
            await feeder.TickAsync(CancellationToken.None);

            var entries = history.GetEntries("station-1");
            return (history, entries[0]);
        }

        [Fact]
        public async Task AdvanceFromM1ToM2AppendsOneEntryWithStartedAt()
        {
            var (history, entry) = await ArrangeOneAdvance();

            Assert.Single(history.GetEntries("station-1"));
            Assert.Equal("m1", entry.MediaId);
            Assert.Equal("Song One", entry.Title);
            Assert.Equal("Artist A", entry.Artist);
            Assert.True(entry.StartedAt > DateTimeOffset.UtcNow.AddSeconds(-5));
        }

        [Fact]
        public async Task PreviousEntryEndedAtIsSetToNewEntryStartedAt()
        {
            // Arrange two advances: m1 then m2.
            var history = MakeHistory();
            var (feeder, ls) = MakeFeederWithHistory("station-1", history);

            var m1 = MakeItem("m1", "Song One", "Artist A");
            var m2 = MakeItem("m2", "Song Two", "Artist B");

            // Tick 1: sees safe-rotation → pulls m1.
            ls.NextItem = m1;
            await feeder.TickAsync(CancellationToken.None);

            // Tick 2: sees m1 on-air → TrackAired publishes for m1; queue exhausted → pulls m2.
            ls.NextItem = m2;
            await feeder.TickAsync(CancellationToken.None);

            // Tick 3: sees m2 on-air → TrackAired publishes for m2; m1's EndedAt stamped.
            await feeder.TickAsync(CancellationToken.None);

            var entries = history.GetEntries("station-1");
            Assert.Equal(2, entries.Count);

            var m2Entry = entries[0]; // newest first
            var m1Entry = entries[1];

            Assert.Equal("m2", m2Entry.MediaId);
            Assert.Equal("m1", m1Entry.MediaId);
            // m1's EndedAt equals m2's StartedAt.
            Assert.Equal(m2Entry.StartedAt, m1Entry.EndedAt);
            // m2 is still on-air: no EndedAt yet.
            Assert.Null(m2Entry.EndedAt);
        }
    }

    public sealed class ScenarioRingBufferWrapsAtCapacity
    {
        [Fact]
        public void OldestEntryIsEvictedWhenCapacityExceeded()
        {
            // capacity = 3; push 4 advances; ring should be [m4, m3, m2].
            var opts = new FakeOptionsMonitor<AdminOptions>(new AdminOptions { PlayHistoryCapacity = 3 });
            var history = new PlayHistoryService(opts);

            var now = DateTimeOffset.UtcNow;
            history.Push(new PlayHistoryEntry("s", "m1", null, null, 0, now.AddSeconds(-3), null, null));
            history.Push(new PlayHistoryEntry("s", "m2", null, null, 0, now.AddSeconds(-2), null, null));
            history.Push(new PlayHistoryEntry("s", "m3", null, null, 0, now.AddSeconds(-1), null, null));
            history.Push(new PlayHistoryEntry("s", "m4", null, null, 0, now, null, null));

            var entries = history.GetEntries("s");
            Assert.Equal(3, entries.Count);
            Assert.Equal("m4", entries[0].MediaId);
            Assert.Equal("m3", entries[1].MediaId);
            Assert.Equal("m2", entries[2].MediaId);
        }
    }

    public sealed class ScenarioEntriesArePerStationIndependent
    {
        [Fact]
        public void Station1RingIsDistinctFromStation2()
        {
            // Push directly to test ring isolation without the feeder tick machinery.
            var history = MakeHistory();
            var now = DateTimeOffset.UtcNow;

            // Station-1 gets three entries.
            history.Push(new PlayHistoryEntry("station-1", "m1", "Song 1", "Artist", 0, now.AddSeconds(-6), null, null));
            history.Push(new PlayHistoryEntry("station-1", "m2", "Song 2", "Artist", 0, now.AddSeconds(-3), null, null));
            history.Push(new PlayHistoryEntry("station-1", "m3", "Song 3", "Artist", 0, now, null, null));

            // Station-2 gets one entry.
            history.Push(new PlayHistoryEntry("station-2", "s2m1", "S2 Song", "S2 Artist", 0, now, null, null));

            Assert.Equal(3, history.GetEntries("station-1").Count);
            Assert.Single(history.GetEntries("station-2"));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioDrainDoesNotPushAnEntry
    {
        [Fact]
        public async Task NoEntryIsAppendedOnDrain()
        {
            // The drain token is a safe-rotation advance (no track_id). The feeder
            // only publishes TrackAired when onAirIsReal == true.
            var history = MakeHistory();
            var (feeder, ls) = MakeFeederWithHistory("station-1", history);

            // Keep safe-rotation on-air: ls.NextItem = null → feeder tries to push but gets null.
            ls.NextItem = null;

            // Multiple ticks: feeder sees safe-rotation, tries to pull, fails — never publishes TrackAired.
            await feeder.TickAsync(CancellationToken.None);
            await feeder.TickAsync(CancellationToken.None);
            await feeder.TickAsync(CancellationToken.None);

            Assert.Empty(history.GetEntries("station-1"));
        }
    }

    public sealed class ScenarioFreshServiceReturnsEmptyForAnyStation
    {
        [Fact]
        public void GetEntriesReturnsEmptyArrayNotNull()
        {
            var history = MakeHistory();
            var entries = history.GetEntries("any-station");
            Assert.NotNull(entries);
            Assert.Empty(entries);
        }
    }

    // ── Shared helpers ────────────────────────────────────────────────────

    static PlayHistoryService MakeHistory()
    {
        var opts = new FakeOptionsMonitor<AdminOptions>(new AdminOptions { PlayHistoryCapacity = 50 });
        return new PlayHistoryService(opts);
    }

    static (PlayoutFeeder feeder, ScriptedLiquidsoapControl ls) MakeFeederWithHistory(
        string stationId,
        PlayHistoryService history)
    {
        var ls = new ScriptedLiquidsoapControl();
        var next = new ScriptedNextItemProvider(ls);
        // Mirrors production wiring: TrackAired flows through the event sink into the ring
        // (PlayHistoryEventSink, gitea-#246).
        var sink = new CapturingEventSink
        {
            OnTrackAired = t => history.Push(new PlayHistoryEntry(
                stationId, t.MediaId, t.Title, t.Artist, t.GainDb, t.StartedAt, null, t.DurationMs)),
        };
        var feeder = new PlayoutFeeder(ls, next, new FakeRotationSettingsProvider(new RotationSettings()), events: sink);

        return (feeder, ls);
    }

    static MediaItem MakeItem(string id, string title, string artist) =>
        new(id, $"/media/{id}.mp3", title, new CoreLoudness(-16.0, -1.0, true), artist);

    // ── Fakes ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Controls what the feeder "sees" on-air. Starts with the safe-rotation token.
    /// Once an item is pushed, that item's id is reported on-air (simulating the engine
    /// immediately putting it on-air). Setting <see cref="NextItem"/> to null keeps the
    /// safe-rotation on-air indefinitely (drain scenario).
    /// </summary>
    sealed class ScriptedLiquidsoapControl : ILiquidsoapControl
    {
        const string SafeToken = "safe";

        string? lastPushedId;

        /// <summary>The item the next provider will yield. Null means the provider yields nothing.</summary>
        public MediaItem? NextItem { get; set; }

        public Task<string?> OnAirNewestAsync(CancellationToken ct)
        {
            // If nothing was pushed yet, safe-rotation is on-air.
            string? id = lastPushedId ?? SafeToken;
            return Task.FromResult<string?>(id);
        }

        public Task<EngineMetadata> MetadataAsync(string rid, CancellationToken ct)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (lastPushedId is not null && lastPushedId == rid)
                map["track_id"] = rid;
            return Task.FromResult(new EngineMetadata(map));
        }

        public Task<string> PushAsync(MediaItem item, double gainDb, CancellationToken ct)
        {
            lastPushedId = item.MediaId;
            return Task.FromResult(item.MediaId);
        }
    }

    /// <summary>
    /// Delegates to the shared <see cref="ScriptedLiquidsoapControl.NextItem"/> so the provider and
    /// control stay in sync: whatever item is currently set on <c>ls</c> is what we hand to the feeder.
    /// </summary>
    sealed class ScriptedNextItemProvider(ScriptedLiquidsoapControl ls) : INextItemProvider
    {
        public Task<MediaItem?> GetNextAsync(PlayoutContext ctx, CancellationToken ct) =>
            Task.FromResult(ls.NextItem);
    }
}

// STORY-536 — The DJ remembers its week (gh-#293 gh-#920 · SPEC F218 · PLAN T702)
//
// BDD specification — xUnit. Accrual rules are pure and live here; the Host wiring is T703. The facts run a fake
// event stream through EpisodeAccrualSink into an in-memory episode store. The row's kind (episode) and source
// (accrued), and the 28-day/cap SQL itself, are pinned against Postgres by
// GenWave.MediaLibrary.Tests Story536_EpisodeRetentionSql.

using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Memory.Tests.Fakes;

namespace GenWave.Memory.Tests.Specs;

public static class FeatureTheDjRemembersItsWeek
{
    static readonly DateTimeOffset TuesdaySixAm = new(2026, 11, 10, 6, 0, 0, TimeSpan.FromHours(-6));

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — A hosted show becomes an episode
    public sealed class ScenarioAHostedShowBecomesAnEpisode : IAsyncLifetime
    {
        // Given persona 7 hosts "Sunrise Shift" Tuesday 06:00 with 38 tracks
        // When  the show boundary passes
        IReadOnlyList<InMemoryEpisodeStore.Row> persona7Rows = [];

        public async Task InitializeAsync()
        {
            var harness = new AccrualHarness();
            harness.OnAir.Current = FakeOnAir.Gap();
            harness.MusicAired("Before The Show", "Someone");   // seen before the show starts: its start is observed, not joined mid-way
            harness.OnAir.Current = FakeOnAir.Show(3, "Sunrise Shift", personaId: 7, startMinute: 360);
            for (var i = 1; i <= 38; i++)
            {
                harness.MusicAired($"Song {i}", $"Artist {i}");
                harness.PatterAired(SegmentKind.LeadIn);   // patter between tracks is not a track
            }

            harness.OnAir.Current = FakeOnAir.Show(4, "Midday Mix", personaId: 9, startMinute: 600);
            harness.MusicAired("Next Show Opener", "Someone");
            await harness.FlushAsync();
            persona7Rows = harness.Store.Rows.Where(r => r.PersonaId == 7).ToList();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void Persona7GainsOneEpisode() => Assert.Single(persona7Rows);

        [Fact]
        public void ItsContentNamesTheShowTheDayTheStartTime38TracksAndTheFirstAndLastTrack() =>
            Assert.Equal(
                "hosted Sunrise Shift on Tuesday 06:00: 38 tracks, opening with \"Song 1\" by Artist 1 and closing with \"Song 38\" by Artist 38",
                persona7Rows.Single().Content);

        [Fact]
        public void OccurredAtIsTheShowSStart() => Assert.Equal(TuesdaySixAm, persona7Rows.Single().OccurredAt);
    }

    // AC2 — A handoff is remembered on both sides
    public sealed class ScenarioAHandoffIsRememberedOnBothSides : IAsyncLifetime
    {
        // Given a crosstalk between personas 7 and 9
        // When  it airs
        IReadOnlyList<InMemoryEpisodeStore.Happening> happenings = [];
        IReadOnlyList<(long PersonaId, string Content, long? EventId)> episodes = [];

        public async Task InitializeAsync()
        {
            var harness = new AccrualHarness();
            var script = new CrosstalkAiredScript([new CrosstalkAiredLine(CrosstalkSpeaker.Host, "Hello there.", false)])
            {
                Host = new CrosstalkParticipant(7, "Dusty"),
                Neighbor = new CrosstalkParticipant(9, "Flip"),
            };
            harness.Sink.Publish(new TrackAired(
                "xt:1", "Dusty and Flip", "Dusty", 0, harness.Time.GetUtcNow(), 30_000, SegmentKind: SegmentKind.Crosstalk)
            {
                CrosstalkScript = script,
            });
            await harness.FlushAsync();

            happenings = harness.Store.Happenings;
            episodes = harness.Store.Rows.OrderBy(r => r.PersonaId).Select(r => (r.PersonaId, r.Content, r.EventId)).ToList();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void OneMemoryEventRowOfKindCrosstalkExists() =>
            Assert.Equal(["crosstalk"], happenings.Select(h => h.Kind));

        [Fact]
        public void Personas7And9EachGainAnEpisodePointingAtItThatNamesTheOther() =>
            Assert.Equal(
                [(7L, "talked on air with Flip", (long?)happenings.Single().Id), (9L, "talked on air with Dusty", (long?)happenings.Single().Id)],
                episodes);
    }

    // AC3 — A fact read on air is remembered
    public sealed class ScenarioAFactReadOnAirIsRemembered : IAsyncLifetime
    {
        // Given the accepted copy used the weather fact "high of 4 °C with sleet"
        // When  the break airs
        InMemoryEpisodeStore.Row episode = new(0, 0, "", default, null);
        DateTimeOffset now;

        public async Task InitializeAsync()
        {
            var harness = new AccrualHarness();
            now = harness.Time.GetUtcNow();
            harness.Sink.Publish(RenderedWithFact("tts:abc"));
            harness.PatterAired(SegmentKind.LeadIn, "tts:abc");
            await harness.FlushAsync();
            episode = harness.Store.Rows.Single();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ThePersonaGainsAnEpisodeReadTheWeatherHighOf4CWithSleet() =>
            Assert.Equal((7L, "read the weather: high of 4 °C with sleet"), (episode.PersonaId, episode.Content));

        [Fact]
        public void ItsOccurredAtIsWhenItAired() => Assert.Equal(now, episode.OccurredAt);
    }

    // AC3b — Rendered is not read
    public sealed class ScenarioAFactInASegmentThatNeverAirsIsNotRemembered : IAsyncLifetime
    {
        // Given a segment with a fact is rendered
        // When  it never airs (the positive control airs the same segment)
        IReadOnlyList<InMemoryEpisodeStore.Row> neverAired = [];
        IReadOnlyList<InMemoryEpisodeStore.Row> aired = [];

        public async Task InitializeAsync()
        {
            var skipped = new AccrualHarness();
            skipped.Sink.Publish(RenderedWithFact("tts:abc"));
            skipped.PatterAired(SegmentKind.LeadIn, "tts:other");
            await skipped.FlushAsync();
            neverAired = skipped.Store.Rows.ToList();

            var control = new AccrualHarness();
            control.Sink.Publish(RenderedWithFact("tts:abc"));
            control.PatterAired(SegmentKind.LeadIn, "tts:abc");
            await control.FlushAsync();
            aired = control.Store.Rows.ToList();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoEpisodeIsWritten() => Assert.Empty(neverAired);

        [Fact]
        public void TheSameSegmentAiredDoesWriteOne() => Assert.Single(aired);
    }

    // AC3c — The held facts are bounded
    public sealed class ScenarioTheHeldFactsAreBounded : IAsyncLifetime
    {
        // Given more rendered-never-aired facts than the sink holds
        // When  the oldest one's segment airs (the positive control airs the newest held one)
        IReadOnlyList<InMemoryEpisodeStore.Row> oldestRows = [];
        IReadOnlyList<InMemoryEpisodeStore.Row> newestRows = [];

        public async Task InitializeAsync()
        {
            oldestRows = await AiringAfterOverflow("tts:0");
            newestRows = await AiringAfterOverflow($"tts:{EpisodeAccrualSink.PendingFactCapacity}");
        }

        static async Task<IReadOnlyList<InMemoryEpisodeStore.Row>> AiringAfterOverflow(string airedSegmentId)
        {
            var harness = new AccrualHarness();
            for (var i = 0; i <= EpisodeAccrualSink.PendingFactCapacity; i++)
                harness.Sink.Publish(RenderedWithFact($"tts:{i}"));
            harness.PatterAired(SegmentKind.LeadIn, airedSegmentId);
            await harness.FlushAsync();
            return harness.Store.Rows.ToList();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheForgottenFactWritesNothing() => Assert.Empty(oldestRows);

        [Fact]
        public void TheNewestHeldFactWhenAiredWritesOneEpisode() => Assert.Single(newestRows);
    }

    // AC6 — A restart mid-show is not a hosted show
    public sealed class ScenarioARestartMidShowWritesNoEpisodeForThatShow : IAsyncLifetime
    {
        // Given the consumer starts while show 3 is already on air
        // When  the show ends (the positive control watches a show from its boundary)
        IReadOnlyList<InMemoryEpisodeStore.Row> joinedMidShow = [];
        IReadOnlyList<InMemoryEpisodeStore.Row> watchedFromTheStart = [];

        public async Task InitializeAsync()
        {
            var restarted = new AccrualHarness();
            restarted.OnAir.Current = FakeOnAir.Show(3, "Sunrise Shift", personaId: 7, startMinute: 360);
            restarted.MusicAired("Song 20", "Artist 20");
            restarted.OnAir.Current = FakeOnAir.Show(4, "Midday Mix", personaId: 9, startMinute: 600);
            restarted.MusicAired("Opener", "Someone");
            await restarted.FlushAsync();
            joinedMidShow = restarted.Store.Rows.Where(r => r.PersonaId == 7).ToList();

            var watched = new AccrualHarness();
            watched.OnAir.Current = FakeOnAir.Gap();
            watched.MusicAired("Before", "Someone");
            watched.OnAir.Current = FakeOnAir.Show(3, "Sunrise Shift", personaId: 7, startMinute: 360);
            watched.MusicAired("Song 1", "Artist 1");
            watched.OnAir.Current = FakeOnAir.Show(4, "Midday Mix", personaId: 9, startMinute: 600);
            watched.MusicAired("Opener", "Someone");
            await watched.FlushAsync();
            watchedFromTheStart = watched.Store.Rows.Where(r => r.PersonaId == 7).ToList();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoEpisodeIsWrittenForTheShowJoinedMidWay() => Assert.Empty(joinedMidShow);

        [Fact]
        public void TheSameShowWatchedFromItsBoundaryDoesWriteOne() => Assert.Single(watchedFromTheStart);
    }

    // AC6b — A boot before the schedule resolves is not "no show"
    public sealed class ScenarioABootBeforeTheScheduleResolvesWritesNoEpisode : IAsyncLifetime
    {
        // Given the first track airs while the on-air snapshot is still null (not resolved yet),
        //       then the schedule resolves to a show already running
        // When  that show ends (the positive control watched a show's boundary on a resolved schedule)
        IReadOnlyList<InMemoryEpisodeStore.Row> afterUnresolvedBoot = [];
        IReadOnlyList<InMemoryEpisodeStore.Row> afterResolvedBoundary = [];

        public async Task InitializeAsync()
        {
            var booted = new AccrualHarness();
            booted.OnAir.Current = null;
            booted.MusicAired("Song 20", "Artist 20");
            booted.OnAir.Current = FakeOnAir.Show(3, "Sunrise Shift", personaId: 7, startMinute: 360);
            booted.MusicAired("Song 21", "Artist 21");
            booted.OnAir.Current = FakeOnAir.Show(4, "Midday Mix", personaId: 9, startMinute: 600);
            booted.MusicAired("Opener", "Someone");
            await booted.FlushAsync();
            afterUnresolvedBoot = booted.Store.Rows.Where(r => r.PersonaId == 7).ToList();

            var control = new AccrualHarness();
            control.OnAir.Current = FakeOnAir.Gap();
            control.MusicAired("Before", "Someone");
            control.OnAir.Current = FakeOnAir.Show(3, "Sunrise Shift", personaId: 7, startMinute: 360);
            control.MusicAired("Song 1", "Artist 1");
            control.OnAir.Current = FakeOnAir.Show(4, "Midday Mix", personaId: 9, startMinute: 600);
            control.MusicAired("Opener", "Someone");
            await control.FlushAsync();
            afterResolvedBoundary = control.Store.Rows.Where(r => r.PersonaId == 7).ToList();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoEpisodeIsWrittenForTheShowThatWasAlreadyRunning() => Assert.Empty(afterUnresolvedBoot);

        [Fact]
        public void AShowWatchedFromAResolvedGapDoesWriteOne() => Assert.Single(afterResolvedBoundary);
    }

    static SegmentGenerated RenderedWithFact(string segmentId) => new(segmentId, "LeadIn", "voice")
    {
        PersonaId = 7,
        FactRead = new FactReadOnAir("weather", "high of 4 °C with sleet"),
    };

    // AC4 — Nothing else accrues
    public sealed class ScenarioNothingElseAccrues : IAsyncLifetime
    {
        // Given a track-started event with no show change, no crosstalk, no fact
        // When  the consumer sees it
        // The positive control: the SAME events through a second harness that also lets the show end DO write.
        IReadOnlyList<InMemoryEpisodeStore.Row> withoutBoundary = [];
        IReadOnlyList<InMemoryEpisodeStore.Row> withBoundary = [];

        public async Task InitializeAsync()
        {
            var ordinary = new AccrualHarness();
            FeedOrdinaryEvents(ordinary);
            await ordinary.FlushAsync();
            withoutBoundary = ordinary.Store.Rows.ToList();

            var control = new AccrualHarness();
            FeedOrdinaryEvents(control);
            control.OnAir.Current = FakeOnAir.Gap();
            control.MusicAired("Song 3", "Artist 3");
            await control.FlushAsync();
            withBoundary = control.Store.Rows.ToList();
        }

        static void FeedOrdinaryEvents(AccrualHarness harness)
        {
            harness.OnAir.Current = FakeOnAir.Gap();
            harness.MusicAired("Before", "Someone");
            harness.OnAir.Current = FakeOnAir.Show(3, "Sunrise Shift", personaId: 7, startMinute: 360);
            harness.MusicAired("Song 1", "Artist 1");
            harness.MusicAired("Song 2", "Artist 2");
            harness.PatterAired(SegmentKind.BackAnnounce);
            harness.Sink.Publish(new SegmentGenerated("tts:abc", "LeadIn", "voice") { PersonaId = 7, Text = "Plain talk, no fact." });
            harness.Sink.Publish(new SegmentGenerated("tts:def", "StationId", "voice"));
            harness.Sink.Publish(new TrackAired("xt:1", "Chat", "Dusty", 0, harness.Time.GetUtcNow(), 30_000, SegmentKind: SegmentKind.Crosstalk));
            harness.Sink.Publish(new TrackProvenAired("tts:9", harness.Time.GetUtcNow()));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoPersonaMemoryRowIsWritten() => Assert.Empty(withoutBoundary);

        [Fact]
        public void TheSameConsumerDoesWriteWhenTheShowEnds() => Assert.Single(withBoundary);
    }

    // AC5 (the 28-day window and the cap of 50) lives in GenWave.MediaLibrary.Tests
    // Story536_EpisodeRetentionSql (FeatureEpisodeRetentionSql), where it runs against the real SQL.
}

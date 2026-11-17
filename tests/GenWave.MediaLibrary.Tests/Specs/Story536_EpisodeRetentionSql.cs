// STORY-536 — Episode accrual SQL (SPEC F218.15, F218.16 · PLAN T702)
//
// BDD specification — xUnit. Postgres-backed (Category=Integration, shared DatabaseFixture). The shared
// memory_event write, the 28-day window and the cap of 50 run in one transaction on the database clock, so only a
// real database exercises them; the fake used by GenWave.Memory.Tests mirrors them and is pinned by this file.

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureEpisodeRetentionSql
{
    static PersonaMemoryRepository Repo(DatabaseFixture db) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource),
            Microsoft.Extensions.Options.Options.Create(new PersonaMemoryOptions()));

    static async Task<long> CreatePersonaAsync(DatabaseFixture db, string name)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<long>(
            "insert into station.persona (name) values (@name) returning id::bigint", new { name });
    }

    // ResetStationAsync truncates persona (and its cascade) but memory_event has no FK into persona.
    static async Task ResetMemoryEventsAsync(DatabaseFixture db)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        await conn.ExecuteAsync("truncate table station.memory_event restart identity cascade");
    }

    /// <summary>Plants a row whose happening is <paramref name="daysAgo"/> days back on the database clock, bypassing eviction.</summary>
    static async Task PlantAsync(DatabaseFixture db, long personaId, string kind, string content, string source, double daysAgo, double minutesAgo = 0)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            """
            insert into station.persona_memory (persona_id, kind, content, source, occurred_at)
            values (@personaId, @kind, @content, @source,
                    now() - make_interval(days => @daysAgo::int, mins => @minutesAgo::int))
            """,
            new { personaId, kind, content, source, daysAgo, minutesAgo });
    }

    static async Task<IReadOnlyList<string>> ContentsAsync(DatabaseFixture db, long personaId, string kind)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return (await conn.QueryAsync<string>(
            "select content from station.persona_memory where persona_id = @personaId and kind = @kind order by id",
            new { personaId, kind })).ToList();
    }

    // A crosstalk writes one shared event and an episode per side
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioACrosstalkIsOneEventAndAnEpisodePerSide(DatabaseFixture db) : IAsyncLifetime
    {
        IReadOnlyList<(string Kind, string Summary)> events = [];
        IReadOnlyList<(long PersonaId, string Kind, string Source, string Content, bool HasOccurredAt, bool PointsAtTheEvent)> rows = [];

        public async Task InitializeAsync()
        {
            await db.ResetStationAsync();
            await ResetMemoryEventsAsync(db);
            var dusty = await CreatePersonaAsync(db, "Dusty");
            var flip = await CreatePersonaAsync(db, "Flip");
            var at = DateTimeOffset.UtcNow;

            await Repo(db).AppendSharedAsync(
                new NewMemoryEvent("crosstalk", "Dusty and Flip talked on air", at),
                [new NewEpisode(dusty, "talked on air with Flip", at), new NewEpisode(flip, "talked on air with Dusty", at)],
                CancellationToken.None);

            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            events = (await conn.QueryAsync<(string, string)>("select kind, summary from station.memory_event")).ToList();
            rows = (await conn.QueryAsync<(long, string, string, string, bool, bool)>(
                """
                select p.persona_id::bigint, p.kind, p.source, p.content, p.occurred_at is not null,
                       p.event_id = (select id from station.memory_event)
                from station.persona_memory p order by p.persona_id
                """)).ToList();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void OneMemoryEventOfKindCrosstalkIsWritten() =>
            Assert.Equal([("crosstalk", "Dusty and Flip talked on air")], events);

        [Fact]
        public void EachSideGainsAnAccruedEpisodeWithATimePointingAtTheEvent() =>
            Assert.Equal(
                [
                    ("episode", "accrued", "talked on air with Flip", true, true),
                    ("episode", "accrued", "talked on air with Dusty", true, true),
                ],
                rows.Select(r => (r.Kind, r.Source, r.Content, r.HasOccurredAt, r.PointsAtTheEvent)));
    }

    // AC1 — A solo episode is stored as an accrued episode
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioASoloEpisodeIsStoredAsAnAccruedEpisode(DatabaseFixture db) : IAsyncLifetime
    {
        IReadOnlyList<(string Kind, string Source)> rows = [];

        public async Task InitializeAsync()
        {
            await db.ResetStationAsync();
            var persona = await CreatePersonaAsync(db, "Solo DJ");
            await Repo(db).AppendAsync(new NewEpisode(persona, "hosted a show", DateTimeOffset.UtcNow), CancellationToken.None);

            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            rows = (await conn.QueryAsync<(string, string)>(
                "select kind, source from station.persona_memory where persona_id = @persona", new { persona })).ToList();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheRowIsKindEpisode() => Assert.Equal(["episode"], rows.Select(r => r.Kind));

        [Fact]
        public void TheRowIsSourceAccrued() => Assert.Equal(["accrued"], rows.Select(r => r.Source));
    }

    // A station-local (-06:00) happening is stored as the same instant (Npgsql accepts only offset 0)
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAStationLocalHappeningKeepsItsInstant(DatabaseFixture db) : IAsyncLifetime
    {
        static readonly DateTimeOffset LocalShiftStart = new(2026, 11, 15, 6, 0, 0, TimeSpan.FromHours(-6));

        DateTime soloEpisodeAt;
        DateTime sharedEventAt;
        DateTime sharedEpisodeAt;

        public async Task InitializeAsync()
        {
            await db.ResetStationAsync();
            await ResetMemoryEventsAsync(db);
            var solo = await CreatePersonaAsync(db, "Shift DJ");
            var duo = await CreatePersonaAsync(db, "Duo DJ");

            await Repo(db).AppendAsync(new NewEpisode(solo, "hosted a shift", LocalShiftStart), CancellationToken.None);
            await Repo(db).AppendSharedAsync(
                new NewMemoryEvent("crosstalk", "talked on air", LocalShiftStart),
                [new NewEpisode(duo, "talked on air", LocalShiftStart)],
                CancellationToken.None);

            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            soloEpisodeAt = await conn.QuerySingleAsync<DateTime>(
                "select occurred_at from station.persona_memory where persona_id = @solo", new { solo });
            sharedEpisodeAt = await conn.QuerySingleAsync<DateTime>(
                "select occurred_at from station.persona_memory where persona_id = @duo", new { duo });
            sharedEventAt = await conn.QuerySingleAsync<DateTime>("select occurred_at from station.memory_event");
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ASoloEpisodeReadsBackTheSameInstant() => Assert.Equal(LocalShiftStart.UtcDateTime, soloEpisodeAt);

        [Fact]
        public void ASharedEventReadsBackTheSameInstant() => Assert.Equal(LocalShiftStart.UtcDateTime, sharedEventAt);

        [Fact]
        public void ASharedEpisodeReadsBackTheSameInstant() => Assert.Equal(LocalShiftStart.UtcDateTime, sharedEpisodeAt);
    }

    // AC5 — 50 recent episodes plus one 29 days old, then a new one
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheWindowAndTheCap(DatabaseFixture db) : IAsyncLifetime
    {
        IReadOnlyList<string> contents = [];

        public async Task InitializeAsync()
        {
            await db.ResetStationAsync();
            var persona = await CreatePersonaAsync(db, "Cap DJ");
            await PlantAsync(db, persona, MemoryKinds.Episode, "episode 0 (29 days old)", "accrued", daysAgo: 29);
            for (var i = 1; i <= 50; i++)
                await PlantAsync(db, persona, MemoryKinds.Episode, $"episode {i}", "accrued", daysAgo: 1, minutesAgo: 51 - i);

            await Repo(db).AppendAsync(new NewEpisode(persona, "the new one", DateTimeOffset.UtcNow), CancellationToken.None);
            contents = await ContentsAsync(db, persona, MemoryKinds.Episode);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void The29DayOldRowIsEvicted() => Assert.DoesNotContain("episode 0 (29 days old)", contents);

        [Fact]
        public void TheOldestRecentRowIsEvictedByTheCap() => Assert.DoesNotContain("episode 1", contents);

        [Fact]
        public void TheCountIs50() => Assert.Equal(50, contents.Count);
    }

    // The window evicts on its own, well under the cap, and only accrued episodes
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioOldEpisodesLeaveWithoutTheCap(DatabaseFixture db) : IAsyncLifetime
    {
        IReadOnlyList<string> episodes = [];
        IReadOnlyList<string> quirks = [];

        public async Task InitializeAsync()
        {
            await db.ResetStationAsync();
            var persona = await CreatePersonaAsync(db, "Window DJ");
            await PlantAsync(db, persona, MemoryKinds.Episode, "accrued, 29 days old", "accrued", daysAgo: 29);
            await PlantAsync(db, persona, MemoryKinds.Episode, "authored, 29 days old", "authored", daysAgo: 29);
            await PlantAsync(db, persona, MemoryKinds.Episode, "accrued, 27 days old", "accrued", daysAgo: 27);
            await PlantAsync(db, persona, MemoryKinds.Quirk, "accrued quirk, 29 days old", "accrued", daysAgo: 29);

            await Repo(db).AppendAsync(new NewEpisode(persona, "the new one", DateTimeOffset.UtcNow), CancellationToken.None);
            episodes = await ContentsAsync(db, persona, MemoryKinds.Episode);
            quirks = await ContentsAsync(db, persona, MemoryKinds.Quirk);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void OnlyTheOldAccruedEpisodeIsEvicted() =>
            Assert.Equal(["authored, 29 days old", "accrued, 27 days old", "the new one"], episodes);

        [Fact]
        public void AnOldQuirkIsNotAnEpisodeAndStays() => Assert.Equal(["accrued quirk, 29 days old"], quirks);
    }

    // gh-#1010: a shared event lives while either side remembers it, and goes once both episodes are evicted
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAnEventGoesWithItsLastEpisode(DatabaseFixture db) : IAsyncLifetime
    {
        long eventsAfterOneSideForgot;
        long eventsAfterBothForgot;
        long freshEventsAfterBothForgot;

        static async Task<long> CountEventsAsync(DatabaseFixture db, string summary)
        {
            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            return await conn.QuerySingleAsync<long>(
                "select count(*) from station.memory_event where summary = @summary", new { summary });
        }

        public async Task InitializeAsync()
        {
            await db.ResetStationAsync();
            await ResetMemoryEventsAsync(db);
            var dusty = await CreatePersonaAsync(db, "Dusty");
            var flip = await CreatePersonaAsync(db, "Flip");

            await using (var conn = await db.StationDataSource.OpenConnectionAsync())
                await conn.ExecuteAsync(
                    """
                    with e as (
                        insert into station.memory_event (occurred_at, kind, summary)
                        values (now() - interval '29 days', 'crosstalk', 'old crosstalk')
                        returning id)
                    insert into station.persona_memory (persona_id, kind, content, source, occurred_at, event_id)
                    select p, 'episode', 'talked on air, 29 days ago', 'accrued', now() - interval '29 days', e.id
                    from e, unnest(array[@dusty, @flip]::integer[]) as p
                    """,
                    new { dusty, flip });

            var at = DateTimeOffset.UtcNow;
            await Repo(db).AppendAsync(new NewEpisode(dusty, "a new day", at), CancellationToken.None);
            eventsAfterOneSideForgot = await CountEventsAsync(db, "old crosstalk");

            await Repo(db).AppendSharedAsync(
                new NewMemoryEvent("crosstalk", "fresh crosstalk", at),
                [new NewEpisode(dusty, "talked again", at), new NewEpisode(flip, "talked again", at)],
                CancellationToken.None);
            eventsAfterBothForgot = await CountEventsAsync(db, "old crosstalk");
            freshEventsAfterBothForgot = await CountEventsAsync(db, "fresh crosstalk");
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheEventSurvivesWhileFlipStillRemembersIt() => Assert.Equal(1, eventsAfterOneSideForgot);

        [Fact]
        public void TheEventIsGoneOnceBothEpisodesAreEvicted() => Assert.Equal(0, eventsAfterBothForgot);

        [Fact]
        public void TheNewEventStays() => Assert.Equal(1, freshEventsAfterBothForgot);
    }
}

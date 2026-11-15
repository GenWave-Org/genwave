// STORY-537 — Life claim SQL (SPEC F218.18 · PLAN T704)
//
// BDD specification — xUnit. Postgres-backed (Category=Integration, shared DatabaseFixture). The dedupe, the outing
// transaction and the cap of 100 run in one transaction on the database, so only a real database exercises them. The
// fake used by GenWave.Memory.Tests is a simplified stand-in (it has no forgotten rows); the real rule is pinned here.

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureLifeRetentionSql
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

    static async Task ResetAsync(DatabaseFixture db)
    {
        await db.ResetStationAsync();
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        await conn.ExecuteAsync("truncate table station.memory_event restart identity cascade");
    }

    /// <summary>Plants an extracted life row created <paramref name="minutesAgo"/> minutes back, aired or not, bypassing the cap.</summary>
    static async Task PlantAsync(DatabaseFixture db, long personaId, string content, int minutesAgo, bool aired = false, string source = "extracted")
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            """
            insert into station.persona_memory (persona_id, kind, content, source, created_at, last_aired_at, aired_count)
            values (@personaId, 'life', @content, @source, now() - make_interval(mins => @minutesAgo),
                    case when @aired then now() - make_interval(mins => @minutesAgo) end, case when @aired then 1 else 0 end)
            """,
            new { personaId, content, source, minutesAgo, aired });
    }

    static async Task<IReadOnlyList<string>> LifeAsync(DatabaseFixture db, long personaId)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return (await conn.QueryAsync<string>(
            "select content from station.persona_memory where persona_id = @personaId and kind = 'life' order by id",
            new { personaId })).ToList();
    }

    // A claim is stored once
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAClaimIsStoredOnce(DatabaseFixture db) : IAsyncLifetime
    {
        long sal;
        bool first;
        bool again;

        public async Task InitializeAsync()
        {
            await ResetAsync(db);
            sal = await CreatePersonaAsync(db, "Sal");
            var repo = Repo(db);
            first = await repo.AddAsync(new NewLifeClaim(sal, "has a beagle named Gus"), CancellationToken.None);
            again = await repo.AddAsync(new NewLifeClaim(sal, "Has a  beagle named Gus!"), CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheFirstClaimIsWritten() => Assert.True(first);

        [Fact]
        public void TheSameClaimInAnotherCaseOrPunctuationIsNot() => Assert.False(again);

        [Fact]
        public async Task OneRowKindLifeSourceExtractedWithNoTimeExists()
        {
            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            var rows = (await conn.QueryAsync<(string, string, string, bool)>(
                "select kind, source, content, occurred_at is null from station.persona_memory where persona_id = @sal", new { sal })).ToList();

            Assert.Equal([("life", "extracted", "has a beagle named Gus", true)], rows);
        }
    }

    // A forgotten claim is still a duplicate, and so is another persona's claim not
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAForgottenClaimIsNotRevived(DatabaseFixture db) : IAsyncLifetime
    {
        long sal;
        long flip;

        public async Task InitializeAsync()
        {
            await ResetAsync(db);
            sal = await CreatePersonaAsync(db, "Sal");
            flip = await CreatePersonaAsync(db, "Flip");
            await PlantAsync(db, sal, "has a beagle named Gus", 5);
            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            await conn.ExecuteAsync("update station.persona_memory set forgotten_at = now()");
            var repo = Repo(db);
            await repo.AddAsync(new NewLifeClaim(sal, "has a beagle named Gus"), CancellationToken.None);
            await repo.AddAsync(new NewLifeClaim(flip, "has a beagle named Gus"), CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task TheForgottenRowIsTheOnlyRowForSal() => Assert.Single(await LifeAsync(db, sal));

        [Fact]
        public async Task FlipStillGainsTheClaim() => Assert.Single(await LifeAsync(db, flip));
    }

    // An outing is one event and a row each side
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAnOutingIsOneEventAndARowEachSide(DatabaseFixture db) : IAsyncLifetime
    {
        IReadOnlyList<(string Kind, string Summary)> events = [];
        IReadOnlyList<(string Content, string Source, bool PointsAtTheEvent)> rows = [];
        long eventsAfterRepeat;

        public async Task InitializeAsync()
        {
            await ResetAsync(db);
            var sal = await CreatePersonaAsync(db, "Sal");
            var flip = await CreatePersonaAsync(db, "Captain Flip");
            // A station-local offset: Npgsql only writes offset 0 to timestamptz.
            var at = new DateTimeOffset(2026, 11, 10, 6, 0, 0, TimeSpan.FromHours(-6));
            var outing = new NewMemoryEvent(MemoryEventKinds.Outing, "Sal and Captain Flip: tacos", at);
            NewLifeClaim[] claims = [new(sal, "grabbed tacos with Flip"), new(flip, "grabbed tacos with Sal")];

            var repo = Repo(db);
            await repo.AddOutingAsync(outing, claims, CancellationToken.None);
            await repo.AddOutingAsync(outing, claims, CancellationToken.None);

            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            eventsAfterRepeat = await conn.QuerySingleAsync<long>("select count(*) from station.memory_event");
            events = (await conn.QueryAsync<(string, string)>("select kind, summary from station.memory_event")).ToList();
            rows = (await conn.QueryAsync<(string, string, bool)>(
                """
                select content, source, event_id = (select id from station.memory_event)
                from station.persona_memory where kind = 'life' order by persona_id
                """)).ToList();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void OneMemoryEventOfKindOutingIsWritten() => Assert.Equal([("outing", "Sal and Captain Flip: tacos")], events);

        [Fact]
        public void EachSideGainsAnExtractedLifeRowPointingAtIt() => Assert.Equal(
            [("grabbed tacos with Flip", "extracted", true), ("grabbed tacos with Sal", "extracted", true)], rows);

        [Fact]
        public void AnOutingBothSidesAlreadyHaveWritesNoSecondEvent() => Assert.Equal(1, eventsAfterRepeat);
    }

    // The cap evicts the oldest never-aired row
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheCapEvictsTheOldestNeverAiredRow(DatabaseFixture db) : IAsyncLifetime
    {
        IReadOnlyList<string> contents = [];

        public async Task InitializeAsync()
        {
            await ResetAsync(db);
            var sal = await CreatePersonaAsync(db, "Sal");
            // claim 1 is the oldest and has aired; claim 2 is the oldest never-aired; an authored life row is not counted.
            for (var n = 1; n <= LifeMemoryRules.Cap; n++)
                await PlantAsync(db, sal, $"claim {n}", minutesAgo: 1000 - n, aired: n == 1);
            await PlantAsync(db, sal, "authored life", minutesAgo: 2000, source: "authored");

            await Repo(db).AddAsync(new NewLifeClaim(sal, "plays the banjo"), CancellationToken.None);
            contents = await LifeAsync(db, sal);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheOldestNeverAiredRowIsGone() => Assert.DoesNotContain("claim 2", contents);

        [Fact]
        public void TheOldestRowThatHasAiredStays() => Assert.Contains("claim 1", contents);

        [Fact]
        public void TheNewClaimStaysAndAuthoredLifeIsNotCounted() =>
            Assert.Equal(LifeMemoryRules.Cap + 1, contents.Count);

        [Fact]
        public void TheNewClaimIsKept() => Assert.Contains("plays the banjo", contents);
    }

    // When every row has aired, the least recently aired goes, never the new claim
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAllAiredEvictsTheLeastRecentlyAiredNotTheNewClaim(DatabaseFixture db) : IAsyncLifetime
    {
        IReadOnlyList<string> contents = [];

        public async Task InitializeAsync()
        {
            await ResetAsync(db);
            var sal = await CreatePersonaAsync(db, "Sal");
            for (var n = 1; n <= LifeMemoryRules.Cap; n++)
                await PlantAsync(db, sal, $"claim {n}", minutesAgo: 1000 - n, aired: true);

            await Repo(db).AddAsync(new NewLifeClaim(sal, "plays the banjo"), CancellationToken.None);
            contents = await LifeAsync(db, sal);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheLeastRecentlyAiredRowIsGone() => Assert.DoesNotContain("claim 1", contents);

        [Fact]
        public void TheNewNeverAiredClaimIsKept() => Assert.Contains("plays the banjo", contents);

        [Fact]
        public void TheCountIsTheCap() => Assert.Equal(LifeMemoryRules.Cap, contents.Count);
    }

    // The cap neither counts nor evicts a forgotten row, so its tombstone keeps the claim from being learned again
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheCapKeepsAForgottenTombstone(DatabaseFixture db) : IAsyncLifetime
    {
        const string Forgotten = "has a beagle named Gus";

        IReadOnlyList<string> contents = [];
        bool relearned;

        public async Task InitializeAsync()
        {
            await ResetAsync(db);
            var sal = await CreatePersonaAsync(db, "Sal");
            // The forgotten row is the oldest never-aired row of all, so an eviction that counted it would take it first.
            await PlantAsync(db, sal, Forgotten, minutesAgo: 2000);
            await using (var conn = await db.StationDataSource.OpenConnectionAsync())
                await conn.ExecuteAsync("update station.persona_memory set forgotten_at = now()");
            for (var n = 1; n <= LifeMemoryRules.Cap; n++)
                await PlantAsync(db, sal, $"claim {n}", minutesAgo: 1000 - n);

            var repo = Repo(db);
            await repo.AddAsync(new NewLifeClaim(sal, "plays the banjo"), CancellationToken.None);
            relearned = await repo.AddAsync(new NewLifeClaim(sal, Forgotten), CancellationToken.None);
            contents = await LifeAsync(db, sal);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ALiveNeverAiredRowIsEvictedInstead() => Assert.DoesNotContain("claim 1", contents);

        [Fact]
        public void TheForgottenRowSurvives() => Assert.Contains(Forgotten, contents);

        [Fact]
        public void OnlyOneLiveRowWentSoTheRestStay() => Assert.Contains("claim 2", contents);

        [Fact]
        public void ReExtractingTheForgottenClaimWritesNothing() => Assert.False(relearned);
    }
}

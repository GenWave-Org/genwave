// STORY-532 — Every DJ line is on the record (gh-#293 gh-#920 · SPEC F218 · PLAN T682)
//
// BDD specification — xUnit, REAL Postgres via the shared DatabaseFixture (the behaviour under test
// IS the SQL: a NULL show_id, the insert-time retention sweep). AC5/AC7 (T682, IUtteranceStore) live
// here rather than in GenWave.Memory.Tests so GenWave.Memory keeps its "Core + Abstractions only" edge.
//
// Spans three files; see tests/GenWave.Tts.Tests/Specs/Story532_EveryDjLineIsOnTheRecord_Copy.cs for the map.

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureEveryDjLineIsOnTheRecordStore
{
    /// <summary>The repository under test plus the persona/row plumbing both scenarios share.</summary>
    sealed class UtteranceFixture(DatabaseFixture db)
    {
        public Core.Abstractions.IUtteranceStore Store { get; } = new UtteranceRepository(
            new Lazy<Npgsql.NpgsqlDataSource>(() => db.StationDataSource),
            new FakeOptionsMonitor<UtteranceOptions>(new UtteranceOptions()));

        public async Task<long> InsertPersonaAsync(string name)
        {
            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            return await conn.ExecuteScalarAsync<long>(
                "insert into station.persona (name) values (@name) returning id::bigint", new { name });
        }
    }

    static NewUtterance Line(long personaId, ShiftKey key, string text) =>
        new(personaId, ShowId: null, UtteranceKind.Patter, "LeadIn", key, text, Fallback: false);

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC5 — unscheduled hours have no show
    // entry point: IUtteranceStore.RecordAsync (via LlmCopyWriter.WriteAsync)
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioUnscheduledHoursHaveNoShow(DatabaseFixture db)
    {
        // Given no schedule segment is on air
        // When  a copy is stored

        static readonly DateOnly Today = new(2026, 11, 8);

        async Task<(long PersonaId, UtteranceEntry Entry)> StoreAsync()
        {
            await db.ResetStationAsync();
            var repo = new UtteranceFixture(db);
            var personaId = await repo.InsertPersonaAsync("Unscheduled DJ");
            await repo.Store.RecordAsync(Line(personaId, ShiftKey.Unscheduled(personaId, Today), "Good evening."), CancellationToken.None);
            var entry = Assert.Single(await repo.Store.RecentAsync(personaId, 5, CancellationToken.None));
            return (personaId, entry);
        }

        [Fact]
        public async Task ShowIdIsNull()
        {
            var (_, entry) = await StoreAsync();

            Assert.Null(entry.ShowId);
        }

        [Fact]
        public async Task ShiftKeyIsThePersonaIdPlusTheStationLocalDate()
        {
            var (personaId, entry) = await StoreAsync();

            Assert.Equal($"{personaId}|2026-11-08", entry.ShiftKey.ToString());
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC7 — old rows go
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioOldRowsGo(DatabaseFixture db)
    {
        // Given a row written 29 days ago
        // When  a new utterance is inserted

        [Fact]
        public async Task The29DayOldRowIsGone()
        {
            await db.ResetStationAsync();
            var repo = new UtteranceFixture(db);
            var personaId = await repo.InsertPersonaAsync("Retention DJ");
            var key = ShiftKey.Unscheduled(personaId, new DateOnly(2026, 11, 8));
            var oldId = await repo.Store.RecordAsync(Line(personaId, key, "An old line."), CancellationToken.None);
            await using (var conn = await db.StationDataSource.OpenConnectionAsync())
            {
                await conn.ExecuteAsync(
                    "update station.utterance set written_at = now() - interval '29 days' where id = @oldId", new { oldId });
            }

            await repo.Store.RecordAsync(Line(personaId, key, "A new line."), CancellationToken.None);

            var texts = (await repo.Store.RecentAsync(personaId, 10, CancellationToken.None)).Select(e => e.Text);
            Assert.Equal(["A new line."], texts);
        }
    }
}

// STORY-533 — A once-a-shift quirk fires once a shift (gh-#293 · SPEC F218.7 · PLAN T686)
//
// The Postgres-backed IQuirkSpendLedger: exact-content counting, and the F71.6 eviction trap — spend
// rows are kind quirk / accrued, so the per-kind cap evicts the OLDEST first and a current-shift row
// survives. Category=Integration (shared DatabaseFixture).

using Dapper;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureQuirkSpendLedger
{
    const string Current = "Bird fact @ 7|Tuesday|360|2026-11-10";

    static PersonaMemoryRepository Repo(DatabaseFixture db, int capPerKind) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource),
            Microsoft.Extensions.Options.Options.Create(new PersonaMemoryOptions { CapPerKind = capPerKind }));

    static async Task<long> CreatePersonaAsync(DatabaseFixture db)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<long>(
            "insert into station.persona (name) values ('Spender') returning id::bigint");
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioCounting(DatabaseFixture db)
    {
        [Fact]
        public async Task ASpendCountsOnlyForItsExactShiftContent()
        {
            // Given spends for this shift and for the same slot the following week
            await db.ResetStationAsync();
            var personaId = await CreatePersonaAsync(db);
            var repo = Repo(db, capPerKind: 50);

            await repo.RecordSpendAsync(personaId, Current, CancellationToken.None);
            await repo.RecordSpendAsync(personaId, "Bird fact @ 7|Tuesday|360|2026-11-17", CancellationToken.None);

            // When  the current shift's spends are counted
            // Then  only its own row is counted
            Assert.Equal(1, await repo.CountSpendsAsync(personaId, Current, CancellationToken.None));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioEviction(DatabaseFixture db)
    {
        [Fact]
        public async Task TheCurrentShiftSpendSurvivesTheCapEvictingOlderShifts()
        {
            // Given a cap of 3 and three spends from earlier shifts
            await db.ResetStationAsync();
            var personaId = await CreatePersonaAsync(db);
            var repo = Repo(db, capPerKind: 3);
            for (var week = 0; week < 3; week++)
                await repo.RecordSpendAsync(personaId, $"Bird fact @ 7|Tuesday|360|2026-10-{10 + week}", CancellationToken.None);

            // When the current shift spends (the cap is now exceeded)
            await repo.RecordSpendAsync(personaId, Current, CancellationToken.None);

            // Then the current-shift row is still counted
            Assert.Equal(1, await repo.CountSpendsAsync(personaId, Current, CancellationToken.None));
        }
    }
}

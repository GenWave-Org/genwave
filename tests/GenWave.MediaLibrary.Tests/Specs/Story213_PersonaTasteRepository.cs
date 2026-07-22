// STORY-213 — Persona taste has a home
//
// BDD specification — xUnit (SPEC F82.1). Postgres-backed (Category=Integration, shared
// DatabaseFixture) — the CHECK constraint's teeth, jsonb equality for upsert-by-identity, and cascade
// delete are SQL-side behavior a fake store would never exercise honestly. T59 lands db/06
// (fresh-install shape) + db/16 (idempotent migration) + PersonaTasteRepository; no consumer yet —
// the ranker (T63) and card import/accrual (T66-T70) are later tasks.

using System.Text.Json;
using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeaturePersonaTasteRepository
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static PersonaTasteRepository Repo(DatabaseFixture db) => new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));

    static async Task<long> CreatePersonaAsync(DatabaseFixture db, string name)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<long>(
            "insert into station.persona (name) values (@name) returning id::bigint", new { name });
    }

    /// <summary>
    /// Every field populated, including the PRD's Sunday-Zeppelin shape (day-of-week + hour gate).
    /// Weight (0.75) is a power-of-two fraction — exactly representable in both <see langword="float"/>
    /// (the column's storage width) and <see langword="double"/> (<see cref="TasteRule.Weight"/>'s
    /// type) — so a round trip through the <c>real</c> column loses no precision for this assertion to
    /// trip over; an arbitrary decimal like 0.8 would not survive that narrowing exactly.
    /// </summary>
    static readonly TasteRule SundayZeppelin = new(
        Predicate: new TastePredicate(Artist: "Led Zeppelin", Genre: null, Tag: null),
        Context: new TasteContext(DaysOfWeek: [DayOfWeek.Sunday], StartHour: 6, EndHour: 12),
        Weight: 0.75);

    /// <summary>
    /// Round-trips <paramref name="rule"/> through the same JSON convention
    /// <see cref="PersonaTasteRepository"/> itself uses for <c>predicate</c>/<c>context</c>
    /// (<see cref="PersonaCardSerializer.Options"/>) — comparing canonical JSON strings sidesteps
    /// <see cref="TasteContext.DaysOfWeek"/>'s <see cref="IReadOnlyList{T}"/> field never overriding
    /// value equality (two structurally-identical lists from two different reads are NOT "equal" via
    /// <see cref="TasteRule"/>'s own record-generated <c>Equals</c>), while still asserting every
    /// predicate/context/weight field in one comparison.
    /// </summary>
    static string CanonicalJson(TasteRule rule) => JsonSerializer.Serialize(rule, PersonaCardSerializer.Options);

    // ---------------------------------------------------------------------
    // HAPPY PATH — round trip (F82.1)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRoundTrip(DatabaseFixture db)
    {
        [Fact]
        public async Task InsertedRuleRoundTripsEveryPredicateContextAndWeightField()
        {
            // Given a persona and an authored taste rule with every predicate/context field populated...
            await db.ResetStationAsync();
            var personaId = await CreatePersonaAsync(db, "Sunday DJ");
            var repo = Repo(db);
            await repo.InsertAsync(personaId, SundayZeppelin, PersonaTasteSource.Authored, CancellationToken.None);

            // When it is listed back...
            var listed = await repo.ListAsync(personaId, source: null, CancellationToken.None);

            // Then the round-tripped rule is identical, field for field, to the one inserted.
            Assert.Equal(CanonicalJson(SundayZeppelin), CanonicalJson(Assert.Single(listed).Rule));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — source filter (F82.1)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioSourceFilter(DatabaseFixture db)
    {
        [Fact]
        public async Task ListingFilteredToOneSourceExcludesEveryOther()
        {
            // Given a persona with one authored rule and one accrued rule...
            await db.ResetStationAsync();
            var personaId = await CreatePersonaAsync(db, "Filter DJ");
            var repo = Repo(db);
            await repo.InsertAsync(personaId, SundayZeppelin, PersonaTasteSource.Authored, CancellationToken.None);
            var accruedRule = new TasteRule(
                new TastePredicate(Artist: null, Genre: "Smooth Jazz", Tag: null),
                new TasteContext(DaysOfWeek: [], StartHour: null, EndHour: null),
                Weight: -0.5);
            await repo.InsertAsync(personaId, accruedRule, PersonaTasteSource.Accrued, CancellationToken.None);

            // When listing filtered to source=authored...
            var listed = await repo.ListAsync(personaId, PersonaTasteSource.Authored, CancellationToken.None);

            // Then only the authored rule returns — the accrued rule is excluded.
            Assert.Equal(PersonaTasteSource.Authored, Assert.Single(listed).Source);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — upsert-by-identity (F82.1; the primitive import/accrual build on)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioUpsertByIdentity(DatabaseFixture db)
    {
        [Fact]
        public async Task ReplacingTheSamePredicateAndContextUpdatesTheExistingRowInPlace()
        {
            // Given a persona with one rule already recorded via Replace...
            await db.ResetStationAsync();
            var personaId = await CreatePersonaAsync(db, "Nudge DJ");
            var repo = Repo(db);
            var firstId = await repo.ReplaceAsync(personaId, SundayZeppelin, PersonaTasteSource.Authored, CancellationToken.None);

            // When Replace runs again with the identical predicate/context but a different weight
            // (the accrual thumb's own "nudge" shape, minus its ±0.2 clamp math which is T70's)...
            var nudged = SundayZeppelin with { Weight = 1.0 };
            var secondId = await repo.ReplaceAsync(personaId, nudged, PersonaTasteSource.Authored, CancellationToken.None);

            // Then the same row is updated in place — no duplicate row is created.
            Assert.Equal(firstId, secondId);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — cascade delete (F82.1 FK CASCADE)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioCascadeDelete(DatabaseFixture db)
    {
        [Fact]
        public async Task DeletingThePersonaCascadesItsTasteRows()
        {
            // Given a persona with an authored taste rule...
            await db.ResetStationAsync();
            var personaId = await CreatePersonaAsync(db, "Cascade DJ");
            var repo = Repo(db);
            await repo.InsertAsync(personaId, SundayZeppelin, PersonaTasteSource.Authored, CancellationToken.None);

            // When the persona itself is deleted, through the FK's owning repository...
            var personaRepo = new PersonaRepository(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));
            await personaRepo.DeleteAsync(personaId, CancellationToken.None);

            // Then its taste rows are gone too — never orphaned.
            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            var remaining = await conn.ExecuteScalarAsync<long>(
                "select count(*) from station.persona_taste where persona_id = @personaId", new { personaId });
            Assert.Equal(0, remaining);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — weight CHECK rejection (F82.1)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class SadPathWeightCheck(DatabaseFixture db)
    {
        [Fact]
        public async Task DirectInsertOutsideMinusOneToOneIsRejectedByTheDatabase()
        {
            // Given a persona row to attach the (rejected) taste row to...
            await db.ResetStationAsync();
            var personaId = await CreatePersonaAsync(db, "Out Of Range DJ");

            // When a direct SQL insert supplies weight = 1.5 — outside the CHECK's [-1, 1] — bypassing
            // TasteRule's own C# constructor guard entirely (this proves the DB's own teeth, not the
            // C# type's)...
            await using var conn = await db.StationDataSource.OpenConnectionAsync();

            // Then the database itself rejects it.
            await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
                """
                insert into station.persona_taste (persona_id, predicate, context, weight, source)
                values (@personaId, '{}'::jsonb, '{}'::jsonb, 1.5, 'authored')
                """,
                new { personaId }));
        }
    }
}

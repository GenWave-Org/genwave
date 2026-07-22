// STORY-215 — Accrual write path at the repository layer (T70)
//
// BDD specification — xUnit (SPEC F84.1, F84.3, F84.5, F84.6). Postgres-backed (Category=Integration,
// shared DatabaseFixture), same convention as Story209_PersonaImportRepository.cs — this file proves
// exactly the plumbing a fake could never exercise honestly: the nudge's read-modify-write actually
// running as ONE transaction with the cap-50 eviction (F84.3), the (persona, airing, direction)
// idempotency ledger being a DURABLE row rather than an in-memory cache (F84.5), and the
// pg_advisory_xact_lock serialization the CARRIED T59 review note called for actually preventing a
// lost update under REAL concurrency. GenWave.Host.Tests/Specs/Story215_TasteLearningGuardrails.cs's
// own REPLICATING fake covers the equivalent business rules at the controller layer (per the T60
// precedent — Host.Tests has no station-Postgres convention).

using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureTasteAccrualRepository
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static PersonaTasteAccrualRepository AccrualRepo(DatabaseFixture db) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));

    static BoothLogRepository BoothLogRepo(DatabaseFixture db) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource), Microsoft.Extensions.Options.Options.Create(new BoothLogOptions()));

    static PersonaTasteRepository TasteRepo(DatabaseFixture db) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));

    static PersonaRepository PersonaRepo(DatabaseFixture db) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));

    static async Task<long> CreatePersonaAsync(DatabaseFixture db, string name)
    {
        var result = await PersonaRepo(db).CreateAsync(new PersonaDraft(name, "", "", ""), CancellationToken.None);
        return Assert.IsType<PersonaWriteResult.Created>(result).Persona.Id;
    }

    /// <summary>
    /// Appends one real track-start row through <see cref="BoothLogRepository.AppendAsync"/> (the
    /// exact write path <c>BoothLogDrainService</c> uses) and reads its assigned id back — the
    /// simplest honest way to get a real, attributable booth-log row id for
    /// <see cref="PersonaTasteAccrualRepository.ThumbAsync"/> to operate on.
    /// </summary>
    static async Task<long> SeedTrackRowAsync(DatabaseFixture db, long? personaId, string? artist)
    {
        var repo = BoothLogRepo(db);
        await repo.AppendAsync("track-started", "Started 'Song' by Someone", personaId, artist, pick: null, CancellationToken.None);
        var page = await repo.ReadAsync(before: null, take: 1, CancellationToken.None);
        return page.Entries.Single().Id;
    }

    static readonly TasteContext NoGate = new([], null, null);

    // ---------------------------------------------------------------------
    // HAPPY PATH — the nudge writes the accrued artist rule (F84.1)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioNudgeWritesTheAccruedArtistRule(DatabaseFixture db)
    {
        [Fact]
        public async Task AFirstThumbInsertsAnAccruedArtistRuleAtStepWeight()
        {
            await db.ResetStationAsync();
            await db.ResetBoothLogAsync();
            var personaId = await CreatePersonaAsync(db, "Nova");
            var rowId = await SeedTrackRowAsync(db, personaId, "The Waveforms");

            var outcome = await AccrualRepo(db).ThumbAsync(rowId, TasteThumbDirection.Up, CancellationToken.None);

            var nudged = Assert.IsType<TasteThumbOutcome.Nudged>(outcome);
            Assert.Equal(personaId, nudged.PersonaId);
            Assert.Equal(0.2, nudged.Weight, 3);

            var taste = await TasteRepo(db).ListAsync(personaId, PersonaTasteSource.Accrued, CancellationToken.None);
            var rule = Assert.Single(taste);
            Assert.Equal("The Waveforms", rule.Rule.Predicate.Artist);
            Assert.Equal(0.2, rule.Rule.Weight, 3);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — cap-50-weakest-evicted runs IN THE SAME TRANSACTION as the nudge (F84.3)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioCapEvictionInOneTransaction(DatabaseFixture db)
    {
        [Fact]
        public async Task TheWeakestAccruedRuleIsEvictedAtomicallyWithTheNudge()
        {
            await db.ResetStationAsync();
            await db.ResetBoothLogAsync();
            var personaId = await CreatePersonaAsync(db, "Nova");

            // 50 accrued rows already at the cap — Artist0 is deliberately the weakest.
            for (var i = 0; i < 50; i++)
            {
                var weight = i == 0 ? 0.01 : 0.5;
                await TasteRepo(db).InsertAsync(
                    personaId, new TasteRule(new TastePredicate($"Artist{i}", null, null), NoGate, weight),
                    PersonaTasteSource.Accrued, CancellationToken.None);
            }
            var rowId = await SeedTrackRowAsync(db, personaId, "New Artist");

            // When a thumb creates the 51st accrued rule...
            await AccrualRepo(db).ThumbAsync(rowId, TasteThumbDirection.Up, CancellationToken.None);

            // Then the cap holds at 50, the weakest is gone, and the new rule survives — all in the
            // one transaction the nudge itself ran in.
            var accrued = await TasteRepo(db).ListAsync(personaId, PersonaTasteSource.Accrued, CancellationToken.None);
            Assert.Equal(50, accrued.Count);
            Assert.DoesNotContain(accrued, r => r.Rule.Predicate.Artist == "Artist0");
            Assert.Contains(accrued, r => r.Rule.Predicate.Artist == "New Artist");
        }

        [Fact]
        public async Task AuthoredAndOperatorRowsSurviveTheEvictionSweep()
        {
            await db.ResetStationAsync();
            await db.ResetBoothLogAsync();
            var personaId = await CreatePersonaAsync(db, "Nova");

            for (var i = 0; i < 50; i++)
            {
                await TasteRepo(db).InsertAsync(
                    personaId, new TasteRule(new TastePredicate($"Artist{i}", null, null), NoGate, 0.5),
                    PersonaTasteSource.Accrued, CancellationToken.None);
            }
            var authoredId = await TasteRepo(db).InsertAsync(
                personaId, new TasteRule(new TastePredicate("Led Zeppelin", null, null), NoGate, 0.9),
                PersonaTasteSource.Authored, CancellationToken.None);
            var operatorId = await TasteRepo(db).InsertAsync(
                personaId, new TasteRule(new TastePredicate(null, "Vaporwave", null), NoGate, -0.9),
                PersonaTasteSource.Operator, CancellationToken.None);

            var rowId = await SeedTrackRowAsync(db, personaId, "New Artist");
            await AccrualRepo(db).ThumbAsync(rowId, TasteThumbDirection.Up, CancellationToken.None);

            // The card's signature (authored) and the direct operator edit both survive untouched —
            // only source='accrued' rows were ever eviction candidates.
            var authored = await TasteRepo(db).ListAsync(personaId, PersonaTasteSource.Authored, CancellationToken.None);
            var operatorRows = await TasteRepo(db).ListAsync(personaId, PersonaTasteSource.Operator, CancellationToken.None);
            Assert.Equal([authoredId], authored.Select(r => r.Id));
            Assert.Equal([operatorId], operatorRows.Select(r => r.Id));

            var accrued = await TasteRepo(db).ListAsync(personaId, PersonaTasteSource.Accrued, CancellationToken.None);
            Assert.Equal(50, accrued.Count);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the idempotency ledger is durable, and safe under real concurrency (F84.5)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioLedgerIdempotencyIsDurable(DatabaseFixture db)
    {
        [Fact]
        public async Task ADoubleTapAcrossFreshRepositoryInstancesMovesTheWeightOnce()
        {
            await db.ResetStationAsync();
            await db.ResetBoothLogAsync();
            var personaId = await CreatePersonaAsync(db, "Nova");
            var rowId = await SeedTrackRowAsync(db, personaId, "The Waveforms");

            // First tap through one repository instance...
            var first = await AccrualRepo(db).ThumbAsync(rowId, TasteThumbDirection.Up, CancellationToken.None);
            Assert.IsType<TasteThumbOutcome.Nudged>(first);

            // ...a SECOND, entirely fresh instance (shares no in-process state with the first) taps
            // the exact same (persona, row, direction) again — proving the dedup is a durable row,
            // not an in-memory cache that would forget across a process restart.
            var second = await AccrualRepo(db).ThumbAsync(rowId, TasteThumbDirection.Up, CancellationToken.None);
            var alreadyRecorded = Assert.IsType<TasteThumbOutcome.AlreadyRecorded>(second);
            Assert.Equal(personaId, alreadyRecorded.PersonaId);

            var rule = Assert.Single(await TasteRepo(db).ListAsync(personaId, PersonaTasteSource.Accrued, CancellationToken.None));
            Assert.Equal(0.2, rule.Rule.Weight, 3);
        }

        [Fact]
        public async Task ConcurrentThumbsForTheSamePersonaNeverLoseAnUpdate()
        {
            await db.ResetStationAsync();
            await db.ResetBoothLogAsync();
            var personaId = await CreatePersonaAsync(db, "Nova");

            // Four DISTINCT airings of the same artist under the same persona — small enough
            // (4 x 0.2 = 0.8) that clamping at the [-1,1] rail can never mask a lost update.
            var rowIds = new List<long>();
            for (var i = 0; i < 4; i++)
                rowIds.Add(await SeedTrackRowAsync(db, personaId, "The Waveforms"));

            var repo = AccrualRepo(db);

            // Fired CONCURRENTLY — this is the CARRIED T59 review note's advisory-xact-lock
            // serialization actually being exercised: a naive (unlocked) read-modify-write would lose
            // some of these four nudges under a real race.
            await Task.WhenAll(rowIds.Select(rowId => repo.ThumbAsync(rowId, TasteThumbDirection.Up, CancellationToken.None)));

            var rule = Assert.Single(await TasteRepo(db).ListAsync(personaId, PersonaTasteSource.Accrued, CancellationToken.None));
            Assert.Equal(0.8, rule.Rule.Weight, 2);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — unknown row, and an unstamped row rejects the thumb (F84.6)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioSadPath(DatabaseFixture db)
    {
        [Fact]
        public async Task AnUnknownBoothLogRowReturnsRowNotFound()
        {
            await db.ResetStationAsync();
            await db.ResetBoothLogAsync();

            var outcome = await AccrualRepo(db).ThumbAsync(999_999, TasteThumbDirection.Up, CancellationToken.None);

            Assert.IsType<TasteThumbOutcome.RowNotFound>(outcome);
        }

        [Fact]
        public async Task AnUnstampedRowIsNotThumbableAndWritesNothing()
        {
            await db.ResetStationAsync();
            await db.ResetBoothLogAsync();
            var rowId = await SeedTrackRowAsync(db, personaId: null, artist: "Nobody");

            var outcome = await AccrualRepo(db).ThumbAsync(rowId, TasteThumbDirection.Up, CancellationToken.None);

            Assert.IsType<TasteThumbOutcome.NotThumbable>(outcome);

            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            var count = await conn.ExecuteScalarAsync<long>("select count(*) from station.persona_taste");
            Assert.Equal(0, count);
        }
    }
}

// STORY-372 — The show carries the rotation rule (SPEC F152.3, F152.4 · PLAN T360)
//
// BDD specification — xUnit, Postgres-backed (Category=Integration) via DatabaseCollection. Owns
// ShowRepository/IShowStore's own read (envelope ->> 'rotation' normalization) and write
// (SetRotationAsync's jsonb merge) halves of SPEC F152.3 — the resolver-side half (ScheduleResolver's
// block ?? show layering, ScheduleEnvelopeProvider) lives in
// GenWave.Orchestration.Tests/Specs/Story372_DeepCutsAndTheRelaxLadder.cs (AC4–AC6) instead. Mirrors
// Story305_ShowRepository.cs's own RunMigrationScript/ResetShowAsync-per-fact idiom throughout — every
// row here is seeded by raw SQL directly against station.show.envelope (ShowRepository itself has no
// parameter for it beyond SetRotationAsync, SPEC F115.2's own "unread this epic" law for every OTHER
// envelope key/persona_id).

using Dapper;
using GenWave.Abstractions.Playout;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;
using GenWave.MediaLibrary.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureTheShowCarriesTheRotationRule
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static void RunMigrationScript(DatabaseFixture db) =>
        db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "35-show-identity-migration.sh"));

    static ShowRepository Repo(DatabaseFixture db, ILogger<ShowRepository> logger) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource), logger);

    /// <summary>Reads the show back through <see cref="ShowRepository.GetByIdAsync"/>, failing loudly
    /// (never a null-forgiving <c>!</c>) if the row this fact just inserted somehow isn't there.</summary>
    static async Task<Show> GetExistingAsync(ShowRepository repo, long id) =>
        await repo.GetByIdAsync(id, CancellationToken.None)
            ?? throw new InvalidOperationException($"show {id} should exist — this fact just inserted it");

    /// <summary>Inserts a single show row with <paramref name="envelopeJson"/> written straight into
    /// <c>envelope</c> (raw SQL — <c>null</c> leaves the column NULL, mirroring every show shipped
    /// before this task). Returns the new row's id.</summary>
    static async Task<long> InsertShowWithEnvelopeAsync(DatabaseFixture db, string? envelopeJson)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            "insert into station.show (name, slug, envelope) values ('Deep Cuts', 'deep-cuts', @envelope::jsonb) returning id",
            new { envelope = envelopeJson });
    }

    /// <summary>True when <c>station.show.envelope</c> equals <paramref name="expectedJson"/> BY
    /// VALUE (jsonb <c>=</c> compares parsed structure, not literal text — key order is never
    /// guaranteed, mirrors <c>PersonaTasteRepository.ReplaceAsync</c>'s own remarks on the identical
    /// operator) — the one way to assert a jsonb write's shape without depending on Postgres's own
    /// internal key ordering.</summary>
    static async Task<bool> EnvelopeEqualsAsync(DatabaseFixture db, long id, string expectedJson)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<bool>(
            "select envelope = @expected::jsonb from station.show where id = @id",
            new { id, expected = expectedJson });
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the read normalizes every shape SPEC F152.3/F152.4 names
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioReadingAWellFormedRule(DatabaseFixture db)
    {
        // Given envelope {"rotation":{"maxPlays":0}}, When the show is read.
        [Fact]
        public async Task MaxPlaysZeroReadsBackWithDaysNull()
        {
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var id = await InsertShowWithEnvelopeAsync(db, """{"rotation":{"maxPlays":0}}""");
            var repo = Repo(db, NullLogger<ShowRepository>.Instance);

            var read = await GetExistingAsync(repo, id);

            Assert.Equal(new RotationPredicate(MaxPlays: 0, NotAiredWithinDays: null), read.Rotation);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioReadingAnEmptyRotationObject(DatabaseFixture db)
    {
        // Given envelope {"rotation":{}}, When the show is read.
        [Fact]
        public async Task NormalizesToNull()
        {
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var id = await InsertShowWithEnvelopeAsync(db, """{"rotation":{}}""");
            var repo = Repo(db, NullLogger<ShowRepository>.Instance);

            var read = await GetExistingAsync(repo, id);

            Assert.Null(read.Rotation);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioReadingBothMembersExplicitlyNull(DatabaseFixture db)
    {
        // Given envelope {"rotation":{"maxPlays":null,"notAiredWithinDays":null}}, When the show is read.
        [Fact]
        public async Task NormalizesToNull()
        {
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var id = await InsertShowWithEnvelopeAsync(
                db, """{"rotation":{"maxPlays":null,"notAiredWithinDays":null}}""");
            var repo = Repo(db, NullLogger<ShowRepository>.Instance);

            var read = await GetExistingAsync(repo, id);

            Assert.Null(read.Rotation);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioReadingAMalformedRotation(DatabaseFixture db)
    {
        // Given envelope {"rotation":"nope"} (a string, not an object), When the show is read.
        async Task<(Show Show, CapturingLogger<ShowRepository> Logger)> ReadMalformedAsync()
        {
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var id = await InsertShowWithEnvelopeAsync(db, """{"rotation":"nope"}""");
            var logger = new CapturingLogger<ShowRepository>();
            var repo = Repo(db, logger);

            var read = await GetExistingAsync(repo, id);
            return (read, logger);
        }

        [Fact]
        public async Task NormalizesToNull() =>
            Assert.Null((await ReadMalformedAsync()).Show.Rotation);

        [Fact]
        public async Task WarnsNamingTheShow()
        {
            var (_, logger) = await ReadMalformedAsync();

            Assert.Contains(logger.Warnings, w => w.Contains("Deep Cuts", StringComparison.Ordinal));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the write merges into envelope, never overwrites it whole
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioWritingPreservesSiblingKeys(DatabaseFixture db)
    {
        // Given envelope {"foo":1}, When SetRotationAsync(MaxPlays: 1) is called.
        [Fact]
        public async Task TheEnvelopeGainsRotationAndKeepsFoo()
        {
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var id = await InsertShowWithEnvelopeAsync(db, """{"foo":1}""");
            var repo = Repo(db, NullLogger<ShowRepository>.Instance);

            await repo.SetRotationAsync(id, new RotationPredicate(MaxPlays: 1), CancellationToken.None);

            // RotationEnvelopeCodec.ToJson serializes BOTH RotationPredicate members (STJ's own
            // default — no [JsonIgnore]/DefaultIgnoreCondition trims a null member), so the merged
            // fragment carries notAiredWithinDays: null explicitly, not merely absent; jsonb equality
            // is structural, so the expected literal must match that shape exactly.
            Assert.True(await EnvelopeEqualsAsync(
                db, id, """{"foo":1,"rotation":{"maxPlays":1,"notAiredWithinDays":null}}"""));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioWritingNullRemovesTheKey(DatabaseFixture db)
    {
        // Given envelope {"foo":1,"rotation":{"maxPlays":5}}, When SetRotationAsync(null) is called.
        [Fact]
        public async Task TheEnvelopeLosesRotationAndKeepsFoo()
        {
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var id = await InsertShowWithEnvelopeAsync(db, """{"foo":1,"rotation":{"maxPlays":5}}""");
            var repo = Repo(db, NullLogger<ShowRepository>.Instance);

            await repo.SetRotationAsync(id, null, CancellationToken.None);

            Assert.True(await EnvelopeEqualsAsync(db, id, """{"foo":1}"""));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioWritingABothNullPredicateIsTreatedAsRemove(DatabaseFixture db)
    {
        // PLAN T360 review LOW-6: RotationEnvelopeCodec.ToJson's own both-null guard — a caller
        // passing a RotationPredicate whose MaxPlays/NotAiredWithinDays are BOTH null must produce the
        // identical write a bare null would (remove the key), never a filters-nothing
        // {"maxPlays":null,"notAiredWithinDays":null} fragment Parse would immediately normalize back
        // to null on the next read anyway. Given envelope {"foo":1,"rotation":{"maxPlays":5}}.
        [Fact]
        public async Task TheEnvelopeLosesRotationAndKeepsFoo()
        {
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var id = await InsertShowWithEnvelopeAsync(db, """{"foo":1,"rotation":{"maxPlays":5}}""");
            var repo = Repo(db, NullLogger<ShowRepository>.Instance);

            // When SetRotationAsync is called with a both-null predicate (not a bare null)
            await repo.SetRotationAsync(
                id, new RotationPredicate(MaxPlays: null, NotAiredWithinDays: null), CancellationToken.None);

            // Then the rotation key is removed entirely, exactly as if null had been passed
            Assert.True(await EnvelopeEqualsAsync(db, id, """{"foo":1}"""));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioSetRotationRaisesShowChanged(DatabaseFixture db)
    {
        // PLAN T360 review HIGH-1: a rotation edit must tell CachingScheduleResolver its cached
        // ShowSummary may be stale — the resolver-side propagation fact (proving the cache actually
        // reloads and observes the new rule) lives in Orchestration.Tests/Story372_DeepCutsAndTheRelaxLadder.cs;
        // this fact pins the store's own half of that contract in isolation: exactly one raise per
        // successful write, never zero, never more than one.
        [Fact]
        public async Task ExactlyOneRaisePerSuccessfulWrite()
        {
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var id = await InsertShowWithEnvelopeAsync(db, """{}""");
            var repo = Repo(db, NullLogger<ShowRepository>.Instance);
            var raiseCount = 0;
            repo.ShowChanged += () => raiseCount++;

            await repo.SetRotationAsync(id, new RotationPredicate(MaxPlays: 1), CancellationToken.None);

            Assert.Equal(1, raiseCount);
        }
    }
}

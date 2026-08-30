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

    /// <summary>Inserts a single ALREADY-IMPORTED show row (<c>imported_from</c> non-null) with
    /// <paramref name="envelopeJson"/> written straight into <c>envelope</c> — the ONE precondition
    /// <see cref="ShowRepository.ImportAsync"/>'s own conflict-branch WHERE clause requires
    /// (<c>imported_from IS NOT NULL</c>) before a re-import's UPDATE ever applies, so
    /// <see cref="InsertShowWithEnvelopeAsync"/>'s own plain (authored) row cannot stand in for the
    /// import-over-import facts below — a re-import targeting an authored row declines atomically
    /// instead (SPEC F115.5, already proven elsewhere). Returns the new row's id.</summary>
    static async Task<long> InsertImportedShowWithEnvelopeAsync(DatabaseFixture db, string slug, string? envelopeJson)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            """
            insert into station.show (name, slug, imported_from, imported_at, envelope)
            values ('Deep Cuts', @slug, 'seed-import', now(), @envelope::jsonb)
            returning id
            """,
            new { slug, envelope = envelopeJson });
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

    // ---------------------------------------------------------------------
    // HAPPY PATH — the import write path (SPEC F152.6, PLAN T363) carries the rule too, through the
    // SAME merge-preserving-siblings discipline SetRotationAsync's own facts above already pin — plus
    // ImportAsync's own "no opinion, never a clear" divergence from SetRotationAsync's null semantics.
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioImportWritesAPresentRotationRule(DatabaseFixture db)
    {
        // Given a fresh slug, When ImportAsync carries a validated, present rotation object.
        [Fact]
        public async Task TheFreshRowsEnvelopeCarriesIt()
        {
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db, NullLogger<ShowRepository>.Instance);

            var imported = await repo.ImportAsync(
                "deep-cuts", "Deep Cuts", null, "steady, unhurried", "catalog-entry",
                new RotationPredicate(MaxPlays: 0), CancellationToken.None);

            Assert.NotNull(imported);
            Assert.Equal(new RotationPredicate(0, null), imported.Rotation);
            Assert.True(await EnvelopeEqualsAsync(
                db, imported.Id, """{"rotation":{"maxPlays":0,"notAiredWithinDays":null}}"""));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioReimportWithNoRotationOpinionLeavesTheExistingRuleUntouched(DatabaseFixture db)
    {
        // Given a show already imported once with a rotation rule, When it is RE-imported carrying no
        // rotation opinion (envelope absent, or envelope.rotation absent/null — ShowsController.Import
        // collapses all three to a null ImportAsync parameter before this seam is ever reached).
        [Fact]
        public async Task TheExistingRuleSurvivesByteForByte()
        {
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db, NullLogger<ShowRepository>.Instance);
            var first = await repo.ImportAsync(
                "deep-cuts", "Deep Cuts", null, "steady", "catalog-entry",
                new RotationPredicate(MaxPlays: 0), CancellationToken.None);
            Assert.NotNull(first);

            // When re-imported with a DIFFERENT name/flavor but rotation: null (no opinion) —
            var second = await repo.ImportAsync(
                "deep-cuts", "Deep Cuts Redux", null, "steadier still", "catalog-entry",
                null, CancellationToken.None);

            // Then name/flavor changed as any re-import would, but the rotation rule this import
            // never mentioned is exactly as SetRotationAsync last left it — never cleared.
            Assert.NotNull(second);
            Assert.Equal("Deep Cuts Redux", second.Name);
            Assert.Equal(new RotationPredicate(0, null), second.Rotation);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioImportOverAPriorImportWithANewRuleReplacesTheOldOne(DatabaseFixture db)
    {
        // PLAN T363 review MED-2 — nothing previously pinned the import CONFLICT branch's own merge
        // (ShowRepository.ImportAsync's `on conflict ... do update` half; every fact above either hits
        // the INSERT branch or the conflict branch with NO prior rotation to replace). Given a show
        // already imported once with a rotation rule, When it is re-imported carrying a DIFFERENT
        // rotation object.
        [Fact]
        public async Task TheNewRuleReplacesTheOldOne()
        {
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var id = await InsertImportedShowWithEnvelopeAsync(
                db, "deep-cuts", """{"rotation":{"maxPlays":0,"notAiredWithinDays":null}}""");
            var repo = Repo(db, NullLogger<ShowRepository>.Instance);

            var reimported = await repo.ImportAsync(
                "deep-cuts", "Deep Cuts", null, "steady", "catalog-entry",
                new RotationPredicate(MaxPlays: 5), CancellationToken.None);

            // Then the OLD rule (MaxPlays: 0) is gone — the new one (MaxPlays: 5) is all that reads
            // back, on both the mapped Show and the raw envelope column.
            Assert.NotNull(reimported);
            Assert.Equal(new RotationPredicate(5, null), reimported.Rotation);
            Assert.True(await EnvelopeEqualsAsync(
                db, id, """{"rotation":{"maxPlays":5,"notAiredWithinDays":null}}"""));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioImportPreservesSiblingEnvelopeKeysOnTheConflictBranch(DatabaseFixture db)
    {
        // PLAN T363 review MED-2 (the mutation-check fact): a sibling envelope key survives BOTH
        // re-import shapes. Collapsing ShowRepository.ImportAsync's own conflict-branch merge
        // (`coalesce(station.show.envelope, jsonb_build_object()) || jsonb_build_object('rotation', ...)`)
        // down to a bare `jsonb_build_object('rotation', ...)` would drop `foo` in the "carries a new
        // rule" fact below — that regression is exactly what this fact exists to catch (the "no
        // opinion" fact never even reaches the jsonb_build_object call, so it alone could not).

        // Given a show already imported once with a sibling envelope key AND a rotation rule, When it
        // is re-imported carrying a NEW rotation object.
        [Fact]
        public async Task ASiblingKeySurvivesAReimportCarryingANewRule()
        {
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var id = await InsertImportedShowWithEnvelopeAsync(
                db, "deep-cuts", """{"foo":1,"rotation":{"maxPlays":0,"notAiredWithinDays":null}}""");
            var repo = Repo(db, NullLogger<ShowRepository>.Instance);

            await repo.ImportAsync(
                "deep-cuts", "Deep Cuts", null, "steady", "catalog-entry",
                new RotationPredicate(MaxPlays: 5), CancellationToken.None);

            Assert.True(await EnvelopeEqualsAsync(
                db, id, """{"foo":1,"rotation":{"maxPlays":5,"notAiredWithinDays":null}}"""));
        }

        // Given a show already imported once with a sibling envelope key, When it is RE-imported
        // carrying no rotation opinion.
        [Fact]
        public async Task ASiblingKeySurvivesANoOpinionReimport()
        {
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var id = await InsertImportedShowWithEnvelopeAsync(db, "deep-cuts", """{"foo":1}""");
            var repo = Repo(db, NullLogger<ShowRepository>.Instance);

            await repo.ImportAsync(
                "deep-cuts", "Deep Cuts Redux", null, "steadier", "catalog-entry",
                null, CancellationToken.None);

            Assert.True(await EnvelopeEqualsAsync(db, id, """{"foo":1}"""));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioImportRaisesShowChanged(DatabaseFixture db)
    {
        // PLAN T363 (the T360 review HIGH-1 fix, extended to the import path — the T360 note this
        // task carries forward): an import can rewrite name/tagline/flavor and now the rotation rule
        // on an EXISTING show — without this event CachingScheduleResolver's TTL-less snapshot would
        // go stale until an unrelated schedule write or a restart.
        [Fact]
        public async Task ExactlyOneRaisePerSuccessfulImport()
        {
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db, NullLogger<ShowRepository>.Instance);
            var raiseCount = 0;
            repo.ShowChanged += () => raiseCount++;

            var imported = await repo.ImportAsync(
                "deep-cuts", "Deep Cuts", null, "steady", "catalog-entry", null, CancellationToken.None);

            Assert.NotNull(imported);
            Assert.Equal(1, raiseCount);
        }

        [Fact]
        public async Task NoRaiseOnADeclinedAuthoredCollision()
        {
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db, NullLogger<ShowRepository>.Instance);
            var authored = Assert.IsType<ShowWriteResult.Created>(
                await repo.CreateAsync(new ShowDraft("Authored Show"), CancellationToken.None));
            var raiseCount = 0;
            repo.ShowChanged += () => raiseCount++;

            var declined = await repo.ImportAsync(
                authored.Show.Slug, "Hijack Attempt", null, null, "some-catalog-entry", null, CancellationToken.None);

            Assert.Null(declined);
            Assert.Equal(0, raiseCount);
        }
    }
}

// STORY-271 — ThemeRepository's own SQL, proven against real Postgres (SPEC F103.7, PLAN T182)
//
// BDD specification — xUnit, Postgres-backed (Category=Integration) via DatabaseCollection. T181
// shipped ThemeRepository with no real-Postgres coverage at all (its contract was proven only by
// FakeThemeStore, GenWave.Host.Tests' own in-memory double — see that fake's own remarks); this file
// is the real-SQL proof T181 deferred, the same split Story209_PersonaImportRepository.cs draws for
// PersonaImportRepository and Story240_ScheduleStore.cs draws for ScheduleRepository. ThemeCatalog's
// own shipped∪owner load path (PLAN T182, GenWave.Host.Tests' Story271_OwnerThemeStorage.cs) is
// proven against FakeThemeStore instead — that project carries no Postgres fixture; this file proves
// the repository underneath actually has the SQL teeth the fake merely simulates.
//
// PLAN T207 addition (SPEC F104.13): ScenarioUpsertingAnAuthoredTheme proves UpsertAsync's own CASE
// expression at the REAL SQL layer, not merely against GenWave.Host.Tests' FakeThemeStore double — a
// null importedFrom (the save-as-own write) must leave imported_at null too, the OwnerTheme invariant
// this file's other Scenario already proves the NON-null half of.
//
// gh-#394 addition: ScenarioSavingAsOwn proves SaveAsOwnAsync's own conditional ON CONFLICT ... WHERE
// clause at the REAL SQL layer — the fix for the read-then-write TOCTOU
// ThemesSaveAsOwnController/Story287_SaveAsOwn.cs's own remarks describe. Proven here against the real
// repository (no controller, no FakeThemeStore double in the loop) so the refusal is provably the
// REPOSITORY'S OWN doing, not merely a pre-check some caller happens to still run.

using Dapper;
using GenWave.MediaLibrary.Station;
using Npgsql;
using System.Text.Json.Nodes;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureThemeRepository
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static ThemeRepository Repo(DatabaseFixture db) => new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));

    const string Definition = """{"slug":"midnight-drive","name":"Midnight Drive"}""";

    /// <summary>
    /// Structural JSON comparison — Postgres's jsonb column reformats whitespace AND reorders object
    /// keys on write (unlike json, jsonb is explicit about preserving neither), so asserting the raw
    /// string this repository wrote against what <see cref="ThemeRepository.GetBySlugAsync"/>/
    /// <see cref="ThemeRepository.GetAllAsync"/> read back is never a literal match even though
    /// nothing was lost; <see cref="JsonNode.DeepEquals"/> compares object members by key regardless
    /// of order, which is exactly the "same content, different serialization" claim this asserts.
    /// </summary>
    static bool JsonEquivalent(string expected, string actual) =>
        JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual));

    // ---------------------------------------------------------------------
    // HAPPY PATH — UpsertAsync then GetAllAsync/GetBySlugAsync round-trip
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioUpsertingANewTheme(DatabaseFixture db)
    {
        [Fact]
        public async Task GetBySlugReturnsTheStoredDefinitionAndProvenance()
        {
            await db.ResetThemeAsync();
            var repo = Repo(db);

            await repo.UpsertAsync("midnight-drive", Definition, "midnight-drive-catalog-entry", CancellationToken.None);
            var theme = await repo.GetBySlugAsync("midnight-drive", CancellationToken.None)
                ?? throw new InvalidOperationException("test arrange: theme not found immediately after upsert");

            // One assertion bundling the whole composite claim (mirrors this codebase's
            // tuple-equality idiom) — the definition round-trips the same JSON CONTENT (see
            // JsonEquivalent's own remarks), imported_from is stamped, and imported_at is non-null.
            Assert.Equal(
                (DefinitionMatches: true, ImportedFrom: "midnight-drive-catalog-entry", ImportedAtStamped: true),
                (DefinitionMatches: JsonEquivalent(Definition, theme.Definition), ImportedFrom: theme.ImportedFrom, ImportedAtStamped: theme.ImportedAt is not null));
        }

        [Fact]
        public async Task GetAllIncludesTheStoredTheme()
        {
            await db.ResetThemeAsync();
            var repo = Repo(db);

            await repo.UpsertAsync("midnight-drive", Definition, "midnight-drive-catalog-entry", CancellationToken.None);

            var all = await repo.GetAllAsync(CancellationToken.None);
            Assert.Equal("midnight-drive", Assert.Single(all).Slug);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — a null importedFrom (save-as-own, SPEC F104.13, PLAN T207) leaves imported_at
    // null too — OwnerTheme's own "ImportedAt is null exactly when ImportedFrom is" invariant, proven
    // at the REAL SQL layer (the CASE expression UpsertAsync's own remarks describe), not merely
    // against the in-memory FakeThemeStore double.
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioUpsertingAnAuthoredTheme(DatabaseFixture db)
    {
        [Fact]
        public async Task ANullImportedFromLeavesImportedAtNullToo()
        {
            await db.ResetThemeAsync();
            var repo = Repo(db);

            await repo.UpsertAsync("midnight-drive", Definition, importedFrom: null, CancellationToken.None);
            var theme = await repo.GetBySlugAsync("midnight-drive", CancellationToken.None)
                ?? throw new InvalidOperationException("test arrange: theme not found immediately after upsert");

            // A regression that reverted to the pre-T207 unconditional now() would fail THIS
            // assertion, not merely leave it unexercised — the mutation this Scenario exists to catch.
            Assert.Equal(
                (DefinitionMatches: true, ImportedFrom: (string?)null, ImportedAt: (DateTime?)null),
                (DefinitionMatches: JsonEquivalent(Definition, theme.Definition), ImportedFrom: theme.ImportedFrom, ImportedAt: theme.ImportedAt));
        }

        [Fact]
        public async Task ReUpsertingWithANonNullImportedFromStampsImportedAt()
        {
            // Given a theme first saved as own (null provenance),
            await db.ResetThemeAsync();
            var repo = Repo(db);
            await repo.UpsertAsync("midnight-drive", Definition, importedFrom: null, CancellationToken.None);

            // When it is re-upserted with a real provenance value (a re-import over a previously
            // authored slug — the shipped-slug reservation guards the OTHER direction, SPEC F103.8;
            // this is the plain re-upsert path either write route shares),
            await repo.UpsertAsync("midnight-drive", Definition, "file", CancellationToken.None);
            var theme = await repo.GetBySlugAsync("midnight-drive", CancellationToken.None)
                ?? throw new InvalidOperationException("test arrange: theme not found immediately after re-upsert");

            // Then imported_at is stamped on this write — the CASE expression's OTHER branch, proven
            // in the same file as the null branch so neither can silently regress without the other
            // catching it.
            Assert.Equal(
                (ImportedFrom: "file", ImportedAtStamped: true),
                (ImportedFrom: theme.ImportedFrom, ImportedAtStamped: theme.ImportedAt is not null));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — re-upsert refreshes the row in place (SPEC F103.6/F103.7)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioReUpsertingAnExistingSlug(DatabaseFixture db)
    {
        [Fact]
        public async Task TheDefinitionAndProvenanceAreReplaced()
        {
            await db.ResetThemeAsync();
            var repo = Repo(db);
            await repo.UpsertAsync("midnight-drive", Definition, "midnight-drive-catalog-entry", CancellationToken.None);

            const string updatedDefinition = """{"slug":"midnight-drive","name":"Midnight Drive (v2)"}""";
            await repo.UpsertAsync("midnight-drive", updatedDefinition, "file", CancellationToken.None);

            var theme = await repo.GetBySlugAsync("midnight-drive", CancellationToken.None)
                ?? throw new InvalidOperationException("test arrange: theme not found immediately after re-upsert");
            Assert.Equal(
                (DefinitionMatches: true, ImportedFrom: "file"),
                (DefinitionMatches: JsonEquivalent(updatedDefinition, theme.Definition), ImportedFrom: theme.ImportedFrom));
        }

        [Fact]
        public async Task NoSecondRowIsCreated()
        {
            await db.ResetThemeAsync();
            var repo = Repo(db);
            await repo.UpsertAsync("midnight-drive", Definition, "midnight-drive-catalog-entry", CancellationToken.None);

            await repo.UpsertAsync("midnight-drive", Definition, "file", CancellationToken.None);

            // Straight from Postgres, not just the returned row — proves the UNIQUE(slug) ON CONFLICT
            // target updated in place rather than the application racing its own duplicate insert.
            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            var count = await conn.ExecuteScalarAsync<int>("select count(*)::int from station.theme");
            Assert.Equal(1, count);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — SaveAsOwnAsync succeeds on a fresh slug and on a slug this same write path
    // authored before (gh-#394; SPEC F104.13)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioSavingAsOwnOntoAFreshOrAuthoredSlug(DatabaseFixture db)
    {
        [Fact]
        public async Task AFreshSlugIsInsertedWithNullProvenance()
        {
            await db.ResetThemeAsync();
            var repo = Repo(db);

            var saved = await repo.SaveAsOwnAsync("midnight-drive", Definition, CancellationToken.None);
            var theme = await repo.GetBySlugAsync("midnight-drive", CancellationToken.None)
                ?? throw new InvalidOperationException("test arrange: theme not found immediately after save");

            Assert.Equal(
                (Saved: true, DefinitionMatches: true, ImportedFrom: (string?)null, ImportedAt: (DateTime?)null),
                (Saved: saved, DefinitionMatches: JsonEquivalent(Definition, theme.Definition), ImportedFrom: theme.ImportedFrom, ImportedAt: theme.ImportedAt));
        }

        [Fact]
        public async Task ASlugAlreadyHoldingAnAuthoredThemeIsUpdated()
        {
            // Given a slug this same write path already authored (imported_from already null — the
            // OTHER half of the WHERE clause's guard, ordinary re-save iteration),
            await db.ResetThemeAsync();
            var repo = Repo(db);
            await repo.SaveAsOwnAsync("midnight-drive", Definition, CancellationToken.None);

            // When it is saved-as-own again with a new definition,
            const string updatedDefinition = """{"slug":"midnight-drive","name":"Midnight Drive (v2)"}""";
            var saved = await repo.SaveAsOwnAsync("midnight-drive", updatedDefinition, CancellationToken.None);

            // Then the write succeeds and the definition is replaced, provenance still null.
            var theme = await repo.GetBySlugAsync("midnight-drive", CancellationToken.None)
                ?? throw new InvalidOperationException("test arrange: theme not found immediately after re-save");
            Assert.Equal(
                (Saved: true, DefinitionMatches: true, ImportedFrom: (string?)null),
                (Saved: saved, DefinitionMatches: JsonEquivalent(updatedDefinition, theme.Definition), ImportedFrom: theme.ImportedFrom));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — SaveAsOwnAsync refuses (atomically, no pre-check) onto a slug holding an IMPORTED
    // theme (gh-#394; SPEC F104.13, PLAN T207 review finding F2)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioSavingAsOwnOntoAnImportedSlug(DatabaseFixture db)
    {
        [Fact]
        public async Task TheWriteIsRefusedAndTheImportedRowSurvivesByteForByte()
        {
            // Given a slug already holding an IMPORTED theme (non-null imported_from) — committed
            // through the plain UpsertAsync path, standing in for a concurrent import that landed
            // between a save-as-own's own read and write in the pre-gh-#394 world,
            await db.ResetThemeAsync();
            var repo = Repo(db);
            await repo.UpsertAsync("midnight-drive", Definition, "midnight-drive-catalog-entry", CancellationToken.None);
            var seeded = await repo.GetBySlugAsync("midnight-drive", CancellationToken.None);

            // When SaveAsOwnAsync is called onto that SAME slug — no pre-check GetBySlugAsync call
            // anywhere in this Fact, so a false return here is provably the conditional upsert's OWN
            // WHERE clause refusing, not a caller-side check this test happened to also run,
            var saved = await repo.SaveAsOwnAsync("midnight-drive", "{\"slug\":\"midnight-drive\",\"name\":\"Clobbered\"}", CancellationToken.None);

            // Then the write reports refusal, and the row is byte-for-byte untouched — the exact
            // "never NULL another theme's own provenance" guarantee SPEC F104.13 makes, now enforced by
            // the WHERE clause itself rather than a read this Fact deliberately never performs first.
            var stillStored = await repo.GetBySlugAsync("midnight-drive", CancellationToken.None);
            Assert.Equal(
                (Saved: false, RowUnchanged: true),
                (Saved: saved, RowUnchanged: seeded == stillStored));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — an unknown slug is a clean miss, never an exception
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioLookingUpAMissingSlug(DatabaseFixture db)
    {
        [Fact]
        public async Task GetBySlugReturnsNull()
        {
            await db.ResetThemeAsync();
            var repo = Repo(db);

            Assert.Null(await repo.GetBySlugAsync("no-such-slug", CancellationToken.None));
        }
    }
}

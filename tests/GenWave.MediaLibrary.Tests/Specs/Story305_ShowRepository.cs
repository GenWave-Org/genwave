// STORY-305 — The show entity & API (F115.1, F115.4, F115.5) — repository half
//
// PLAN T238 (this file's schema half — the T238 acceptance criterion "fresh init and in-place
// upgrade converge; dormant columns exist") is REAL, live-Postgres coverage (Category=Integration
// via DatabaseCollection): a fresh-init scenario (ScenarioTheIdentityPackageFlowsEndToEnd) and an
// in-place scenario (ScenarioMigrationAddsTheColumnsInPlace, proving db/35's own DDL directly by
// dropping the nine T238 columns and re-running the migration script), mirroring
// Story304_AiredKindStamp.cs's own two-scenario shape.
//
// The fresh-init facts assert against DatabaseFixture.InitialSchema/InitialUniqueConstraints — a
// snapshot the fixture takes once, immediately after Postgres finishes running db/01+db/06 (the only
// two files db-compose.yaml mounts as init scripts) and before any spec class runs. That is the one
// instant a fact can observe db/06's own CREATE in isolation from db/35: an earlier revision of this
// file instead (re-)ran db/35 in each fact's own Arrange, which passed even with the db/06 mirror of
// db/35 deleted entirely — db/35 itself would then silently supply the columns db/06 failed to,
// leaving the acceptance word "converge" untested. Because the snapshot is taken once at fixture
// init, ordering against the in-place scenario below (which drops columns then re-runs db/35) no
// longer matters either way.
//
// ScenarioDormantBundleColumns is likewise real: persona_id/envelope exist with no DEFAULT (read from
// the same snapshot) and read back NULL on an inserted row — the table has no writer anywhere this
// epic (F115.2).
//
// ScenarioAuthoredCrud and ScenarioRejectingInvalidShows are now real too (PLAN T239: the Show domain
// type, IShowStore, and ShowRepository — GenWave.Core.Domain/Abstractions,
// GenWave.MediaLibrary.Station). Both are Category=Integration via DatabaseCollection, mirroring the
// two scenarios above: RunMigrationScript(db) first (the convergence idiom — this file's own live
// database may have had station.show's T238 columns dropped and re-added mid-suite by
// ScenarioMigrationAddsTheColumnsInPlace, with no ordering guarantee against it), then
// DatabaseFixture.ResetShowAsync() for a clean table before every fact. The endpoint half lives in
// GenWave.Host.Tests/Specs/Story305_ShowsApi.cs (PLAN T240, still pending).

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;
using GenWave.MediaLibrary.Station;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureShowRepository
{
    // ---------------------------------------------------------------------
    // Helpers (T238 schema facts) — mirror Story304_AiredKindStamp's own idioms.
    // ---------------------------------------------------------------------

    /// <summary>Returns (data_type, is_nullable) for the named column on the given station table,
    /// or null when the column does not exist. Mirrors Story304's own QueryColumnAsync helper.</summary>
    static async Task<(string DataType, string IsNullable)?> QueryColumnAsync(
        DatabaseFixture db, string table, string column)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        var row = await conn.QuerySingleOrDefaultAsync<(string data_type, string is_nullable)>(
            """
            select data_type, is_nullable from information_schema.columns
            where table_schema = 'station' and table_name = @table and column_name = @column
            """,
            new { table, column });

        return row == default ? null : (row.data_type, row.is_nullable);
    }

    /// <summary>Returns (data_type, is_nullable) for the named column on library.media, or null when
    /// the column does not exist — the library_svc-rooted counterpart to <see cref="QueryColumnAsync"/>,
    /// needed because library.media.show_id crosses the db/22 schema-role boundary (Story030's own
    /// QueryColumnAsync helper is the precedent for reading library schema this way).</summary>
    static async Task<(string DataType, string IsNullable)?> QueryLibraryMediaColumnAsync(
        DatabaseFixture db, string column)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var row = await conn.QuerySingleOrDefaultAsync<(string data_type, string is_nullable)>(
            """
            select data_type, is_nullable from information_schema.columns
            where table_schema = 'library' and table_name = 'media' and column_name = @column
            """,
            new { column });

        return row == default ? null : (row.data_type, row.is_nullable);
    }

    /// <summary>The name(s) of the UNIQUE constraint(s) on the given fully-qualified station table —
    /// used by the in-place scenario below to pin that db/35's ALTER lands the show.slug constraint on
    /// the same literal name (<c>show_slug_key</c>) the fresh-init snapshot observes from db/06's
    /// CREATE. QuerySingleOrDefaultAsync's own single-row contract additionally proves exactly one
    /// UNIQUE constraint exists on the table — this says nothing about whether either name is
    /// auto-generated, since Postgres's own auto-generated name for a bare UNIQUE here would in fact
    /// also be <c>show_slug_key</c>.</summary>
    static async Task<string?> QueryUniqueConstraintNameAsync(DatabaseFixture db, string qualifiedTable)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<string?>(
            "select conname from pg_constraint where conrelid = @table::regclass and contype = 'u'",
            new { table = qualifiedTable });
    }

    /// <summary>Runs db/35-show-identity-migration.sh against the test database via the fixture.
    /// Mirrors Story304's own RunMigrationScript helper. Safe to call unconditionally — the script is
    /// idempotent (ADD COLUMN IF NOT EXISTS). Used only by the in-place scenario below — the fresh-init
    /// facts assert against DatabaseFixture.InitialSchema instead (see file header).</summary>
    static void RunMigrationScript(DatabaseFixture db) =>
        db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "35-show-identity-migration.sh"));

    /// <summary>(data_type, is_nullable, column_default) for the named station-schema column, read
    /// from the fixture's fresh-init snapshot (<see cref="DatabaseFixture.InitialSchema"/>) rather
    /// than a live query — see that property's own remarks and this file's header for why that is the
    /// only way a dropped db/06 mirror of a db/35 column can actually turn a fresh-init fact red.
    /// Fails the calling fact immediately if the column is missing from the snapshot.</summary>
    static (string DataType, string IsNullable, string? ColumnDefault) InitialStationColumn(
        DatabaseFixture db, string table, string column)
    {
        var found = db.InitialSchema.TryGetValue(("station", table, column), out var value);
        Assert.True(found, $"station.{table}.{column} missing from the fresh-init schema snapshot");
        return value;
    }

    /// <summary>Same as <see cref="InitialStationColumn"/> but for library.media, crossing the db/22
    /// schema-role boundary the same way <see cref="QueryLibraryMediaColumnAsync"/> already does for
    /// the in-place scenario.</summary>
    static (string DataType, string IsNullable, string? ColumnDefault) InitialLibraryMediaColumn(
        DatabaseFixture db, string column)
    {
        var found = db.InitialSchema.TryGetValue(("library", "media", column), out var value);
        Assert.True(found, $"library.media.{column} missing from the fresh-init schema snapshot");
        return value;
    }

    /// <summary>The UNIQUE constraint names on the given bare station table name at the same
    /// fresh-init instant <see cref="InitialStationColumn"/> reads from — empty when the table carries
    /// none (mirrors <see cref="DatabaseFixture.InitialUniqueConstraints"/>'s own remarks on why this
    /// is a list rather than a single name).</summary>
    static IReadOnlyList<string> InitialUniqueConstraintNames(DatabaseFixture db, string table) =>
        db.InitialUniqueConstraints.GetValueOrDefault(table, []);

    /// <summary>The T239 repository under spec, wired the same "Lazy over the fixture's own
    /// StationDataSource" way Story118_PersonaStorage.cs's own <c>Repo</c> helper wires
    /// <c>PersonaRepository</c>.</summary>
    static ShowRepository Repo(DatabaseFixture db) => new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));

    /// <summary>The show delete guard's own detail-read repository (PLAN T240 review — this method
    /// carried zero live-DB coverage before <see cref="ScenarioDeleteFkGuard"/>'s two round-trip
    /// facts below), wired the same "Lazy over the fixture's own StationDataSource" way as
    /// <see cref="Repo"/>; mirrors Story240_ScheduleStore.cs's own identically-shaped helper.</summary>
    static ScheduleRepository ScheduleRepo(DatabaseFixture db) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource), NullLogger<ScheduleRepository>.Instance);

    // ---------------------------------------------------------------------
    // HAPPY PATH — fresh init (db/06's mirror of db/35)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheIdentityPackageFlowsEndToEnd(DatabaseFixture db)
    {
        // Every fact below reads DatabaseFixture.InitialSchema/InitialUniqueConstraints — the
        // snapshot taken once, at fixture init, of the PURE db/01+db/06 fresh-init world (see file
        // header). No fact here ever runs db/35: that is precisely what makes dropping a column from
        // db/06's own CREATE turn the matching fact red.

        [Fact]
        public void ShowSlugColumnExistsAsTextNotNull()
        {
            var column = InitialStationColumn(db, "show", "slug");

            Assert.Equal("text", column.DataType);
            Assert.Equal("NO", column.IsNullable);
        }

        [Fact]
        public void ShowTaglineColumnExistsAsTextNullable()
        {
            var column = InitialStationColumn(db, "show", "tagline");

            Assert.Equal("text", column.DataType);
            Assert.Equal("YES", column.IsNullable);
        }

        [Fact]
        public void ShowFlavorColumnExistsAsTextNullable()
        {
            var column = InitialStationColumn(db, "show", "flavor");

            Assert.Equal("text", column.DataType);
            Assert.Equal("YES", column.IsNullable);
        }

        [Fact]
        public void ShowImportedFromColumnExistsAsTextNullable()
        {
            var column = InitialStationColumn(db, "show", "imported_from");

            Assert.Equal("text", column.DataType);
            Assert.Equal("YES", column.IsNullable);
        }

        [Fact]
        public void ShowImportedAtColumnExistsAsTimestamptzNullable()
        {
            var column = InitialStationColumn(db, "show", "imported_at");

            Assert.Equal("timestamp with time zone", column.DataType);
            Assert.Equal("YES", column.IsNullable);
        }

        [Fact]
        public void ShowPersonaIdColumnExistsAsIntegerNullable()
        {
            var column = InitialStationColumn(db, "show", "persona_id");

            Assert.Equal("integer", column.DataType);
            Assert.Equal("YES", column.IsNullable);
        }

        [Fact]
        public void ShowEnvelopeColumnExistsAsJsonbNullable()
        {
            var column = InitialStationColumn(db, "show", "envelope");

            Assert.Equal("jsonb", column.DataType);
            Assert.Equal("YES", column.IsNullable);
        }

        [Fact]
        public void BoothLogShowIdColumnExistsAsIntegerNullable()
        {
            // The air-time stamp (F121.1) — deliberately NO FK, history must outlive the entity.
            var column = InitialStationColumn(db, "booth_log", "show_id");

            Assert.Equal("integer", column.DataType);
            Assert.Equal("YES", column.IsNullable);
        }

        [Fact]
        public void LibraryMediaShowIdColumnExistsAsIntegerNullable()
        {
            // Crosses the db/22 schema-role boundary — read from the library half of the snapshot,
            // not the station half (the two roles have no grants into each other's schema).
            var column = InitialLibraryMediaColumn(db, "show_id");

            Assert.Equal("integer", column.DataType);
            Assert.Equal("YES", column.IsNullable);
        }

        [Fact]
        public void ShowSlugUniqueConstraintIsNamedShowSlugKey()
        {
            // db/06's fresh-init CREATE names this constraint explicitly (CONSTRAINT show_slug_key
            // UNIQUE); the list-equality assertion below also proves exactly one UNIQUE constraint
            // exists on the table.
            var constraintNames = InitialUniqueConstraintNames(db, "show");

            Assert.Equal(["show_slug_key"], constraintNames);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — in-place migration (db/35-show-identity-migration.sh)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMigrationAddsTheColumnsInPlace(DatabaseFixture db)
    {
        [Fact]
        public async Task MigrationAddsTheSevenShowColumnsAndBothShowIdColumnsInPlace()
        {
            // T239 review: station.show shipped dormant through T238 (never a single row, any test
            // order), but PLAN T239's own repository specs (ScenarioAuthoredCrud/
            // ScenarioRejectingInvalidShows below) now insert real rows and leave them behind for
            // whichever sibling class runs next — this class carries no ordering guarantee against
            // them. Re-adding `slug text not null` below with no DEFAULT fails outright if a leftover
            // row exists at that instant (NOT NULL against an unbackfillable new column), so clear the
            // table first — this scenario's own DROP COLUMN a few lines down removes any pre-existing
            // slug value anyway, so a row surviving this reset would only ever break the re-ADD, never
            // help prove anything about it.
            await db.ResetShowAsync();

            // Simulate a pre-T238 database by dropping the seven station.show columns db/35 adds
            // (DROP COLUMN also drops the show_slug_key UNIQUE constraint the slug column carries)
            // plus the two show_id stamp columns it adds on the other two tables.
            await using (var conn = await db.StationDataSource.OpenConnectionAsync())
            {
                await conn.ExecuteAsync(
                    """
                    alter table station.show
                      drop column if exists slug,
                      drop column if exists tagline,
                      drop column if exists flavor,
                      drop column if exists imported_from,
                      drop column if exists imported_at,
                      drop column if exists persona_id,
                      drop column if exists envelope
                    """);
                await conn.ExecuteAsync("alter table station.booth_log drop column if exists show_id");
            }
            await using (var conn = await db.DataSource.OpenConnectionAsync())
                await conn.ExecuteAsync("alter table library.media drop column if exists show_id");

            Assert.Null(await QueryColumnAsync(db, "show", "slug"));
            Assert.Null(await QueryColumnAsync(db, "show", "tagline"));
            Assert.Null(await QueryColumnAsync(db, "show", "flavor"));
            Assert.Null(await QueryColumnAsync(db, "show", "imported_from"));
            Assert.Null(await QueryColumnAsync(db, "show", "imported_at"));
            Assert.Null(await QueryColumnAsync(db, "show", "persona_id"));
            Assert.Null(await QueryColumnAsync(db, "show", "envelope"));
            Assert.Null(await QueryColumnAsync(db, "booth_log", "show_id"));
            Assert.Null(await QueryLibraryMediaColumnAsync(db, "show_id"));

            RunMigrationScript(db);

            var slug = await QueryColumnAsync(db, "show", "slug");
            Assert.NotNull(slug);
            Assert.Equal("text", slug.Value.DataType);
            Assert.Equal("NO", slug.Value.IsNullable);

            var tagline = await QueryColumnAsync(db, "show", "tagline");
            Assert.NotNull(tagline);
            Assert.Equal("text", tagline.Value.DataType);
            Assert.Equal("YES", tagline.Value.IsNullable);

            var flavor = await QueryColumnAsync(db, "show", "flavor");
            Assert.NotNull(flavor);
            Assert.Equal("text", flavor.Value.DataType);
            Assert.Equal("YES", flavor.Value.IsNullable);

            var importedFrom = await QueryColumnAsync(db, "show", "imported_from");
            Assert.NotNull(importedFrom);
            Assert.Equal("text", importedFrom.Value.DataType);
            Assert.Equal("YES", importedFrom.Value.IsNullable);

            var importedAt = await QueryColumnAsync(db, "show", "imported_at");
            Assert.NotNull(importedAt);
            Assert.Equal("timestamp with time zone", importedAt.Value.DataType);
            Assert.Equal("YES", importedAt.Value.IsNullable);

            var personaId = await QueryColumnAsync(db, "show", "persona_id");
            Assert.NotNull(personaId);
            Assert.Equal("integer", personaId.Value.DataType);
            Assert.Equal("YES", personaId.Value.IsNullable);

            var envelope = await QueryColumnAsync(db, "show", "envelope");
            Assert.NotNull(envelope);
            Assert.Equal("jsonb", envelope.Value.DataType);
            Assert.Equal("YES", envelope.Value.IsNullable);

            var boothLogShowId = await QueryColumnAsync(db, "booth_log", "show_id");
            Assert.NotNull(boothLogShowId);
            Assert.Equal("integer", boothLogShowId.Value.DataType);
            Assert.Equal("YES", boothLogShowId.Value.IsNullable);

            var libraryMediaShowId = await QueryLibraryMediaColumnAsync(db, "show_id");
            Assert.NotNull(libraryMediaShowId);
            Assert.Equal("integer", libraryMediaShowId.Value.DataType);
            Assert.Equal("YES", libraryMediaShowId.Value.IsNullable);

            // The convergence guard (see file header): the ALTER path lands on the identical literal
            // constraint name (show_slug_key) the fresh-init snapshot observes from db/06's CREATE
            // path. QueryUniqueConstraintNameAsync's own single-row contract additionally proves
            // exactly one UNIQUE constraint exists on the table.
            var constraintName = await QueryUniqueConstraintNameAsync(db, "station.show");
            Assert.Equal("show_slug_key", constraintName);
        }
    }

    // ---------------------------------------------------------------------
    // T239 — repository CRUD, now real (Integration)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAuthoredCrud(DatabaseFixture db)
    {
        [Fact]
        public async Task RoundTripsEveryField()
        {
            // Given an authored show "Night Moves" (tagline + flavor within budgets)
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db);
            var created = Assert.IsType<ShowWriteResult.Created>(await repo.CreateAsync(
                new ShowDraft("Night Moves", "Late-night deep cuts", "moody, sparse, past midnight"),
                CancellationToken.None));

            // When it is created, edited, and re-read through the repository
            var edited = Assert.IsType<ShowWriteResult.Updated>(await repo.UpdateAsync(
                created.Show.Id,
                new ShowDraft("Night Moves", "Late-night deep cuts, revisited", "moodier, sparser, past 1am"),
                CancellationToken.None));
            var read = await repo.GetByIdAsync(created.Show.Id, CancellationToken.None);

            // Then name, slug, tagline, and flavor all round-trip; provenance stays NULL — pinned in
            // one assertion via full-record equality against the literal expected values (CreatedAt/
            // UpdatedAt are server-set, so those two are taken from the write results themselves).
            var expected = new Show(
                created.Show.Id,
                "Night Moves",
                created.Show.Slug,
                "Late-night deep cuts, revisited",
                "moodier, sparser, past 1am",
                ImportedFrom: null,
                ImportedAt: null,
                created.Show.CreatedAt,
                edited.Show.UpdatedAt);
            Assert.Equal(expected, read);
        }

        [Fact]
        public async Task SlugDerivesViaHouseSlugify()
        {
            // Given an authored create with name "Night Moves"
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db);

            // When the row lands
            var created = Assert.IsType<ShowWriteResult.Created>(
                await repo.CreateAsync(new ShowDraft("Night Moves"), CancellationToken.None));

            // Then slug is the house Slugify output (the T68 golden-table contract) — compared
            // against the real LegacyPersonaCardMapper.Slugify call, not a hand-typed literal, so a
            // future Slugify change can never silently disagree with what CreateAsync actually wrote.
            Assert.Equal(LegacyPersonaCardMapper.Slugify("Night Moves"), created.Show.Slug);
        }

        [Fact]
        public async Task OneDjManyShows()
        {
            // Given one persona — deliberately never created here: persona linkage is block-level
            // (later tasks; F115.2's dormant persona_id stays unread this epic), so this fact stays
            // honest to its own GWT comment by proving only the show side: three shows authored with
            // nothing referencing a persona at all.
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db);

            // When three shows are authored (later assignable across their blocks), deliberately out
            // of alphabetical order
            await repo.CreateAsync(new ShowDraft("Morning Drive"), CancellationToken.None);
            await repo.CreateAsync(new ShowDraft("Afternoon Session"), CancellationToken.None);
            await repo.CreateAsync(new ShowDraft("Night Moves"), CancellationToken.None);

            // Then nothing structural objects — shows-per-DJ is unbounded by design (STORY-305 AC3):
            // all three land, none rejected, and GetAllAsync's own IShowStore contract ("ordered by
            // name") reads back alphabetically regardless of the create order above.
            var all = await repo.GetAllAsync(CancellationToken.None);
            Assert.Equal(["Afternoon Session", "Morning Drive", "Night Moves"], all.Select(s => s.Name));
        }

        [Fact]
        public async Task UpdateAdvancesUpdatedAt()
        {
            // Given an authored show (mirrors Story118_PersonaStorage's own UpdateAdvancesUpdatedAt)
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db);
            var created = Assert.IsType<ShowWriteResult.Created>(
                await repo.CreateAsync(new ShowDraft("Original Name"), CancellationToken.None));

            // When it is renamed via an edit
            var updated = Assert.IsType<ShowWriteResult.Updated>(await repo.UpdateAsync(
                created.Show.Id, new ShowDraft("Renamed"), CancellationToken.None));

            // Then updated_at moves forward and the read reflects the rename
            Assert.True(updated.Show.UpdatedAt > created.Show.UpdatedAt);
            var read = await repo.GetByIdAsync(created.Show.Id, CancellationToken.None);
            Assert.NotNull(read);
            Assert.Equal("Renamed", read.Name);
        }

        [Fact]
        public async Task BudgetsAcceptAtExactlyTheLimit()
        {
            // Given a draft with every field at its exact SPEC F115.1 1× budget — the boundary is
            // inclusive (BudgetsRejectAtOneTimes, below, proves budget+1 rejects)
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db);
            var draft = new ShowDraft(
                new string('n', ShowBudgets.NameMaxChars),
                new string('t', ShowBudgets.TaglineMaxChars),
                new string('f', ShowBudgets.FlavorMaxChars));

            // When the repository write is attempted
            var outcome = await repo.CreateAsync(draft, CancellationToken.None);

            // Then it is accepted, not rejected
            var created = Assert.IsType<ShowWriteResult.Created>(outcome);
            Assert.Equal(draft.Name, created.Show.Name);
            Assert.Equal(draft.Tagline, created.Show.Tagline);
            Assert.Equal(draft.Flavor, created.Show.Flavor);
        }

        [Fact]
        public async Task EmptyOrWhitespaceTaglineAndFlavorPersistAsNull()
        {
            // Given a show authored with a blank tagline and a whitespace-only flavor (create path)
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db);
            var created = Assert.IsType<ShowWriteResult.Created>(await repo.CreateAsync(
                new ShowDraft("Night Moves", Tagline: "", Flavor: "   "), CancellationToken.None));

            // Then both coerce to null on create — Show/ShowDraft's own docs promise null "when the
            // show carries none", never a stray empty/whitespace string
            Assert.Null(created.Show.Tagline);
            Assert.Null(created.Show.Flavor);

            // When an edit first gives the show a real tagline/flavor, then a later edit clears both
            // back to blank/whitespace (update path)
            var withValues = Assert.IsType<ShowWriteResult.Updated>(await repo.UpdateAsync(
                created.Show.Id,
                new ShowDraft("Night Moves", "Real tagline", "Real flavor"),
                CancellationToken.None));
            Assert.Equal("Real tagline", withValues.Show.Tagline);
            Assert.Equal("Real flavor", withValues.Show.Flavor);

            var cleared = Assert.IsType<ShowWriteResult.Updated>(await repo.UpdateAsync(
                created.Show.Id,
                new ShowDraft("Night Moves", Tagline: "", Flavor: "   "),
                CancellationToken.None));

            // Then the cleared fields read back null through UpdateAsync too, never the empty/
            // whitespace string verbatim
            Assert.Null(cleared.Show.Tagline);
            Assert.Null(cleared.Show.Flavor);
            var read = await repo.GetByIdAsync(created.Show.Id, CancellationToken.None);
            Assert.NotNull(read);
            Assert.Null(read.Tagline);
            Assert.Null(read.Flavor);
        }
    }

    // ---------------------------------------------------------------------
    // T239 — GetBySlugAsync (Integration)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioGetBySlugAsync(DatabaseFixture db)
    {
        [Fact]
        public async Task ReturnsTheShowWhenTheSlugExists()
        {
            // Given an authored show
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db);
            var created = Assert.IsType<ShowWriteResult.Created>(
                await repo.CreateAsync(new ShowDraft("Night Moves"), CancellationToken.None));

            // When it is looked up by its derived slug
            var read = await repo.GetBySlugAsync(created.Show.Slug, CancellationToken.None);

            // Then the same row comes back
            Assert.Equal(created.Show, read);
        }

        [Fact]
        public async Task ReturnsNullWhenTheSlugIsUnknown()
        {
            // Given an empty table
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db);

            // When a slug naming no row is looked up
            var read = await repo.GetBySlugAsync("no-such-show", CancellationToken.None);

            // Then it misses cleanly
            Assert.Null(read);
        }
    }

    // ---------------------------------------------------------------------
    // T239 — DeleteAsync (Integration)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioDeleteAsync(DatabaseFixture db)
    {
        [Fact]
        public async Task DeletesAnExistingShow()
        {
            // Given an authored show with no references
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db);
            var created = Assert.IsType<ShowWriteResult.Created>(
                await repo.CreateAsync(new ShowDraft("To Be Deleted"), CancellationToken.None));

            // When it is deleted
            var outcome = await repo.DeleteAsync(created.Show.Id, CancellationToken.None);

            // Then it succeeds and the row is gone
            Assert.IsType<ShowWriteResult.Deleted>(outcome);
            Assert.Null(await repo.GetByIdAsync(created.Show.Id, CancellationToken.None));
        }

        [Fact]
        public async Task ReturnsNotFoundForAMissingShow()
        {
            // Given an empty table (no show with this id has ever existed)
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db);

            // When a delete targets an id that names no row
            var outcome = await repo.DeleteAsync(999_999, CancellationToken.None);

            // Then it reports not found, never a silent no-op success
            Assert.IsType<ShowWriteResult.NotFound>(outcome);
        }
    }

    // ---------------------------------------------------------------------
    // T239 — DeleteAsync's FK guard (SPEC F115.4): station.segment_schedule.show_id's own ON DELETE
    // RESTRICT, surfaced as ShowWriteResult.Referenced (mirrors Story118_PersonaStorage's own
    // ScenarioDeleteFkGuard idiom — direct SQL insert into segment_schedule, bypassing ScheduleRepository
    // entirely, so this fact exercises the FK itself rather than any other seam).
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioDeleteFkGuard(DatabaseFixture db)
    {
        static async Task InsertScheduleRowReferencingShowAsync(
            DatabaseFixture db, long showId, int dayOfWeek = 1, int startMinute = 540, int endMinute = 720)
        {
            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            await conn.ExecuteAsync(
                """
                insert into station.segment_schedule (day_of_week, start_minute, end_minute, show_id)
                values (@dayOfWeek, @startMinute, @endMinute, @showId)
                """,
                new { dayOfWeek, startMinute, endMinute, showId });
        }

        [Fact]
        public async Task DeletingAReferencedShowIsRejectedThenSucceedsOnceUnreferenced()
        {
            // Given an authored show referenced by one schedule row
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db);
            var created = Assert.IsType<ShowWriteResult.Created>(
                await repo.CreateAsync(new ShowDraft("Scheduled Show"), CancellationToken.None));
            await InsertScheduleRowReferencingShowAsync(db, created.Show.Id);

            // When delete is attempted while the reference stands
            var whileReferenced = await repo.DeleteAsync(created.Show.Id, CancellationToken.None);

            // Then the FK's own RESTRICT rejects it — the store-level case names nothing beyond
            // "referenced" (PLAN T240 enriches this with block-naming at the endpoint layer)
            Assert.IsType<ShowWriteResult.Referenced>(whileReferenced);

            // When the reference is removed and delete is retried
            await using (var conn = await db.StationDataSource.OpenConnectionAsync())
                await conn.ExecuteAsync(
                    "delete from station.segment_schedule where show_id = @showId", new { showId = created.Show.Id });
            var onceUnreferenced = await repo.DeleteAsync(created.Show.Id, CancellationToken.None);

            // Then it succeeds
            Assert.IsType<ShowWriteResult.Deleted>(onceUnreferenced);
        }

        // PLAN T240 review — ScheduleRepository.GetSlotsByShowIdAsync itself (the show delete guard's
        // own endpoint-layer detail read, ShowsController.Delete's Referenced case) carried zero live
        // Postgres coverage before the two facts below: only the FK's own RESTRICT, above, was proven
        // for real. Mirrors Story118_PersonaStorage.cs's own ScenarioDeleteFkGuard round-trip facts,
        // just against ScheduleRepository directly rather than through a ShowRepository.DeleteAsync
        // FK trip (this method is never called from that seam — ShowsController calls it directly).

        [Fact]
        public async Task GetSlotsByShowIdAsyncRoundTripsASingleSlot()
        {
            // Given an authored show referenced by exactly one schedule row
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db);
            var created = Assert.IsType<ShowWriteResult.Created>(
                await repo.CreateAsync(new ShowDraft("Slot Round Trip Show"), CancellationToken.None));
            await InsertScheduleRowReferencingShowAsync(
                db, created.Show.Id, dayOfWeek: 1, startMinute: 540, endMinute: 720);

            // When ScheduleRepository.GetSlotsByShowIdAsync reads it back
            var slots = await ScheduleRepo(db).GetSlotsByShowIdAsync(created.Show.Id, CancellationToken.None);

            // Then day/start/end round-trip through the real SQL + Dapper mapping exactly — not just
            // scripted on a fake, the way ShowsController's own wire specs cover this shape
            var slot = Assert.Single(slots);
            Assert.Equal(DayOfWeek.Monday, slot.Day);
            Assert.Equal(540, slot.StartMinute);
            Assert.Equal(720, slot.EndMinute);
        }

        [Fact]
        public async Task GetSlotsByShowIdAsyncOrdersMultipleSlotsByDayThenStartMinute()
        {
            // Given an authored show referenced by two schedule rows, inserted deliberately out of
            // order (Tuesday before Monday) — proves the query orders by day/start_minute, not
            // insert order
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db);
            var created = Assert.IsType<ShowWriteResult.Created>(
                await repo.CreateAsync(new ShowDraft("Busy Show"), CancellationToken.None));
            await InsertScheduleRowReferencingShowAsync(
                db, created.Show.Id, dayOfWeek: 2, startMinute: 840, endMinute: 960);
            await InsertScheduleRowReferencingShowAsync(
                db, created.Show.Id, dayOfWeek: 1, startMinute: 540, endMinute: 720);

            // When ScheduleRepository.GetSlotsByShowIdAsync reads them back
            var slots = await ScheduleRepo(db).GetSlotsByShowIdAsync(created.Show.Id, CancellationToken.None);

            // Then the result comes back ordered day-then-start-minute
            Assert.Equal(2, slots.Count);
            Assert.Equal(DayOfWeek.Monday, slots[0].Day);
            Assert.Equal(540, slots[0].StartMinute);
            Assert.Equal(DayOfWeek.Tuesday, slots[1].Day);
            Assert.Equal(840, slots[1].StartMinute);
        }
    }

    // ---------------------------------------------------------------------
    // PLAN T240 review — ShowImagingScopeRepository.UnscopeAsync (SPEC F115.4): the only writer of
    // library.media.show_id, over the library connection (station_svc has no grant on library.media —
    // the same db/22 cross-schema boundary IMediaLibraryMembership's own remarks already carry).
    // Deliberately no station.show row involved: F117.1's own show_id column carries NO FK, by
    // design, so this seam is provably reachable with any long id, real show or not.
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioShowImagingScopeRepository(DatabaseFixture db)
    {
        static async Task<long> InsertScopedMediaRowAsync(DatabaseFixture db, long showId, string title)
        {
            await using var conn = await db.DataSource.OpenConnectionAsync();
            return await conn.ExecuteScalarAsync<long>(
                """
                insert into library.media (path, format, size_bytes, mtime, title, show_id)
                values (@path, 'flac', 100, now(), @title, @showId)
                returning id
                """,
                new { path = $"/imaging/{Guid.NewGuid()}.flac", title, showId });
        }

        [Fact]
        public async Task UnscopeAsyncClearsShowIdAndNamesTheRowItCleared()
        {
            // Given a library.media row scoped to a show
            await db.ResetAsync();
            const long showId = 4242L;
            var mediaId = await InsertScopedMediaRowAsync(db, showId, "Sunset Ident");
            var repo = new ShowImagingScopeRepository(db.DataSource);

            // When UnscopeAsync clears every row scoped to that show
            var unscoped = await repo.UnscopeAsync(showId, CancellationToken.None);

            // Then the single UPDATE ... RETURNING named exactly the row it cleared, and a fresh read
            // confirms show_id genuinely persists as NULL — not just what the RETURNING clause claims
            var row = Assert.Single(unscoped);
            await using var conn = await db.DataSource.OpenConnectionAsync();
            var showIdAfter = await conn.ExecuteScalarAsync<long?>(
                "select show_id from library.media where id = @mediaId", new { mediaId });
            Assert.Equal(
                (MediaId: mediaId, Title: "Sunset Ident", ShowIdAfter: (long?)null),
                (MediaId: row.MediaId, Title: row.Title, ShowIdAfter: showIdAfter));
        }
    }

    // ---------------------------------------------------------------------
    // T238 dormant bundle columns — now real (Integration)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioDormantBundleColumns(DatabaseFixture db)
    {
        [Fact]
        public async Task DormantColumnsExistAndDefaultNull()
        {
            // Given the fresh-init schema snapshot (see file header)...
            var personaId = InitialStationColumn(db, "show", "persona_id");
            var envelope = InitialStationColumn(db, "show", "envelope");

            // ...persona_id and envelope carry NO DEFAULT — an implicit value here would be as much
            // an out-of-band write as a stray non-NULL row below (this column_default assertion was
            // previously missing despite the fact's own name).
            Assert.Null(personaId.ColumnDefault);
            Assert.Null(envelope.ColumnDefault);

            // Converge the live schema before writing: the two assertions above deliberately read the
            // frozen fresh-init snapshot (InitialSchema), never the live database, so this call cannot
            // affect their falsifiability. But the insert below needs station.show.slug to actually
            // exist on the live connection, and this class carries no ordering guarantee against
            // Story304_AiredKindStamp's own in-place scenario, which drops and recreates station.show
            // via db/33's pre-T238 shape (no slug column) elsewhere in the same run. Running the
            // idempotent migration script here — unconditionally, right before the live connection
            // opens — makes this fact's live-schema dependency self-sufficient regardless of xUnit's
            // class scheduling.
            RunMigrationScript(db);

            // When a row is inserted through the bare name/slug columns — deliberately NOT through
            // ShowRepository (PLAN T239's real writer of this table as of this task, but it has no
            // parameter for persona_id/envelope at all — SPEC F115.2's "unread this epic" law — so a
            // raw INSERT is the only way to even attempt setting them, and this one doesn't either) —
            // inside a transaction that always rolls back so this fact leaves nothing behind for its
            // siblings...
            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            var id = await conn.ExecuteScalarAsync<int>(
                "insert into station.show (name, slug) values ('Dormant Probe', 'dormant-probe') returning id",
                transaction: tx);

            var row = await conn.QuerySingleAsync<(bool PersonaIdSet, bool EnvelopeSet)>(
                """
                select persona_id is not null as persona_id_set, envelope is not null as envelope_set
                from station.show where id = @id
                """,
                new { id }, tx);

            await tx.RollbackAsync();

            // Then persona_id and envelope both read back NULL on it — a falsifiable guard: a column
            // DEFAULT or an INSERT trigger/rule that set either column would turn this red, unlike the
            // prior `count(*) where … is not null` scan over a table no writer ever touches, which
            // was structurally 0 and could never fail.
            Assert.False(row.PersonaIdSet);
            Assert.False(row.EnvelopeSet);
        }
    }

    // ---------------------------------------------------------------------
    // T239 — validation & conflicts, now real (Integration)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRejectingInvalidShows(DatabaseFixture db)
    {
        [Fact]
        public async Task BudgetsRejectAtOneTimes()
        {
            // Given a show whose flavor exceeds 400 chars (the same 1x line also covers name > 60,
            // tagline > 120 — ShowRepository.ValidateBudgets checks all three; flavor is the
            // representative case here, mirroring the GWT comment's own primary example)
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db);
            var draft = new ShowDraft("Night Moves", Flavor: new string('a', ShowBudgets.FlavorMaxChars + 1));

            // When the repository write is attempted
            var outcome = await repo.CreateAsync(draft, CancellationToken.None);

            // Then it rejects at the seam — the 1× budget is the app-side hard line (F115.1)
            Assert.Equal(new ShowWriteResult.BudgetExceeded(ShowBudgetField.Flavor), outcome);
        }

        [Fact]
        public async Task NameOverBudgetRejected()
        {
            // Given a show whose name exceeds 60 chars (PLAN T240 review A2 — a dedicated name-budget
            // fact beside the tagline/flavor ones above; ShowBudgets.FirstViolation checks name first,
            // so this also pins that field-order precedence rather than trusting it un-Fact-pinned)
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db);
            var draft = new ShowDraft(new string('n', ShowBudgets.NameMaxChars + 1));

            // When the repository write is attempted
            var outcome = await repo.CreateAsync(draft, CancellationToken.None);

            // Then it rejects at the seam, naming the name field specifically
            Assert.Equal(new ShowWriteResult.BudgetExceeded(ShowBudgetField.Name), outcome);
        }

        [Fact]
        public async Task DuplicateSlugRejected()
        {
            // Given an existing show slug
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db);
            await repo.CreateAsync(new ShowDraft("Night Moves"), CancellationToken.None);

            // When a second show would land on the same slug
            var outcome = await repo.CreateAsync(new ShowDraft("Night Moves"), CancellationToken.None);

            // Then the unique constraint surfaces as a conflict, not a silent overwrite
            Assert.IsType<ShowWriteResult.SlugConflict>(outcome);
        }

        [Fact]
        public async Task UpdateRejectsABudgetViolation()
        {
            // Given an existing show
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db);
            var created = Assert.IsType<ShowWriteResult.Created>(
                await repo.CreateAsync(new ShowDraft("Night Moves"), CancellationToken.None));

            // When it is edited with a tagline over its SPEC F115.1 1× budget
            var outcome = await repo.UpdateAsync(
                created.Show.Id,
                new ShowDraft("Night Moves", Tagline: new string('a', ShowBudgets.TaglineMaxChars + 1)),
                CancellationToken.None);

            // Then it rejects at the seam the same way CreateAsync does — checked before the write
            // ever reaches Postgres, the row left unchanged
            Assert.Equal(new ShowWriteResult.BudgetExceeded(ShowBudgetField.Tagline), outcome);
        }

        [Fact]
        public async Task UpdateRejectsASlugConflict()
        {
            // Given two existing shows
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db);
            await repo.CreateAsync(new ShowDraft("Night Moves"), CancellationToken.None);
            var second = Assert.IsType<ShowWriteResult.Created>(
                await repo.CreateAsync(new ShowDraft("Day Drift"), CancellationToken.None));

            // When the second is renamed onto the first's slug
            var outcome = await repo.UpdateAsync(
                second.Show.Id, new ShowDraft("Night Moves"), CancellationToken.None);

            // Then the unique constraint surfaces as a conflict, not a silent overwrite
            Assert.IsType<ShowWriteResult.SlugConflict>(outcome);
        }

        [Fact]
        public async Task UpdateReturnsNotFoundForAMissingShow()
        {
            // Given an empty table (no show with this id has ever existed)
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db);

            // When an edit targets an id that names no row
            var outcome = await repo.UpdateAsync(999_999, new ShowDraft("Night Moves"), CancellationToken.None);

            // Then it reports not found, never a silent insert
            Assert.IsType<ShowWriteResult.NotFound>(outcome);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task BlankOrWhitespaceNameRejected(string name)
        {
            // Given a draft whose name is blank or whitespace-only
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db);

            // When the repository write is attempted
            var outcome = await repo.CreateAsync(new ShowDraft(name), CancellationToken.None);

            // Then it rejects at the seam, never reaching station.show's own NOT NULL/check
            // (length(btrim(name)) > 0) constraint (db/33)
            Assert.IsType<ShowWriteResult.InvalidName>(outcome);
        }

        [Theory]
        [InlineData("🎧🎶")]
        // PLAN T240 review A1: ValidateName rejects on the RESULTING slug equaling the fallback
        // literal, not on "how" it got there — the literal name "Persona" slugifies to "persona" the
        // ORDINARY way (lowercases unchanged; Slugify's own empty-slug rescue never fires, since no
        // character is non-alphanumeric) and must reject exactly the same as the emoji-only case above.
        [InlineData("Persona")]
        public async Task NameThatSlugifiesToTheFallbackLiteralRejected(string name)
        {
            // Given a name whose Slugify output is the bare literal "persona" — either via the
            // empty-slug rescue (an emoji-only name) or the ordinary lowercase-unchanged path
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db);
            Assert.Equal("persona", LegacyPersonaCardMapper.Slugify(name));

            // When the repository write is attempted
            var outcome = await repo.CreateAsync(new ShowDraft(name), CancellationToken.None);

            // Then it rejects — REJECT, not silently autocorrect into a misleadingly-named show
            Assert.IsType<ShowWriteResult.InvalidName>(outcome);
        }

        [Fact]
        public async Task UpdateRejectsABlankName()
        {
            // Given an existing show
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db);
            var created = Assert.IsType<ShowWriteResult.Created>(
                await repo.CreateAsync(new ShowDraft("Night Moves"), CancellationToken.None));

            // When it is edited with a blank name
            var outcome = await repo.UpdateAsync(created.Show.Id, new ShowDraft(""), CancellationToken.None);

            // Then it rejects at the seam the same way CreateAsync does, the row left unchanged
            Assert.IsType<ShowWriteResult.InvalidName>(outcome);
            var read = await repo.GetByIdAsync(created.Show.Id, CancellationToken.None);
            Assert.NotNull(read);
            Assert.Equal("Night Moves", read.Name);
        }
    }

    // ---------------------------------------------------------------------
    // T254 — ImportAsync (SPEC F118.2, STORY-315): the single-statement upsert-by-slug write path
    // (real Postgres — every OTHER T254 gate, including the collision/reservation/budget/schema-major
    // rules, is app-seam validation proven against FakeShowStore in
    // GenWave.Host.Tests/Specs/Story315_ShowImport.cs; this file's own job is proving the RAW SQL
    // upsert itself: insert, re-import-replaces, blank-collapses-to-null, and updated_at advances).
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioImportAsync(DatabaseFixture db)
    {
        [Fact]
        public async Task InsertsANewRowWithProvenanceStamped()
        {
            // Given an empty table
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db);

            // When a show is imported under a fresh slug
            var imported = await repo.ImportAsync(
                "night-moves", "Night Moves", "Late-night deep cuts", "moody, sparse", "catalog-entry-slug",
                CancellationToken.None);

            // Then it lands whole, provenance stamped, and reads back identically (a fresh slug can
            // never trip the conflict-only WHERE guard — see ImportAsync's own remarks)
            Assert.NotNull(imported);
            Assert.Equal(
                (Name: "Night Moves", Slug: "night-moves", Tagline: "Late-night deep cuts", Flavor: "moody, sparse",
                 ImportedFrom: "catalog-entry-slug", ImportedAtSet: true),
                (imported.Name, imported.Slug, imported.Tagline, imported.Flavor, imported.ImportedFrom,
                 ImportedAtSet: imported.ImportedAt is not null));
            var read = await repo.GetBySlugAsync("night-moves", CancellationToken.None);
            Assert.Equal(imported, read);
        }

        [Fact]
        public async Task ReimportingTheSameSlugReplacesFieldsAndRestampsProvenanceWithExactlyOneRow()
        {
            // Given a show already imported once
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db);
            var first = await repo.ImportAsync(
                "night-moves", "Night Moves", "First cut", "moody", "entry-a", CancellationToken.None);
            Assert.NotNull(first);

            // When it is imported again, under the SAME slug, with different content and provenance —
            // the conflict-only WHERE guard passes (the existing row's own imported_from is non-null)
            var second = await repo.ImportAsync(
                "night-moves", "Night Moves Redux", "Second cut", "moodier", "entry-b", CancellationToken.None);

            // Then the SAME row (id unchanged) is replaced whole — no duplicate insert — and
            // imported_at/updated_at both advance past the first import's own stamp
            Assert.NotNull(second);
            var all = await repo.GetAllAsync(CancellationToken.None);
            Assert.Equal(
                (RowCount: 1, Id: first.Id, Name: "Night Moves Redux", Tagline: "Second cut", Flavor: "moodier",
                 ImportedFrom: "entry-b", ImportedAtAdvanced: second.ImportedAt > first.ImportedAt,
                 UpdatedAtAdvanced: second.UpdatedAt > first.UpdatedAt),
                (RowCount: all.Count, second.Id, second.Name, second.Tagline, second.Flavor, second.ImportedFrom,
                 ImportedAtAdvanced: second.ImportedAt > first.ImportedAt,
                 UpdatedAtAdvanced: second.UpdatedAt > first.UpdatedAt));
        }

        [Fact]
        public async Task BlankOrWhitespaceTaglineAndFlavorPersistAsNull()
        {
            // Given a manifest whose tagline is blank and flavor is whitespace-only (the manifest's
            // own "" export shape for "no tagline/flavor" — ShowManifest's own remarks)
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db);

            // When it is imported
            var imported = await repo.ImportAsync(
                "night-moves", "Night Moves", "", "   ", "file", CancellationToken.None);

            // Then both collapse to null — the SAME NullIfBlank contract CreateAsync/UpdateAsync
            // already honour (Show's own "null when the show carries none" contract), never a stray
            // empty/whitespace string
            Assert.NotNull(imported);
            Assert.Null(imported.Tagline);
            Assert.Null(imported.Flavor);
        }

        [Fact]
        public async Task ImportOverAnAuthoredShowsSlugIsDeclinedAtomicallyAndTheAuthoredRowSurvives()
        {
            // F2 review finding — INVERTED from this fact's own pre-review shape (which pinned the
            // opposite, unguarded overwrite): SPEC F115.5's authored-collision gate now lives INSIDE
            // this very statement (ON CONFLICT ... WHERE imported_from IS NOT NULL — the gh-#394
            // conditional-write form, ImportAsync's own remarks), not only at the ShowsController seam
            // — so a caller bypassing the controller gate entirely (a hand-rolled script, a different
            // route) still cannot silently clobber an authored row through this store.
            RunMigrationScript(db);
            await db.ResetShowAsync();
            var repo = Repo(db);
            var authored = Assert.IsType<ShowWriteResult.Created>(
                await repo.CreateAsync(new ShowDraft("Authored Show"), CancellationToken.None));
            Assert.Null(authored.Show.ImportedFrom);

            // When an import targets the SAME slug
            var imported = await repo.ImportAsync(
                authored.Show.Slug, "Overwritten Show", null, null, "some-catalog-entry", CancellationToken.None);

            // Then the conditional upsert declines (null — the WHERE guard's own signal) and the
            // authored row survives completely unchanged, byte for byte.
            Assert.Null(imported);
            var stillThere = await repo.GetByIdAsync(authored.Show.Id, CancellationToken.None);
            Assert.Equal(authored.Show, stillThere);
        }
    }
}

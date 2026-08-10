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
// epic (F115.2) — rather than the T239 repository CRUD pending scaffold below it, which still awaits
// the Show type and store T239 builds.
//
// ScenarioAuthoredCrud and ScenarioRejectingInvalidShows remain PENDING (T239): the types under spec
// (Show, the show store) do not exist yet. The endpoint half lives in
// GenWave.Host.Tests/Specs/Story305_ShowsApi.cs.

using Dapper;

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
    // T239 pending scaffold — repository CRUD (still pending; unchanged by T238)
    // ---------------------------------------------------------------------

    public sealed class ScenarioAuthoredCrud
    {
        [Fact(Skip = "Pending (T239)")]
        public void RoundTripsEveryField()
        {
            // Given an authored show "Night Moves" (tagline + flavor within budgets)
            // When  it is created, edited, and re-read through the repository
            // Then  name, slug, tagline, and flavor all round-trip; provenance stays NULL
        }

        [Fact(Skip = "Pending (T239)")]
        public void SlugDerivesViaHouseSlugify()
        {
            // Given an authored create with name "Night Moves"
            // When  the row lands
            // Then  slug is the house Slugify output (the T68 golden-table contract)
        }

        [Fact(Skip = "Pending (T239)")]
        public void OneDjManyShows()
        {
            // Given one persona
            // When  three shows are authored (later assignable across their blocks)
            // Then  nothing structural objects — shows-per-DJ is unbounded by design (STORY-305 AC3)
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

            // When a row is inserted through the only two columns any writer touches (name, slug —
            // the table has no writer anywhere this epic, F115.2), inside a transaction that always
            // rolls back so this fact leaves nothing behind for its siblings...
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
    // T239 pending scaffold — validation & conflicts (still pending; unchanged by T238)
    // ---------------------------------------------------------------------

    public sealed class ScenarioRejectingInvalidShows
    {
        [Fact(Skip = "Pending (T239)")]
        public void BudgetsRejectAtOneTimes()
        {
            // Given a show whose flavor exceeds 400 chars (or name > 60, tagline > 120)
            // When  the repository write is attempted
            // Then  it rejects at the seam — the 1× budget is the app-side hard line (F115.1)
        }

        [Fact(Skip = "Pending (T239)")]
        public void DuplicateSlugRejected()
        {
            // Given an existing show slug
            // When  a second show would land on the same slug
            // Then  the unique constraint surfaces as a conflict, not a silent overwrite
        }
    }
}

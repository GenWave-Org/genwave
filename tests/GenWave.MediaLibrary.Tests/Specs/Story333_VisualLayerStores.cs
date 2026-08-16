// STORY-333 — The visual layer's stores (db/37, anchored here for the DatabaseFixture;
// the one migration also serves STORY-332/337/339 — SPEC F128-F131, PLAN T290)
//
// BDD specification — xUnit. Integration: hits real Postgres via DatabaseCollection.
//
// Three honestly-separated shapes (PLAN T290 review — an earlier revision of this file put every fact
// under a single "fresh init carries the four tables" heading while every one of them actually called
// RunMigrationScript(db) first, which left the heading's own promise unproven: deleting db/06's own
// mirror of db/37 left every fact green, since db/37 itself silently supplied whatever db/06 failed to —
// mirrors the exact trap Story305_ShowRepository.cs's own file header describes and fixes the same way):
//
//   1. HAPPY PATH — fresh init (ScenarioFreshInitCarriesTheFiveTables): reads
//      DatabaseFixture.InitialSchema/InitialUniqueConstraints — the snapshot taken once, at fixture
//      init, of the PURE db/01+db/06 world, before any test class gets a chance to run db/37. No fact
//      in that scenario ever calls RunMigrationScript — that is precisely what makes dropping a column
//      (or a UNIQUE constraint) from db/06's own mirror block actually turn a fact red.
//   2. HAPPY PATH — constraint teeth (ScenarioPersonaAvatarIsAOneToOneExtension,
//      ScenarioPackAndIconAndStationTablesExist): every fact under CHECK/UNIQUE/FK cascade the DDL's
//      own constraint teeth directly with raw SQL over DatabaseFixture.StationDataSource — proving
//      business-rule enforcement the DDL carries, not the thin repositories T290 also ships (mirrors
//      Story109_RatingSchemaAndContract.cs's own "constraint teeth" shape for library.media_rating).
//      These DO call RunMigrationScript(db) first — defensively, so the fact holds regardless of
//      whichever sibling scenario class xUnit happens to run first — which is exactly why they are NOT
//      filed under the fresh-init heading above.
//   3. HAPPY PATH — repository round-trips (ScenarioPersonaAvatarRepository, ScenarioAvatarPackRepository,
//      ScenarioStationImageRepository): drives PersonaAvatarRepository/AvatarPackRepository/
//      StationImageRepository themselves (GenWave.MediaLibrary.Station) — T290 review: these three
//      classes carried zero live-Postgres coverage of their own SQL (upsert-by-conflict, the two-table
//      transaction, delete-then-reinsert-on-reinstall, the enum<->text mapping) before this section,
//      only the raw-DDL constraint facts above. Mirrors Story305_ShowRepository.cs's/
//      Story317_SpecialsStore.cs's own "drive the repository directly" precedent. These also call
//      RunMigrationScript(db) defensively for the same ordering reason as (2).
//
// SAD PATH — migration discipline (ScenarioRerunningTheMigrationIsIdempotent) is the one place left
// calling RunMigrationScript(db) TWICE in the same fact — that repetition IS what the fact is about.

using System.Text.Json;
using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureVisualLayerStores
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>Runs db/37-visual-layer-migration.sh against the test database via the fixture. Safe to
    /// call unconditionally — idempotent (CREATE TABLE IF NOT EXISTS). Used only where a fact is
    /// genuinely about the migration script itself (the idempotent-rerun scenario) or must defensively
    /// create the tables ahead of real work whose own ordering against xUnit's other scenario classes in
    /// this file is not guaranteed — mirrors Story305_ShowRepository.cs's own RunMigrationScript idiom.
    /// Deliberately NEVER called by the fresh-init scenario below — see this file's own header.</summary>
    static void RunMigrationScript(DatabaseFixture db) =>
        db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "37-visual-layer-migration.sh"));

    static async Task<bool> TableExistsAsync(DatabaseFixture db, string tableName)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        var count = await conn.ExecuteScalarAsync<long>(
            "select count(*) from information_schema.tables where table_schema = 'station' and table_name = @t",
            new { t = tableName });
        return count > 0;
    }

    /// <summary>Inserts a bare station.persona row (name only) purely to have a real id for a
    /// station.persona_avatar.persona_id foreign key to point at — mirrors ScheduleTestPersonas'
    /// own arrange-step helper, used directly (not through that class) so every raw-SQL fact below binds
    /// a plain `int` that matches station.persona.id's own int4 column type one-for-one. Unrelated to
    /// IPersonaAvatarStore's own persona_id parameter type (`long` — the house int4-column-behind-long-
    /// C#-seam convention IPersonaStore/IPersonaMemory/IPersonaTasteStore already carry): the `int`
    /// return here widens implicitly wherever a repository-round-trip fact below passes it into that
    /// seam instead.</summary>
    static async Task<int> InsertPersonaAsync(DatabaseFixture db, string name)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<int>(
            "insert into station.persona (name) values (@name) returning id", new { name });
    }

    static async Task InsertAvatarAsync(
        DatabaseFixture db, int personaId, string token, string source = "upload", string sha256 = "hash")
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            """
            insert into station.persona_avatar (persona_id, bytes, byte_size, sha256, token, source)
            values (@personaId, @bytes, 4, @sha256, @token, @source)
            """,
            new { personaId, bytes = new byte[] { 1, 2, 3, 4 }, sha256, token, source });
    }

    /// <summary>(data_type, is_nullable, column_default) for the named station-schema column, read from
    /// the fixture's fresh-init snapshot (<see cref="DatabaseFixture.InitialSchema"/>) rather than a
    /// live query — mirrors Story305_ShowRepository.cs's own InitialStationColumn helper (see that
    /// file's own remarks and this file's header for why the snapshot is the only way a dropped db/06
    /// mirror of db/37 can actually turn a fresh-init fact red). Fails the calling fact immediately if
    /// the column is missing from the snapshot.</summary>
    static (string DataType, string IsNullable, string? ColumnDefault) InitialStationColumn(
        DatabaseFixture db, string table, string column)
    {
        var found = db.InitialSchema.TryGetValue(("station", table, column), out var value);
        Assert.True(found, $"station.{table}.{column} missing from the fresh-init schema snapshot");
        return value;
    }

    /// <summary>The UNIQUE constraint names on the given bare station table name at the same fresh-init
    /// instant <see cref="InitialStationColumn"/> reads from — empty when the table carries none.
    /// Mirrors Story305_ShowRepository.cs's own InitialUniqueConstraintNames helper.</summary>
    static IReadOnlyList<string> InitialUniqueConstraintNames(DatabaseFixture db, string table) =>
        db.InitialUniqueConstraints.GetValueOrDefault(table, []);

    /// <summary>The T290 persona-avatar repository under spec, wired the same "Lazy over the fixture's
    /// own StationDataSource" way Story118_PersonaStorage.cs's own `Repo` helper wires
    /// PersonaRepository.</summary>
    static PersonaAvatarRepository PersonaAvatarRepo(DatabaseFixture db) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));

    /// <summary>The T290 avatar-pack repository under spec — same wiring as <see cref="PersonaAvatarRepo"/>.</summary>
    static AvatarPackRepository AvatarPackRepo(DatabaseFixture db) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));

    /// <summary>The T290 station-image repository under spec — same wiring as <see cref="PersonaAvatarRepo"/>.</summary>
    static StationImageRepository StationImageRepo(DatabaseFixture db) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));

    /// <summary>The T290 icon-pack repository under spec (PLAN T303 review rider — see file header's
    /// own (3) HAPPY PATH remarks: this class carried zero live-Postgres coverage before this task)
    /// — same wiring as <see cref="PersonaAvatarRepo"/>.</summary>
    static IconPackRepository IconPackRepo(DatabaseFixture db) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));

    // ---------------------------------------------------------------------
    // HAPPY PATH — fresh init (db/06's mirror of db/37) — see file header
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioFreshInitCarriesTheFiveTables(DatabaseFixture db)
    {
        // Every fact below reads DatabaseFixture.InitialSchema/InitialUniqueConstraints ONLY — no fact
        // here ever calls RunMigrationScript. That is precisely what makes dropping a column or UNIQUE
        // constraint from db/06's own db/37 mirror block turn the matching fact red (see file header).

        [Fact]
        public void PersonaAvatarColumnsAndUniqueConstraintsExist()
        {
            Assert.Equal(("integer", "NO"), Shape(db, "persona_avatar", "persona_id"));
            Assert.Equal(("bytea", "NO"), Shape(db, "persona_avatar", "bytes"));
            Assert.Equal(("integer", "NO"), Shape(db, "persona_avatar", "byte_size"));
            Assert.Equal(("text", "NO"), Shape(db, "persona_avatar", "sha256"));
            Assert.Equal(("text", "NO"), Shape(db, "persona_avatar", "token"));
            Assert.Equal(("text", "NO"), Shape(db, "persona_avatar", "source"));
            Assert.Equal(("text", "YES"), Shape(db, "persona_avatar", "imported_from"));
            Assert.Equal(("timestamp with time zone", "NO"), Shape(db, "persona_avatar", "updated_at"));

            // UNIQUE(persona_id) — one face per persona — AND UNIQUE(token) — F129.1's own globally
            // unique serving key — are two INDEPENDENT auto-named constraints, not one compound key.
            Assert.Equal(
                new[] { "persona_avatar_persona_id_key", "persona_avatar_token_key" },
                InitialUniqueConstraintNames(db, "persona_avatar").OrderBy(n => n, StringComparer.Ordinal));
        }

        [Fact]
        public void AvatarPackColumnsAndSlugUniqueConstraintExist()
        {
            Assert.Equal(("text", "NO"), Shape(db, "avatar_pack", "slug"));
            Assert.Equal(("jsonb", "NO"), Shape(db, "avatar_pack", "definition"));
            Assert.Equal(("text", "NO"), Shape(db, "avatar_pack", "imported_from"));
            Assert.Equal(("timestamp with time zone", "NO"), Shape(db, "avatar_pack", "imported_at"));

            Assert.Equal(["avatar_pack_slug_key"], InitialUniqueConstraintNames(db, "avatar_pack"));
        }

        [Fact]
        public void AvatarPackItemColumnsAndPackScopedUniqueConstraintExist()
        {
            Assert.Equal(("integer", "NO"), Shape(db, "avatar_pack_item", "pack_id"));
            Assert.Equal(("text", "NO"), Shape(db, "avatar_pack_item", "name"));
            Assert.Equal(("text", "YES"), Shape(db, "avatar_pack_item", "suggested_persona"));
            Assert.Equal(("bytea", "NO"), Shape(db, "avatar_pack_item", "bytes"));
            Assert.Equal(("integer", "NO"), Shape(db, "avatar_pack_item", "byte_size"));
            Assert.Equal(("text", "NO"), Shape(db, "avatar_pack_item", "sha256"));

            // UNIQUE(pack_id, name) — scoped PER PACK, not global (unlike persona_avatar's own
            // single-column keys above).
            Assert.Equal(
                ["avatar_pack_item_pack_id_name_key"], InitialUniqueConstraintNames(db, "avatar_pack_item"));
        }

        [Fact]
        public void IconPackColumnsAndSlugUniqueConstraintExist()
        {
            Assert.Equal(("text", "NO"), Shape(db, "icon_pack", "slug"));
            Assert.Equal(("jsonb", "NO"), Shape(db, "icon_pack", "definition"));
            Assert.Equal(("text", "NO"), Shape(db, "icon_pack", "imported_from"));
            Assert.Equal(("timestamp with time zone", "NO"), Shape(db, "icon_pack", "imported_at"));

            Assert.Equal(["icon_pack_slug_key"], InitialUniqueConstraintNames(db, "icon_pack"));
        }

        [Fact]
        public void StationImageColumnsExistAndCarryNoUniqueConstraint()
        {
            Assert.Equal(("integer", "NO"), Shape(db, "station_image", "id"));
            Assert.Equal(("bytea", "NO"), Shape(db, "station_image", "bytes"));
            Assert.Equal(("integer", "NO"), Shape(db, "station_image", "byte_size"));
            Assert.Equal(("text", "NO"), Shape(db, "station_image", "sha256"));
            Assert.Equal(("text", "NO"), Shape(db, "station_image", "token"));
            Assert.Equal(("timestamp with time zone", "NO"), Shape(db, "station_image", "updated_at"));

            // Deliberately NOT unique (see db/37's own DDL comment): there is only ever one row, so a
            // UNIQUE constraint on token would be a no-op — singleton-ness is enforced by the `id = 1`
            // CHECK + PRIMARY KEY instead, neither of which is a UNIQUE-typed constraint
            // (InitialUniqueConstraints only tracks contype = 'u').
            Assert.Empty(InitialUniqueConstraintNames(db, "station_image"));
        }

        static (string DataType, string IsNullable) Shape(DatabaseFixture db, string table, string column)
        {
            var column1 = InitialStationColumn(db, table, column);
            return (column1.DataType, column1.IsNullable);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — constraint teeth (defensive migration; NOT a fresh-init proof — see file header
    // and ScenarioFreshInitCarriesTheFiveTables above for that)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioPersonaAvatarIsAOneToOneExtension(DatabaseFixture db)
    {
        [Fact]
        public async Task PersonaIdIsUniqueAndCascadesWithItsPersona()
        {
            RunMigrationScript(db);
            var personaId = await InsertPersonaAsync(db, $"Avatar Persona {Guid.NewGuid()}");

            await using var conn = await db.StationDataSource.OpenConnectionAsync();

            // information_schema: persona_id is NOT NULL, and its FK into station.persona(id) deletes
            // CASCADE (mirrors Story109_RatingSchemaAndContract.cs's own metadata-shape fact).
            var column = await conn.QuerySingleAsync<(string IsNullable, string DataType)>(
                """
                select is_nullable, data_type from information_schema.columns
                where table_schema = 'station' and table_name = 'persona_avatar' and column_name = 'persona_id'
                """);
            Assert.Equal(("NO", "integer"), column);

            var fk = await conn.QuerySingleAsync<(string ReferencedTable, string ReferencedColumn, string DeleteRule)>(
                """
                select ccu.table_name as referenced_table, ccu.column_name as referenced_column, rc.delete_rule
                from information_schema.table_constraints tc
                join information_schema.referential_constraints rc
                  on rc.constraint_name = tc.constraint_name and rc.constraint_schema = tc.constraint_schema
                join information_schema.constraint_column_usage ccu
                  on ccu.constraint_name = tc.constraint_name and ccu.constraint_schema = tc.constraint_schema
                where tc.table_schema = 'station' and tc.table_name = 'persona_avatar' and tc.constraint_type = 'FOREIGN KEY'
                """);
            Assert.Equal(("persona", "id", "CASCADE"), fk);

            // UNIQUE: a second face for the SAME persona is rejected.
            await InsertAvatarAsync(db, personaId, token: Guid.NewGuid().ToString("N"));
            await Assert.ThrowsAsync<PostgresException>(
                () => InsertAvatarAsync(db, personaId, token: Guid.NewGuid().ToString("N")));

            // ON DELETE CASCADE, proven behaviorally: deleting the persona removes its worn face.
            await conn.ExecuteAsync("delete from station.persona where id = @personaId", new { personaId });
            var remaining = await conn.ExecuteScalarAsync<long>(
                "select count(*) from station.persona_avatar where persona_id = @personaId", new { personaId });
            Assert.Equal(0, remaining);
        }

        [Fact]
        public async Task SourceIsCheckedToUploadOrCatalog()
        {
            RunMigrationScript(db);
            var rejectedPersona = await InsertPersonaAsync(db, $"Source Reject {Guid.NewGuid()}");
            var uploadPersona = await InsertPersonaAsync(db, $"Source Upload {Guid.NewGuid()}");
            var catalogPersona = await InsertPersonaAsync(db, $"Source Catalog {Guid.NewGuid()}");

            // An unrecognised source value trips the CHECK.
            await Assert.ThrowsAsync<PostgresException>(() => InsertAvatarAsync(
                db, rejectedPersona, token: Guid.NewGuid().ToString("N"), source: "weird"));

            // Both allowlisted values pass.
            await InsertAvatarAsync(db, uploadPersona, token: Guid.NewGuid().ToString("N"), source: "upload");
            await InsertAvatarAsync(db, catalogPersona, token: Guid.NewGuid().ToString("N"), source: "catalog");

            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            var sources = await conn.QueryAsync<string>(
                "select source from station.persona_avatar where persona_id in (@uploadPersona, @catalogPersona) order by source",
                new { uploadPersona, catalogPersona });
            Assert.Equal(["catalog", "upload"], sources);
        }

        [Fact]
        public async Task TokenIsUniqueAcrossFaces()
        {
            RunMigrationScript(db);
            var firstPersona = await InsertPersonaAsync(db, $"Token First {Guid.NewGuid()}");
            var secondPersona = await InsertPersonaAsync(db, $"Token Second {Guid.NewGuid()}");
            var sharedToken = Guid.NewGuid().ToString("N");

            await InsertAvatarAsync(db, firstPersona, sharedToken);

            // The SAME token on a DIFFERENT persona's face is rejected — token is unique across every
            // row, not scoped per-persona.
            await Assert.ThrowsAsync<PostgresException>(
                () => InsertAvatarAsync(db, secondPersona, sharedToken));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioPackAndIconAndStationTablesExist(DatabaseFixture db)
    {
        [Fact]
        public async Task AvatarPackItemsAreUniquePerPackAndCascade()
        {
            RunMigrationScript(db);
            var slug = $"avatar-pack-{Guid.NewGuid():N}";

            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            var packId = await conn.ExecuteScalarAsync<int>(
                "insert into station.avatar_pack (slug, definition, imported_from) values (@slug, '{}'::jsonb, 'catalog-slug') returning id",
                new { slug });

            await conn.ExecuteAsync(
                "insert into station.avatar_pack_item (pack_id, name, bytes, byte_size, sha256) values (@packId, 'nova', @bytes, 4, 'hash-a')",
                new { packId, bytes = new byte[] { 1, 2, 3, 4 } });

            // (pack_id, name) UNIQUE: the same name reused in the SAME pack is rejected.
            await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
                "insert into station.avatar_pack_item (pack_id, name, bytes, byte_size, sha256) values (@packId, 'nova', @bytes, 4, 'hash-b')",
                new { packId, bytes = new byte[] { 5, 6, 7, 8 } }));

            // A different name in the SAME pack is fine — the constraint is scoped, not global.
            await conn.ExecuteAsync(
                "insert into station.avatar_pack_item (pack_id, name, bytes, byte_size, sha256) values (@packId, 'max', @bytes, 4, 'hash-c')",
                new { packId, bytes = new byte[] { 9, 10, 11, 12 } });

            // ON DELETE CASCADE: deleting the pack removes both of its items.
            await conn.ExecuteAsync("delete from station.avatar_pack where id = @packId", new { packId });
            var remaining = await conn.ExecuteScalarAsync<long>(
                "select count(*) from station.avatar_pack_item where pack_id = @packId", new { packId });
            Assert.Equal(0, remaining);
        }

        [Fact]
        public async Task IconPackHoldsAJsonbDefinitionKeyedBySlug()
        {
            RunMigrationScript(db);
            var slug = $"icon-pack-{Guid.NewGuid():N}";

            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            await conn.ExecuteAsync(
                "insert into station.icon_pack (slug, definition, imported_from) values (@slug, @definition::jsonb, 'catalog-slug')",
                new { slug, definition = """{"play":{"kind":"path","d":"M0 0"}}""" });

            // definition is genuinely jsonb (not text/json), and reads back keyed by slug.
            var row = await conn.QuerySingleAsync<(string DataType, string Definition)>(
                """
                select pg_typeof(definition)::text as data_type, definition::text as definition
                from station.icon_pack where slug = @slug
                """,
                new { slug });
            Assert.Equal("jsonb", row.DataType);
            Assert.Contains("\"play\"", row.Definition);

            // slug UNIQUE: a second pack on the same slug is rejected.
            await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
                "insert into station.icon_pack (slug, definition, imported_from) values (@slug, '{}'::jsonb, 'catalog-slug')",
                new { slug }));
        }

        [Fact]
        public async Task StationImageIsStructurallySingleRow()
        {
            RunMigrationScript(db);

            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            // Clean slate — this table is a genuine process-wide singleton, unlike every other table
            // in this file, so a leftover row from an earlier run of this same fact would otherwise
            // collide with the "insert lands at id = 1" assertion below.
            await conn.ExecuteAsync("delete from station.station_image");

            var insertedId = await conn.ExecuteScalarAsync<int>(
                "insert into station.station_image (bytes, byte_size, sha256, token) values (@bytes, 4, 'hash', @token) returning id",
                new { bytes = new byte[] { 1, 2, 3, 4 }, token = Guid.NewGuid().ToString("N") });
            Assert.Equal(1, insertedId);

            // CHECK (id = 1): any OTHER explicit id is rejected outright.
            await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
                "insert into station.station_image (id, bytes, byte_size, sha256, token) values (2, @bytes, 4, 'hash', @token)",
                new { bytes = new byte[] { 5, 6, 7, 8 }, token = Guid.NewGuid().ToString("N") }));

            // PRIMARY KEY: a second row AT id = 1 is rejected too — together with the CHECK above,
            // this makes a second row structurally impossible, not merely discouraged.
            await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
                "insert into station.station_image (id, bytes, byte_size, sha256, token) values (1, @bytes, 4, 'hash', @token)",
                new { bytes = new byte[] { 9, 10, 11, 12 }, token = Guid.NewGuid().ToString("N") }));

            var rowCount = await conn.ExecuteScalarAsync<long>("select count(*) from station.station_image");
            Assert.Equal(1, rowCount);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — repository round-trips (real Postgres; PLAN T290 review — see file header)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioPersonaAvatarRepository(DatabaseFixture db)
    {
        [Fact]
        public async Task UpsertAsyncThenGetByPersonaIdAsyncRoundTripsEveryFieldIncludingBothSourceValues()
        {
            // Given a persona with no worn face yet
            RunMigrationScript(db);
            var personaId = await InsertPersonaAsync(db, $"Repo Round Trip {Guid.NewGuid()}");
            var repo = PersonaAvatarRepo(db);
            Assert.Null(await repo.GetByPersonaIdAsync(personaId, CancellationToken.None));

            // When a face is upserted as an upload (no imported_from — an upload has none)
            var uploadBytes = new byte[] { 1, 2, 3, 4 };
            var uploadToken = Guid.NewGuid().ToString("N");
            await repo.UpsertAsync(
                new PersonaAvatarInput(personaId, uploadBytes, "hash-upload", uploadToken, PersonaAvatarSource.Upload, null),
                CancellationToken.None);

            // Then both GetByPersonaIdAsync and GetByTokenAsync read it back with every column mapped
            // correctly — persona_id (the FK, widened back to `long`), byte_size (derived from the
            // payload, never separately trusted), imported_from (null for an upload) — and source
            // parses back to Upload via the repository's own explicit exhaustive switch, not merely
            // Dapper's own implicit string-to-enum conversion.
            var byPersonaId = await repo.GetByPersonaIdAsync(personaId, CancellationToken.None);
            var byToken = await repo.GetByTokenAsync(uploadToken, CancellationToken.None);
            Assert.NotNull(byPersonaId);
            Assert.NotNull(byToken);
            var expectedUpload = (
                PersonaId: (long)personaId, BytesMatch: true, ByteSize: uploadBytes.Length, Sha256: "hash-upload",
                Token: uploadToken, Source: PersonaAvatarSource.Upload, ImportedFrom: (string?)null);
            Assert.Equal(
                expectedUpload,
                (byPersonaId.PersonaId, byPersonaId.Bytes.SequenceEqual(uploadBytes), byPersonaId.ByteSize,
                 byPersonaId.Sha256, byPersonaId.Token, byPersonaId.Source, byPersonaId.ImportedFrom));
            Assert.Equal(
                expectedUpload,
                (byToken.PersonaId, byToken.Bytes.SequenceEqual(uploadBytes), byToken.ByteSize,
                 byToken.Sha256, byToken.Token, byToken.Source, byToken.ImportedFrom));

            // When the SAME persona's face is replaced by a catalog-sourced one — the OTHER enum value,
            // and a non-null imported_from this time
            var catalogBytes = new byte[] { 5, 6, 7, 8, 9 };
            var catalogToken = Guid.NewGuid().ToString("N");
            await repo.UpsertAsync(
                new PersonaAvatarInput(
                    personaId, catalogBytes, "hash-catalog", catalogToken, PersonaAvatarSource.Catalog, "some-pack-slug"),
                CancellationToken.None);

            // Then the single row is replaced whole — the old upload token no longer resolves — and
            // the persona_id-keyed read reflects the catalog values, proving source flips BOTH
            // directions (write: Catalog serializes to 'catalog'; read: 'catalog' parses back to
            // Catalog) through the exact same store, not just the upload direction proven above.
            Assert.Null(await repo.GetByTokenAsync(uploadToken, CancellationToken.None));
            var replaced = await repo.GetByPersonaIdAsync(personaId, CancellationToken.None);
            Assert.NotNull(replaced);
            Assert.Equal(
                (PersonaId: (long)personaId, BytesMatch: true, ByteSize: catalogBytes.Length, Sha256: "hash-catalog",
                 Token: catalogToken, Source: PersonaAvatarSource.Catalog, ImportedFrom: "some-pack-slug"),
                (replaced.PersonaId, BytesMatch: replaced.Bytes.SequenceEqual(catalogBytes), replaced.ByteSize,
                 replaced.Sha256, replaced.Token, replaced.Source, replaced.ImportedFrom));
        }

        [Fact]
        public async Task GetTokenByPersonaIdAsyncRoundTripsTheTokenAloneWithoutTheBytesColumn()
        {
            // Given two personas — one with no worn face, one whose face is about to be upserted —
            // proving the projection is scoped per-persona, not a single global answer (PLAN T299
            // fix round: the bounded-cost projection SpectatorController.ResolveDjAvatarUrlAsync
            // reads instead of GetByPersonaIdAsync's whole-row select).
            RunMigrationScript(db);
            var facelessPersonaId = await InsertPersonaAsync(db, $"Token Repo Faceless {Guid.NewGuid()}");
            var personaId = await InsertPersonaAsync(db, $"Token Repo Round Trip {Guid.NewGuid()}");
            var repo = PersonaAvatarRepo(db);

            // Then the faceless persona reports null — an honest "no face", never an error.
            Assert.Null(await repo.GetTokenByPersonaIdAsync(facelessPersonaId, CancellationToken.None));

            // When a face is upserted for the other persona
            var token = Guid.NewGuid().ToString("N");
            await repo.UpsertAsync(
                new PersonaAvatarInput(personaId, [1, 2, 3, 4], "hash", token, PersonaAvatarSource.Upload, null),
                CancellationToken.None);

            // Then GetTokenByPersonaIdAsync reads back the SAME token GetByPersonaIdAsync's own
            // whole-row read carries — the projection agrees with the wider read, it just never
            // selects bytes/byte_size/sha256/source/imported_from/updated_at to get there — while the
            // still-faceless persona keeps reporting null.
            var wholeRow = await repo.GetByPersonaIdAsync(personaId, CancellationToken.None);
            Assert.NotNull(wholeRow);
            Assert.Equal(wholeRow.Token, await repo.GetTokenByPersonaIdAsync(personaId, CancellationToken.None));
            Assert.Null(await repo.GetTokenByPersonaIdAsync(facelessPersonaId, CancellationToken.None));
        }

        [Fact]
        public async Task DeleteAsyncRemovesTheFaceAndReportsWhetherOneExisted()
        {
            // Given a persona with a worn face
            RunMigrationScript(db);
            var personaId = await InsertPersonaAsync(db, $"Repo Delete {Guid.NewGuid()}");
            var repo = PersonaAvatarRepo(db);
            await repo.UpsertAsync(
                new PersonaAvatarInput(
                    personaId, [1, 2, 3, 4], "hash", Guid.NewGuid().ToString("N"), PersonaAvatarSource.Upload, null),
                CancellationToken.None);

            // When it is deleted
            var deleted = await repo.DeleteAsync(personaId, CancellationToken.None);

            // Then it reports true and the row is gone
            Assert.True(deleted);
            Assert.Null(await repo.GetByPersonaIdAsync(personaId, CancellationToken.None));

            // When deleted again (no row left to delete)
            var deletedAgain = await repo.DeleteAsync(personaId, CancellationToken.None);

            // Then it reports false rather than throwing
            Assert.False(deletedAgain);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAvatarPackRepository(DatabaseFixture db)
    {
        [Fact]
        public async Task UpsertAsyncWritesThePackAndItsItemsInOneTransactionThenGetBySlugAsyncRoundTripsThem()
        {
            // Given a fresh slug with two items
            RunMigrationScript(db);
            var slug = $"avatar-pack-repo-{Guid.NewGuid():N}";
            var repo = AvatarPackRepo(db);
            var novaBytes = new byte[] { 1, 2, 3, 4 };
            var maxBytes = new byte[] { 5, 6, 7, 8, 9 };

            // When the pack installs
            await repo.UpsertAsync(
                slug, """{"kind":"avatar"}""", "catalog-slug",
                [
                    new AvatarPackItemInput("nova", novaBytes, "hash-nova"),
                    new AvatarPackItemInput("max", maxBytes, "hash-max", "dj-max"),
                ],
                CancellationToken.None);

            // Then GetBySlugAsync reads the pack row AND both item rows back, bytes included, in the
            // SAME read — proving UpsertAsync's own two-table transaction actually landed both tables,
            // not just the pack row (a bare "the SQL runs" schema fact could never distinguish a
            // half-committed write from a whole one).
            var pack = await repo.GetBySlugAsync(slug, CancellationToken.None);
            Assert.NotNull(pack);
            Assert.Equal(
                (Slug: slug, ImportedFrom: "catalog-slug", ItemCount: 2),
                (pack.Slug, pack.ImportedFrom, ItemCount: pack.Items.Count));
            Assert.Contains("\"kind\"", pack.Definition);

            var nova = Assert.Single(pack.Items, i => i.Name == "nova");
            Assert.Equal(
                (BytesMatch: true, Sha256: "hash-nova", SuggestedPersona: (string?)null),
                (BytesMatch: nova.Bytes.SequenceEqual(novaBytes), nova.Sha256, nova.SuggestedPersona));
            var max = Assert.Single(pack.Items, i => i.Name == "max");
            Assert.Equal(
                (BytesMatch: true, Sha256: "hash-max", SuggestedPersona: "dj-max"),
                (BytesMatch: max.Bytes.SequenceEqual(maxBytes), max.Sha256, max.SuggestedPersona));
        }

        [Fact]
        public async Task ReinstallingReplacesTheItemListWholeAndGetAllAsyncCarriesItemMetadataWithoutBytes()
        {
            // Given an installed pack with one item
            RunMigrationScript(db);
            var slug = $"avatar-pack-repo-{Guid.NewGuid():N}";
            var repo = AvatarPackRepo(db);
            await repo.UpsertAsync(
                slug, "{}", "catalog-slug-v1", [new AvatarPackItemInput("nova", [1, 2, 3, 4], "hash-nova")],
                CancellationToken.None);

            // When the SAME slug re-installs with a DIFFERENT item list (nova dropped, zed added) and
            // refreshed pack-level fields
            await repo.UpsertAsync(
                slug, """{"kind":"v2"}""", "catalog-slug-v2",
                [new AvatarPackItemInput("zed", [9, 9, 9], "hash-zed")], CancellationToken.None);

            // Then the re-install's item list becomes the pack's entire item set — nova is gone, zed is
            // the only item present — and the pack row's own imported_from reflects the second install,
            // never a merge with the first.
            var pack = await repo.GetBySlugAsync(slug, CancellationToken.None);
            Assert.NotNull(pack);
            Assert.Equal(
                (ImportedFrom: "catalog-slug-v2", ItemNamesMatch: true),
                (pack.ImportedFrom, ItemNamesMatch: pack.Items.Select(i => i.Name).SequenceEqual(["zed"])));
            Assert.Contains("\"v2\"", pack.Definition);

            // And GetAllAsync's own listing read carries the re-installed item's own name/suggested-
            // persona metadata (review finding B1: the listing widened to include this directly, rather
            // than requiring a second per-pack GetBySlugAsync round trip just to read it), but the
            // returned AvatarPackItemSummary shape is structurally incapable of carrying bytes at all —
            // there is no Bytes member to even assert absent.
            var all = await repo.GetAllAsync(CancellationToken.None);
            var listed = Assert.Single(all, p => p.Slug == slug);
            var item = Assert.Single(listed.Items);
            Assert.Equal(("zed", (string?)null), (item.Name, item.SuggestedPersona));
        }

        [Fact]
        public async Task DeleteAsyncRemovesThePackAndReportsWhetherOneExisted()
        {
            // Given an installed pack
            RunMigrationScript(db);
            var slug = $"avatar-pack-repo-{Guid.NewGuid():N}";
            var repo = AvatarPackRepo(db);
            await repo.UpsertAsync(slug, "{}", "catalog-slug", [], CancellationToken.None);

            // When it is deleted
            var deleted = await repo.DeleteAsync(slug, CancellationToken.None);

            // Then it reports true and the pack is gone
            Assert.True(deleted);
            Assert.Null(await repo.GetBySlugAsync(slug, CancellationToken.None));

            // When deleted again
            var deletedAgain = await repo.DeleteAsync(slug, CancellationToken.None);

            // Then it reports false rather than throwing
            Assert.False(deletedAgain);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioIconPackRepository(DatabaseFixture db)
    {
        [Fact]
        public async Task UpsertAsyncThenGetBySlugAsyncRoundTripsTheDefinitionAsJsonbText()
        {
            // Given a fresh slug with a real definition document
            RunMigrationScript(db);
            var slug = $"icon-pack-repo-{Guid.NewGuid():N}";
            var repo = IconPackRepo(db);
            const string definition = """{"schemaVersion":1,"style":{"strokeWidth":1.5,"fill":"none"},"icons":{"play":[{"tag":"path","d":"M0 0"}]}}""";

            // When the pack installs
            await repo.UpsertAsync(slug, definition, "catalog-slug", CancellationToken.None);

            // Then GetBySlugAsync reads the row back whole — the jsonb column round-trips as TEXT
            // (station.icon_pack.definition::text, IconPackRepository's own SelectColumns) carrying
            // the same DOCUMENT written, field for field (jsonb itself reformats whitespace on the
            // way back out — e.g. a space after every ':' — so this compares parsed VALUES, not raw
            // substrings).
            var pack = await repo.GetBySlugAsync(slug, CancellationToken.None);
            Assert.NotNull(pack);
            Assert.Equal(
                (Slug: slug, ImportedFrom: "catalog-slug"),
                (pack.Slug, pack.ImportedFrom));
            using var doc = JsonDocument.Parse(pack.Definition);
            Assert.True(doc.RootElement.GetProperty("icons").TryGetProperty("play", out _));
            Assert.Equal(1.5, doc.RootElement.GetProperty("style").GetProperty("strokeWidth").GetDouble());
        }

        [Fact]
        public async Task ReinstallingReplacesTheDefinitionAndGetAllAsyncListsEveryPack()
        {
            // Given an installed pack
            RunMigrationScript(db);
            var slug = $"icon-pack-repo-{Guid.NewGuid():N}";
            var repo = IconPackRepo(db);
            await repo.UpsertAsync(slug, """{"icons":{"a":[]}}""", "catalog-slug-v1", CancellationToken.None);

            // When the SAME slug re-installs with a DIFFERENT definition and refreshed provenance
            await repo.UpsertAsync(slug, """{"icons":{"b":[]}}""", "catalog-slug-v2", CancellationToken.None);

            // Then the single row is replaced whole — never a second row, never a merge of the two
            // definitions — and GetAllAsync's own listing read carries this pack among every installed
            // one.
            var pack = await repo.GetBySlugAsync(slug, CancellationToken.None);
            Assert.NotNull(pack);
            Assert.Equal("catalog-slug-v2", pack.ImportedFrom);
            Assert.Contains("\"b\"", pack.Definition);
            Assert.DoesNotContain("\"a\"", pack.Definition);

            var all = await repo.GetAllAsync(CancellationToken.None);
            var listed = Assert.Single(all, p => p.Slug == slug);
            Assert.Equal("catalog-slug-v2", listed.ImportedFrom);
        }

        [Fact]
        public async Task DeleteAsyncRemovesThePackAndReportsWhetherOneExisted()
        {
            // Given an installed pack
            RunMigrationScript(db);
            var slug = $"icon-pack-repo-{Guid.NewGuid():N}";
            var repo = IconPackRepo(db);
            await repo.UpsertAsync(slug, "{}", "catalog-slug", CancellationToken.None);

            // When it is deleted
            var deleted = await repo.DeleteAsync(slug, CancellationToken.None);

            // Then it reports true and the pack is gone
            Assert.True(deleted);
            Assert.Null(await repo.GetBySlugAsync(slug, CancellationToken.None));

            // When deleted again
            var deletedAgain = await repo.DeleteAsync(slug, CancellationToken.None);

            // Then it reports false rather than throwing
            Assert.False(deletedAgain);
        }

        [Fact]
        public async Task GetAllSlugsAsyncSurfacesEveryInstalledPacksSlug()
        {
            // Proves the RESULT SET, not the SQL shape underneath it — the lighter-weight
            // `select slug from station.icon_pack` projection (PLAN T303 review finding F2, see
            // IconPackRepository.GetAllSlugsAsync's own remarks) needs nothing past the slug for
            // Station:IconPack's live choices, but this fact has no way to observe query-column
            // width from the returned `IReadOnlyList<string>` alone — a `Select(p => p.Slug)` over
            // GetAllAsync's own full-row read would pass it identically. The name says only what it
            // proves: the same installed-pack set GetAllAsync would list, reachable through this
            // narrower read too.
            RunMigrationScript(db);
            var repo = IconPackRepo(db);
            var slugA = $"icon-pack-repo-{Guid.NewGuid():N}";
            var slugB = $"icon-pack-repo-{Guid.NewGuid():N}";
            await repo.UpsertAsync(slugA, "{}", "catalog-slug-a", CancellationToken.None);
            await repo.UpsertAsync(slugB, "{}", "catalog-slug-b", CancellationToken.None);

            // When the settings hot path's own projection runs
            var slugs = await repo.GetAllSlugsAsync(CancellationToken.None);

            // Then both installed packs' slugs are present — the lighter SELECT still surfaces every
            // installed pack, the same set GetAllAsync's own full-row read would.
            Assert.Contains(slugA, slugs);
            Assert.Contains(slugB, slugs);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioStationImageRepository(DatabaseFixture db)
    {
        [Fact]
        public async Task UpsertAsyncInsertsThenReplacesTheSingleRowOnConflict()
        {
            // Given a clean slate — station_image is a genuine process-wide singleton (see
            // ScenarioPackAndIconAndStationTablesExist.StationImageIsStructurallySingleRow's own
            // remarks); a leftover row from an earlier fact would otherwise make the "no row yet" half
            // of this fact untestable.
            RunMigrationScript(db);
            await using (var conn = await db.StationDataSource.OpenConnectionAsync())
                await conn.ExecuteAsync("delete from station.station_image");
            var repo = StationImageRepo(db);
            Assert.Null(await repo.GetAsync(CancellationToken.None));

            // When the first write lands — no row yet, the INSERT branch of the ON CONFLICT
            var firstBytes = new byte[] { 1, 2, 3, 4 };
            var firstToken = Guid.NewGuid().ToString("N");
            await repo.UpsertAsync(new StationImageInput(firstBytes, "hash-first", firstToken), CancellationToken.None);

            // Then it round-trips whole, byte_size derived from the payload's own length
            var afterFirst = await repo.GetAsync(CancellationToken.None);
            Assert.NotNull(afterFirst);
            Assert.Equal(
                (BytesMatch: true, ByteSize: firstBytes.Length, Sha256: "hash-first", Token: firstToken),
                (BytesMatch: afterFirst.Bytes.SequenceEqual(firstBytes), afterFirst.ByteSize, afterFirst.Sha256, afterFirst.Token));

            // When a second write lands against the SAME (only ever) row — the ON CONFLICT(id) branch
            var secondBytes = new byte[] { 9, 9, 9 };
            var secondToken = Guid.NewGuid().ToString("N");
            await repo.UpsertAsync(new StationImageInput(secondBytes, "hash-second", secondToken), CancellationToken.None);

            // Then the row is replaced whole, never a second row inserted alongside it
            var afterSecond = await repo.GetAsync(CancellationToken.None);
            Assert.NotNull(afterSecond);
            Assert.Equal(
                (BytesMatch: true, ByteSize: secondBytes.Length, Sha256: "hash-second", Token: secondToken),
                (BytesMatch: afterSecond.Bytes.SequenceEqual(secondBytes), afterSecond.ByteSize, afterSecond.Sha256, afterSecond.Token));
            await using var checkConn = await db.StationDataSource.OpenConnectionAsync();
            var rowCount = await checkConn.ExecuteScalarAsync<long>("select count(*) from station.station_image");
            Assert.Equal(1, rowCount);
        }

        [Fact]
        public async Task GetTokenAsyncSurfacesTheTokenAloneWithoutTheBytesColumn()
        {
            // Given a clean slate — no row yet (PLAN T307 fix round: the token-only projection
            // AuthController.Stations reads instead of the whole-row GetAsync).
            RunMigrationScript(db);
            await using (var conn = await db.StationDataSource.OpenConnectionAsync())
                await conn.ExecuteAsync("delete from station.station_image");
            var repo = StationImageRepo(db);

            // Then it reports null — an honest "no customization", never an error.
            Assert.Null(await repo.GetTokenAsync(CancellationToken.None));

            // When an image is upserted,
            var bytes = new byte[] { 1, 2, 3, 4 };
            var token = Guid.NewGuid().ToString("N");
            await repo.UpsertAsync(new StationImageInput(bytes, "hash", token), CancellationToken.None);

            // Then GetTokenAsync reads back the SAME token GetAsync's own whole-row read carries — the
            // projection agrees with the wider read, it just never selects bytes/byte_size/sha256/
            // updated_at to get there.
            var wholeRow = await repo.GetAsync(CancellationToken.None);
            Assert.NotNull(wholeRow);
            Assert.Equal(wholeRow.Token, await repo.GetTokenAsync(CancellationToken.None));
        }

        [Fact]
        public async Task DeleteAsyncRemovesTheRowAndReportsWhetherOneExisted()
        {
            // Given a stored image
            RunMigrationScript(db);
            await using (var conn = await db.StationDataSource.OpenConnectionAsync())
                await conn.ExecuteAsync("delete from station.station_image");
            var repo = StationImageRepo(db);
            await repo.UpsertAsync(new StationImageInput([1, 2, 3, 4], "hash", Guid.NewGuid().ToString("N")), CancellationToken.None);

            // When it is deleted
            var deleted = await repo.DeleteAsync(CancellationToken.None);

            // Then it reports true and the row is gone
            Assert.True(deleted);
            Assert.Null(await repo.GetAsync(CancellationToken.None));

            // When deleted again
            var deletedAgain = await repo.DeleteAsync(CancellationToken.None);

            // Then it reports false rather than throwing
            Assert.False(deletedAgain);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — migration discipline
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRerunningTheMigrationIsIdempotent(DatabaseFixture db)
    {
        [Fact]
        public async Task SecondRunExitsSuccessfullyWithoutErrors()
        {
            // Run db/37-visual-layer-migration.sh twice; the second run must exit clean (CREATE TABLE
            // IF NOT EXISTS guards) and leave all four tables standing — mirrors
            // Story109_RatingSchemaAndContract.cs's own idempotency fact.
            RunMigrationScript(db);
            RunMigrationScript(db);

            Assert.True(await TableExistsAsync(db, "persona_avatar"));
            Assert.True(await TableExistsAsync(db, "avatar_pack"));
            Assert.True(await TableExistsAsync(db, "avatar_pack_item"));
            Assert.True(await TableExistsAsync(db, "icon_pack"));
            Assert.True(await TableExistsAsync(db, "station_image"));
        }
    }
}

// STORY-192 — Persona schema and default migration
//
// BDD specification — xUnit (SPEC F71.1–F71.2). Postgres-backed specs follow this project's
// db-compose convention (Category=Integration, shared DatabaseFixture); the card round-trip needs
// no database, so it runs in the ordinary suite.

using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeaturePersonaSchemaAndMigration
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>Runs db/11-persona-card-migration.sh against the test database via the fixture.</summary>
    static void RunCardMigrationScript(DatabaseFixture db) =>
        db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "11-persona-card-migration.sh"));

    static PersonaRepository Repo(DatabaseFixture db) => new(db.StationDataSource);

    static PersonaCardMigrator Migrator(DatabaseFixture db, Persona? active) =>
        new(db.StationDataSource, new FakeActivePersonaAccessor(active), NullLogger<PersonaCardMigrator>.Instance);

    /// <summary>A fixed <see cref="IActivePersonaAccessor"/> answer — no live options/store needed.</summary>
    sealed class FakeActivePersonaAccessor(Persona? persona) : IActivePersonaAccessor
    {
        public Task<Persona?> ResolveAsync(CancellationToken ct) => Task.FromResult(persona);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — schema shape (F71.1, AC1)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioSchema(DatabaseFixture db)
    {
        [Fact]
        public async Task Persona_tables_match_the_specced_shape_including_recall_index()
        {
            // Given the database after migration (simulate a pre-STORY-192 database first, so this
            // proves db/11's own DDL — not just db/06's fresh-init shape).
            await using (var conn = await db.StationDataSource.OpenConnectionAsync())
            {
                await conn.ExecuteAsync("drop table if exists station.persona_memory");
                await conn.ExecuteAsync("alter table station.persona drop column if exists slug");
                await conn.ExecuteAsync("alter table station.persona drop column if exists definition");
                await conn.ExecuteAsync("alter table station.persona drop column if exists enabled");
            }

            RunCardMigrationScript(db);

            // When the persona and persona_memory tables are inspected...
            await using var conn2 = await db.StationDataSource.OpenConnectionAsync();

            // Then persona has slug UNIQUE NOT NULL, definition jsonb NOT NULL, enabled boolean NOT NULL.
            Assert.Equal("text", await ColumnTypeAsync(conn2, "persona", "slug"));
            Assert.True(await HasUniqueConstraintOnAsync(conn2, "persona", "slug"));
            Assert.Equal("jsonb", await ColumnTypeAsync(conn2, "persona", "definition"));
            Assert.Equal("boolean", await ColumnTypeAsync(conn2, "persona", "enabled"));

            // And persona_memory has a CASCADE FK into persona...
            var deleteRule = await conn2.QuerySingleOrDefaultAsync<string>(
                """
                select rc.delete_rule
                from information_schema.referential_constraints rc
                join information_schema.table_constraints tc
                  on tc.constraint_name = rc.constraint_name and tc.constraint_schema = rc.constraint_schema
                where tc.table_schema = 'station' and tc.table_name = 'persona_memory'
                  and tc.constraint_type = 'FOREIGN KEY'
                """);
            Assert.Equal("CASCADE", deleteRule);

            // ...plus the (persona_id, kind, last_aired_at DESC NULLS FIRST) recall index. Postgres's
            // canonical index definition omits "NULLS FIRST" for a DESC column because that ordering
            // IS the default for DESC (it only ever prints a NULLS clause that overrides the
            // default) — so a plain "... last_aired_at DESC)" with no "NULLS LAST" override already
            // proves the nulls-first semantics F71.1 specifies.
            var indexDef = await conn2.QuerySingleOrDefaultAsync<string>(
                "select indexdef from pg_indexes where schemaname = 'station' and indexname = 'persona_memory_recall'");
            Assert.NotNull(indexDef);
            Assert.Contains("persona_id", indexDef);
            Assert.Contains("kind", indexDef);
            Assert.Contains("last_aired_at DESC", indexDef);
            Assert.DoesNotContain("NULLS LAST", indexDef);
        }

        static Task<string?> ColumnTypeAsync(NpgsqlConnection conn, string table, string column) =>
            conn.QuerySingleOrDefaultAsync<string?>(
                """
                select data_type from information_schema.columns
                where table_schema = 'station' and table_name = @table and column_name = @column
                """,
                new { table, column });

        static async Task<bool> HasUniqueConstraintOnAsync(NpgsqlConnection conn, string table, string column)
        {
            var count = await conn.ExecuteScalarAsync<long>(
                """
                select count(*)
                from information_schema.table_constraints tc
                join information_schema.key_column_usage kcu
                  on kcu.constraint_name = tc.constraint_name and kcu.table_schema = tc.table_schema
                where tc.table_schema = 'station' and tc.table_name = @table
                  and tc.constraint_type = 'UNIQUE' and kcu.column_name = @column
                """,
                new { table, column });
            return count > 0;
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — card round-trip (F71.1, AC2) — no database needed.
    // ---------------------------------------------------------------------

    public static class ScenarioCardRoundTrip
    {
        [Fact]
        public static void Fully_populated_card_serializes_byte_stable()
        {
            // Given a fully-populated persona card...
            const string canonicalJson =
                "{\"schemaVersion\":1,\"name\":\"Nova Q\",\"tagline\":\"Late-night frequencies.\"," +
                "\"soul\":\"Grew up chasing pirate radio signals across the north of England.\"," +
                "\"quirks\":[\"hums between sentences\",\"never says the actual time\"]," +
                "\"voice\":{\"engine\":\"kokoro\",\"voiceId\":\"af_heart\",\"pace\":1.05,\"language\":\"en\"}," +
                "\"energyDisposition\":0.4," +
                "\"lore\":[\"Started broadcasting from a garden shed in 2003.\"]," +
                "\"corrections\":[{\"from\":\"MacLeod\",\"to\":\"muh-CLOUD\"}]}";

            // When it is serialized and deserialized...
            var card = PersonaCardSerializer.Deserialize(canonicalJson);
            Assert.NotNull(card);
            var roundTripped = PersonaCardSerializer.Serialize(card);

            // Then the result is byte-stable.
            Assert.Equal(canonicalJson, roundTripped);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — default persona migration (F71.2, AC3/AC4)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioDefaultPersona(DatabaseFixture db)
    {
        async Task<Persona> SeedActivePersonaAsync()
        {
            RunCardMigrationScript(db);
            await db.ResetStationAsync();

            var created = Assert.IsType<PersonaWriteResult.Created>(
                await Repo(db).CreateAsync(
                    new PersonaDraft("Nova Q", "grew up chasing pirate radio", "wry and warm", "af_heart"),
                    CancellationToken.None));
            return created.Persona;
        }

        async Task<PersonaCard> ReadDefaultCardAsync()
        {
            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            var json = await conn.ExecuteScalarAsync<string?>(
                "select definition::text from station.persona where slug = @slug",
                new { slug = PersonaCardMigrator.DefaultSlug });

            Assert.NotNull(json);
            var card = PersonaCardSerializer.Deserialize(json);
            Assert.NotNull(card);
            return card;
        }

        [Fact]
        public async Task Existing_dj_config_migrates_to_default_persona_row()
        {
            // Given a station with existing DJ personality config...
            var active = await SeedActivePersonaAsync();

            // When the host starts post-migration...
            await Migrator(db, active).RunAsync(CancellationToken.None);

            // Then a persona row slug "default" exists with the prompt fragment as soul, and no
            // quirks/lore (F71.2).
            var card = await ReadDefaultCardAsync();
            Assert.Contains("grew up chasing pirate radio", card.Soul);
            Assert.Empty(card.Quirks);
            Assert.Empty(card.Lore);

            // Idempotent: a second boot finds the row already present and changes nothing.
            await Migrator(db, active).RunAsync(CancellationToken.None);
            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            var defaultCount = await conn.ExecuteScalarAsync<long>(
                "select count(*) from station.persona where slug = @slug",
                new { slug = PersonaCardMigrator.DefaultSlug });
            Assert.Equal(1, defaultCount);
        }

        [Fact]
        public async Task Migration_causes_zero_prompt_change_without_quirks_or_lore()
        {
            // Given the migrated default persona (no quirks or lore)...
            var active = await SeedActivePersonaAsync();
            await Migrator(db, active).RunAsync(CancellationToken.None);
            var card = await ReadDefaultCardAsync();

            // When copy is generated, the card's soul feeds the exact same persona-section text
            // GenWave.Tts.LlmCopyWriter.BuildPersonaSection composed pre-migration (labeled
            // Backstory/Style lines, both present here) — so prompts are equivalent (F71.2).
            const string preMigrationPersonaSection =
                "Backstory: grew up chasing pirate radio\nStyle: wry and warm";

            Assert.Equal(preMigrationPersonaSection, card.Soul);
        }
    }
}

// STORY-192 — Persona schema and default migration
//
// BDD specification — xUnit (SPEC F71.1–F71.2). Pending scaffold; /build-loop (PLAN T36)
// implements and removes Skip. Postgres-backed specs follow this project's db-compose
// convention.

using Xunit;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeaturePersonaSchemaAndMigration
{
    private const string Pending = "pending — PLAN T36 (/build-loop)";

    public static class ScenarioSchema
    {
        [Fact(Skip = Pending)]
        public static void Persona_tables_match_the_specced_shape_including_recall_index()
        {
            // Given the database after migration
            // When  the persona and persona_memory tables are inspected
            // Then  they match the spec'd shape including the
            //       (persona_id, kind, last_aired_at DESC NULLS FIRST) index (F71.1)
            Assert.Fail(Pending);
        }
    }

    public static class ScenarioCardRoundTrip
    {
        [Fact(Skip = Pending)]
        public static void Fully_populated_card_serializes_byte_stable()
        {
            // Given a fully-populated persona card
            // When  it is serialized and deserialized
            // Then  the result is byte-stable (F71.1)
            Assert.Fail(Pending);
        }
    }

    public static class ScenarioDefaultPersona
    {
        [Fact(Skip = Pending)]
        public static void Existing_dj_config_migrates_to_default_persona_row()
        {
            // Given a station with existing DJ personality config
            // When  the host starts post-migration
            // Then  a persona row slug "default" exists with the prompt fragment as
            //       soul (F71.2)
            Assert.Fail(Pending);
        }

        [Fact(Skip = Pending)]
        public static void Migration_causes_zero_prompt_change_without_quirks_or_lore()
        {
            // Given the migrated default persona with no quirks or lore
            // When  copy is generated
            // Then  prompts are equivalent to pre-migration prompts (F71.2)
            Assert.Fail(Pending);
        }
    }
}

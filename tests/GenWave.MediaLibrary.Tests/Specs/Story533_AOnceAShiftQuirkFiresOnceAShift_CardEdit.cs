// STORY-533 AC2 — an edit keeps the rules (SPEC F218.5 · PLAN T683)
//
// PATCH /api/personas/{id} -> IPersonaStore.UpdateAsync -> PersonaRepository.MergeCard is the only
// place the stored card is rewritten: the pure merge is one fact, the real store round trip
// (import -> UpdateAsync -> GetCardByIdAsync, Postgres) the other; see
// tests/GenWave.Core.Tests/Specs/Story533_AOnceAShiftQuirkFiresOnceAShift_Card.cs for the map.

using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureAOnceAShiftQuirkFiresOnceAShiftCardEdit
{
    public sealed class ScenarioAnEditKeepsTheRules
    {
        // Given a persona with QuirkRules
        // When  PATCH renames the persona

        [Fact]
        public void QuirkRulesAreUnchanged()
        {
            IReadOnlyList<QuirkRule> rules = [new QuirkRule("signs off with a pun", MaxPerShift: 1, PerBreakChance: 0.15)];
            var existing = new PersonaCard(
                PersonaCard.CurrentSchemaVersion, "DJ Nova", "Late-night frequencies.", "Chases pirate signals.",
                ["hums between sentences", "signs off with a pun"],
                new VoiceSpec("kokoro", "af_heart", 1.05, "en"), 0.4, [], []) { QuirkRules = rules };

            var merged = PersonaRepository.MergeCard(
                existing, new PersonaDraft("DJ Supernova", Backstory: "", Style: "", Voice: "af_heart"));

            Assert.Equal(rules, merged.QuirkRules);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheStoreKeepsTheRulesThroughAnEdit(DatabaseFixture db)
    {
        // Given an imported card with QuirkRules, stored
        // When  UpdateAsync renames the persona

        [Fact]
        public async Task QuirkRulesReadBackUnchanged()
        {
            await db.ResetStationAsync();
            db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "11-persona-card-migration.sh"));
            db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "25-persona-provenance-migration.sh"));
            var source = new Lazy<NpgsqlDataSource>(() => db.StationDataSource);
            IReadOnlyList<QuirkRule> rules = [new QuirkRule("signs off with a pun", MaxPerShift: 1, PerBreakChance: 0.15)];
            var card = new PersonaCard(
                PersonaCard.CurrentSchemaVersion, "DJ Nova", "Late-night frequencies.", "Chases pirate signals.",
                ["hums between sentences", "signs off with a pun"],
                new VoiceSpec("kokoro", "af_heart", 1.05, "en"), 0.4, [], []) { QuirkRules = rules };
            var outcome = await new PersonaImportRepository(source).ImportAsync(
                new PersonaImportRequest("dj-nova", LegacyVoice: "af_heart", card, "dj-nova"), CancellationToken.None);
            var personaId = Assert.IsType<PersonaImportOutcome.Imported>(outcome).PersonaId;
            var repo = new PersonaRepository(source);

            await repo.UpdateAsync(
                personaId, new PersonaDraft("DJ Supernova", Backstory: "", Style: "", Voice: "af_heart"), CancellationToken.None);
            var stored = await repo.GetCardByIdAsync(personaId, CancellationToken.None);

            Assert.Equal(rules, stored?.QuirkRules);
        }
    }
}

// STORY-533 — A once-a-shift quirk fires once a shift (gh-#293 gh-#920 · SPEC F218 · PLAN T683 T686 T687)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-08); the named task
// activates its facts, watches them fail, then makes them pass.
//
// Spans six files by seam: AC1 (T683, card shape) here; AC2 (T683, PATCH keeps the rules) in
// tests/GenWave.MediaLibrary.Tests/Specs/Story533_AOnceAShiftQuirkFiresOnceAShift_CardEdit.cs; the import-boundary check (T683) in
// tests/GenWave.Host.Tests/Specs/Story533_AOnceAShiftQuirkFiresOnceAShift_Import.cs; AC6 (T683, old cards
// sample as before) in tests/GenWave.Tts.Tests/Specs/Story533_AOnceAShiftQuirkFiresOnceAShift_CardSampling.cs; AC3/AC4 (T686, selector) in
// tests/GenWave.Memory.Tests/Specs/Story533_AOnceAShiftQuirkFiresOnceAShift_Selector.cs; AC5 (T687, prompt) in
// tests/GenWave.Tts.Tests/Specs/Story533_AOnceAShiftQuirkFiresOnceAShift_Prompt.cs; AC7 (T686, preview endpoint) in
// tests/GenWave.Host.Tests/Specs/Story533_AOnceAShiftQuirkFiresOnceAShift_Preview.cs.

using GenWave.Core.Domain;

namespace GenWave.Core.Tests.Specs;

public static class FeatureAOnceAShiftQuirkFiresOnceAShiftCard
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — the card carries rules and round-trips
    // entry point: PersonaCard import/export JSON
    public sealed class ScenarioTheCardCarriesRulesAndRoundTrips
    {
        // Given a card JSON with QuirkRules [{text, maxPerShift: 1, perBreakChance: 0.15}]
        // When  it is imported and exported

        const string ImportedCardJson =
            "{\"schemaVersion\":1,\"name\":\"DJ Nova\",\"tagline\":\"Late-night frequencies.\"," +
            "\"soul\":\"Chases pirate signals.\",\"quirks\":[\"hums between sentences\",\"signs off with a pun\"]," +
            "\"voice\":{\"engine\":\"kokoro\",\"voiceId\":\"af_heart\",\"pace\":1.05,\"language\":\"en\"}," +
            "\"energyDisposition\":0.4,\"lore\":[\"Started in a garden shed.\"],\"corrections\":[]," +
            "\"quirkRules\":[{\"text\":\"signs off with a pun\",\"maxPerShift\":1,\"perBreakChance\":0.15}]}";

        static PersonaCard Import() =>
            PersonaCardSerializer.Deserialize(ImportedCardJson)
            ?? throw new InvalidOperationException("The fixture card deserialized to null.");

        [Fact]
        public void TheExportIsByteEqualToTheImport() =>
            Assert.Equal(ImportedCardJson, PersonaCardSerializer.Serialize(Import()));

        [Fact]
        public void QuirksAndQuirkRulesNameTheSameTexts()
        {
            var card = Import();

            Assert.Equal(
                card.QuirkRules?.Select(rule => rule.Text),
                card.Quirks.Where(quirk => card.QuirkRules?.Any(rule => rule.Text == quirk) == true));
        }

        [Fact]
        public void ACardWithoutRulesExportsWithNoQuirkRulesMember()
        {
            var json = PersonaCardSerializer.Serialize(Import() with { QuirkRules = null });

            Assert.DoesNotContain("quirkRules", json);
        }
    }
}

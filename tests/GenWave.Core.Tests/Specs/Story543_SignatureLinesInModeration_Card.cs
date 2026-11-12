// STORY-543 — Signature lines in moderation (gh-#293 gh-#920 · SPEC F218.30–.38 · PLAN T709)
//
// BDD specification — xUnit. T709 activates this fact.
// entry point: PersonaCard import/export (the card serializer)

using GenWave.Core.Domain;

namespace GenWave.Core.Tests.Specs;

public static class FeatureSignatureLinesInModerationCard
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC6 — the quota fields survive a round trip
    public sealed class ScenarioTheQuotaFieldsSurviveARoundTrip
    {
        // Given a card whose QuirkRules entry has maxPerHour 4 and minBreaksApart 1
        // When  the card is imported and exported

        const string ImportedCardJson =
            "{\"schemaVersion\":1,\"name\":\"DJ Nova\",\"tagline\":\"Late-night frequencies.\"," +
            "\"soul\":\"Chases pirate signals.\",\"quirks\":[\"hums between sentences\",\"signs off with a pun\"]," +
            "\"voice\":{\"engine\":\"kokoro\",\"voiceId\":\"af_heart\",\"pace\":1.05,\"language\":\"en\"}," +
            "\"energyDisposition\":0.4,\"lore\":[\"Started in a garden shed.\"],\"corrections\":[]," +
            "\"quirkRules\":[{\"text\":\"signs off with a pun\",\"maxPerHour\":4,\"minBreaksApart\":1}]}";

        [Fact]
        public void TheExportIsByteEqualToTheImport() =>
            Assert.Equal(
                ImportedCardJson,
                PersonaCardSerializer.Serialize(
                    PersonaCardSerializer.Deserialize(ImportedCardJson)
                    ?? throw new InvalidOperationException("The fixture card deserialized to null.")));
    }
}

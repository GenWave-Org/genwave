// STORY-556 — A card saved with voices per engine exports and imports back (SPEC F219.7/F219.10 · PLAN T754a)

namespace GenWave.Host.Tests.Specs;

using GenWave.Core.Domain;
using GenWave.Host.Api;

public static class FeatureVoicesSurviveExportImport
{
    static PersonaCard Built(string voice, IReadOnlyDictionary<string, string>? voices) =>
        PersonaVoiceLockstep.Apply(
            new PersonaCard(PersonaCard.CurrentSchemaVersion, "DJ Nova", "", "", [], new VoiceSpec("", voice, 1.0, "en"), 0, [], []),
            new PersonaDraft("DJ Nova", "", "", voice, Voices: voices));

    static string? ImportProblem(PersonaCard saved) =>
        PersonaCardVoicesValidator.FirstProblem(
            PersonaCardSerializer.Deserialize(PersonaCardSerializer.Serialize(saved)) ?? throw new InvalidOperationException("round trip failed"));

    public sealed class ScenarioAMapOnALegacyEngineCard
    {
        [Fact]
        public void TheExportedCardPassesTheImportCheck() =>
            Assert.Null(ImportProblem(Built("af_nova", new Dictionary<string, string> { ["chatterbox"] = "reader-1" })));
    }

    public sealed class ScenarioABlankVoiceWithAMap
    {
        [Fact]
        public void TheExportedCardPassesTheImportCheck() =>
            Assert.Null(ImportProblem(Built("", new Dictionary<string, string> { ["chatterbox"] = "reader-1" })));
    }
}

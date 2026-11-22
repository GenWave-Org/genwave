// STORY-556 — the card's legacy-engine fold names the same engine as the Tts profile (SPEC F219.7 · PLAN T754a)
//
// PersonaCard (the published Abstractions package) cannot reference GenWave.Tts, so it carries its own "kokoro".
// This pins the two together.

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Domain;

public static class FeatureLegacyEngineNamesKokoro
{
    public sealed class ScenarioTheCardFoldsTheEmptyEngineName
    {
        [Fact]
        public void ItKeysTheEntryByTheKokoroProfileName()
        {
            var card = new PersonaCard(PersonaCard.CurrentSchemaVersion, "DJ", "", "", [], new VoiceSpec("", "af_nova", 1.0, "en"), 0, [], [])
            {
                Voices = new Dictionary<string, string> { ["chatterbox"] = "r" },
            };

            Assert.Contains(VoiceEngineProfiles.KokoroName, card.WithVoice(card.Voice).Voices?.Keys ?? []);
        }
    }
}

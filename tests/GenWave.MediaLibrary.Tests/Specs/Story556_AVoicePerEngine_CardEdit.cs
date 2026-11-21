// STORY-556 — Every speaker keeps a voice per engine, the edit half (SPEC F219.7 · PLAN T751)
//
// PATCH /api/personas/{id} -> IPersonaStore.UpdateAsync -> PersonaRepository.MergeCard is the only
// place the stored card is rewritten; the pure merge is what these facts drive. The card map is in
// tests/GenWave.Core.Tests/Specs/Story556_AVoicePerEngine_Card.cs.

using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureAVoicePerEngineCardEdit
{
    static PersonaCard CardWithTwoEngines() => new PersonaCard(
        PersonaCard.CurrentSchemaVersion, "Flip", "", "", [],
        new VoiceSpec("kokoro", "af_heart", 1.0, "en"), 0.4, [], [])
    {
        Voices = new Dictionary<string, string> { ["kokoro"] = "af_heart", ["chatterbox"] = "flip_ref" },
    };

    public sealed class ScenarioARenameKeepsTheMap
    {
        // Given a persona with a voice per engine
        // When  PATCH renames the persona and keeps its voice
        private readonly PersonaCard merged = PersonaRepository.MergeCard(
            CardWithTwoEngines(), new PersonaDraft("Flop", Backstory: "", Style: "", Voice: "af_heart"));

        [Fact]
        public void VoicesAreUnchanged() =>
            Assert.Equal(CardWithTwoEngines().Voices, merged.Voices);
    }

    public sealed class ScenarioAVoiceChangeStaysInLockstep
    {
        // Given a persona with a voice per engine
        // When  PATCH picks a new voice
        private readonly PersonaCard merged = PersonaRepository.MergeCard(
            CardWithTwoEngines(), new PersonaDraft("Flip", Backstory: "", Style: "", Voice: "af_bella"));

        [Fact]
        public void VoiceIsTheNewVoice() =>
            Assert.Equal("af_bella", merged.Voice.VoiceId);

        [Fact]
        public void TheVoiceEnginesEntryIsTheNewVoice() =>
            Assert.Equal("af_bella", merged.Voices?["kokoro"]);

        [Fact]
        public void TheOtherEnginesEntriesSurvive() =>
            Assert.Equal("flip_ref", merged.Voices?["chatterbox"]);
    }

    // The edit writes the voice of the engine the card's voice belongs to, whichever engine airs
    public sealed class ScenarioAVoiceChangeOnAChatterboxVoicedCard
    {
        private readonly PersonaCard merged = PersonaRepository.MergeCard(
            new PersonaCard(
                PersonaCard.CurrentSchemaVersion, "Flip", "", "", [],
                new VoiceSpec("chatterbox", "flip_ref", 1.0, "en"), 0.4, [], [])
            {
                Voices = new Dictionary<string, string> { ["kokoro"] = "af_heart", ["chatterbox"] = "flip_ref" },
            },
            new PersonaDraft("Flip", Backstory: "", Style: "", Voice: "flip_ref_2"));

        [Fact]
        public void TheChatterboxEntryIsTheNewVoice() =>
            Assert.Equal("flip_ref_2", merged.Voices?["chatterbox"]);

        [Fact]
        public void TheKokoroEntrySurvives() =>
            Assert.Equal("af_heart", merged.Voices?["kokoro"]);
    }
}

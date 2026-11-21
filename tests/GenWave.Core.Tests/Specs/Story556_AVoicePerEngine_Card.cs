// STORY-556 — Every speaker keeps a voice per engine, the card half (SPEC F219 · PLAN F219)

using GenWave.Core.Domain;

namespace GenWave.Core.Tests.Specs;

public static class FeatureAVoicePerEngineOnTheCard
{
    // AC1 — a card with only Voice (kokoro, af_heart)
    public sealed class ScenarioAnOldCard
    {
        private readonly IReadOnlyDictionary<string, string> map = new PersonaCard(
            1, "Flip", "", "", [], new VoiceSpec("kokoro", "af_heart", 1.0, "en"), 0, [], []).VoicesByEngine();

        [Fact]
        public void ItsMapIsKokoroToAfHeart() =>
            Assert.Equal(new Dictionary<string, string> { ["kokoro"] = "af_heart" }, map);
    }

    // AC2 — a card with Voices, imported then exported
    public sealed class ScenarioRoundTrip
    {
        private const string ImportedJson =
            "{\"schemaVersion\":1,\"name\":\"Flip\",\"tagline\":\"\",\"soul\":\"\",\"quirks\":[]," +
            "\"voice\":{\"engine\":\"kokoro\",\"voiceId\":\"af_heart\",\"pace\":1,\"language\":\"en\"}," +
            "\"energyDisposition\":0,\"lore\":[],\"corrections\":[]," +
            "\"voices\":{\"kokoro\":\"af_heart\",\"chatterbox\":\"flip_ref\"}}";

        private readonly PersonaCard imported =
            PersonaCardSerializer.Deserialize(ImportedJson) ?? throw new InvalidOperationException("null card");

        [Fact]
        public void TheBytesAreEqual() =>
            Assert.Equal(ImportedJson, PersonaCardSerializer.Serialize(imported));
    }

    // AC2 continued — editing: the real writer is PersonaRepository.MergeCard, driven in
    // tests/GenWave.MediaLibrary.Tests/Specs/Story556_AVoicePerEngine_CardEdit.cs (Core cannot see it).
    // The pure lockstep rule it delegates to is one fact here.
    public sealed class ScenarioTheLockstepRule
    {
        private readonly PersonaCard imported =
            PersonaCardSerializer.Deserialize(
                "{\"schemaVersion\":1,\"name\":\"Flip\",\"tagline\":\"\",\"soul\":\"\",\"quirks\":[]," +
                "\"voice\":{\"engine\":\"kokoro\",\"voiceId\":\"af_heart\",\"pace\":1,\"language\":\"en\"}," +
                "\"energyDisposition\":0,\"lore\":[],\"corrections\":[]," +
                "\"voices\":{\"kokoro\":\"af_heart\",\"chatterbox\":\"flip_ref\"}}")
            ?? throw new InvalidOperationException("null card");

        [Fact]
        public void VoiceAndVoicesStayInLockstep() =>
            Assert.Equal(
                new Dictionary<string, string> { ["kokoro"] = "af_bella", ["chatterbox"] = "flip_ref" },
                imported.WithVoice(imported.Voice with { VoiceId = "af_bella" }).Voices);
    }
}

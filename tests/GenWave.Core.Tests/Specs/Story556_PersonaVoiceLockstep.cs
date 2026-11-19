// STORY-556 — A DJ's voice per engine stays in lockstep with its voice (SPEC F219.7/F219.10 · PLAN T754a)
//
// BDD specification — xUnit. The pure rule that folds an edit's per-engine voices into a card.

using GenWave.Core.Domain;

namespace GenWave.Core.Tests.Specs;

public static class FeaturePersonaVoiceLockstep
{
    static PersonaCard CardOn(string engine, string voiceId) =>
        new(PersonaCard.CurrentSchemaVersion, "DJ Nova", "", "", [], new VoiceSpec(engine, voiceId, 1.0, "en"), 0, [], []);

    static PersonaDraft Edit(string voice, IReadOnlyDictionary<string, string>? voices) =>
        new("DJ Nova", "", "", voice, Voices: voices);

    public sealed class ScenarioNoVoicesInTheEdit
    {
        [Fact]
        public void TheCardIsUntouched()
        {
            var card = CardOn("", "af_nova");

            Assert.Same(card, PersonaVoiceLockstep.Apply(card, Edit("af_nova", null)));
        }
    }

    public sealed class ScenarioVoicesForAnotherEngine
    {
        readonly PersonaCard saved = PersonaVoiceLockstep.Apply(
            CardOn("", "af_nova"), Edit("af_nova", new Dictionary<string, string> { ["chatterbox"] = "reader-1" }));

        [Fact]
        public void TheEngineGetsItsVoice() => Assert.Equal("reader-1", saved.Voices?["chatterbox"]);

        [Fact]
        public void TheLegacyEmptyEngineReadsAsKokoro() => Assert.Equal("af_nova", saved.Voices?["kokoro"]);

        [Fact]
        public void NoEmptyEngineKeyIsStored() => Assert.DoesNotContain("", saved.Voices?.Keys ?? []);
    }

    public sealed class ScenarioAMapThatDisagreesWithTheVoice
    {
        [Fact]
        public void TheVoiceWinsForTheCardsOwnEngine()
        {
            var saved = PersonaVoiceLockstep.Apply(
                CardOn("kokoro", "af_sky"), Edit("af_sky", new Dictionary<string, string> { ["Kokoro"] = "af_nova" }));

            Assert.Equal("af_sky", saved.Voices?["kokoro"]);
        }
    }

    public sealed class ScenarioAnEngineTheEditDoesNotNameKeepsItsVoice
    {
        [Fact]
        public void ItSurvives()
        {
            var existing = CardOn("", "af_nova") with
            {
                Voices = new Dictionary<string, string> { ["kokoro"] = "af_nova", ["chatterbox"] = "reader-1" },
            };

            var saved = PersonaVoiceLockstep.Apply(existing, Edit("af_nova", new Dictionary<string, string> { ["piper"] = "p" }));

            Assert.Equal("reader-1", saved.Voices?["chatterbox"]);
        }
    }

    public sealed class ScenarioAVoiceOnlyEditAfterAMapSave
    {
        [Fact]
        public void TheOwnEnginesEntryMovesWithTheVoiceAndNoEmptyKeyAppears()
        {
            var saved = PersonaVoiceLockstep.Apply(
                CardOn("", "af_nova"),
                Edit("af_nova", new Dictionary<string, string> { ["kokoro"] = "af_nova", ["chatterbox"] = "reader-1" }));

            var edited = saved.WithVoice(saved.Voice with { VoiceId = "af_sky" });

            Assert.Equal("af_sky", edited.Voices?["kokoro"]);
            Assert.Equal("reader-1", edited.Voices?["chatterbox"]);
            Assert.DoesNotContain("", edited.Voices?.Keys ?? []);
        }
    }

    public sealed class ScenarioABlankVoiceWithAMap
    {
        [Fact]
        public void NoBlankVoiceIdIsStored()
        {
            var saved = PersonaVoiceLockstep.Apply(
                CardOn("", ""), Edit("", new Dictionary<string, string> { ["chatterbox"] = "reader-1" }));

            Assert.Equal("reader-1", saved.Voices?["chatterbox"]);
            Assert.DoesNotContain(saved.Voices ?? new Dictionary<string, string>(), e => string.IsNullOrWhiteSpace(e.Value));
        }
    }

    public sealed class ScenarioABlankIdClearsAnEngine
    {
        readonly PersonaCard saved = PersonaVoiceLockstep.Apply(
            CardOn("", "af_nova") with
            {
                Voices = new Dictionary<string, string> { ["kokoro"] = "af_nova", ["chatterbox"] = "reader-1" },
            },
            Edit("af_nova", new Dictionary<string, string> { ["chatterbox"] = "" }));

        [Fact]
        public void TheEngineHasNoVoice() => Assert.DoesNotContain("chatterbox", saved.Voices?.Keys ?? []);

        [Fact]
        public void NoBlankIdIsStored() =>
            Assert.DoesNotContain(saved.Voices ?? new Dictionary<string, string>(), e => string.IsNullOrWhiteSpace(e.Value));

        [Fact]
        public void TheOwnEnginesVoiceStillWins()
        {
            var again = PersonaVoiceLockstep.Apply(
                CardOn("", "af_nova"), Edit("af_nova", new Dictionary<string, string> { ["kokoro"] = "" }));

            Assert.Equal("af_nova", again.Voices?["kokoro"]);
        }
    }
}

// STORY-558 — DJs can laugh (SPEC F219 · PLAN F219)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Domain;
using GenWave.Memory;
using GenWave.Tts.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

public static class FeatureDjsCanLaugh
{
    // AC1 — chatterbox profile, cap 1
    public sealed class ScenarioThePromptOffersTags
    {
        [Fact(Skip = "pending: T760")]
        public void TheDjPromptListsTheNineEventTags() { }

        [Fact(Skip = "pending: T760")]
        public void TheDjPromptStatesTheCap() { }

        [Fact(Skip = "pending: T760")]
        public void TheTwoDjPromptListsTheTags() { }
    }

    // AC2 — "[laugh] hi [sigh] [dance]", cap 1
    public sealed class ScenarioTagsKeptUpToTheCap
    {
        readonly string text = SoundTags.Apply("[laugh] hi [sigh] [dance]", VoiceEngineProfiles.Chatterbox, cap: 1);

        [Fact]
        public void TheTextIsLaughHi() => Assert.Equal("[laugh] hi", text);
    }

    // AC3 — "[clear throat] hello"
    public sealed class ScenarioTheNormaliserKeepsThem
    {
        readonly string normalized = SpeechText.Normalize("[clear throat] hello", SpeechCorrectionSet.Empty);

        [Fact]
        public void TheTagIsByteIntact() => Assert.Equal("[clear throat] hello", normalized);
    }

    // AC4 — a line that differs from a recent one only by a tag
    public sealed class ScenarioChecksIgnoreThem
    {
        const string Recent = "Now let me tell you about the odd little shop around the corner today.";
        const string Plain = "Let me tell you about the odd little shop around the corner.";
        const string Tagged = "Let me tell you about the [sigh] odd little shop around the corner.";

        readonly CrosstalkLineMemory memory = new(
            new RepetitionCheck(), utteranceStore: null, new RecordingStationEventSink(), NullLogger.Instance);

        readonly CrosstalkLineMemory.SpeakerHistory history =
            new(CrosstalkSpeaker.Host, 7, "Nova", [Recent]);

        readonly ClaimCheckResult gate = LlmCopyWriter.CheckTruthGate(
            "The show [sigh] starts at nine tonight.", factBlock: null,
            new DateTimeOffset(2026, 11, 9, 21, 0, 0, TimeSpan.Zero), trackTitle: null, stationName: "GenWave",
            requiredCore: "the show starts at nine");

        IReadOnlyList<string> PhrasesOf(string line) =>
            memory.FindRepeats(
                    new CrosstalkAiredScript([new CrosstalkAiredLine(CrosstalkSpeaker.Host, line, false)]),
                    "GenWave", [history])
                .SelectMany(hit => hit.Phrases)
                .ToList();

        [Fact]
        public void TheRepetitionCheckSeesTheSameWords() => Assert.Equal(PhrasesOf(Plain), PhrasesOf(Tagged));

        [Fact]
        public void TheTruthGateReadsTagFreeCopy() => Assert.True(gate.Passed);
    }

    // Sad path
    // AC5, AC6
    public sealed class ScenarioTagsOff
    {
        [Fact(Skip = "pending: T760")]
        public void WithCapZeroTheDjPromptIsByteEqualToTodays() { }

        [Fact(Skip = "pending: T760")]
        public void OnKokoroThePromptIsByteEqualToTodays() { }

        [Fact]
        public void OnKokoroTheTextIsHi() =>
            Assert.Equal("hi", SoundTags.Apply("[laugh] hi [sigh] [dance]", VoiceEngineProfiles.Kokoro, cap: 1));
    }
}

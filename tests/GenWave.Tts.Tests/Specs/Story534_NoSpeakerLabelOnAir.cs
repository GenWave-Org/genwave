// STORY-534 — The DJ does not say that again (PLAN T723): aired copy never starts with a speaker label.
//
// entry point: LlmCopyWriter.WriteAsync (end to end) and LlmCopyWriter.CleanupOf (the hygiene seam it calls)

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Memory;
using GenWave.Tts.Tests.Fakes;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureNoSpeakerLabelOnAir
{
    sealed class NoQuirks : IQuirkSelector
    {
        public Task<QuirkSelection> SelectAsync(
            long? personaId, PersonaCard? card, ShiftKey? shiftKey, bool spend, CancellationToken ct) =>
            Task.FromResult(QuirkSelection.None);
    }

    static string Clean(string content, string? name = "Big Tony")
    {
        var cleanup = LlmCopyWriter.CleanupOf(new CompletionReply(content, "stop", 0), 500, name);
        return Assert.IsType<LlmCopyCleanupResult.Fits>(cleanup).Text;
    }

    public sealed class ScenarioTheLabelIsStripped
    {
        [Fact]
        public void ALabelAndItsWrappingQuotesAreRemoved() =>
            Assert.Equal("Ladies and gents, welcome back.", Clean("Big Tony: \"Ladies and gents, welcome back.\""));

        [Fact]
        public void CurlyQuotesAreRemovedToo() =>
            Assert.Equal("Ladies and gents.", Clean("big tony: “Ladies and gents.”"));

        [Fact]
        public void ABoldLabelIsRemoved() =>
            Assert.Equal("Ladies and gents.", Clean("**Big Tony:** \"Ladies and gents.\""));

        [Fact]
        public void AnUnquotedBodyKeepsItsText() =>
            Assert.Equal("Ladies and gents.", Clean("Big Tony: Ladies and gents."));

        [Fact]
        public async Task TheAiredCopyHasNoLabel()
        {
            var prompts = new List<string>();
            var writer = QuirkSpecHarness.WriterFor(
                QuirkSpecHarness.Card([], null), new NoQuirks(), httpOverride: QuirkSpecHarness.ScriptedHttp(
                    prompts, ["DJ Nova: \"Here comes a real stunner for the late shift.\""]));
            var copy = await writer.WriteAsync(QuirkSpecHarness.Request(null), CancellationToken.None);
            Assert.Equal("Here comes a real stunner for the late shift.", copy.Text);
        }

        [Fact]
        public async Task TheReAskReplyIsStrippedToo()
        {
            var store = new RecordingUtteranceStore
            {
                Seeded =
                {
                    new UtteranceEntry(
                        1, QuirkSpecHarness.PersonaId, null, UtteranceKind.Patter, "LeadIn",
                        ShiftKey.Unscheduled(QuirkSpecHarness.PersonaId, new DateOnly(2026, 11, 9)),
                        "Here comes a golden hour groove from the vault tonight.", false, DateTimeOffset.UnixEpoch, null),
                },
            };
            var prompts = new List<string>();
            var writer = QuirkSpecHarness.WriterFor(
                QuirkSpecHarness.Card([], null), new NoQuirks(), utteranceStore: store,
                httpOverride: QuirkSpecHarness.ScriptedHttp(prompts, [
                    "Settle in for a golden hour groove from the vault, folks.",
                    "DJ Nova: \"Next up, a real stunner for the late shift.\""]),
                repetitionCheck: new RepetitionCheck());
            var copy = await writer.WriteAsync(QuirkSpecHarness.Request(null), CancellationToken.None);
            Assert.Equal(2, prompts.Count);
            Assert.Equal("Next up, a real stunner for the late shift.", copy.Text);
        }

        [Theory]
        [InlineData("Big Tony:")]
        [InlineData("Big Tony: \"\"")]
        [InlineData("**Big Tony:**")]
        public void AReplyThatIsOnlyTheLabelIsRejected(string content)
        {
            var cleanup = LlmCopyWriter.CleanupOf(new CompletionReply(content, "stop", 0), 500, "Big Tony");
            Assert.IsType<LlmCopyCleanupResult.Rejected>(cleanup);
        }
    }

    public sealed class ScenarioNothingElseIsTouched
    {
        [Fact]
        public void AnotherNamesLabelStays() =>
            Assert.Equal("Sal: Ladies and gents.", Clean("Sal: Ladies and gents."));

        [Fact]
        public void AMidLineNameStays() =>
            Assert.Equal("Tonight Big Tony: is here.", Clean("Tonight Big Tony: is here."));

        [Fact]
        public void InnerQuotesStay() =>
            Assert.Equal("He said \"hello\" and left.", Clean("Big Tony: He said \"hello\" and left."));

        [Fact]
        public void AQuoteThatDoesNotWrapTheWholeLineStays() =>
            Assert.Equal("\"Hello\" he said, and left.", Clean("Big Tony: \"Hello\" he said, and left."));

        [Fact]
        public void ABlankNameDoesNothing() =>
            Assert.Equal("Big Tony: Hi there.", Clean("Big Tony: Hi there.", name: " "));

        [Fact]
        public void CrosstalkHygieneKeepsItsLabels() =>
            Assert.Equal("Big Tony: Hi there.", LlmCopyWriter.ApplyCopyHygiene("Big Tony: Hi there."));
    }
}

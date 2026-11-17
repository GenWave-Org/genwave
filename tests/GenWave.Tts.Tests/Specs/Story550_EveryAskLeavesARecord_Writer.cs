// STORY-550 — Every ask leaves a record, the writer half (gh-#1001 · SPEC F218.44 · PLAN T736)
//
// BDD specification — xUnit. LlmCopyWriter settles the offer from RecordAsync: AC2 (the recorded line settles it with the
// utterance id) and AC7 (a template fallback leaves it unused). The planner's own settle rules are Memory.Tests Story550.

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureEveryAskLeavesARecordWriter
{
    // AC2 — settled with the line
    public sealed class ScenarioSettledWithTheLine : IAsyncLifetime
    {
        // Given that offer and the aired line "like Daisy chasin' her tail"
        // When  the line is recorded
        readonly CallbackSpecHarness h = new(reply: "Here is Astral Plane, like Daisy chasin' her tail.");

        public async Task InitializeAsync() =>
            await h.Writer.WriteAsync(CallbackSpecHarness.RecordableRequest(SegmentKind.LeadIn), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheOfferHasTheUtteranceId() => Assert.Equal(1, Assert.Single(h.Store.Offers).UtteranceId);

        [Fact]
        public void TheOfferIsUsed() => Assert.True(Assert.Single(h.Store.Offers).Used);

        [Fact]
        public void TheMatchedWordsAreDaisy() => Assert.Equal(["daisy"], Assert.Single(h.Store.Offers).MatchedWords);
    }

    // AC7 — a fallback line leaves used false (sad path)
    public sealed class ScenarioAFallbackLineIsNotUsed : IAsyncLifetime
    {
        // Given a callback break whose copy falls back to the template (the model answers with nothing usable)
        // When  the line is recorded
        readonly CallbackSpecHarness h = new(reply: "");

        public async Task InitializeAsync() =>
            await h.Writer.WriteAsync(CallbackSpecHarness.RecordableRequest(SegmentKind.LeadIn), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheOfferIsSettledUnused()
        {
            var offer = Assert.Single(h.Store.Offers);
            Assert.True(offer.Settled);
            Assert.False(offer.Used);
        }

        [Fact]
        public void TheTemplateLineWasRecordedAsAFallback() => Assert.True(Assert.Single(h.Utterances.Recorded).Fallback);
    }

    // The store down (sad path): the break still airs its line
    public sealed class ScenarioAFailingStoreNeverLosesTheBreak : IAsyncLifetime
    {
        // Given a callback store that throws
        readonly CallbackSpecHarness h = new(storeThrows: true);
        SegmentCopy copy = new("", false);

        public async Task InitializeAsync() =>
            copy = await h.Writer.WriteAsync(CallbackSpecHarness.RecordableRequest(SegmentKind.LeadIn), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheModelLineAirs() => Assert.Equal("Coming up next a classic from the vault.", copy.Text);

        [Fact]
        public void NoAskReachedThePrompt() => Assert.DoesNotContain(LlmPromptBuilder.CallbackAskLabel, h.Prompts);
    }
}

// STORY-570 — One flow for flagged talk (SPEC F220 · PLAN F220)
//
// T797: the callbacks and never-mention rules, run by the real rule book with fakes at the I/O edges (the callback
// store, the memory store, the persona roster, the context window). The golden-prompt proof (F220.21) is in
// GenWave.Tts.Tests (Story570_GoldenPromptsMoveUnderForThisBreak).

using System.Text.Json;
using GenWave.Core.Domain;
using GenWave.Core.TalkRules;
using GenWave.Memory;
using GenWave.TalkRules.Tests.Fixtures;
using GenWave.Tts;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenWave.TalkRules.Tests.Specs;

public static class FeatureCallbacksAndNeverMentionShapers
{
    private const string Remembered = "has a dog named Daisy";

    private static readonly PersonaCard Card =
        new(PersonaCard.CurrentSchemaVersion, "Flip", "", "Warm and unhurried.", [], new VoiceSpec("", "af_nova", 1.0, "en"), 0, [], []);

    private static readonly PersonaCard CardWithNothingToSay =
        new(PersonaCard.CurrentSchemaVersion, "Flip", "", "", [], new VoiceSpec("", "af_nova", 1.0, "en"), 0, [], []);

    private static TalkSnapshot Before(string segmentKind = "LeadIn", bool preview = false) =>
        TalkSnapshots.DjLine(string.Empty) with
        {
            SegmentKind = segmentKind,
            Lines = TalkList<TalkLine>.Empty,
            Preview = preview,
        };

    private static TalkSnapshot Aired(string text, TalkMemos memos, long? utteranceId = 42) =>
        TalkSnapshots.DjLine(text, memos) with { SegmentKind = "LeadIn", UtteranceId = utteranceId };

    private static TalkSnapshot AiredAfter(TalkShaping shaping, string text, long? utteranceId = 42) => Aired(text, shaping.Memos, utteranceId);

    private static MemoryBreakGate Gate(MemoryBudget budget, PersonaCard? card = null) =>
        new(new RosterPersonaStore("Flip", "Rusty Strings"), new OneCardSource(card ?? Card), new FixedContextWindow(budget));

    private static CallbacksRule CallbacksOver(CallbackStoreOfOneMemory store, MemoryBudget? budget = null, int perHour = 6) =>
        new(
            new BreakCallbacks(
                new CallbackPlanner(store, new AlwaysDraws()), new FixedCallbackRate(perHour), new RosterPersonaStore("Flip", "Rusty Strings"),
                NullLogger<BreakCallbacks>.Instance),
            Gate(budget ?? MemoryBudget.Standard));

    private static TalkRuleBook BookOf(ITalkRule rule, InMemoryTalkRuleSwitches? switches = null) =>
        new([rule], switches ?? new InMemoryTalkRuleSwitches(), NullLogger<TalkRuleBook>.Instance);

    private static InMemoryTalkRuleSwitches Off(string ruleId)
    {
        var switches = new InMemoryTalkRuleSwitches();
        switches.SetAsync(ruleId, TalkKinds.DjLine, false, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return switches;
    }

    private static CallbackOfferMemo? MemoOf(CallbacksRule rule, TalkShaping shaping) =>
        TalkSnapshots.DjLine(string.Empty, shaping.Memos).MemoOf(rule)?.Deserialize<CallbackOfferMemo>(TalkSnapshotJson.Options);

    private static readonly TalkShaping Nothing = new(TalkList<string>.Empty, TalkMemos.Empty, TalkList<string>.Empty);

    // F218.40, F218.44 — the offer is made in the shape, text from the one builder the legacy prompt uses
    public sealed class ScenarioCallbacksShapes : IAsyncLifetime
    {
        private readonly CallbackStoreOfOneMemory store = new(Remembered);
        private readonly CallbacksRule rule;
        private TalkShaping shaping = Nothing;

        public ScenarioCallbacksShapes() => rule = CallbacksOver(store);

        public async Task InitializeAsync() => shaping = await BookOf(rule).ShapeAsync(Before(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheNoteIsTheAskTheLegacyPromptBuilds() =>
            Assert.Equal(
                string.Join('\n', [LlmPromptBuilder.CallbackAskLabel + Remembered, LlmPromptBuilder.CallbackAskInstructionLine]),
                shaping.PromptNotes.Single());

        [Fact]
        public void TheMemoCarriesTheOfferedMemory() => Assert.Equal(Remembered, MemoOf(rule, shaping)?.Ask.Memory.Content);

        [Fact]
        public void TheMemoCarriesTheOfferRowTheStoreWrote() => Assert.Equal(store.Offers.Single().Id, MemoOf(rule, shaping)?.Ask.OfferId);

        [Fact]
        public void TheOfferRowIsWrittenBeforeAnythingIsAired() => Assert.False(store.Offers.Single().Settled);

        [Fact]
        public void TheOfferIsForTheSpeaker() => Assert.Equal(1, store.Offers.Single().PersonaId);
    }

    public sealed class ScenarioABackAnnounceIsShaped : IAsyncLifetime
    {
        private TalkShaping shaping = Nothing;

        public async Task InitializeAsync() =>
            shaping = await BookOf(CallbacksOver(new CallbackStoreOfOneMemory(Remembered))).ShapeAsync(Before("BackAnnounce"), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItIsOfferedACallbackToo() => Assert.Single(shaping.PromptNotes);
    }

    // F220.15 — accepted talk settles the offered callback by the word rule, from the memo alone
    public sealed class ScenarioAcceptedTalkUsesTheMemory : IAsyncLifetime
    {
        private readonly CallbackStoreOfOneMemory store = new(Remembered);
        private TalkList<string> failed = new(["unset"]);

        public async Task InitializeAsync()
        {
            var book = BookOf(CallbacksOver(store));
            var shaping = await book.ShapeAsync(Before(), CancellationToken.None);
            failed = await book.AcceptedAsync(AiredAfter(shaping, "Daisy would have howled at this one."), CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheOfferIsSettledUsed() => Assert.True(store.Offers.Single().Used);

        [Fact]
        public void TheSettleNamesTheUtteranceAndTheMatchedWord() =>
            Assert.Equal((42L, "daisy"), (store.Offers.Single().UtteranceId, string.Join(", ", store.Offers.Single().MatchedWords)));

        [Fact]
        public void NothingFailsToRecord() => Assert.Empty(failed);
    }

    // F218.44 — talk with no utterance id on the record cannot be used: the offer settles unused
    public sealed class ScenarioAcceptedTalkHasNoUtteranceId : IAsyncLifetime
    {
        private readonly CallbackStoreOfOneMemory store = new(Remembered);

        public async Task InitializeAsync()
        {
            var book = BookOf(CallbacksOver(store));
            var shaping = await book.ShapeAsync(Before(), CancellationToken.None);
            await book.AcceptedAsync(AiredAfter(shaping, "Daisy would have howled at this one.", utteranceId: null), CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheOfferIsSettledUnused() => Assert.Equal((true, false), (store.Offers.Single().Settled, store.Offers.Single().Used));
    }

    // The word rule decides: aired talk that never says the memory settles it unused
    public sealed class ScenarioAcceptedTalkLeavesTheMemoryOut : IAsyncLifetime
    {
        private readonly CallbackStoreOfOneMemory store = new(Remembered);

        public async Task InitializeAsync()
        {
            var book = BookOf(CallbacksOver(store));
            var shaping = await book.ShapeAsync(Before(), CancellationToken.None);
            await book.AcceptedAsync(AiredAfter(shaping, "Here is something warm for the evening."), CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheOfferIsSettledUnused() => Assert.Equal((true, false), (store.Offers.Single().Settled, store.Offers.Single().Used));
    }

    // STORY-570 AC9 — talk that was rejected never reaches AcceptedAsync, so the callback is not settled
    public sealed class ScenarioTalkIsRejected : IAsyncLifetime
    {
        private readonly CallbackStoreOfOneMemory store = new(Remembered);

        public async Task InitializeAsync() => await BookOf(CallbacksOver(store)).ShapeAsync(Before(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheCallbackIsNotSettled() => Assert.False(store.Offers.Single().Settled);
    }

    // A recorder reads only its own memo: accepted talk with none settles nothing
    public sealed class ScenarioAcceptedTalkWithNoMemo : IAsyncLifetime
    {
        private readonly CallbackStoreOfOneMemory store = new(Remembered);
        private TalkList<string> failed = new(["unset"]);

        public async Task InitializeAsync() =>
            failed = await BookOf(CallbacksOver(store)).AcceptedAsync(Aired("Daisy would have howled.", TalkMemos.Empty), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NothingIsSettledAndNothingFails() => Assert.Equal((0, 0), (store.Offers.Count, failed.Count));
    }

    // F220.10 — a switched-off shaper adds nothing and keeps no state
    public sealed class ScenarioCallbacksIsSwitchedOffWhenShaping : IAsyncLifetime
    {
        private readonly CallbackStoreOfOneMemory store = new(Remembered);
        private readonly CallbacksRule rule;
        private TalkShaping shaping = Nothing;

        public ScenarioCallbacksIsSwitchedOffWhenShaping() => rule = CallbacksOver(store);

        public async Task InitializeAsync() =>
            shaping = await BookOf(rule, Off(CallbackOfferMemo.RuleId)).ShapeAsync(Before(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoOfferIsWritten() => Assert.Empty(store.Offers);

        [Fact]
        public void NoNoteIsOffered() => Assert.Empty(shaping.PromptNotes);

        [Fact]
        public void NoMemoIsLeft() => Assert.Null(MemoOf(rule, shaping));
    }

    // F220.15 — a switched-off recorder records nothing, even for a memo that is on the snapshot
    public sealed class ScenarioCallbacksIsSwitchedOffWhenRecording : IAsyncLifetime
    {
        private readonly CallbackStoreOfOneMemory store = new(Remembered);

        public async Task InitializeAsync()
        {
            var rule = CallbacksOver(store);
            var shaping = await BookOf(rule).ShapeAsync(Before(), CancellationToken.None);
            await BookOf(rule, Off(CallbackOfferMemo.RuleId)).AcceptedAsync(AiredAfter(shaping, "Daisy would have howled."), CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NothingIsSettled() => Assert.False(store.Offers.Single().Settled);
    }

    public sealed class ScenarioThereIsNoMemoryToOffer : IAsyncLifetime
    {
        private readonly CallbacksRule rule;
        private TalkShaping shaping = Nothing;

        public ScenarioThereIsNoMemoryToOffer() => rule = CallbacksOver(new CallbackStoreOfOneMemory(null));

        public async Task InitializeAsync() => shaping = await BookOf(rule).ShapeAsync(Before(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoNoteIsOffered() => Assert.Empty(shaping.PromptNotes);

        [Fact]
        public void NoMemoIsLeft() => Assert.Null(MemoOf(rule, shaping));
    }

    public sealed class ScenarioAPreviewIsShapedForCallbacks : IAsyncLifetime
    {
        private readonly CallbackStoreOfOneMemory store = new(Remembered);

        public async Task InitializeAsync() => await BookOf(CallbacksOver(store)).ShapeAsync(Before(preview: true), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoOfferIsWritten() => Assert.Empty(store.Offers);
    }

    public sealed class ScenarioMemoryIsOffForCallbacks : IAsyncLifetime
    {
        private readonly CallbackStoreOfOneMemory store = new(Remembered);

        public async Task InitializeAsync() =>
            await BookOf(CallbacksOver(store, MemoryBudget.Off)).ShapeAsync(Before(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoOfferIsWritten() => Assert.Empty(store.Offers);
    }

    public sealed class ScenarioTheSegmentCarriesNoCallback : IAsyncLifetime
    {
        private readonly CallbackStoreOfOneMemory store = new(Remembered);

        public async Task InitializeAsync() => await BookOf(CallbacksOver(store)).ShapeAsync(Before("SignOn"), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoOfferIsWritten() => Assert.Empty(store.Offers);
    }

    public sealed class ScenarioTheCallbackSettingIsZero : IAsyncLifetime
    {
        private readonly CallbackStoreOfOneMemory store = new(Remembered);

        public async Task InitializeAsync() =>
            await BookOf(CallbacksOver(store, perHour: 0)).ShapeAsync(Before(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoOfferIsWritten() => Assert.Empty(store.Offers);
    }

    // F220.9 — a shaper that throws is reported, and the prompt goes without its note
    public sealed class ScenarioTheCallbackStoreThrowsWhenOffering : IAsyncLifetime
    {
        private TalkShaping shaping = Nothing;

        public async Task InitializeAsync() =>
            shaping = await BookOf(CallbacksOver(new CallbackStoreOfOneMemory(Remembered) { ThrowOnOffer = true })).ShapeAsync(Before(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheBookReportsCouldNotShape() => Assert.Equal(CallbackOfferMemo.RuleId, string.Join(", ", shaping.CouldNotShape));

        [Fact]
        public void NoNoteIsOffered() => Assert.Empty(shaping.PromptNotes);
    }

    public sealed class ScenarioTheCallbackStoreThrowsWhenSettling : IAsyncLifetime
    {
        private TalkList<string> failed = TalkList<string>.Empty;

        public async Task InitializeAsync()
        {
            var book = BookOf(CallbacksOver(new CallbackStoreOfOneMemory(Remembered) { ThrowOnSettle = true }));
            var shaping = await book.ShapeAsync(Before(), CancellationToken.None);
            failed = await book.AcceptedAsync(AiredAfter(shaping, "Daisy would have howled."), CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheBookReportsCouldNotRecord() => Assert.Equal(CallbackOfferMemo.RuleId, string.Join(", ", failed));
    }

    // F218.19 — never-mention offers the list the legacy persona section carries, from the one builder
    public sealed class ScenarioNeverMentionShapes : IAsyncLifetime
    {
        private readonly RecordingMemoryRecall recall = new("the night the transmitter failed", "Daisy the dog");
        private readonly NeverMentionRule rule;
        private TalkShaping shaping = Nothing;

        public ScenarioNeverMentionShapes() => rule = new NeverMentionRule(recall, Gate(MemoryBudget.Standard));

        public async Task InitializeAsync() => shaping = await BookOf(rule).ShapeAsync(Before(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheNoteIsTheListTheLegacyPromptBuilds() =>
            Assert.Equal(
                string.Join('\n', LlmPromptBuilder.BuildMemoryBlocks(new MemoryPacket(["the night the transmitter failed", "Daisy the dog"]))),
                shaping.PromptNotes.Single());

        [Fact]
        public void TheListIsAskedForTheSpeakerAtTheBudget() => Assert.Equal((1L, MemoryBudget.Standard), recall.Calls.Single());

        [Fact]
        public void ItLeavesNoMemo() => Assert.Null(TalkSnapshots.DjLine(string.Empty, shaping.Memos).MemoOf(rule));
    }

    public sealed class ScenarioNeverMentionIsSwitchedOff : IAsyncLifetime
    {
        private readonly RecordingMemoryRecall recall = new("Daisy the dog");
        private TalkShaping shaping = Nothing;

        public async Task InitializeAsync() =>
            shaping = await BookOf(new NeverMentionRule(recall, Gate(MemoryBudget.Standard)), Off("never-mention")).ShapeAsync(Before(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheListIsNotEvenRead() => Assert.Empty(recall.Calls);

        [Fact]
        public void NoNoteIsOffered() => Assert.Empty(shaping.PromptNotes);
    }

    public sealed class ScenarioNothingWasLetGo : IAsyncLifetime
    {
        private TalkShaping shaping = Nothing;

        public async Task InitializeAsync() =>
            shaping = await BookOf(new NeverMentionRule(new RecordingMemoryRecall(), Gate(MemoryBudget.Standard))).ShapeAsync(Before(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoNoteIsOffered() => Assert.Empty(shaping.PromptNotes);
    }

    public sealed class ScenarioMemoryIsOffForNeverMention : IAsyncLifetime
    {
        private readonly RecordingMemoryRecall recall = new("Daisy the dog");

        public async Task InitializeAsync() =>
            await BookOf(new NeverMentionRule(recall, Gate(MemoryBudget.Off))).ShapeAsync(Before(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheListIsNotRead() => Assert.Empty(recall.Calls);
    }

    public sealed class ScenarioAPreviewIsShapedForNeverMention : IAsyncLifetime
    {
        private readonly RecordingMemoryRecall recall = new("Daisy the dog");

        public async Task InitializeAsync() =>
            await BookOf(new NeverMentionRule(recall, Gate(MemoryBudget.Standard))).ShapeAsync(Before(preview: true), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheListIsNotRead() => Assert.Empty(recall.Calls);
    }

    public sealed class ScenarioAnAnnouncementIsShapedForNeverMention : IAsyncLifetime
    {
        private readonly RecordingMemoryRecall recall = new("Daisy the dog");

        public async Task InitializeAsync() =>
            await BookOf(new NeverMentionRule(recall, Gate(MemoryBudget.Standard))).ShapeAsync(Before("Announcement"), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheListIsNotRead() => Assert.Empty(recall.Calls);
    }

    // gh-#150, T701 — a persona with no soul and no quirks has no section for the list to ride on
    public sealed class ScenarioThePersonaHasNothingToSay : IAsyncLifetime
    {
        private readonly RecordingMemoryRecall recall = new("Daisy the dog");

        public async Task InitializeAsync() =>
            await BookOf(new NeverMentionRule(recall, Gate(MemoryBudget.Standard, CardWithNothingToSay))).ShapeAsync(Before(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheListIsNotRead() => Assert.Empty(recall.Calls);
    }

    public sealed class ScenarioTheMemoryStoreThrows : IAsyncLifetime
    {
        private TalkShaping shaping = Nothing;

        public async Task InitializeAsync() =>
            shaping = await BookOf(new NeverMentionRule(new RecordingMemoryRecall { Throws = true }, Gate(MemoryBudget.Standard)))
                .ShapeAsync(Before(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheBookReportsCouldNotShape() => Assert.Equal("never-mention", string.Join(", ", shaping.CouldNotShape));

        [Fact]
        public void NoNoteIsOffered() => Assert.Empty(shaping.PromptNotes);
    }
}

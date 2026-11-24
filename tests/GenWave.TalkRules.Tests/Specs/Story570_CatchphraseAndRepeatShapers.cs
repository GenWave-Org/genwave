// STORY-570 — One flow for flagged talk (SPEC F220 · PLAN F220)
//
// T796: the catchphrase-limits and repeat-phrases shapers, run by the real rule book over real rules with fake
// stores at the edge. The golden-prompt proof (F220.21) is in GenWave.Tts.Tests (Story570_GoldenPromptsMoveUnderForThisBreak).

using System.Text.Json;
using GenWave.Core.Domain;
using GenWave.Core.TalkRules;
using GenWave.Memory;
using GenWave.TalkRules.Tests.Fixtures;
using GenWave.Tts;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenWave.TalkRules.Tests.Specs;

public static class FeatureCatchphraseAndRepeatShapers
{
    private const string Capped = "Say \"you're welcome\" after any long ramble";
    private const string Plain = "Compare every record to a sandwich";
    private const string Tagline = "stay golden friends";

    private static readonly DateTimeOffset Now = TalkSnapshots.DjLine(string.Empty).LocalNow;

    private static readonly PersonaCard Card =
        new(PersonaCard.CurrentSchemaVersion, "Flip", Tagline, "", [Capped, Plain], new VoiceSpec("", "af_nova", 1.0, "en"), 0, [], [])
        {
            QuirkRules = [new QuirkRule(Capped, MaxPerShift: 1)],
        };

    private static TalkRecentLine TalkRecent(string text, int minutesAgo, long personaId = 1, bool fallback = false) =>
        new(personaId, text, Now.AddMinutes(-minutesAgo), fallback);

    private static TalkSnapshot Before(bool preview = false, params TalkRecentLine[] recent) =>
        TalkSnapshots.DjLine(string.Empty) with
        {
            Lines = TalkList<TalkLine>.Empty,
            RecentLines = new TalkList<TalkRecentLine>(recent),
            ShiftKey = ShiftKey.Unscheduled(1, new DateOnly(2026, 11, 22)).ToString(),
            Preview = preview,
        };

    private static (TalkRuleBook Book, CatchphraseLimitsRule Rule, RecordingQuirkSelector Selector) CatchphraseBook(
        InMemoryTalkRuleSwitches switches)
    {
        var selector = new RecordingQuirkSelector(new QuirkSelection([Plain], [Capped]));
        var rule = new CatchphraseLimitsRule(new RepetitionCheck(), selector, new TaglineLedger(), new OneCardSource(Card));
        return (new TalkRuleBook([rule], switches, NullLogger<TalkRuleBook>.Instance), rule, selector);
    }

    // F220.15 — a quirk offered by the shaper is spent by the selector at once
    public sealed class ScenarioCatchphraseLimitsShapes : IAsyncLifetime
    {
        private readonly (TalkRuleBook Book, CatchphraseLimitsRule Rule, RecordingQuirkSelector Selector) setup =
            CatchphraseBook(new InMemoryTalkRuleSwitches());
        private TalkShaping shaping = new(TalkList<string>.Empty, TalkMemos.Empty, TalkList<string>.Empty);
        private string legacyNote = string.Empty;
        private CatchphraseLimitsMemo? memo;

        public async Task InitializeAsync()
        {
            const string Said = "Back with the night owls and stay golden friends.";
            shaping = await setup.Book.ShapeAsync(Before(false, TalkRecent(Said, 5)), CancellationToken.None);

            var shift = ShiftKey.Unscheduled(1, new DateOnly(2026, 11, 22));
            var statuses = new TaglineLedger().Evaluate(
                Card, [new UtteranceEntry(0, 1, null, UtteranceKind.Patter, "Banter", shift, Said, false, Now.AddMinutes(-5), null)],
                [Capped], Now);
            legacyNote = string.Join('\n', LlmPromptBuilder.BuildQuirksBlock([Plain], [Capped], statuses));

            memo = TalkSnapshots.DjLine(string.Empty, shaping.Memos).MemoOf(setup.Rule)?.Deserialize<CatchphraseLimitsMemo>(TalkSnapshotJson.Options);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSelectorIsAskedToSpendForTheSpeakerAndShift() =>
            Assert.Equal(
                (1L, ShiftKey.Unscheduled(1, new DateOnly(2026, 11, 22)).ToString(), true),
                (setup.Selector.Calls.Single().PersonaId, setup.Selector.Calls.Single().ShiftKey?.ToString(), setup.Selector.Calls.Single().Spend));

        [Fact]
        public void TheNoteIsTheQuirksBlockTheLegacyPromptBuilds() => Assert.Equal(legacyNote, shaping.PromptNotes.Single());

        [Fact]
        public void ASpentTaglineIsOnTheSkipList() => Assert.Contains(LlmPromptBuilder.SaidLatelyPrefix + "\"stay golden friends\"", shaping.PromptNotes.Single());

        [Fact]
        public void TheMemoIsTheOneItsJudgeReads() =>
            Assert.Equal(
                (Plain, "you're welcome, stay golden friends"),
                (string.Join(", ", memo?.Offered ?? []), string.Join(", ", memo?.Unavailable ?? [])));
    }

    // A capped quirk the selector did not offer is on the memo as spent
    public sealed class ScenarioAnUnofferedCappedQuirk : IAsyncLifetime
    {
        private CatchphraseLimitsMemo? memo;

        public async Task InitializeAsync()
        {
            var selector = new RecordingQuirkSelector(new QuirkSelection([Plain], []));
            var rule = new CatchphraseLimitsRule(new RepetitionCheck(), selector, new TaglineLedger(), new OneCardSource(Card));
            memo = (await rule.ShapeAsync(Before(), CancellationToken.None))?.Memo?.Deserialize<CatchphraseLimitsMemo>(TalkSnapshotJson.Options);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItIsOnTheMemoAsSpent() => Assert.Equal(Capped, string.Join(", ", memo?.Spent ?? []));
    }

    // A preview picks without spending and weighs no taglines
    public sealed class ScenarioAPreviewIsShaped : IAsyncLifetime
    {
        private readonly (TalkRuleBook Book, CatchphraseLimitsRule Rule, RecordingQuirkSelector Selector) setup =
            CatchphraseBook(new InMemoryTalkRuleSwitches());
        private TalkShaping shaping = new(TalkList<string>.Empty, TalkMemos.Empty, TalkList<string>.Empty);

        public async Task InitializeAsync() =>
            shaping = await setup.Book.ShapeAsync(Before(true, TalkRecent("stay golden friends.", 5)), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NothingIsSpent() => Assert.False(setup.Selector.Calls.Single().Spend);

        [Fact]
        public void NoSkipListIsOffered() => Assert.DoesNotContain(LlmPromptBuilder.SaidLatelyPrefix, shaping.PromptNotes.Single());
    }

    // SPEC F220.10 / F220.15 — a switched-off shaper adds nothing and keeps no state
    public sealed class ScenarioCatchphraseLimitsIsSwitchedOff : IAsyncLifetime
    {
        private readonly (TalkRuleBook Book, CatchphraseLimitsRule Rule, RecordingQuirkSelector Selector) setup;
        private TalkShaping shaping = new(TalkList<string>.Empty, TalkMemos.Empty, TalkList<string>.Empty);

        public ScenarioCatchphraseLimitsIsSwitchedOff()
        {
            var switches = new InMemoryTalkRuleSwitches();
            switches.SetAsync(CatchphraseLimitsMemo.RuleId, TalkKinds.DjLine, false, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            setup = CatchphraseBook(switches);
        }

        public async Task InitializeAsync() => shaping = await setup.Book.ShapeAsync(Before(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoQuirkIsPickedOrSpent() => Assert.Empty(setup.Selector.Calls);

        [Fact]
        public void NoNoteIsOffered() => Assert.Empty(shaping.PromptNotes);

        [Fact]
        public void NoMemoIsLeft() => Assert.Null(TalkSnapshots.DjLine(string.Empty, shaping.Memos).MemoOf(setup.Rule));
    }

    // A shaper that throws on the selector costs the break its note and nothing else
    public sealed class ScenarioTheSelectorThrows : IAsyncLifetime
    {
        private TalkShaping shaping = new(TalkList<string>.Empty, TalkMemos.Empty, TalkList<string>.Empty);

        public async Task InitializeAsync()
        {
            var rule = new CatchphraseLimitsRule(new RepetitionCheck(), new ThrowingQuirkSelector(), new TaglineLedger(), new OneCardSource(Card));
            var book = new TalkRuleBook([rule], new InMemoryTalkRuleSwitches(), NullLogger<TalkRuleBook>.Instance);
            shaping = await book.ShapeAsync(Before(), CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheBookReportsIt() => Assert.Equal(CatchphraseLimitsMemo.RuleId, string.Join(", ", shaping.CouldNotShape));

        [Fact]
        public void TheOutcomeIsUnchangedAndNoNoteIsOffered() => Assert.Empty(shaping.PromptNotes);

        private sealed class ThrowingQuirkSelector : GenWave.Core.Abstractions.IQuirkSelector
        {
            public Task<QuirkSelection> SelectAsync(long? personaId, PersonaCard? card, ShiftKey? shiftKey, bool spend, CancellationToken ct) =>
                throw new InvalidOperationException("selector down");
        }
    }

    // repeat-phrases offers the recent-lines block
    public sealed class ScenarioRepeatPhrasesShapes : IAsyncLifetime
    {
        private static readonly RepeatPhrasesRule Rule = new(new RepetitionCheck());

        private static async Task<RuleShaping?> Shape(TalkSnapshot before) => await Rule.ShapeAsync(before, CancellationToken.None);

        private RuleShaping? newest;
        private RuleShaping? template;
        private RuleShaping? preview;
        private RuleShaping? none;
        private RuleShaping? chat;
        private string legacyBlock = string.Empty;

        public async Task InitializeAsync()
        {
            var recent = Enumerable.Range(1, 8).Select(i => TalkRecent($"Line {i}.", i)).Append(TalkRecent("A neighbor line.", 0, personaId: 2)).ToArray();
            newest = await Shape(Before(false, recent));
            legacyBlock = string.Join('\n', LlmPromptBuilder.BuildRecentLinesBlock(["Line 1.", "Line 2.", "Line 3.", "Line 4.", "Line 5.", "Line 6."]));
            template = await Shape(Before(false, TalkRecent("A stand-in line.", 1, fallback: true)));
            preview = await Shape(Before(true, TalkRecent("Earlier.", 1)));
            none = await Shape(Before());
            chat = await Shape(TalkSnapshots.Chat("Hello there.", "Hi.") with { RecentLines = new TalkList<TalkRecentLine>([TalkRecent("Earlier.", 1)]) });
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheNoteIsTheSpeakersNewestLinesAsTheLegacyPromptBuildsThem() => Assert.Equal(legacyBlock, newest?.PromptNote);

        [Fact]
        public void ATemplateLineIsListedAsToday() => Assert.Contains("- A stand-in line.", template?.PromptNote);

        [Fact]
        public void APreviewGetsTheBlockToo() => Assert.NotNull(preview);

        [Fact]
        public void NoRecentLinesMeansNoNote() => Assert.Null(none);

        [Fact]
        public void AChatGetsNoNote() => Assert.Null(chat);

        [Fact]
        public void ItLeavesNoMemo() => Assert.Null(newest?.Memo);
    }
}

// STORY-570 — One flow for flagged talk (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. T800 activated the DJ-line facts; T801 activated the chat fact (AC4) and the chat facts live in Story570_OneFlowForFlaggedChat.cs.

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Core.TalkRules;
using GenWave.Memory;
using GenWave.TalkRules;
using GenWave.Tts.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

public static class FeatureOneFlowForFlaggedTalk
{
    const string First = "Coming up next a classic from the vault, a slow one for the long way home.";
    const string Second = "Here is something warm to keep the evening moving along nicely tonight.";

    static RuleFlag? Never(TalkSnapshot snapshot) => null;

    static RuleFlag? FirstAttemptOnly(string ask, TalkSnapshot snapshot) =>
        snapshot.Attempt == 1 ? new RuleFlag("flagged", ask, "evidence") : null;

    static RuleFlag? Always(string ask, TalkSnapshot snapshot) => new("flagged", ask, "evidence");

    // AC1 — mode rules, a clean reply
    public sealed class ScenarioACleanReply : IAsyncLifetime
    {
        readonly ScriptedJudgingRule judge = new("a-rule", "Alpha rule", Never);
        readonly ScriptedShapingRule recorder = new("z-recorder", "Zeta recorder");
        RulesSpecHarness harness = null!;
        SegmentCopy copy = null!;

        public async Task InitializeAsync()
        {
            harness = new RulesSpecHarness([First], [judge, recorder]);
            copy = await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheAiredTextEqualsTheCleanedText() => Assert.Equal(First, copy.Text);

        [Fact]
        public void TheModelIsAskedOnce() => Assert.Single(harness.UserPrompts);

        [Fact]
        public void OneCheckRowIsWrittenInRulesMode() => Assert.Equal(TalkCheckMode.Rules, Assert.Single(harness.CheckLog.Records).Mode);

        [Fact]
        public void TheRowIsAccepted() => Assert.Equal(TalkOutcome.Accepted, Assert.Single(harness.CheckLog.Records).Outcome);

        [Fact]
        public void NoRuleFlaggedEventIsPublished() => Assert.Empty(harness.Events.Events.OfType<RuleFlagged>());

        [Fact]
        public void TheRecordingRuleIsToldOnce() => Assert.Single(recorder.Accepted);

        [Fact]
        public void TheAcceptedSnapshotCarriesTheUtteranceId() => Assert.Equal(1L, Assert.Single(recorder.Accepted).UtteranceId);

        [Fact]
        public void NoRepeatCaughtEventIsPublished() => Assert.Empty(harness.Events.Events.OfType<RepeatCaught>());
    }

    // AC2 — a reply two rules flag (registered out of id order: the asks must still sort by id)
    public sealed class ScenarioAReplyTwoRulesFlag : IAsyncLifetime
    {
        const string AskA = "Ask from the a rule.";
        const string AskB = "Ask from the b rule.";
        RulesSpecHarness harness = null!;
        SegmentCopy copy = null!;

        public async Task InitializeAsync()
        {
            harness = new RulesSpecHarness(
                [First, Second],
                [new ScriptedJudgingRule("b-rule", "Bravo rule", s => FirstAttemptOnly(AskB, s)),
                 new ScriptedJudgingRule("a-rule", "Alpha rule", s => FirstAttemptOnly(AskA, s))]);
            copy = await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ExactlyOneMoreLlmCallIsMade() => Assert.Equal(2, harness.UserPrompts.Count);

        [Fact]
        public void TheReAskListsBothAsks() =>
            Assert.Equal(
                LlmPromptBuilder.BuildRuleReAsk(harness.UserPrompts[0], [AskA, AskB]),
                harness.UserPrompts[1]);

        [Fact]
        public void TheAsksAreSortedByRuleId() =>
            Assert.True(harness.UserPrompts[1].IndexOf(AskA, StringComparison.Ordinal) < harness.UserPrompts[1].IndexOf(AskB, StringComparison.Ordinal));

        [Fact]
        public void TheReAskStartsWithTheOriginalPrompt() =>
            Assert.StartsWith(harness.UserPrompts[0], harness.UserPrompts[1], StringComparison.Ordinal);

        [Fact]
        public void TheReAskNamesTheFix() =>
            Assert.Contains("Rewrite it. Fix all of these:", harness.UserPrompts[1], StringComparison.Ordinal);

        [Fact]
        public void TheSecondReplyAirs() => Assert.Equal(Second, copy.Text);

        [Fact]
        public void EveryAttemptWritesARulesRow() =>
            Assert.Equal([TalkCheckMode.Rules, TalkCheckMode.Rules], harness.CheckLog.Records.Select(r => r.Mode));

        [Fact]
        public void TheFirstRowIsReAskedAndTheSecondAccepted() =>
            Assert.Equal([TalkOutcome.ReAsked, TalkOutcome.Accepted], harness.CheckLog.Records.Select(r => r.Outcome));

        [Fact]
        public void TheFlaggedCallIsCountedUnderTheRuleFlaggedCause() =>
            Assert.Contains(harness.Counters.Snapshot(), c => c.Cause == LlmCallCause.RuleFlagged);
    }

    // AC3 — a DJ line still flagged after the re-ask
    public sealed class ScenarioADjLineIsStillFlagged : IAsyncLifetime
    {
        RulesSpecHarness harness = null!;

        public async Task InitializeAsync()
        {
            harness = new RulesSpecHarness(
                [First, Second], [new ScriptedJudgingRule("a-rule", "Alpha rule", s => Always("Ask.", s))]);
            await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TemplateCopyAirs() => Assert.True(Assert.Single(harness.Utterances.Recorded).Fallback);

        [Fact]
        public void OnlyOneReAskIsMade() => Assert.Equal(2, harness.UserPrompts.Count);

        [Fact]
        public void BothAttemptsAreRecorded() => Assert.Equal(2, harness.CheckLog.Records.Count);

        [Fact]
        public void TheSecondRowIsTemplate() => Assert.Equal(TalkOutcome.Template, harness.CheckLog.Records[1].Outcome);
    }

    public sealed class ScenarioASignOffIsStillFlagged : IAsyncLifetime
    {
        RulesSpecHarness harness = null!;

        public async Task InitializeAsync()
        {
            harness = new RulesSpecHarness(
                [First, Second], [new ScriptedJudgingRule("a-rule", "Alpha rule", s => Always("Ask.", s))], kind: SegmentKind.SignOff);
            await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheTalkIsDroppedWhereTemplateCopyNeverAirs() => Assert.Empty(harness.Utterances.Recorded);

        [Fact]
        public void TheSecondRowIsDropped() => Assert.Equal(TalkOutcome.Dropped, harness.CheckLog.Records[1].Outcome);
    }

    // AC4 — a chat still flagged after the re-ask
    public sealed class ScenarioAChatIsStillFlagged : IAsyncLifetime
    {
        ChatRulesSpecHarness harness = null!;
        CrosstalkWriteResult result = null!;

        public async Task InitializeAsync()
        {
            harness = new ChatRulesSpecHarness(
                [ChatRulesSpecHarness.Clean, ChatRulesSpecHarness.CleanAgain],
                [new ScriptedJudgingRule("a-rule", "Alpha rule", s => Always("Ask.", s), TalkKinds.Chat)]);
            result = await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheChatIsDropped() => Assert.IsType<CrosstalkWriteResult.Discarded>(result);

        [Fact]
        public void OnlyOneReAskWasMade() => Assert.Equal(2, harness.UserPrompts.Count);
    }

    // AC7 — the golden prompts after the shapers moved: the prompts the writer really posts in rules mode are the rules form
    public sealed class ScenarioTheGoldenPromptsAfterTheMove : IAsyncLifetime
    {
        IReadOnlyList<GoldenPrompt> composed = [];
        IReadOnlyList<GoldenPrompt> posted = [];

        public async Task InitializeAsync()
        {
            composed = await GoldenPromptFixtures.DjLinesInRulesFormAsync();
            posted = await GoldenPromptFixtures.DjLinesFromTheRulesWriterAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheOnlyDifferenceIsTextMovedUnderForThisBreak() =>
            Assert.Equal(
                "",
                string.Join(", ", composed.Where(c => posted.Single(p => p.Name == c.Name) is var p && (p.System != c.System || p.User != c.User)).Select(c => c.Name)));

        [Fact]
        public void EveryFixtureIsPosted() => Assert.Equal(composed.Select(c => c.Name), posted.Select(p => p.Name));
    }

    // AC7 note — a quirk card with no soul whose selection comes back empty: the persona opening, nothing after it.
    // The real catchphrase-limits rule runs over a selector that offers nothing; the exact rules-form system prompt is pinned
    // as Golden/rules-quirk-card-no-soul-empty-selection.system.txt (a new file; the legacy goldens are untouched).
    public sealed class ScenarioAQuirkCardWithNoSoulAndAnEmptySelection : IAsyncLifetime
    {
        string system = "";

        sealed class EmptySelector : IQuirkSelector
        {
            public Task<QuirkSelection> SelectAsync(
                long? personaId, PersonaCard? card, ShiftKey? shiftKey, bool spend, CancellationToken ct) =>
                Task.FromResult(QuirkSelection.None);
        }

        public async Task InitializeAsync()
        {
            var card = QuirkCardWithNoSoul();
            var cards = new FakePersonaCardByIdSource();
            cards.Add(QuirkSpecHarness.PersonaId, card);
            var rule = new CatchphraseLimitsRule(new RepetitionCheck(), new EmptySelector(), new TaglineLedger(), cards);
            var harness = new RulesSpecHarness([First], [rule], card, time: new FakeTimeProvider(GoldenPromptFixtures.Now));
            await harness.WriteAsync();
            system = harness.SystemPrompts[0];
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ThePersonaOpeningIsUsed() =>
            Assert.StartsWith("You are a radio DJ writing live station patter - write every word in the voice", system, StringComparison.Ordinal);

        [Fact]
        public void NoQuirksLineFollows() => Assert.DoesNotContain("Quirks: ", system, StringComparison.Ordinal);

        [Fact]
        public void TheExactRulesFormSystemPromptIsPinned() =>
            Assert.Empty(GoldenFiles.Mismatches([("rules-quirk-card-no-soul-empty-selection.system.txt", system)]));
    }

    static PersonaCard QuirkCardWithNoSoul() =>
        new(PersonaCard.CurrentSchemaVersion, "DJ Nova", "", "", ["Compare every record to a sandwich"],
            new VoiceSpec("kokoro", "af_heart", 1.0, "en"), 0.0, [], []);

    // Dean 11-25: allowed - a quirk card with no soul whose selection comes back empty may be offered recent lines in rules mode
    public sealed class ScenarioAQuirkCardWithNoSoulAndAnEmptySelectionHasRecentLines : IAsyncLifetime
    {
        readonly ScriptedShapingRule shaper = new("b-shaper", "Bravo shaper");

        public async Task InitializeAsync()
        {
            var harness = new RulesSpecHarness([First], [shaper], QuirkCardWithNoSoul());
            harness.Utterances.Seeded.Add(
                new UtteranceEntry(1, QuirkSpecHarness.PersonaId, null, UtteranceKind.Patter, "LeadIn",
                    ShiftKey.Unscheduled(QuirkSpecHarness.PersonaId, new DateOnly(2026, 11, 16)),
                    "An earlier line the DJ aired.", false, DateTimeOffset.UtcNow.AddMinutes(-5), null));
            await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheShapersAreOfferedTheRecentLines() => Assert.NotEmpty(shaper.Shaped[0].RecentLines);
    }

    // AC8 — a flag that changes the outcome
    public sealed class ScenarioAFlagChangesTheOutcome : IAsyncLifetime
    {
        RulesSpecHarness harness = null!;

        public async Task InitializeAsync()
        {
            harness = new RulesSpecHarness(
                [First, Second], [new ScriptedJudgingRule("a-rule", "Alpha rule", s => FirstAttemptOnly("Ask.", s))]);
            await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        RuleFlagged Flagged => Assert.Single(harness.Events.Events.OfType<RuleFlagged>());

        [Fact]
        public void ARuleFlaggedRowNamesTheRule() => Assert.Equal("Alpha rule", Flagged.RuleName);

        [Fact]
        public void TheRowNamesTheDj() => Assert.Equal("DJ Nova", Flagged.Speaker);

        [Fact]
        public void TheRowNamesTheOutcome() => Assert.Equal(RuleFlagOutcome.ReAsked, Flagged.Outcome);

        [Fact]
        public void TheRowBelongsToThePersona() => Assert.Equal(QuirkSpecHarness.PersonaId, Flagged.PersonaId);
    }

    public sealed class ScenarioAFlagEndsInTemplate : IAsyncLifetime
    {
        RulesSpecHarness harness = null!;

        public async Task InitializeAsync()
        {
            harness = new RulesSpecHarness(
                [First, Second], [new ScriptedJudgingRule("a-rule", "Alpha rule", s => Always("Ask.", s))]);
            await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ARowIsWrittenForTheReAskAndOneForTheTemplate() =>
            Assert.Equal([RuleFlagOutcome.ReAsked, RuleFlagOutcome.Template], harness.Events.Events.OfType<RuleFlagged>().Select(f => f.Outcome));
    }

    public sealed class ScenarioAFlagEndsInADrop : IAsyncLifetime
    {
        RulesSpecHarness harness = null!;

        public async Task InitializeAsync()
        {
            harness = new RulesSpecHarness(
                [First, Second], [new ScriptedJudgingRule("a-rule", "Alpha rule", s => Always("Ask.", s))], kind: SegmentKind.SignOff);
            await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheLastRowSaysDropped() =>
            Assert.Equal(RuleFlagOutcome.Dropped, harness.Events.Events.OfType<RuleFlagged>().Last().Outcome);
    }

    // AC9 — a callback offered and the talk rejected
    public sealed class ScenarioACallbackOnRejectedTalk : IAsyncLifetime
    {
        readonly RecordingCallbackStore store = new("has a dog named Daisy");
        readonly ScriptedShapingRule recorder = new("z-recorder", "Zeta recorder");

        public async Task InitializeAsync()
        {
            var harness = new RulesSpecHarness(
                [First, Second],
                [new ScriptedJudgingRule("a-rule", "Alpha rule", s => Always("Ask.", s)), recorder, CallbacksRuleFor(store)]);
            await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheCallbackWasOffered() => Assert.Single(store.Offers);

        [Fact]
        public void TheCallbackIsNotSettled() => Assert.False(Assert.Single(store.Offers).Settled);

        [Fact]
        public void NoRecordingRuleIsTold() => Assert.Empty(recorder.Accepted);
    }

    // AC9 happy path: an accepted callback is settled through the recording rule, with the utterance id
    public sealed class ScenarioACallbackOnAcceptedTalk : IAsyncLifetime
    {
        readonly RecordingCallbackStore store = new("has a dog named Daisy");

        public async Task InitializeAsync()
        {
            var harness = new RulesSpecHarness([First], [CallbacksRuleFor(store)]);
            await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheCallbackIsSettledOnTheUtterance() => Assert.Equal(1L, Assert.Single(store.Offers).UtteranceId);
    }

    // Sad path
    // AC10 — an empty reply
    public sealed class ScenarioAnEmptyReply : IAsyncLifetime
    {
        readonly ScriptedJudgingRule judge = new("a-rule", "Alpha rule", s => Always("Ask.", s));
        RulesSpecHarness harness = null!;

        public async Task InitializeAsync()
        {
            harness = new RulesSpecHarness(["", Second], [judge]);
            await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoRuleRuns() => Assert.Empty(judge.Judged);

        [Fact]
        public void NoReAskIsMade() => Assert.Single(harness.UserPrompts);

        [Fact]
        public void TheAttemptIsStillRecorded() => Assert.Single(harness.CheckLog.Records);
    }

    // Sad path
    // AC12 — a reply with one repeated sentence
    public sealed class ScenarioARepeatedSentence : IAsyncLifetime
    {
        const string Echo = "Here is a slow one that tastes a lot like a grilled cheese. Next up Astral Plane.";
        RulesSpecHarness harness = null!;
        SegmentCopy copy = null!;

        public async Task InitializeAsync()
        {
            harness = new RulesSpecHarness(
                [Echo, Second], [new RepeatPhrasesRule(new RepetitionCheck())]);
            harness.Utterances.Seeded.Add(new UtteranceEntry(
                1, QuirkSpecHarness.PersonaId, null, UtteranceKind.Patter, "LeadIn",
                ShiftKey.Unscheduled(QuirkSpecHarness.PersonaId, new DateOnly(2026, 11, 16)),
                "Here is a slow one that tastes a lot like a grilled cheese.", false, DateTimeOffset.UtcNow.AddMinutes(-5), null));
            copy = await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSentenceIsNotCut() =>
            Assert.Contains("Here is a slow one that tastes a lot like a grilled cheese.", harness.CheckLog.Records[0].Cleaned, StringComparison.Ordinal);

        [Fact]
        public void TheReplyIsReAsked() => Assert.Equal(2, harness.UserPrompts.Count);

        [Fact]
        public void TheCleanSecondReplyAirs() => Assert.Equal(Second, copy.Text);

        [Fact]
        public void NoRepeatCaughtEventIsPublished() => Assert.Empty(harness.Events.Events.OfType<RepeatCaught>());
    }

    // F220.9 — a rule that breaks never stops the talk
    public sealed class ScenarioAShaperThrows : IAsyncLifetime
    {
        readonly ScriptedShapingRule shaper = new("b-shaper", "Bravo shaper", shapeThrows: true);
        RulesSpecHarness harness = null!;
        SegmentCopy copy = null!;
        RuleBroken broken = null!;

        public async Task InitializeAsync()
        {
            harness = new RulesSpecHarness([First], [shaper]);
            copy = await harness.WriteAsync();
            broken = harness.Events.Events.OfType<RuleBroken>().Single();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheLineStillAirs() => Assert.Equal(First, copy.Text);

        [Fact]
        public void AWarningNamesTheRule() =>
            Assert.Contains(harness.RuleBookLog.Warnings, w => w.Contains("b-shaper", StringComparison.Ordinal));

        [Fact]
        public void NoReAskIsMade() => Assert.Single(harness.UserPrompts);

        [Fact]
        public void TheVerdictRecordsTheBreakage() =>
            Assert.Equal(TalkRuleReasons.CouldNotShape, harness.CheckLog.Records[0].Verdicts.Single(v => v.RuleId == "b-shaper").Broken);

        [Fact]
        public void TheBreakageIsNotAFlag() =>
            Assert.Null(harness.CheckLog.Records[0].Verdicts.Single(v => v.RuleId == "b-shaper").Flag);

        // T804a — the booth hears of it, once, naming the rule and the DJ
        [Fact]
        public void OneRuleBrokenEventSaysShape() =>
            Assert.Equal(RuleBrokenStep.Shape, Assert.Single(harness.Events.Events.OfType<RuleBroken>()).Step);

        [Fact]
        public void TheBrokenEventNamesTheRule() => Assert.Equal("Bravo shaper", broken.RuleName);

        [Fact]
        public void TheBrokenEventNamesTheDj() => Assert.Equal("DJ Nova", broken.Speaker);

        [Fact]
        public void TheBrokenEventSaysLine() => Assert.Equal(RuleFlagTalk.Line, broken.Talk);
    }

    // T804a (F220.9) — the shaper runs once per request, so a flagged-then-accepted line is one breakage, not one per attempt
    public sealed class ScenarioAShaperThrowsAndTheLineIsReAsked : IAsyncLifetime
    {
        RulesSpecHarness harness = null!;

        public async Task InitializeAsync()
        {
            harness = new RulesSpecHarness(
                [First, Second],
                [new ScriptedShapingRule("b-shaper", "Bravo shaper", shapeThrows: true), new ScriptedJudgingRule("a-rule", "Alpha rule", s => FirstAttemptOnly("Ask.", s))]);
            await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheLineWasReAsked() => Assert.Equal(2, harness.UserPrompts.Count);

        [Fact]
        public void TheBoothHearsOfTheBreakageOnce() => Assert.Single(harness.Events.Events.OfType<RuleBroken>());
    }

    public sealed class ScenarioARecorderThrows : IAsyncLifetime
    {
        readonly ScriptedShapingRule recorder = new("b-recorder", "Bravo recorder", recordThrows: true);
        RulesSpecHarness harness = null!;
        SegmentCopy copy = null!;

        public async Task InitializeAsync()
        {
            harness = new RulesSpecHarness([First], [recorder]);
            copy = await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheAcceptedLineStaysAccepted() => Assert.Equal(First, copy.Text);

        [Fact]
        public void AWarningNamesTheRule() =>
            Assert.Contains(harness.RuleBookLog.Warnings, w => w.Contains("b-recorder", StringComparison.Ordinal));

        [Fact]
        public void TheStoredRowCarriesCouldNotRecordForThatRule() =>
            Assert.Equal(
                TalkRuleReasons.CouldNotRecord,
                Assert.Single(harness.CheckLog.Records).Verdicts.Single(v => v.RuleId == "b-recorder").Broken);

        [Fact]
        public void TheStoredRowIsStillAccepted() => Assert.Equal(TalkOutcome.Accepted, Assert.Single(harness.CheckLog.Records).Outcome);

        // T804a — the booth hears of it, once
        [Fact]
        public void OneRuleBrokenEventSaysRecord() =>
            Assert.Equal(RuleBrokenStep.Record, Assert.Single(harness.Events.Events.OfType<RuleBroken>()).Step);
    }

    // T800 — an accepted announcement has no utterance, but the recording rules still hear of it (F220.13)
    public sealed class ScenarioAnAcceptedAnnouncement : IAsyncLifetime
    {
        readonly ScriptedShapingRule recorder = new("z-recorder", "Zeta recorder");
        RulesSpecHarness harness = null!;
        string? aired;

        public async Task InitializeAsync()
        {
            harness = new RulesSpecHarness([First], [recorder], kind: SegmentKind.Announcement);
            aired = await harness.Writer.WriteAnnouncementAsync(harness.Request, "The station is back after a short break.", CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheAnnouncementAirsTheModelsLine() => Assert.Equal(First, aired);

        [Fact]
        public void TheRecordingRuleIsToldOnce() => Assert.Single(recorder.Accepted);

        [Fact]
        public void TheAcceptedSnapshotHasNoUtteranceId() => Assert.Null(Assert.Single(recorder.Accepted).UtteranceId);
    }

    // T800 — the mode is read once per talk, however many places use it
    public sealed class ScenarioTheModeIsReadOnce : IAsyncLifetime
    {
        sealed class CountingMonitor : IOptionsMonitor<TalkRulesOptions>
        {
            readonly TalkRulesOptions options = new() { Mode = "rules" };

            public int Reads { get; private set; }

            public TalkRulesOptions CurrentValue
            {
                get
                {
                    Reads++;
                    return options;
                }
            }

            public TalkRulesOptions Get(string? name) => options;

            public IDisposable? OnChange(Action<TalkRulesOptions, string?> listener) => null;
        }

        readonly CountingMonitor monitor = new();

        public async Task InitializeAsync() =>
            await new RulesSpecHarness([First], [new ScriptedJudgingRule("a-rule", "Alpha rule", Never)], modeMonitor: monitor).WriteAsync();

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheLiveModeIsReadOnceForTheTalk() => Assert.Equal(1, monitor.Reads);
    }

    // T798 notes — the taste notes and the extra-line flag reach the snapshots the rules see
    public sealed class ScenarioATasteBreakFollowsAnEarlierOne : IAsyncLifetime
    {
        readonly ScriptedJudgingRule judge = new("a-rule", "Alpha rule", Never);
        readonly ScriptedShapingRule shaper = new("b-shaper", "Bravo shaper");

        public async Task InitializeAsync()
        {
            var rule = new TasteRule(new TastePredicate("Valerie June", null, null), new TasteContext([], null, null), 0.8);
            var request = QuirkSpecHarness.Request(ShiftKey.Unscheduled(QuirkSpecHarness.PersonaId, new DateOnly(2026, 11, 16)).ToString()) with
            {
                Track = new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June")
                {
                    PersonaPick = new PersonaPickDiagnostics(12, [0.9, 0.7], [rule], IsExploration: false),
                },
                CrosstalkAiredThisBreak = true,
            };
            var harness = new RulesSpecHarness([First], [judge, shaper], request: request);
            await harness.WriteAsync(); // the first break
            await harness.WriteAsync(); // the second, with the first one's taste notes remembered
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheFirstBreakHasTasteNotes() => Assert.NotEmpty(shaper.Shaped[0].TasteNotes ?? TalkList<string>.Empty);

        [Fact]
        public void TheFirstBreakHasNoPreviousTasteNotes() => Assert.Null(shaper.Shaped[0].PreviousTasteNotes);

        [Fact]
        public void TheSecondBreakCarriesTheFirstOnes() => Assert.Equal(shaper.Shaped[0].TasteNotes, shaper.Shaped[1].PreviousTasteNotes);

        [Fact]
        public void TheExtraLineIsTakenWhenCrosstalkAired() => Assert.True(shaper.Shaped[0].ExtraLineTaken);

        [Fact]
        public void TheJudgedSnapshotCarriesTheSameNotes() => Assert.Equal(shaper.Shaped[0].TasteNotes, judge.Judged[0].TasteNotes);
    }

    public sealed class ScenarioABreakWithNoPickAndNoExtraLine : IAsyncLifetime
    {
        readonly ScriptedShapingRule shaper = new("b-shaper", "Bravo shaper");

        public async Task InitializeAsync()
        {
            var harness = new RulesSpecHarness([First], [shaper]);
            await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoTasteNotes() => Assert.Null(shaper.Shaped[0].TasteNotes);

        [Fact]
        public void NoExtraLine() => Assert.False(shaper.Shaped[0].ExtraLineTaken);
    }

    // Dean 11-25 — recent lines only where legacy offers them
    public sealed class ScenarioABareDjHasRecentLines : IAsyncLifetime
    {
        readonly ScriptedShapingRule shaper = new("b-shaper", "Bravo shaper");
        readonly ScriptedShapingRule bareShaper = new("b-shaper", "Bravo shaper");

        public async Task InitializeAsync()
        {
            foreach (var (card, rule) in new[] { (RulesSpecHarness.BareCard(), bareShaper), (QuirkSpecHarness.Card([], null), shaper) })
            {
                var harness = new RulesSpecHarness([First], [rule], card);
                harness.Utterances.Seeded.Add(Recent());
                await harness.WriteAsync();
            }
        }

        public Task DisposeAsync() => Task.CompletedTask;

        static UtteranceEntry Recent() =>
            new(1, QuirkSpecHarness.PersonaId, null, UtteranceKind.Patter, "LeadIn",
                ShiftKey.Unscheduled(QuirkSpecHarness.PersonaId, new DateOnly(2026, 11, 16)),
                "An earlier line the DJ aired.", false, DateTimeOffset.UtcNow.AddMinutes(-5), null);

        [Fact]
        public void ACardWithASoulIsOfferedThem() => Assert.NotEmpty(shaper.Shaped[0].RecentLines);

        [Fact]
        public void ABareDjIsOfferedNone() => Assert.Empty(bareShaper.Shaped[0].RecentLines);
    }

    // T797 note — memory recall is gated by HasPersonaSection(persona, card)
    public sealed class ScenarioMemoryRecallForABareDj : IAsyncLifetime
    {
        readonly CountingMemoryRecall recall = new();

        public async Task InitializeAsync()
        {
            var bare = RulesSpecHarness.BareCard();
            var cards = new FakePersonaCardByIdSource();
            cards.Add(QuirkSpecHarness.PersonaId, bare);
            var gate = new MemoryBreakGate(new FakePersonaRoster("DJ Nova"), cards, new FixedContextWindow(MemoryBudget.Standard));
            var harness = new RulesSpecHarness([First], [new NeverMentionRule(recall, gate)], bare);
            await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NothingIsRecalled() => Assert.Equal(0, recall.Calls);
    }

    public sealed class ScenarioMemoryRecallForACardWithASoul : IAsyncLifetime
    {
        readonly CountingMemoryRecall recall = new();

        public async Task InitializeAsync()
        {
            var card = QuirkSpecHarness.Card([], null);
            var cards = new FakePersonaCardByIdSource();
            cards.Add(QuirkSpecHarness.PersonaId, card);
            var gate = new MemoryBreakGate(new FakePersonaRoster("DJ Nova"), cards, new FixedContextWindow(MemoryBudget.Standard));
            var harness = new RulesSpecHarness([First], [new NeverMentionRule(recall, gate)], card);
            await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheMemoryIsRecalled() => Assert.Equal(1, recall.Calls);
    }

    // Legacy stays legacy: the same writer in legacy mode never runs a rule
    // F220.29 — a preview never airs, yet every written attempt is kept: its accepted row is written, no recorder is told
    public sealed class ScenarioAPreviewWithACleanReply : IAsyncLifetime
    {
        readonly ScriptedShapingRule recorder = new("z-recorder", "Zeta recorder");
        RulesSpecHarness harness = null!;

        public async Task InitializeAsync()
        {
            harness = new RulesSpecHarness([First], [new ScriptedJudgingRule("a-rule", "Alpha rule", Never), recorder]);
            await harness.Writer.WritePreviewAsync(harness.Request, personaOverride: null, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void OneCheckRowIsWritten() => Assert.Single(harness.CheckLog.Records);

        [Fact]
        public void TheRowIsAccepted() => Assert.Equal(TalkOutcome.Accepted, Assert.Single(harness.CheckLog.Records).Outcome);

        [Fact]
        public void TheRowIsInRulesMode() => Assert.Equal(TalkCheckMode.Rules, Assert.Single(harness.CheckLog.Records).Mode);

        [Fact]
        public void TheRecordingRuleIsNotTold() => Assert.Empty(recorder.Accepted);
    }

    public sealed class ScenarioAPreviewFlaggedThenClean : IAsyncLifetime
    {
        RulesSpecHarness harness = null!;

        public async Task InitializeAsync()
        {
            harness = new RulesSpecHarness(
                [First, Second], [new ScriptedJudgingRule("a-rule", "Alpha rule", s => FirstAttemptOnly("Ask.", s))]);
            await harness.Writer.WritePreviewAsync(harness.Request, personaOverride: null, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TwoCheckRowsAreWritten() => Assert.Equal(2, harness.CheckLog.Records.Count);

        [Fact]
        public void TheFirstRowIsReAsked() => Assert.Equal(TalkOutcome.ReAsked, harness.CheckLog.Records[0].Outcome);

        [Fact]
        public void TheSecondRowIsAccepted() => Assert.Equal(TalkOutcome.Accepted, harness.CheckLog.Records[1].Outcome);
    }

    public sealed class ScenarioLegacyModeWithTheFlowWired : IAsyncLifetime
    {
        readonly ScriptedJudgingRule judge = new("a-rule", "Alpha rule", s => Always("Ask.", s));
        RulesSpecHarness harness = null!;

        public async Task InitializeAsync()
        {
            harness = new RulesSpecHarness([First], [judge], mode: "legacy");
            await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoRuleRuns() => Assert.Empty(judge.Judged);

        [Fact]
        public void NoCheckRowIsWritten() => Assert.Empty(harness.CheckLog.Records);
    }

    static CallbacksRule CallbacksRuleFor(RecordingCallbackStore store)
    {
        var card = QuirkSpecHarness.Card([], null);
        var cards = new FakePersonaCardByIdSource();
        cards.Add(QuirkSpecHarness.PersonaId, card);
        var roster = new FakePersonaRoster("DJ Nova", "Rusty Strings");
        var gate = new MemoryBreakGate(roster, cards, new FixedContextWindow(MemoryBudget.Standard));
        var callbacks = new BreakCallbacks(
            new CallbackPlanner(store, new AlwaysPassDraw()), new FixedCallbackRate(6), roster, NullLogger<BreakCallbacks>.Instance);
        return new CallbacksRule(callbacks, gate);
    }

    sealed class AlwaysPassDraw : Random
    {
        public override double NextDouble() => 0.0;
    }

    sealed class FixedCallbackRate(int perHour) : ICallbackRate
    {
        public int CallbacksPerHour => perHour;
    }

    sealed class CountingMemoryRecall : IMemoryRecallSource
    {
        public int Calls { get; private set; }

        public Task<MemoryPacket> NeverMentionAsync(long personaId, MemoryBudget budget, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(MemoryPacket.Empty);
        }
    }
}

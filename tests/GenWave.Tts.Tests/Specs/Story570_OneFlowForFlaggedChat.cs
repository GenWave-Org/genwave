// STORY-570 — One flow for flagged talk, the two-DJ chat (SPEC F220.9–.17, .29 · PLAN T801)
//
// Entry point: CrosstalkScriptWriter.WriteExchangeAsync in rules mode, with a scripted LLM, a real rule book of scripted rules
// that cover chat, and recording doubles for the check log and the station events.

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Core.TalkRules;
using GenWave.Memory;
using GenWave.Tts.Tests.Fakes;
using Microsoft.Extensions.Options;

public static class FeatureOneFlowForFlaggedChat
{
    const string Clean = ChatRulesSpecHarness.Clean;
    const string CleanAgain = ChatRulesSpecHarness.CleanAgain;

    static RuleFlag? Never(TalkSnapshot snapshot) => null;

    static RuleFlag? FirstAttemptOnly(string ask, TalkSnapshot snapshot) =>
        snapshot.Attempt == 1 ? new RuleFlag("flagged", ask, "evidence") : null;

    static ScriptedJudgingRule Judge(string id, string name, Func<TalkSnapshot, RuleFlag?> judge) => new(id, name, judge, TalkKinds.Chat);

    // AC1 — a clean chat
    public sealed class ScenarioACleanChat : IAsyncLifetime
    {
        readonly ScriptedJudgingRule judge = Judge("a-rule", "Alpha rule", Never);
        readonly ScriptedShapingRule recorder = new("z-recorder", "Zeta recorder", covers: TalkKinds.Chat);
        ChatRulesSpecHarness harness = null!;
        CrosstalkWriteResult result = null!;

        public async Task InitializeAsync()
        {
            harness = new ChatRulesSpecHarness([Clean], [judge, recorder]);
            result = await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheAiredTextEqualsTheCleanedText() => Assert.Equal(ChatRulesSpecHarness.CleanAired, ChatRulesSpecHarness.AiredOf(result));

        [Fact]
        public void TheModelIsAskedOnce() => Assert.Single(harness.UserPrompts);

        [Fact]
        public void OneRulesRowIsWritten() => Assert.Equal(TalkCheckMode.Rules, Assert.Single(harness.CheckLog.Records).Mode);

        [Fact]
        public void TheRowIsAccepted() => Assert.Equal(TalkOutcome.Accepted, Assert.Single(harness.CheckLog.Records).Outcome);

        [Fact]
        public void TheRowKeepsTheRawReply() => Assert.Equal(Clean, Assert.Single(harness.CheckLog.Records).RawReply);

        [Fact]
        public void TheRecorderIsToldOnce() => Assert.Single(recorder.Accepted);

        [Fact]
        public void TheAcceptedSnapshotIsAChat() => Assert.Equal(TalkKinds.Chat, Assert.Single(recorder.Accepted).Kind);

        [Fact]
        public void NoRuleFlaggedEventIsPublished() => Assert.Empty(harness.Events.Events.OfType<RuleFlagged>());

        [Fact]
        public void NoRepeatCaughtEventIsPublished() => Assert.Empty(harness.Events.Events.OfType<RepeatCaught>());
    }

    // AC2 — two rules flag the first reply (registered out of id order: the asks must still sort by id)
    public sealed class ScenarioAChatTwoRulesFlag : IAsyncLifetime
    {
        const string AskA = "Ask from the a rule.";
        const string AskB = "Ask from the b rule.";
        ChatRulesSpecHarness harness = null!;
        CrosstalkWriteResult result = null!;

        public async Task InitializeAsync()
        {
            harness = new ChatRulesSpecHarness(
                [Clean, CleanAgain],
                [Judge("b-rule", "Bravo rule", s => FirstAttemptOnly(AskB, s)), Judge("a-rule", "Alpha rule", s => FirstAttemptOnly(AskA, s))]);
            result = await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ExactlyOneMoreLlmCallIsMade() => Assert.Equal(2, harness.UserPrompts.Count);

        [Fact]
        public void TheReAskListsBothAsksSortedByRuleId() =>
            Assert.Equal(LlmPromptBuilder.BuildRuleReAsk(harness.UserPrompts[0], [AskA, AskB]), harness.UserPrompts[1]);

        [Fact]
        public void TheSecondReplyAirs() => Assert.Equal(
            "Welcome back, the records are warm tonight.\nAnd the lights are low, just right.\nSettle in, friends, we have all night.",
            ChatRulesSpecHarness.AiredOf(result));

        [Fact]
        public void EveryAttemptWritesARulesRow() =>
            Assert.Equal([TalkCheckMode.Rules, TalkCheckMode.Rules], harness.CheckLog.Records.Select(r => r.Mode));

        [Fact]
        public void TheRowsAreReAskedThenAccepted() =>
            Assert.Equal([TalkOutcome.ReAsked, TalkOutcome.Accepted], harness.CheckLog.Records.Select(r => r.Outcome));

        [Fact]
        public void TheFlaggedCallIsCountedUnderTheRuleFlaggedCause() =>
            Assert.Contains(harness.Counters.Snapshot(), c => c.Cause == LlmCallCause.RuleFlagged);
    }

    // F220.16 — the booth row names the rule, the host and the outcome
    public sealed class ScenarioTheBoothRowForAFlaggedChat : IAsyncLifetime
    {
        ChatRulesSpecHarness harness = null!;

        public async Task InitializeAsync()
        {
            harness = new ChatRulesSpecHarness(
                [Clean, CleanAgain], [Judge("a-rule", "Alpha rule", s => FirstAttemptOnly("Ask.", s))]);
            await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void OneRuleFlaggedEventIsPublished() => Assert.Single(harness.Events.Events.OfType<RuleFlagged>());

        [Fact]
        public void ItNamesTheRule() => Assert.Equal("Alpha rule", harness.Events.Events.OfType<RuleFlagged>().Single().RuleName);

        [Fact]
        public void ItNamesTheHost() => Assert.Equal("Nova", harness.Events.Events.OfType<RuleFlagged>().Single().Speaker);

        [Fact]
        public void ItCarriesTheHostsPersonaId() =>
            Assert.Equal(ChatRulesSpecHarness.HostId, harness.Events.Events.OfType<RuleFlagged>().Single().PersonaId);

        [Fact]
        public void ItSaysReAsked() => Assert.Equal(RuleFlagOutcome.ReAsked, harness.Events.Events.OfType<RuleFlagged>().Single().Outcome);

        [Fact]
        public void ItIsAboutAChat() => Assert.Equal(RuleFlagTalk.Chat, harness.Events.Events.OfType<RuleFlagged>().Single().Talk);
    }

    // F220.14 — the dropped outcome reaches the booth too
    public sealed class ScenarioTheBoothRowForADroppedChat : IAsyncLifetime
    {
        ChatRulesSpecHarness harness = null!;

        public async Task InitializeAsync()
        {
            harness = new ChatRulesSpecHarness(
                [Clean, CleanAgain], [Judge("a-rule", "Alpha rule", s => new RuleFlag("flagged", "Ask.", "evidence"))]);
            await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheOutcomesAreReAskedThenDropped() =>
            Assert.Equal([RuleFlagOutcome.ReAsked, RuleFlagOutcome.Dropped], harness.Events.Events.OfType<RuleFlagged>().Select(e => e.Outcome));

        [Fact]
        public void TheRowsAreReAskedThenDropped() =>
            Assert.Equal([TalkOutcome.ReAsked, TalkOutcome.Dropped], harness.CheckLog.Records.Select(r => r.Outcome));

        [Fact]
        public void NothingIsStoredAsSaid() => Assert.Empty(harness.Utterances.Recorded);
    }

    // F220.11 — a clean-up failure keeps today's path: no rule runs, no re-ask
    public sealed class ScenarioTheCleanUpFails : IAsyncLifetime
    {
        readonly ScriptedJudgingRule judge = Judge("a-rule", "Alpha rule", Never);
        ChatRulesSpecHarness harness = null!;
        CrosstalkWriteResult result = null!;

        public async Task InitializeAsync()
        {
            harness = new ChatRulesSpecHarness(["Just some words with no speaker tags.", Clean], [judge]);
            result = await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheChatIsDropped() => Assert.IsType<CrosstalkWriteResult.Discarded>(result);

        [Fact]
        public void NoRuleRuns() => Assert.Empty(judge.Judged);

        [Fact]
        public void NoReAskIsMade() => Assert.Single(harness.UserPrompts);

        [Fact]
        public void TheAttemptIsStillRecordedAsDropped() => Assert.Equal(TalkOutcome.Dropped, Assert.Single(harness.CheckLog.Records).Outcome);
    }

    // F220.14 — attempt 1 flagged, then attempt 2 fails the clean-up: the chat is dropped
    public sealed class ScenarioTheReAskFailsTheCleanUp : IAsyncLifetime
    {
        ChatRulesSpecHarness harness = null!;
        CrosstalkWriteResult result = null!;

        public async Task InitializeAsync()
        {
            harness = new ChatRulesSpecHarness(
                [Clean, "Just some words with no speaker tags."], [Judge("a-rule", "Alpha rule", s => FirstAttemptOnly("Ask.", s))]);
            result = await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheChatIsDiscarded() => Assert.IsType<CrosstalkWriteResult.Discarded>(result);

        [Fact]
        public void TheRowsAreReAskedThenDropped() =>
            Assert.Equal([TalkOutcome.ReAsked, TalkOutcome.Dropped], harness.CheckLog.Records.Select(r => r.Outcome));

        [Fact]
        public void OnlyTheReAskedBoothRowIsPublished() =>
            Assert.Equal([RuleFlagOutcome.ReAsked], harness.Events.Events.OfType<RuleFlagged>().Select(e => e.Outcome));
    }

    // F220.10 — a fault while shaping, before any prompt is composed, still leaves a real system prompt on the ring entry
    public sealed class ScenarioShapingFaultsBeforeThePromptIsComposed : IAsyncLifetime
    {
        sealed class ThrowingSnapshots : ITalkSnapshotSource<ChatSnapshotInput>
        {
            public ValueTask<TalkSnapshot> BuildAsync(ChatSnapshotInput input, CancellationToken ct) =>
                throw new InvalidOperationException("snapshot source faulted");
        }

        ChatRulesSpecHarness harness = null!;

        public async Task InitializeAsync()
        {
            harness = new ChatRulesSpecHarness([Clean], [Judge("a-rule", "Alpha rule", Never)], snapshots: new ThrowingSnapshots());
            await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheRingEntryNeverCarriesAnEmptySystemPrompt() =>
            Assert.False(string.IsNullOrWhiteSpace(Assert.Single(harness.Ring.Snapshot()).PromptSystem));

        [Fact]
        public void NoModelCallWasMade() => Assert.Empty(harness.UserPrompts);
    }

    // F220.12 — nothing is trimmed: a repeated line is re-asked when a rule flags it
    public sealed class ScenarioARepeatedLineIsNotTrimmed : IAsyncLifetime
    {
        const string Earlier = "Here comes a golden hour groove from the vault tonight.";
        const string Repeating = "HOST: " + Earlier + " Welcome to the late shift.\nNEIGHBOR: Evening, the kettle is on.\nHOST: Then the night is in good hands.";
        ChatRulesSpecHarness harness = null!;
        CrosstalkWriteResult result = null!;

        public async Task InitializeAsync()
        {
            var store = new RecordingUtteranceStore();
            store.Seeded.Add(new UtteranceEntry(
                1, ChatRulesSpecHarness.HostId, null, UtteranceKind.Patter, "LeadIn",
                ShiftKey.Unscheduled(ChatRulesSpecHarness.HostId, new DateOnly(2026, 11, 9)), Earlier, false, DateTimeOffset.UnixEpoch, null));
            harness = new ChatRulesSpecHarness(
                [Repeating, CleanAgain],
                [Judge("a-rule", "Alpha rule", s => s.Lines.Any(l => l.Text.Contains(Earlier, StringComparison.Ordinal)) ? new RuleFlag("flagged", "Say it fresh.", "repeat") : null)],
                utterances: store);
            result = await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheChatIsAskedAgain() => Assert.Equal(2, harness.UserPrompts.Count);

        [Fact]
        public void TheSecondReplyAirs() => Assert.StartsWith("Welcome back, the records", ChatRulesSpecHarness.AiredOf(result), StringComparison.Ordinal);

        [Fact]
        public void NothingIsTrimmed() => Assert.Empty(harness.Events.Events.OfType<RepeatCaught>());

        [Fact]
        public void TheFlaggedRowKeepsTheRepeatedSentence() =>
            Assert.Contains(Earlier, harness.CheckLog.Records[0].Cleaned, StringComparison.Ordinal);
    }

    // T804a (F220.9) — the shaper runs once per request, so a flagged-then-accepted chat is one breakage, not one per attempt
    public sealed class ScenarioAShaperThrowsAndTheChatIsReAsked : IAsyncLifetime
    {
        ChatRulesSpecHarness harness = null!;

        public async Task InitializeAsync()
        {
            harness = new ChatRulesSpecHarness(
                [Clean, CleanAgain],
                [new ScriptedShapingRule("b-shaper", "Bravo shaper", shapeThrows: true, covers: TalkKinds.Chat), Judge("a-rule", "Alpha rule", s => FirstAttemptOnly("Ask.", s))]);
            await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheChatWasReAsked() => Assert.Equal(2, harness.UserPrompts.Count);

        [Fact]
        public void TheBoothHearsOfTheBreakageOnce() => Assert.Single(harness.Events.Events.OfType<RuleBroken>());
    }

    // F220.9 — an accepted chat tells the recorders first, and only then is its row written
    public sealed class ScenarioARecorderThrows : IAsyncLifetime
    {
        readonly ScriptedShapingRule recorder = new("b-recorder", "Bravo recorder", recordThrows: true, covers: TalkKinds.Chat);
        ChatRulesSpecHarness harness = null!;
        CrosstalkWriteResult result = null!;
        RuleBroken broken = null!;

        public async Task InitializeAsync()
        {
            harness = new ChatRulesSpecHarness([Clean], [recorder]);
            result = await harness.WriteAsync();
            broken = harness.Events.Events.OfType<RuleBroken>().Single();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheAcceptedChatStaysAccepted() => Assert.Equal(ChatRulesSpecHarness.CleanAired, ChatRulesSpecHarness.AiredOf(result));

        [Fact]
        public void TheStoredRowCarriesCouldNotRecordForThatRule() =>
            Assert.Equal(
                TalkRuleReasons.CouldNotRecord,
                Assert.Single(harness.CheckLog.Records).Verdicts.Single(v => v.RuleId == "b-recorder").Broken);

        [Fact]
        public void TheBreakageIsNotAFlag() =>
            Assert.Null(Assert.Single(harness.CheckLog.Records).Verdicts.Single(v => v.RuleId == "b-recorder").Flag);

        [Fact]
        public void TheStoredRowIsStillAccepted() => Assert.Equal(TalkOutcome.Accepted, Assert.Single(harness.CheckLog.Records).Outcome);

        // T804a — the booth hears of it once, naming the host and saying chat
        [Fact]
        public void TheBrokenEventSaysRecord() => Assert.Equal(RuleBrokenStep.Record, broken.Step);

        [Fact]
        public void TheBrokenEventNamesTheHost() => Assert.Equal("Nova", broken.Speaker);

        [Fact]
        public void TheBrokenEventSaysChat() => Assert.Equal(RuleFlagTalk.Chat, broken.Talk);

        [Fact]
        public void TheBrokenEventIsKeyedOnTheHost() => Assert.Equal(ChatRulesSpecHarness.HostId, broken.PersonaId);
    }

    // F220.15 — a rejected chat is not told to the recorders
    public sealed class ScenarioARejectedChatIsNotRecorded : IAsyncLifetime
    {
        readonly ScriptedShapingRule recorder = new("z-recorder", "Zeta recorder", covers: TalkKinds.Chat);

        public async Task InitializeAsync() =>
            await new ChatRulesSpecHarness(
                [Clean, CleanAgain],
                [Judge("a-rule", "Alpha rule", s => new RuleFlag("flagged", "Ask.", "evidence")), recorder]).WriteAsync();

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheRecorderIsNeverTold() => Assert.Empty(recorder.Accepted);
    }

    // F220.10 — the shaper runs once, before the first ask, over an empty script
    public sealed class ScenarioTheShaperRunsFirst : IAsyncLifetime
    {
        readonly ScriptedShapingRule shaper = new("b-shaper", "Bravo shaper", "Keep it brief.", covers: TalkKinds.Chat);
        ChatRulesSpecHarness harness = null!;

        public async Task InitializeAsync()
        {
            harness = new ChatRulesSpecHarness([Clean], [shaper]);
            await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheShaperSeesNoScriptYet() => Assert.Empty(Assert.Single(shaper.Shaped).Lines);

        [Fact]
        public void TheNoteIsUnderForThisBreak() =>
            Assert.Contains("For this break:\nKeep it brief.", harness.SystemPrompts[0], StringComparison.Ordinal);

        [Fact]
        public void TheClockGuardMovesOutOfTheScaffold() =>
            Assert.DoesNotContain("Never name another day or time of day.", harness.SystemPrompts[0], StringComparison.Ordinal);
    }

    // T801 — the mode is read once per chat
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
            await new ChatRulesSpecHarness([Clean], [Judge("a-rule", "Alpha rule", Never)], modeMonitor: monitor).WriteAsync();

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheLiveModeIsReadOnceForTheChat() => Assert.Equal(1, monitor.Reads);
    }
}

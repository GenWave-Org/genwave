// STORY-570 — One flow for flagged talk, the ad spot (SPEC F220.10–.16, .29 · PLAN T802)
//
// Entry point: AdScriptWriter.WriteAsync in rules mode, with a scripted LLM, the real ad snapshot source and a real rule book of
// scripted rules that cover ads (plus the real phone-number rule for the phone question). The facts live here, beside the writer's
// harness, because GenWave.Ads.Tests has no rule book to run; they moved from the pending file in GenWave.Ads.Tests.

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Core.TalkRules;
using GenWave.TalkRules;
using GenWave.Tts.Tests.Fakes;
using Microsoft.Extensions.Options;

public static class FeatureFlaggedAdsFail
{
    const string Clean = AdRulesSpecHarness.Clean;
    const string CleanAgain = AdRulesSpecHarness.CleanAgain;

    static RuleFlag? Never(TalkSnapshot snapshot) => null;

    static RuleFlag? Always(string ask, TalkSnapshot snapshot) => new("flagged", ask, "evidence");

    static RuleFlag? FirstAttemptOnly(string ask, TalkSnapshot snapshot) =>
        snapshot.Attempt == 1 ? new RuleFlag("flagged", ask, "evidence") : null;

    static ScriptedJudgingRule Judge(string id, string name, Func<TalkSnapshot, RuleFlag?> judge) => new(id, name, judge, TalkKinds.Ad);

    /// <summary>A recording rule that notes how many rows the check log held when it was told of an accepted ad.</summary>
    public sealed class RowCountingRecorder(RecordingTalkCheckLog log) : IRecordingTalkRule
    {
        public List<int> RowsWhenTold { get; } = [];

        public string Id => "z-recorder";

        public TalkKinds CanCover => TalkKinds.Ad;

        public TalkKinds OnByDefault => TalkKinds.Ad;

        public TalkRuleDescription Describe() => new("Zeta recorder.", "-", "-", "-");

        public ValueTask AcceptedAsync(TalkSnapshot accepted, CancellationToken ct)
        {
            RowsWhenTold.Add(log.Records.Count);
            return ValueTask.CompletedTask;
        }
    }

    // AC5 — an ad still flagged by two rules after the re-ask
    public sealed class ScenarioAnAdIsStillFlagged : IAsyncLifetime
    {
        AdRulesSpecHarness harness = null!;
        AdScriptWriteResult result = null!;

        public async Task InitializeAsync()
        {
            harness = new AdRulesSpecHarness(
                [Clean, CleanAgain],
                [Judge("b-rule", "Bravo rule.", s => Always("Ask B.", s)), Judge("a-rule", "Alpha rule.", s => Always("Ask A.", s))]);
            result = await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSpotIsFailed() => Assert.IsType<AdScriptWriteResult.Failed>(result);

        [Fact]
        public void TheReasonNamesEveryFlaggedRule() => Assert.Equal(
            "the re-ask was flagged again (Alpha rule, Bravo rule)", Assert.IsType<AdScriptWriteResult.Failed>(result).Reason);

        [Fact]
        public void TheFailureCarriesARuleId() =>
            Assert.Equal(AdRulesResult.StillFlagged.FailedRuleId, Assert.IsType<AdScriptWriteResult.Failed>(result).RuleId);

        [Fact]
        public void OnlyOneReAskWasMade() => Assert.Equal(2, harness.UserPrompts.Count);

        [Fact]
        public void TheFailureIsCountedUnderTheRuleFlaggedCause() =>
            Assert.Equal(LlmCallCause.RuleFlagged, Assert.IsType<AdScriptWriteResult.Failed>(result).Cause);

        [Fact]
        public void TheRowsAreReAskedThenFailed() =>
            Assert.Equal([TalkOutcome.ReAsked, TalkOutcome.Failed], harness.CheckLog.Records.Select(r => r.Outcome));
    }

    // AC2 — an ad two rules flag, then a clean re-ask
    public sealed class ScenarioAnAdTwoRulesFlag : IAsyncLifetime
    {
        const string AskA = "Ask from the a rule.";
        const string AskB = "Ask from the b rule.";
        AdRulesSpecHarness harness = null!;
        AdScriptWriteResult result = null!;

        public async Task InitializeAsync()
        {
            harness = new AdRulesSpecHarness(
                [Clean, CleanAgain],
                [Judge("b-rule", "Bravo rule.", s => FirstAttemptOnly(AskB, s)), Judge("a-rule", "Alpha rule.", s => FirstAttemptOnly(AskA, s))]);
            result = await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheReAskListsBothAsks() =>
            Assert.Equal(LlmPromptBuilder.BuildRuleReAsk(harness.UserPrompts[0], [AskA, AskB]), harness.UserPrompts[1]);

        [Fact]
        public void ExactlyOneMoreLlmCallIsMade() => Assert.Equal(2, harness.UserPrompts.Count);

        [Fact]
        public void TheReAskIsAccepted() => Assert.Equal(CleanAgain, AdRulesSpecHarness.AiredOf(result));

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

    // F220.12 — a clean ad airs as cleaned, and the phone rewrite is not applied (a wrong number is the phone rule's to flag)
    public sealed class ScenarioACleanAd : IAsyncLifetime
    {
        readonly AdRulesSpecHarness harness = new([AdRulesSpecHarness.WrongPhone], [Judge("a-rule", "Alpha rule.", Never)]);
        AdScriptWriteResult result = null!;

        public async Task InitializeAsync() => result = await harness.WriteAsync();

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheAiredTextIsTheCleanedTextWithNoPhoneRewrite() =>
            Assert.Equal(AdRulesSpecHarness.WrongPhone, AdRulesSpecHarness.AiredOf(result));

        [Fact]
        public void TheModelIsAskedOnce() => Assert.Single(harness.UserPrompts);

        [Fact]
        public void TheLegacyValidatorIsNotRun() => Assert.Equal(0, harness.LegacyValidations);

        [Fact]
        public void TheRowIsAccepted() => Assert.Equal(TalkOutcome.Accepted, Assert.Single(harness.CheckLog.Records).Outcome);
    }

    // F220.15, F220.9 — the recorders hear of an accepted ad, and its row is written after them
    public sealed class ScenarioAnAcceptedAdIsRecorded : IAsyncLifetime
    {
        readonly AdRulesSpecHarness harness;
        readonly RowCountingRecorder recorder;

        public ScenarioAnAcceptedAdIsRecorded()
        {
            // The recorder reads the harness's own log, so the harness is built first and the recorder second via a shared log.
            var log = new RecordingTalkCheckLog();
            recorder = new RowCountingRecorder(log);
            harness = new AdRulesSpecHarness([Clean], [recorder], checkLog: log);
        }

        public async Task InitializeAsync() => await harness.WriteAsync();

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheRecorderIsToldOnceBeforeTheRowIsWritten() => Assert.Equal([0], recorder.RowsWhenTold);

        [Fact]
        public void TheAcceptedRowIsWrittenAfterwards() =>
            Assert.Equal(TalkOutcome.Accepted, Assert.Single(harness.CheckLog.Records).Outcome);
    }

    // F220.15 — a rejected ad is never recorded
    public sealed class ScenarioARejectedAdIsNotRecorded : IAsyncLifetime
    {
        readonly RecordingTalkCheckLog log = new();
        RowCountingRecorder recorder = null!;

        public async Task InitializeAsync()
        {
            recorder = new RowCountingRecorder(log);
            await new AdRulesSpecHarness(
                [Clean, CleanAgain], [Judge("a-rule", "Alpha rule.", s => Always("Ask.", s)), recorder], checkLog: log).WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheRecorderIsNeverTold() => Assert.Empty(recorder.RowsWhenTold);
    }

    // F220.11 — a reply that fails the fixed shape checks fails the spot: no rule runs and nothing is re-asked
    public sealed class ScenarioTheCleanUpFails : IAsyncLifetime
    {
        readonly ScriptedJudgingRule judge = Judge("a-rule", "Alpha rule.", s => Always("Ask.", s));
        AdRulesSpecHarness harness = null!;
        AdScriptWriteResult result = null!;

        public async Task InitializeAsync()
        {
            // One line over the 200-character per-line budget: the hygiene keeps it and the format rule refuses it.
            harness = new AdRulesSpecHarness(["ANNOUNCER: " + string.Join(' ', Enumerable.Repeat("pie", 80)), Clean], [judge]);
            result = await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSpotIsFailedWithTheFormatRule() =>
            Assert.Equal("format", Assert.IsType<AdScriptWriteResult.Failed>(result).RuleId);

        [Fact]
        public void NoRuleRuns() => Assert.Empty(judge.Judged);

        [Fact]
        public void NothingIsReAsked() => Assert.Single(harness.UserPrompts);

        [Fact]
        public void TheAttemptIsStillRecorded() => Assert.Equal(TalkOutcome.Failed, Assert.Single(harness.CheckLog.Records).Outcome);
    }

    // F220.16 — the booth rows: one per flagging rule, naming the sponsor, saying "ad", re-asked then failed
    public sealed class ScenarioTheBoothRowsForAFailedAd : IAsyncLifetime
    {
        AdRulesSpecHarness harness = null!;

        public async Task InitializeAsync()
        {
            harness = new AdRulesSpecHarness([Clean, CleanAgain], [Judge("a-rule", "Alpha rule.", s => Always("Ask.", s))]);
            await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheRowsAreReAskedThenFailed() =>
            Assert.Equal([RuleFlagOutcome.ReAsked, RuleFlagOutcome.Failed], harness.Events.Events.OfType<RuleFlagged>().Select(f => f.Outcome));

        [Fact]
        public void TheRowsSayAd() => Assert.All(harness.Events.Events.OfType<RuleFlagged>(), f => Assert.Equal(RuleFlagTalk.Ad, f.Talk));

        [Fact]
        public void TheRowsNameTheSponsorAndNoPersona() =>
            Assert.All(harness.Events.Events.OfType<RuleFlagged>(), f => Assert.Equal((null, AdRulesSpecHarness.SponsorName), (f.PersonaId, f.Speaker)));
    }

    // The phone question: the legacy phone rewrite is gone in rules mode, so the phone-number rule (on by default for ads) catches a wrong number
    public sealed class ScenarioAWrongPhoneNumber : IAsyncLifetime
    {
        AdRulesSpecHarness harness = null!;
        AdScriptWriteResult result = null!;

        public async Task InitializeAsync()
        {
            harness = new AdRulesSpecHarness([AdRulesSpecHarness.WrongPhone, CleanAgain], [new PhoneNumberRule()]);
            result = await harness.WriteAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ThePhoneRuleIsOnByDefaultForAds() => Assert.True(new PhoneNumberRule().OnByDefault.HasFlag(TalkKinds.Ad));

        [Fact]
        public void TheFirstAttemptIsFlaggedByThePhoneRule() =>
            Assert.NotNull(Assert.Single(harness.CheckLog.Records[0].Verdicts, v => v.RuleId == "phone-number" && v.SwitchedOn).Flag);

        [Fact]
        public void TheReAskCarriesThePhoneRulesAsk() =>
            Assert.Contains("Say only the sponsor's phone number, 222-0100", harness.UserPrompts[1], StringComparison.Ordinal);

        [Fact]
        public void TheCleanReplyAirs() => Assert.Equal(CleanAgain, AdRulesSpecHarness.AiredOf(result));
    }

    // F220.10 — the mode is read once per ad
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
            await new AdRulesSpecHarness([Clean], [Judge("a-rule", "Alpha rule.", Never)], modeMonitor: monitor).WriteAsync();

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheLiveModeIsReadOnceForTheAd() => Assert.Equal(1, monitor.Reads);
    }
}

// STORY-570 — One flow for flagged talk, through the booted host (SPEC F220.10-F220.16 · PLAN T800, T801)
//
// Boots the real composition root (Program.cs) in mode rules with the LLM edge scripted to answer a flagged line and then a clean
// one, and writes one lead-in through the production ISegmentCopyWriter: writer -> DjLineRulesFlow -> real rule book. The chat scenario
// writes one crosstalk exchange through the production CrosstalkScriptWriter: writer -> ChatRulesFlow -> real rule book.

namespace GenWave.Host.Tests.Specs;

using GenWave.Core.TalkRules;
using GenWave.Host.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

public static class FeatureRulesModeWire
{
    const string Clean = "Next up is Astral Plane by Valerie June, a slow burner for the late shift.";

    public sealed class ScenarioAFlaggedLineThenACleanOne : EngineSettingScenario
    {
        readonly ShadowWeekRig rig = new(
            startingMode: "rules", hostSaidTheEarlierLine: true, replies: [ShadowWeekRig.RepeatedLine, Clean]);
        string aired = string.Empty;

        protected override void ConfigureServices(IServiceCollection services) => rig.Configure(services);

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            aired = await rig.WriteLeadInAsync(Service<IServiceProvider>());
        }

        [Fact]
        public void TheSecondReplyAirs() => Assert.Equal(Clean, aired);

        [Fact]
        public void TwoLlmCallsWereMade() => Assert.Equal(2, rig.LlmCalls);

        [Fact]
        public void TwoRowsAreRecordedInRulesMode() =>
            Assert.Equal([TalkCheckMode.Rules, TalkCheckMode.Rules], rig.Rows.Select(r => r.Mode));

        [Fact]
        public void TheFirstRowWasReAskedAndTheSecondAccepted() =>
            Assert.Equal([TalkOutcome.ReAsked, TalkOutcome.Accepted], rig.Rows.Select(r => r.Outcome));

        [Fact]
        public void TheFirstRowCarriesTheRepeatFlag() =>
            Assert.NotNull(Assert.Single(rig.Rows[0].Verdicts, v => v.RuleId == "repeat-phrases" && v.SwitchedOn).Flag);
    }

    public sealed class ScenarioAFlaggedChatThenACleanOne : EngineSettingScenario
    {
        readonly ShadowWeekRig rig = new(
            startingMode: "rules", hostSaidTheEarlierLine: true,
            replies: [ShadowWeekRig.ChatRepeatingAWholeLine, ShadowWeekRig.ChatReply]);
        string aired = string.Empty;

        protected override void ConfigureServices(IServiceCollection services) => rig.Configure(services);

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            aired = await rig.WriteChatAsync(Service<IServiceProvider>());
        }

        [Fact]
        public void TheSecondReplyAirs() => Assert.Equal(ShadowWeekRig.ChatAired, aired);

        [Fact]
        public void TwoLlmCallsWereMade() => Assert.Equal(2, rig.LlmCalls);

        [Fact]
        public void TwoRowsAreRecordedInRulesMode() =>
            Assert.Equal([TalkCheckMode.Rules, TalkCheckMode.Rules], rig.Rows.Select(r => r.Mode));

        [Fact]
        public void TheFirstRowWasReAskedAndTheSecondAccepted() =>
            Assert.Equal([TalkOutcome.ReAsked, TalkOutcome.Accepted], rig.Rows.Select(r => r.Outcome));

        [Fact]
        public void TheFirstRowCarriesTheRepeatFlag() =>
            Assert.NotNull(Assert.Single(rig.Rows[0].Verdicts, v => v.RuleId == "repeat-phrases" && v.SwitchedOn).Flag);

        [Fact]
        public void TheRowsAreChatRows() => Assert.All(rig.Rows, r => Assert.Equal(TalkKinds.Chat, r.Snapshot.Kind));
    }
}

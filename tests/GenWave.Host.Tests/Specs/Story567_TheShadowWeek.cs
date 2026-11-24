// STORY-567 — The shadow week (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Host.Tests.Specs;

using System.Net;
using System.Net.Http.Json;
using GenWave.Core.TalkRules;
using GenWave.Host.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

file sealed record ModeRow(string Key, string Value);

public static class FeatureTheShadowWeek
{
    // AC1 — a fresh station
    public sealed class ScenarioAFreshStation : EngineSettingScenario
    {
        readonly ShadowWeekRig rig = new();
        string aired = string.Empty;

        protected override void ConfigureServices(IServiceCollection services) => rig.Configure(services);

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            aired = await rig.WriteLeadInAsync(Service<IServiceProvider>());
        }

        [Fact]
        public async Task TheModeIsLegacy()
        {
            var rows = await Client.GetFromJsonAsync<List<ModeRow>>("/api/settings");

            Assert.NotNull(rows);
            Assert.Equal("legacy", Assert.Single(rows, r => r.Key == "TalkRules:Mode").Value);
        }

        [Fact]
        public void TheRuleBookIsNotCalled() => Assert.Equal(0, rig.JudgeCalls);

        [Fact]
        public void TheTalkStillAirs() => Assert.Equal(ShadowWeekRig.RepeatedLine, aired);
    }

    // AC2 — mode shadow, a reply the rules flag but today's code passes
    public sealed class ScenarioShadowAirsTodaysVerdict : EngineSettingScenario
    {
        readonly ShadowWeekRig rig = new(startingMode: "shadow");
        string aired = string.Empty;

        protected override void ConfigureServices(IServiceCollection services) => rig.Configure(services);

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            aired = await rig.WriteLeadInAsync(Service<IServiceProvider>());
        }

        [Fact]
        public void TheReplyAirs() => Assert.Equal(ShadowWeekRig.RepeatedLine, aired);

        [Fact]
        public void TheRowStoresTheRuleVerdicts() =>
            Assert.NotNull(Assert.Single(
                Assert.Single(rig.Rows).Verdicts, verdict => verdict.RuleId == "repeat-phrases" && verdict.SwitchedOn).Flag);

        [Fact]
        public void TheRowStoresTheLegacyOutcome() =>
            Assert.Equal(TalkLegacyOutcome.Passed, Assert.Single(rig.Rows).LegacyOutcome);
    }

    // AC2 — the same scripted reply in legacy and then in shadow, with the mode changed through the settings endpoint
    public sealed class ScenarioTheSameReplyInLegacyAndShadow : EngineSettingScenario
    {
        readonly ShadowWeekRig rig = new();
        string airedInLegacy = string.Empty;
        string airedInShadow = string.Empty;

        protected override void ConfigureServices(IServiceCollection services) => rig.Configure(services);

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            airedInLegacy = await rig.WriteLeadInAsync(Service<IServiceProvider>());
            await PutModeAsync(Client, "shadow");
            airedInShadow = await rig.WriteLeadInAsync(Service<IServiceProvider>());
        }

        [Fact]
        public void TheAiredTextIsIdentical() => Assert.Equal(airedInLegacy, airedInShadow);
    }

    // AC3 — the mode changes
    public sealed class ScenarioTheModeChanges : EngineSettingScenario
    {
        readonly ShadowWeekRig rig = new();

        protected override void ConfigureServices(IServiceCollection services) => rig.Configure(services);

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            await rig.WriteLeadInAsync(Service<IServiceProvider>()); // legacy: no row
            await PutModeAsync(Client, "shadow");
            await rig.WriteLeadInAsync(Service<IServiceProvider>()); // the very next talk, same running host
        }

        [Fact]
        public void TheNextWrittenTalkUsesItWithNoRestart() => Assert.Single(rig.Rows);
    }

    // AC4 — POST /api/talk-rules/replay {days: 7, compare: legacy}
    public sealed class ScenarioTheShadowReportIsRequested
    {
        [Fact(Skip = "pending: T791")]
        public void EachRuleListsTheLinesWhereTheVerdictsDiffer() { }

        [Fact(Skip = "pending: T791")]
        public void TheLinesATrimSavedAreCounted() { }
    }

    // AC5 — mode shadow, a written chat
    public sealed class ScenarioAChatIsWrittenInShadow : EngineSettingScenario
    {
        readonly ShadowWeekRig rig = new(startingMode: "shadow", reply: ShadowWeekRig.ChatReply);

        protected override void ConfigureServices(IServiceCollection services) => rig.Configure(services);

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            await rig.WriteChatAsync(Service<IServiceProvider>());
        }

        [Fact]
        public void AChatRowIsStored() => Assert.Equal(TalkKinds.Chat, Assert.Single(rig.Rows).Snapshot.Kind);

        [Fact]
        public void TheRowStoresTheLegacyOutcome() =>
            Assert.Equal(TalkLegacyOutcome.Passed, Assert.Single(rig.Rows).LegacyOutcome);

        [Fact]
        public void TheRowStoresTheRuleVerdicts() => Assert.NotEmpty(Assert.Single(rig.Rows).Verdicts);

        [Fact]
        public void TheRowDescribesTheLinesLegacyMeasured() =>
            Assert.Equal(ShadowWeekRig.ChatLineChars, ChatFieldsOf(Assert.Single(rig.Rows)).LineChars.ToList());
    }

    // T789 (F220.19) — the same exchange in legacy and then in shadow
    public sealed class ScenarioTheSameChatInLegacyAndShadow : EngineSettingScenario
    {
        readonly ShadowWeekRig rig = new(reply: ShadowWeekRig.ChatReply);
        string airedInLegacy = string.Empty;
        string airedInShadow = string.Empty;

        protected override void ConfigureServices(IServiceCollection services) => rig.Configure(services);

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            airedInLegacy = await rig.WriteChatAsync(Service<IServiceProvider>());
            await PutModeAsync(Client, "shadow");
            airedInShadow = await rig.WriteChatAsync(Service<IServiceProvider>());
        }

        [Fact]
        public void TheAiredTextIsTheScriptedReply() => Assert.Equal(ShadowWeekRig.ChatAired, airedInShadow);

        [Fact]
        public void TheAiredTextIsIdentical() => Assert.Equal(airedInLegacy, airedInShadow);

        [Fact]
        public void OnlyTheShadowTalkStoredARow() => Assert.Single(rig.Rows);
    }

    // T789 (F220.19) — a reply that never parses is dropped by today's code, and the row says so
    public sealed class ScenarioAChatReplyNeverParses : EngineSettingScenario
    {
        readonly ShadowWeekRig rig = new(startingMode: "shadow", reply: "Nothing in the agreed shape.");

        protected override void ConfigureServices(IServiceCollection services) => rig.Configure(services);

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            await rig.WriteChatAsync(Service<IServiceProvider>());
        }

        [Fact]
        public void TheRowStoresTheLegacyOutcomeDropped() =>
            Assert.Equal(TalkLegacyOutcome.Dropped, Assert.Single(rig.Rows).LegacyOutcome);
    }

    // T789 (F220.19) — a chat the truth gate drops is still judged by the rule book
    public sealed class ScenarioAChatTheTruthGateDrops : EngineSettingScenario
    {
        readonly ShadowWeekRig rig = new(startingMode: "shadow", reply: ShadowWeekRig.ChatNamingAFrequency);

        protected override void ConfigureServices(IServiceCollection services) => rig.Configure(services);

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            await rig.WriteChatAsync(Service<IServiceProvider>());
        }

        [Fact]
        public void TheRowStoresTheLegacyOutcomeDropped() =>
            Assert.Equal(TalkLegacyOutcome.Dropped, Assert.Single(rig.Rows).LegacyOutcome);

        [Fact]
        public void TheRowStoresTheRuleVerdicts() => Assert.NotEmpty(Assert.Single(rig.Rows).Verdicts);

        [Fact]
        public void TheStationDetailsRuleFlagsTheFrequency() =>
            Assert.NotNull(Assert.Single(Assert.Single(rig.Rows).Verdicts, v => v.RuleId == "station-details").Flag);
    }

    // T789 (F220.19) — a repeated sentence that a trim saves
    public sealed class ScenarioAChatARepeatTrimSaves : EngineSettingScenario
    {
        readonly ShadowWeekRig rig = new(startingMode: "shadow", reply: ShadowWeekRig.ChatRepeatingOneSentence, hostSaidTheEarlierLine: true);

        protected override void ConfigureServices(IServiceCollection services) => rig.Configure(services);

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            await rig.WriteChatAsync(Service<IServiceProvider>());
        }

        [Fact]
        public void TheRowStoresTheLegacyOutcomeTrimmed() =>
            Assert.Equal(TalkLegacyOutcome.Trimmed, Assert.Single(rig.Rows).LegacyOutcome);
    }

    // T789 (F220.19) — a wholly repeated line makes today's code ask again
    public sealed class ScenarioAChatARepeatMakesTheWriterAskAgain : EngineSettingScenario
    {
        readonly ShadowWeekRig rig = new(startingMode: "shadow", reply: ShadowWeekRig.ChatRepeatingAWholeLine, hostSaidTheEarlierLine: true);

        protected override void ConfigureServices(IServiceCollection services) => rig.Configure(services);

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            await rig.WriteChatAsync(Service<IServiceProvider>());
        }

        [Fact]
        public void TheRowStoresTheLegacyOutcomeReAsked() =>
            Assert.Equal(TalkLegacyOutcome.ReAsked, Assert.Single(rig.Rows).LegacyOutcome);

        [Fact]
        public void TheWriterAskedAgain() => Assert.Equal(2, rig.LlmCalls);
    }

    // AC5 — mode shadow, a written ad
    public sealed class ScenarioAnAdIsWrittenInShadow
    {
        [Fact(Skip = "pending: T790")]
        public void AnAdRowIsStored() { }

        [Fact(Skip = "pending: T790")]
        public void TheRulesSawTheTextBeforeThePhoneRewrite() { }
    }

    // T788 (F220.19) — a context segment whose reply is empty is dropped by today's code, and the row says so
    public sealed class ScenarioAContextSegmentGetsNoReply : EngineSettingScenario
    {
        readonly ShadowWeekRig rig = new(startingMode: "shadow", reply: "");

        protected override void ConfigureServices(IServiceCollection services) => rig.Configure(services);

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            await rig.WriteContextSegmentAsync(Service<IServiceProvider>());
        }

        [Fact]
        public void TheRowStoresTheLegacyOutcomeDropped() =>
            Assert.Equal(TalkLegacyOutcome.Dropped, Assert.Single(rig.Rows).LegacyOutcome);
    }

    // Sad path
    // AC7 — PUT TalkRules:Mode nope
    public sealed class ScenarioABadMode : EngineSettingScenario
    {
        [Fact]
        public async Task ItIsRejectedWith400() =>
            Assert.Equal(HttpStatusCode.BadRequest, (await PutModeAsync(Client, "nope")).StatusCode);
    }

    // Sad path
    // T789 — the rule book throws while a chat is written in shadow
    public sealed class ScenarioTheRuleBookThrowsOnAChat : EngineSettingScenario
    {
        readonly ShadowWeekRig rig = new(ruleBookThrows: true, startingMode: "shadow", reply: ShadowWeekRig.ChatReply);
        string aired = string.Empty;

        protected override void ConfigureServices(IServiceCollection services) => rig.Configure(services);

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            aired = await rig.WriteChatAsync(Service<IServiceProvider>());
        }

        [Fact]
        public void TheChatAirsAsTodaysCodeDecided() => Assert.Equal(ShadowWeekRig.ChatAired, aired);

        [Fact]
        public void AWarningIsLogged() => Assert.True(rig.LoggedWarningContaining("Shadow talk check failed"));
    }

    // Sad path
    // AC8 — the rule book throws in shadow
    public sealed class ScenarioTheRuleBookThrowsInShadow : EngineSettingScenario
    {
        readonly ShadowWeekRig rig = new(ruleBookThrows: true, startingMode: "shadow");
        string aired = string.Empty;

        protected override void ConfigureServices(IServiceCollection services) => rig.Configure(services);

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            aired = await rig.WriteLeadInAsync(Service<IServiceProvider>());
        }

        [Fact]
        public void TheTalkAirsAsTodaysCodeDecided() => Assert.Equal(ShadowWeekRig.RepeatedLine, aired);

        [Fact]
        public void AWarningIsLogged() => Assert.True(rig.LoggedWarningContaining("Shadow talk check failed"));
    }

    static TalkChatFields ChatFieldsOf(TalkCheckRecord row) =>
        row.Snapshot.Chat ?? throw new InvalidOperationException("The row's snapshot carries no chat fields.");

    static Task<HttpResponseMessage> PutModeAsync(HttpClient client, string mode) =>
        client.PutAsJsonAsync("/api/settings", new[] { new { key = "TalkRules:Mode", value = mode } });
}

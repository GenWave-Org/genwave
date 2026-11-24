// STORY-566 — Every attempt is recorded and can be replayed (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Host.Tests.Specs;

using System.Net;
using System.Net.Http.Json;
using GenWave.Core.TalkRules;
using GenWave.Host.Tests.Support;
using static GenWave.Host.Tests.Support.ReplayClient;
using Microsoft.Extensions.DependencyInjection;

public static class FeatureEveryAttemptIsRecordedAndReplayed
{
    // AC3 — a stored row replayed with unchanged rules
    public sealed class ScenarioAStoredRowIsReplayed : EngineSettingScenario
    {
        readonly ShadowWeekRig rig = new(startingMode: "shadow");
        ReplayBody body = new([], [], 0);

        protected override void ConfigureServices(IServiceCollection services) => rig.Configure(services);

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            await rig.WriteLeadInAsync(Service<IServiceProvider>());
            body = await ReplayAsync(Client, new { days = 7 });
        }

        [Fact]
        public void NoVerdictChanges() => Assert.Empty(body.Changes);
    }

    // AC4 — POST /api/talk-rules/replay {days: 7} through the running host. Two rows recorded by the real writer; the second is
    // stored as an earlier rule set judged it (the repeat-phrases flag missing), so today's rules disagree with it.
    public sealed class ScenarioTheReplayIsRequested : EngineSettingScenario
    {
        readonly ShadowWeekRig rig = new(startingMode: "shadow");
        ReplayBody body = new([], [], 0);
        int llmCallsBefore;
        int rowsBefore;

        protected override void ConfigureServices(IServiceCollection services) => rig.Configure(services);

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            await rig.WriteLeadInAsync(Service<IServiceProvider>());
            var recorded = Assert.Single(rig.Rows);
            rig.CheckStore.Add(recorded with
            {
                Verdicts = new TalkList<RuleVerdict>(recorded.Verdicts.Select(v => v.RuleId == "repeat-phrases" ? v with { Flag = null } : v)),
            });
            llmCallsBefore = rig.LlmCalls;
            rowsBefore = rig.CheckStore.RowCount;
            body = await ReplayAsync(Client, new { days = 7 });
        }

        [Fact]
        public void EachRuleHasAFlagCount() =>
            Assert.Equal(2, Assert.Single(body.Counts, c => c.RuleId == "repeat-phrases").Flagged);

        [Fact]
        public void EachRuleListsItsChangedAttempts() =>
            Assert.Equal(("repeat-phrases", 2L), (Assert.Single(body.Changes).RuleId, Assert.Single(body.Changes).CheckId));

        [Fact]
        public void TheChangeShowsTheStoredAndTheReplayedFlag() =>
            Assert.Equal((false, true), (Assert.Single(body.Changes).Stored is not null, Assert.Single(body.Changes).Replayed is not null));

        [Fact]
        public void NoLlmCallIsMade() => Assert.Equal(llmCallsBefore, rig.LlmCalls);

        [Fact]
        public void NoRowIsWritten() => Assert.Equal((rowsBefore, 0), (rig.CheckStore.RowCount, rig.CheckStore.Appends));
    }

    // Sad path
    // AC7 — the record queue is full
    public sealed class ScenarioTheRecordQueueIsFull : EngineSettingScenario
    {
        // The factory removes every hosted service, so nothing drains the real queue: it fills, as in a database outage.
        const int Attempts = 300;

        readonly List<bool> accepted = [];
        readonly ShadowWeekRig rig = new(recordRowsInMemory: false, startingMode: "shadow");
        string aired = string.Empty;

        protected override void ConfigureServices(IServiceCollection services) => rig.Configure(services);

        static TalkCheckRecord AnyRecord() =>
            new(
                1,
                TalkCheckMode.Shadow,
                "raw",
                "cleaned",
                new TalkSnapshot(
                    TalkKinds.DjLine, "Banter", 1,
                    new TalkList<TalkLine>([new TalkLine("Flip", "Hello.")]),
                    new TalkList<TalkSpeaker>([new TalkSpeaker(1, "Flip", TalkList<string>.Empty)]),
                    null, null, DateTimeOffset.UnixEpoch, "UTC", null, new TalkStation("GenWave", "friendly"),
                    TalkList<TalkRecentLine>.Empty, null, TalkMemos.Empty),
                TalkList<RuleVerdict>.Empty,
                null,
                TalkOutcome.Accepted);

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            var log = Service<ITalkCheckLog>();
            for (var i = 0; i < Attempts; i++)
            {
                accepted.Add(log.TryRecord(AnyRecord()));
            }

            aired = await rig.WriteLeadInAsync(Service<IServiceProvider>()); // a shadow talk against the full queue
        }

        [Fact]
        public void TheRowIsDropped() => Assert.False(accepted[^1]);

        [Fact]
        public void SomeRowsAreAccepted() => Assert.Contains(true, accepted);

        [Fact]
        public void TheTalkStillAirs() => Assert.Equal(ShadowWeekRig.RepeatedLine, aired);
    }

    // Sad path
    // AC8 — no admin cookie
    public sealed class ScenarioNoAdminCookie : EngineSettingScenario
    {
        // The base fixture logs in; this scenario never does.
        public override Task InitializeAsync() => Task.CompletedTask;

        [Fact]
        public async Task TheReplayReturns401() =>
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsJsonAsync("/api/talk-rules/replay", new { days = 7 })).StatusCode);
    }

    // Sad path
    // T791 — a replay request outside what the route accepts
    public sealed class ScenarioABadReplayRequest : EngineSettingScenario
    {
        [Fact]
        public async Task ZeroDaysIsRejectedWith400() =>
            Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("/api/talk-rules/replay", new { days = 0 })).StatusCode);

        [Fact]
        public async Task NinetyOneDaysIsRejectedWith400() =>
            Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("/api/talk-rules/replay", new { days = 91 })).StatusCode);

        [Fact]
        public async Task AnUnknownCompareIsRejectedWith400() =>
            Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("/api/talk-rules/replay", new { days = 7, compare = "nope" })).StatusCode);

        [Fact]
        public async Task AnUnknownKindIsRejectedWith400() =>
            Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("/api/talk-rules/replay", new { days = 7, kinds = new[] { "nope" } })).StatusCode);
    }

    // Sad path
    // AC9 — retention out of range
    public sealed class ScenarioRetentionRange : EngineSettingScenario
    {
        Task<HttpResponseMessage> PutRetentionAsync(string days) =>
            Client.PutAsJsonAsync("/api/settings", new[] { new { key = "TalkRules:CheckRetentionDays", value = days } });

        [Fact]
        public async Task ZeroDaysIsRejectedWith400() =>
            Assert.Equal(HttpStatusCode.BadRequest, (await PutRetentionAsync("0")).StatusCode);

        [Fact]
        public async Task NinetyOneDaysIsRejectedWith400() =>
            Assert.Equal(HttpStatusCode.BadRequest, (await PutRetentionAsync("91")).StatusCode);

        [Fact]
        public async Task OneDayIsAccepted() =>
            Assert.Equal(HttpStatusCode.OK, (await PutRetentionAsync("1")).StatusCode);

        [Fact]
        public async Task NinetyDaysIsAccepted() =>
            Assert.Equal(HttpStatusCode.OK, (await PutRetentionAsync("90")).StatusCode);
    }
}

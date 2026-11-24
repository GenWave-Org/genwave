// STORY-566 — Every attempt is recorded and can be replayed (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Host.Tests.Specs;

using System.Net;
using System.Net.Http.Json;
using GenWave.Core.TalkRules;
using GenWave.Host.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

public static class FeatureEveryAttemptIsRecordedAndReplayed
{
    // AC3 — a stored row replayed with unchanged rules
    public sealed class ScenarioAStoredRowIsReplayed
    {
        [Fact(Skip = "pending: T791")]
        public void NoVerdictChanges() { }
    }

    // AC4 — POST /api/talk-rules/replay {days: 7} through the running host
    public sealed class ScenarioTheReplayIsRequested
    {
        [Fact(Skip = "pending: T791")]
        public void EachRuleHasAFlagCount() { }

        [Fact(Skip = "pending: T791")]
        public void EachRuleListsItsChangedAttempts() { }

        [Fact(Skip = "pending: T791")]
        public void NoLlmCallIsMade() { }

        [Fact(Skip = "pending: T791")]
        public void NoRowIsWritten() { }
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
    public sealed class ScenarioNoAdminCookie
    {
        [Fact(Skip = "pending: T791")]
        public void TheReplayReturns401() { }
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

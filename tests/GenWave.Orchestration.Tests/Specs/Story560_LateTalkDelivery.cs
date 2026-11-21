// STORY-560 AC2 (delivery half) — only a render that ran past its budget is a late talk (SPEC F219.26, PLAN T767)

using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.TestSupport.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureLateTalkDelivery
{
    static readonly DateTimeOffset Start = DateTimeOffset.Parse("2030-01-01T00:00:00Z");

    static CapturingStationEventSink Deliver(SlotOutcome outcome)
    {
        var identity = new StationIdentity("station-1", "GenWave", "voice-station");
        var request = new SegmentRequest(SegmentKind.LeadIn, identity.Voice, identity.Name, null, Start, identity.Id, PersonaName: "Flip");
        var slot = new PlannedSlot(1, SegmentKind.LeadIn, new RenderSource(request), null, new SilentDrop(), ObserveDuration: true);
        var context = new BreakContext(1, null, null, null, new CadenceConfig(), identity, Start, null, new HashSet<SpeechDeferralKind>(), TimeSpan.Zero);
        var events = new CapturingStationEventSink();

        new BreakDelivery(NullLogger<BreakDelivery>.Instance, events, new RollingPatterDurationEstimator())
            .Deliver(new BreakPlan(1, context, TimeSpan.FromSeconds(5), [slot]), [outcome]);
        return events;
    }

    public sealed class ScenarioARenderRanPastItsBudget
    {
        [Fact]
        public void OneTalkDroppedLateEventNamesTheDj()
        {
            var late = Assert.IsType<TalkDroppedLate>(Assert.Single(Deliver(new TimedOutOutcome()).Events));
            Assert.Equal("Flip", late.PersonaName);
        }
    }

    public sealed class ScenarioARenderFailedOutright
    {
        [Fact]
        public void NoTalkDroppedLateEventIsPublished() =>
            Assert.DoesNotContain(Deliver(new FailedOutcome()).Events, e => e is TalkDroppedLate);
    }
}

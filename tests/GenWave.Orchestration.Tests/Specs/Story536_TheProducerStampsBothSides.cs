// STORY-536 AC2 (producer half) — HandoffCeremonyProducer stamps the counterpart and the boundary (SPEC F218.15(b) · PLAN T726)
//
// ArmAsync is the one place that knows both DJs and the boundary, so each armed half carries the OTHER
// persona's id and the resolver's boundary instant; a later hold on the SignOn never moves that instant.

using GenWave.Abstractions.Playout;
using GenWave.Core.Domain;
using Harness = GenWave.Orchestration.Tests.Specs.FeatureHandoffCeremonyProducer.Harness;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureTheProducerStampsBothSides
{
    static readonly DateTimeOffset NoonBoundary = new(2026, 3, 2, 12, 0, 0, TimeSpan.Zero);

    static Harness BuildTwoDjHarness(DateTimeOffset now)
    {
        var store = new FakePersonaStore();
        store.Add(FeatureHandoffCeremonyProducer.MakePersona(10, "DJ Alpha", "af_alpha"));
        store.Add(FeatureHandoffCeremonyProducer.MakePersona(20, "DJ Beta", "af_beta"));
        var monday = FeatureHandoffCeremonyProducer.Monday;
        var schedule = new ScheduleWeekSnapshot(
        [
            new ScheduleSegment(Id: 1, Day: monday, StartMinute: 0, EndMinute: 720, PersonaId: 10, Genres: null, EnergyMin: null, EnergyMax: null),
            new ScheduleSegment(Id: 2, Day: monday, StartMinute: 720, EndMinute: 1440, PersonaId: 20, Genres: null, EnergyMin: null, EnergyMax: null),
        ]);
        return FeatureHandoffCeremonyProducer.Build(store, schedule, now, TimeSpan.FromMinutes(10));
    }

    public sealed class ScenarioABoundaryBetweenTwoDjs
    {
        /// <summary>Each half names the OTHER half's persona as its counterpart.</summary>
        [Fact]
        public async Task EachHalfNamesTheOtherPersonaAsCounterpart()
        {
            var harness = BuildTwoDjHarness(FeatureHandoffCeremonyProducer.JustBeforeNoon);

            await harness.Producer.ArmAsync(FeatureHandoffCeremonyProducer.Identity, harness.Time.GetUtcNow(), CancellationToken.None);

            var signOff = harness.Queue.Peek(SpeechDeferralKind.SignOff)?.Handoff;
            var signOn = harness.Queue.Peek(SpeechDeferralKind.SignOn)?.Handoff;
            Assert.NotNull(signOff);
            Assert.NotNull(signOn);
            Assert.Equal(signOn.PersonaId, signOff.CounterpartPersonaId);
            Assert.Equal(signOff.PersonaId, signOn.CounterpartPersonaId);
            Assert.Equal(10L, signOff.PersonaId);
            Assert.Equal(20L, signOn.PersonaId);
        }

        /// <summary>Both halves carry the one boundary instant the resolver reported.</summary>
        [Fact]
        public async Task BothHalvesCarryTheResolversBoundary()
        {
            var harness = BuildTwoDjHarness(FeatureHandoffCeremonyProducer.JustBeforeNoon);

            await harness.Producer.ArmAsync(FeatureHandoffCeremonyProducer.Identity, harness.Time.GetUtcNow(), CancellationToken.None);

            var signOff = harness.Queue.Peek(SpeechDeferralKind.SignOff)?.Handoff;
            var signOn = harness.Queue.Peek(SpeechDeferralKind.SignOn)?.Handoff;
            Assert.NotNull(signOff);
            Assert.NotNull(signOn);
            Assert.Equal(signOn.BoundaryAt, signOff.BoundaryAt);
            Assert.Equal(NoonBoundary, signOff.BoundaryAt);
        }
    }

    public sealed class ScenarioASignOnHeldAfterTheSignOffAired
    {
        /// <summary>Draining the SignOff, holding the SignOn behind a tail and re-arming the same tuple
        /// leaves the held SignOn's boundary exactly as first stamped.</summary>
        [Fact]
        public async Task TheHeldSignOnKeepsItsBoundary()
        {
            var harness = BuildTwoDjHarness(FeatureHandoffCeremonyProducer.JustBeforeNoon);
            await harness.Producer.ArmAsync(FeatureHandoffCeremonyProducer.Identity, harness.Time.GetUtcNow(), CancellationToken.None);
            harness.Time.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(50));
            var now = harness.Time.GetUtcNow();
            var drained = harness.Queue.TryDequeueDue(now);
            Assert.Contains(drained, d => d.Kind == SpeechDeferralKind.SignOff);

            harness.Producer.HoldSignOnPastQueuedTail(now, TimeSpan.FromSeconds(40));
            await harness.Producer.ArmAsync(FeatureHandoffCeremonyProducer.Identity, now, CancellationToken.None);

            var held = harness.Queue.Peek(SpeechDeferralKind.SignOn);
            Assert.NotNull(held);
            Assert.NotNull(held.NotBefore);
            Assert.Equal(NoonBoundary, held.Handoff?.BoundaryAt);
        }
    }
}

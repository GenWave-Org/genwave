// STORY-241 — The station follows the clock (SPEC F91.2–F91.5, F91.7, F94.5, PLAN T119/T120)
//
// BDD specification — xUnit, pending. T119 facts exercise ScheduleResolver as a pure
// function (week snapshot + FakeTimeProvider — the DST scenarios pin real tzdata
// transitions). T120 facts are wire: a real playout run over a seeded two-segment
// schedule through the production provider chain, with /api/status covered in the
// Host-side factory idiom.

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureStationFollowsTheClock
{
    public sealed class ScenarioResolvingTheCurrentSegment
    {
        // Given a stored week snapshot and a station-local wall-clock instant.

        [Fact(Skip = "Pending (T119)")]
        public void SnapshotCarriesSegmentPersonaEnvelopeBoundaryAndNext() { }

        [Fact(Skip = "Pending (T119)")]
        public void FeederTickPathIssuesNoScheduleStoreQuery() { }
    }

    public sealed class ScenarioConsumersFlipAtTheBoundary
    {
        // Given a schedule where DJ A's segment ends and DJ B's begins, When a real
        // playout run crosses the boundary (zero call-site changes — F91.5).

        [Fact(Skip = "Pending (T120)")]
        public void PatterVoiceFlipsFromAToB() { }

        [Fact(Skip = "Pending (T120)")]
        public void BoothLogStampFlipsFromAToB() { }

        [Fact(Skip = "Pending (T120)")]
        public void RankerRungZeroObservesB() { }

        [Fact(Skip = "Pending (T120)")]
        public void StatusEndpointReportsBResolverSourced() { }
    }

    public sealed class ScenarioGapsAreStationDefault
    {
        // Given a wall-clock instant covered by no segment.

        [Fact(Skip = "Pending (T119)")]
        public void EnvelopeIsStationDefaultValues() { }

        [Fact(Skip = "Pending (T119)")]
        public void PersonaIsNone() { }

        [Fact(Skip = "Pending (T120)")]
        public void EmptyGridBehavesAsMusicOnlyStation() { }
    }

    public sealed class ScenarioSlotSignsThePick
    {
        // Given a pick made during segment 42 (F91.7).

        [Fact(Skip = "Pending (T120)")]
        public void EnvelopeIdIsSegmentColonId() { }

        [Fact(Skip = "Pending (T120)")]
        public void GapsUseStationDefaultSentinel() { }

        [Fact(Skip = "Pending (T120)")]
        public void RelaxationLadderAppliesUnchangedPerSegment() { }
    }

    public sealed class ScenarioWallClockAcrossDst
    {
        // Given segments spanning the spring-forward and fall-back transitions (F91.2).

        [Fact(Skip = "Pending (T119)")]
        public void SpringForwardCrossingSegmentAirsOneHourShort() { }

        [Fact(Skip = "Pending (T119)")]
        public void FallBackCrossingSegmentAirsOneHourLong() { }

        [Fact(Skip = "Pending (T119)")]
        public void NonCrossingSegmentsHitTheirWallClockTimesExactly() { }
    }

    public sealed class ScenarioStalePersonaDegrades
    {
        // Sad path — a schedule row whose persona was deleted out-of-band (F91.5).

        [Fact(Skip = "Pending (T120)")]
        public void SegmentBehavesPersonaLessWithWarnOnce() { }

        [Fact(Skip = "Pending (T120)")]
        public void ResolverNeverThrows() { }
    }
}

// STORY-227 — The request line jumps the queue, within the law (SPEC F87.6, PLAN T90)
//
// BDD specification — xUnit, authored PENDING at /plan time. Drives the Orchestrator's
// pick chain with a scripted pending-request source — fulfillment, one-shot, TTL, and BOTH
// OverrideEnvelope modes travel in one reviewable unit (T90, the T70 precedent).

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureRequestFulfillment
{
    public static class ScenarioShortCircuitWithinTheWindow
    {
        [Fact(Skip = "Pending — PLAN T90 (/build-loop)")]
        public static void APendingMatchedRequestWinsThePickAheadOfThePersonaRung() { }

        [Fact(Skip = "Pending — PLAN T90 (/build-loop)")]
        public static void AFulfilledRequestNeverInfluencesALaterPick()
        {
            // One-shot — fulfilled_at stamped at push (F87.6).
        }

        [Fact(Skip = "Pending — PLAN T90 (/build-loop)")]
        public static void OverrideTrueBypassesEnvelopeAndRotationRecency() { }

        [Fact(Skip = "Pending — PLAN T90 (/build-loop)")]
        public static void AVibeRequestConstrainsExactlyOnePickThroughTheMoodMachinery() { }
    }

    public static class SadPathLawAndExpiry
    {
        [Fact(Skip = "Pending — PLAN T90 (/build-loop)")]
        public static void OverrideFalseIdlesAnOffEnvelopeMatchToExpiry() { }

        [Fact(Skip = "Pending — PLAN T90 (/build-loop)")]
        public static void ANeverPlayFlipAfterMatchingStopsFulfillmentInEitherMode() { }

        [Fact(Skip = "Pending — PLAN T90 (/build-loop)")]
        public static void ARequestPastItsWindowIsMarkedExpiredAndIgnored() { }
    }
}

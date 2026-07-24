// STORY-224 — Anyone can ask; nobody can probe (SPEC F87.1–F87.3, F87.8; PLAN T86–T87)
//
// BDD specification — xUnit, authored PENDING at /plan time. Entry-point discipline: every
// scenario drives POST /spectator/api/requests through the production pipeline
// (WebApplicationFactory) — limiter, surface gate, and contract all real.

namespace GenWave.Host.Tests.Specs;

public static class FeatureRequestIntake
{
    public static class ScenarioConstantAcceptance
    {
        [Fact(Skip = "Pending — PLAN T87 (/build-loop)")]
        public static void AnAcceptedWishReturns202()
        {
        }

        [Fact(Skip = "Pending — PLAN T87 (/build-loop)")]
        public static void TheBodyIsByteIdenticalForMatchableUnmatchableAndGibberishWishes()
        {
            // The request line is not a catalog oracle and not a request-state oracle (F87.1).
        }

        [Fact(Skip = "Pending — PLAN T87 (/build-loop)")]
        public static void AnAcceptedWishCreatesAPendingRowWithWindowExpiry()
        {
            // expires_at = received_at + WindowMinutes (F87.1).
        }

        [Fact(Skip = "Pending — PLAN T87 (/build-loop)")]
        public static void TheBoothLogRecordsRequestReceivedWithoutTheWishText()
        {
            // F87.8 — narrative visibility, zero listener text.
        }
    }

    public static class SadPathFailClosed
    {
        [Fact(Skip = "Pending — PLAN T87 (/build-loop)")]
        public static void DisabledRequestsMeansTheEndpointIsAStandard404()
        {
            // F87.2 — surface-off semantics; not a "requests closed" oracle.
        }

        [Fact(Skip = "Pending — PLAN T87 (/build-loop)")]
        public static void ACallerInsideTheCooldownGets429AndNoRow() { }

        [Fact(Skip = "Pending — PLAN T87 (/build-loop)")]
        public static void ACallerOverTheDailyCapGets429AndNoRow() { }

        [Fact(Skip = "Pending — PLAN T87 (/build-loop)")]
        public static void AnOverLengthWishGets400AndNothingIsWritten() { }

        [Fact(Skip = "Pending — PLAN T87 (/build-loop)")]
        public static void AtThePendingCapTheOldestPendingRowIsEvicted() { }
    }
}

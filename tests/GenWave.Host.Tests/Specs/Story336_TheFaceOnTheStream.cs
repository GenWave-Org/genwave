// STORY-336 — The face on the stream (SPEC F129.4/.5/.6, gh-#297 · PLAN T300)
//
// BDD specification — xUnit. ArtworkUrlResolver's per-kind mapping (amends F88.4).
// The live ICY contract (F129.6) is the T301 wire's acceptance. Skip-pinned until T300.

namespace GenWave.Host.Tests.Specs;

public static class FeatureTheFaceOnTheStream
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the mapping
    // ---------------------------------------------------------------------

    public sealed class ScenarioSingleVoiceSpeechWearsItsFace
    {
        [Fact(Skip = "Pending T300 — see docs/PLAN.md")]
        public void APersonaAttributedItemWithAWornFaceStampsTheDjTokenUrl()
        {
            // LeadIn/BackAnnounce/TimeDate/ceremony kinds, persona wears a face
            // → annotation url= …/spectator/api/artwork/dj/<token>.
            Assert.Fail("pending T300");
        }

        [Fact(Skip = "Pending T300 — see docs/PLAN.md")]
        public void AFacelessPersonasItemStampsTheStationImageUrl()
        {
            Assert.Fail("pending T300");
        }
    }

    public sealed class ScenarioTheStationSpeaksAsTheStation
    {
        [Fact(Skip = "Pending T300 — see docs/PLAN.md")]
        public void ACrosstalkItemStampsTheStationImageUrl()
        {
            // Ruled: two voices = the station, never one DJ's face.
            Assert.Fail("pending T300");
        }

        [Fact(Skip = "Pending T300 — see docs/PLAN.md")]
        public void IdentsAndSafeItemsStampTheStationImageUrl()
        {
            Assert.Fail("pending T300");
        }

        [Fact(Skip = "Pending T300 — see docs/PLAN.md")]
        public void MusicItemsAreByteIdenticalToPreF129()
        {
            Assert.Fail("pending T300");
        }
    }

    public sealed class ScenarioTheHotPathStaysCold
    {
        [Fact(Skip = "Pending T300 — see docs/PLAN.md")]
        public void PersonaTokenResolutionIssuesNoPerTickRead()
        {
            // Memoized ≤30s TTL, extending an EXISTING persona cache (gh-#482 rider:
            // the fact asserts no new cache type joined the three known copies).
            Assert.Fail("pending T300");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the gate
    // ---------------------------------------------------------------------

    public sealed class ScenarioNoBaseUrlMeansNoEmission
    {
        [Fact(Skip = "Pending T300 — see docs/PLAN.md")]
        public void WithPublicBaseUrlEmptyNoUrlIsEmittedAtAll()
        {
            // F88.4's gating unchanged — HTTP-only deployments stay honest.
            Assert.Fail("pending T300");
        }
    }
}

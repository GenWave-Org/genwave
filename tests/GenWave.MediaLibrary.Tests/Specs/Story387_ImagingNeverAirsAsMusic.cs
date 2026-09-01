// STORY-387 — Imaging can never air as music (F158.4 · pending T395)
// Every fact here runs against live Postgres (the T362 loop law).

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureImagingNeverAirsAsMusic
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — both directions of the fence
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheFenceHoldsFromTheImagingSide
    {
        [Fact(Skip = "Pending T395 — see docs/PLAN.md")]
        public void RotationSelectionNeverReturnsAnAdRow()
        {
            // Ready, eligible imaging_kind='ad' row inside the music scope:
            //   GetRotationCandidateAsync never returns it (loop the pick to exhaustion).
            Assert.Fail("pending T395");
        }

        [Fact(Skip = "Pending T395 — see docs/PLAN.md")]
        public void EnvelopeSelectionNeverReturnsAnAdRow()
        {
            Assert.Fail("pending T395");
        }

        [Fact(Skip = "Pending T395 — see docs/PLAN.md")]
        public void MediaRandomNeverReturnsAnAdRow()
        {
            Assert.Fail("pending T395");
        }

        [Fact(Skip = "Pending T395 — see docs/PLAN.md")]
        public void TheFenceCoversEveryImagingKindNotJustAd()
        {
            // A station_id row in the music scope is equally invisible — the fence is
            //   `imaging_kind is null`, not `!= 'ad'` (retro-fixes the standing leak).
            Assert.Fail("pending T395");
        }
    }

    public sealed class ScenarioTheFenceIsInvisibleFromTheMusicSide
    {
        [Fact(Skip = "Pending T395 — see docs/PLAN.md")]
        public void ANullKindMusicRowSurfacesExactlyAsBefore()
        {
            Assert.Fail("pending T395");
        }
    }

    public sealed class ScenarioTheAdsPoolRead
    {
        [Fact(Skip = "Pending T395 — see docs/PLAN.md")]
        public void TheAdsPoolReturnsOnlyReadyEligibleAdRows()
        {
            // ready+measurable+eligible+not never_play+imaging_kind='ad' in the ads library;
            //   an ineligible or never_play ad row never vends.
            Assert.Fail("pending T395");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — what the fence must NOT touch
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheSafeFloorIsUntouched
    {
        [Fact(Skip = "Pending T395 — see docs/PLAN.md")]
        public void SafeTrackSelectionAnswersExactlyAsBefore()
        {
            // The never-silence path deliberately skips the fence (F158.4) — a SafeScope row
            //   with a non-null imaging_kind still vends from GetRandomReadyAsync.
            Assert.Fail("pending T395");
        }
    }

    public sealed class ScenarioAdsNeverEnterTheRotationLedger
    {
        [Fact(Skip = "Pending T395 — see docs/PLAN.md")]
        public void AnAdAiringLeavesMediaRotationByteIdentical()
        {
            // TrackAired for an imaging_kind='ad' row: library.media_rotation unchanged
            //   (the F149.2 exclusion re-pinned over the new kind).
            Assert.Fail("pending T395");
        }
    }
}

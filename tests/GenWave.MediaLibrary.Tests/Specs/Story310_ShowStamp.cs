// STORY-310 — Show airings are countable (F121.1)
//
// BDD specification — xUnit, PENDING scaffold (planned 2026-08-10). Comment-bodied on
// purpose: booth_log.show_id and its air-time stamp land at T238/T242. The F113.1
// pattern exactly: stamped from the resolver snapshot at AIR time, no FK (history
// outlives the entity), immune to grid repaints.

namespace GenWave.MediaLibrary.Tests.Specs;

using Xunit;

public static class FeatureShowStamp
{
    public sealed class ScenarioRowsDuringAShow
    {
        [Fact(Skip = "Pending (T242)")]
        public void KindedRowsCarryTheShowId()
        {
            // Given a show on the air
            // When  a tts airing writes its track-started row
            // Then  show_id carries the snapshot's show (SignOn/StationId = the gate's evidence)
        }

        [Fact(Skip = "Pending (T242)")]
        public void MusicRowsCarryItToo()
        {
            // Given a show on the air
            // When  a music track-started row is written
            // Then  show_id is stamped from the same chokepoint (verify ONE stamp point
            //       covers music and kinded alike — the /design TODO made a fact)
        }
    }

    public sealed class ScenarioShowlessRows
    {
        [Fact(Skip = "Pending (T242)")]
        public void NoShowMeansNullStamp()
        {
            // Given no show on the air
            // When  rows are written
            // Then  show_id is NULL — pre-F121 and showless rows are indistinguishable
        }
    }
}

// STORY-182 — Listener-visible metadata carries no internal keys
//
// BDD specification — xUnit (SPEC F67.4). Pending scaffold; /build-loop (PLAN T26)
// implements and removes Skip. AC2 (live ICY + status-page capture) runs once against the
// compose stack as T26's wire acceptance; the static guard below pins it thereafter.

using Xunit;

namespace GenWave.Host.Tests.Specs;

public static class FeatureListenerMetadataDisclosure
{
    private const string Pending = "pending — PLAN T26 (/build-loop)";

    public static class ScenarioStreamTitleInputsPinned
    {
        [Fact(Skip = Pending)]
        public static void StreamTitle_builder_consumes_only_artist_title_and_station_name()
        {
            // Given the engine's StreamTitle builder (engine/genwave.liq gw_icy_song)
            // When  its inputs are enumerated by the guard test
            // Then  only artist, title, and station name are consumed — no track_id,
            //       replay_gain, or on_air* keys (F67.4)
            Assert.Fail(Pending);
        }
    }
}

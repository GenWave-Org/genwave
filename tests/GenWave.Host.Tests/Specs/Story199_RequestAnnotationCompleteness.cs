// STORY-199 — Request annotations carry full metadata
//
// BDD specification — xUnit (SPEC F75.1–F75.2). Pending scaffold; /build-loop (PLAN T44)
// implements and removes Skip. Sits beside Story055 (LiquidsoapAnnotationBuilder), which
// owns the existing replay_gain/cue annotation contract.

using Xunit;

namespace GenWave.Host.Tests.Specs;

public static class FeatureRequestAnnotationCompleteness
{
    private const string Pending = "pending — PLAN T44 (/build-loop)";

    public static class ScenarioAnnotatedUri
    {
        [Fact(Skip = Pending)]
        public static void Feeder_push_annotates_title_artist_and_media_id()
        {
            // Given a feeder push of a catalog track
            // When  the request URI is inspected
            // Then  it annotates title, artist, and media id alongside replay_gain (F75.1)
            Assert.Fail(Pending);
        }
    }

    public static class ScenarioTaglessCorrelation
    {
        [Fact(Skip = Pending)]
        public static void Tagless_file_still_correlates_and_displays_by_annotated_id()
        {
            // Given a track whose file has no readable tags
            // When  it airs
            // Then  now-playing shows correct title/artist and correlates by the
            //       annotated id (F75.2)
            Assert.Fail(Pending);
        }
    }
}

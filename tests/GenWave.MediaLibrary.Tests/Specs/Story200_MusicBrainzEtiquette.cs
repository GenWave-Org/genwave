// STORY-200 — MusicBrainz etiquette
//
// BDD specification — xUnit (SPEC F76.1–F76.2). Pending scaffold; /build-loop (PLAN T45)
// implements and removes Skip. Extends the MusicBrainzYearLookup seam (Story144).

using Xunit;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureMusicBrainzEtiquette
{
    private const string Pending = "pending — PLAN T45 (/build-loop)";

    public static class ScenarioThrottleAndIdentity
    {
        [Fact(Skip = Pending)]
        public static void Requests_never_exceed_one_per_second_and_carry_descriptive_user_agent()
        {
            // Given a batch of MusicBrainz lookups
            // When  requests are traced
            // Then  they never exceed 1/s and carry the descriptive User-Agent (F76.1)
            Assert.Fail(Pending);
        }
    }

    public static class ScenarioMissStamping
    {
        [Fact(Skip = Pending)]
        public static void Second_pass_makes_zero_calls_for_stamped_misses()
        {
            // Given a library with miss-stamped rows
            // When  a second enrichment pass runs
            // Then  zero MusicBrainz calls are made for stamped rows (F76.2)
            Assert.Fail(Pending);
        }
    }
}

// STORY-226 — The station checks what it actually has (SPEC F87.5, PLAN T89)
//
// BDD specification — xUnit. Owns the DECISION-TREE half of STORY-226: RequestMatcher.MatchAsync
// against a scripted FakeRequestCatalogProbe + FakeRequestStore — no Postgres needed, this is a
// component-level test of the matcher's branching alone. The catalog-probe SQL itself (ready +
// eligible + not-never-play, wildcard-escape correctness) is proven against the real database by
// GenWave.MediaLibrary.Tests' own Story226_RequestMatching.cs (that project cannot reference
// GenWave.Host, where RequestMatcher lives — see that file's own header for the full split rationale).

using GenWave.Host.Requests;

namespace GenWave.Host.Tests.Specs;

public static class FeatureRequestMatcherDecisions
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static RequestMatcher BuildMatcher(FakeRequestCatalogProbe probe, FakeRequestStore store) => new(probe, store);

    // ---------------------------------------------------------------------
    // HAPPY PATH — a catalog hit stores the matched media id (F87.5)
    // ---------------------------------------------------------------------

    public static class ScenarioCatalogHit
    {
        [Fact]
        public static async Task AHeldArtistPredicateStoresTheMatchedMediaId()
        {
            var probe = new FakeRequestCatalogProbe { Result = 42L };
            var store = new FakeRequestStore();
            var matcher = BuildMatcher(probe, store);

            await matcher.MatchAsync(1, "Led Zeppelin", null, [], CancellationToken.None);

            var call = Assert.Single(store.MarkMatchedCalls);
            Assert.Equal((1L, 42L), call);
            Assert.Empty(store.MarkUnmatchedCalls);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — a miss with a surviving mood predicate stays pending as a vibe request
    // ---------------------------------------------------------------------

    public static class ScenarioVibeFallback
    {
        [Fact]
        public static async Task AMissedArtistWithMoodsStaysPendingAsAVibeRequest()
        {
            var probe = new FakeRequestCatalogProbe { Result = null };
            var store = new FakeRequestStore();
            var matcher = BuildMatcher(probe, store);

            await matcher.MatchAsync(1, "A Band That Does Not Exist", null, ["dreamy"], CancellationToken.None);

            // Neither write happens — the row stays exactly as MarkParsedAsync already left it
            // (pending, moods already stored), ready for T90's mood-filter pick.
            Assert.Empty(store.MarkMatchedCalls);
            Assert.Empty(store.MarkUnmatchedCalls);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — a miss with nothing left to try flips to unmatched, silently (F87.5)
    // ---------------------------------------------------------------------

    public static class ScenarioNothingLeftToTry
    {
        [Fact]
        public static async Task EmptyPredicatesBecomeUnmatchedSilently()
        {
            var probe = new FakeRequestCatalogProbe { Result = null };
            var store = new FakeRequestStore();
            var matcher = BuildMatcher(probe, store);

            await matcher.MatchAsync(1, "A Band That Does Not Exist", null, [], CancellationToken.None);

            var id = Assert.Single(store.MarkUnmatchedCalls);
            Assert.Equal(1, id);
            Assert.Empty(store.MarkMatchedCalls);
        }
    }
}

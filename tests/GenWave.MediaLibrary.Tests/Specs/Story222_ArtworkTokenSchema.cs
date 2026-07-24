// STORY-222 — artwork_token seam (SPEC F88.2, PLAN T83)
//
// BDD specification — xUnit, Postgres-backed (Category=Integration), authored PENDING at
// /plan time. db/23 + the lazy-generation catalog member + token→media resolution.

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureArtworkTokenSeam
{
    public static class ScenarioLazyStableTokens
    {
        [Fact(Skip = "Pending — PLAN T83 (/build-loop)")]
        public static void FirstNeedGeneratesARandomTokenOnce()
        {
            // Given a row with no token  When the token is first requested
            // Then a 128-bit value is stored and returned.
        }

        [Fact(Skip = "Pending — PLAN T83 (/build-loop)")]
        public static void SubsequentReadsReturnTheSameToken() { }

        [Fact(Skip = "Pending — PLAN T83 (/build-loop)")]
        public static void TokensResolveBackToTheirMediaPath()
        {
            // Token → media resolution on the library connection, unknown token → null.
        }
    }

    public static class SadPathUniqueness
    {
        [Fact(Skip = "Pending — PLAN T83 (/build-loop)")]
        public static void TheUniqueIndexRejectsADuplicateToken()
        {
            // Direct SQL duplicate insert violates the unique index (DB's own teeth).
        }
    }
}

// STORY-360 — The smart home holds a key, not the keys (SPEC F145.3/.4 · PLAN T340)
using Xunit;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAnnounceToken
{
    public sealed class ScenarioGeneratedOnceHashedAtRest
    {
        [Fact(Skip = "pending T340 (STORY-360 AC1)")]
        public void GenerationReturnsThePlaintextExactlyOnce() { }

        [Fact(Skip = "pending T340 (STORY-360 AC1)")]
        public void OnlyTheHashIsStoredInSettings() { }

        [Fact(Skip = "pending T340 (STORY-360 AC1)")]
        public void NoLaterReadBackOrApiResponseContainsThePlaintext() { }
    }

    public sealed class ScenarioScopeIsExactlyTwoSurfaces
    {
        [Fact(Skip = "pending T340 (STORY-360 AC2)")]
        public void TheTokenAuthorizesTheAnnouncementsFamily() { }

        [Fact(Skip = "pending T340 (STORY-360 AC2)")]
        public void TheTokenAuthorizesTheNowPlayingRead() { }

        [Fact(Skip = "pending T340 (STORY-360 AC2)")]
        public void AnyOtherAuthenticatedRouteRefusesTheToken() { }

        [Fact(Skip = "pending T340 (STORY-360 AC5)")]
        public void AdminSessionAuthStillWorksOnTheSameRoutes() { }
    }

    public sealed class ScenarioRevocationFailsClosed
    {
        [Fact(Skip = "pending T340 (STORY-360 AC3)")]
        public void ARevokedTokenIsRefusedOnItsNextRequest() { }

        [Fact(Skip = "pending T340 (STORY-360 AC3)")]
        public void ARegeneratedTokenRefusesTheOldPlaintext() { }

        [Fact(Skip = "pending T340 (STORY-360 AC4)")]
        public void WithNoHashRowConfiguredEveryBearerTokenIsRefused() { }
    }
}

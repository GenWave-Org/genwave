// STORY-366 — My sensor works on the appliance with the admin plane off (SPEC F145.6 · PLAN T351)
//
// BDD specification — xUnit. PENDING until T351. Entry-point discipline: every fact drives the
// REAL production binary (WebApplicationFactory<Program>, the Story345 factory idiom over the
// DatabaseFixture) — two factories on the SAME station db: one with Admin:Enabled=true to mint
// the token through POST /api/announcements/token (reveal-once), then one with
// Admin:Enabled=false (the compose.demo.yaml posture) driven with ONLY that Bearer. F145.6:
// the token-authed now-playing read answers with the admin plane off; submit and the token
// endpoints stay admin-surface (404).
namespace GenWave.Host.Tests.Specs;

public static class FeatureSensorWorksWithAdminOff
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the read answers, everything else stays dark
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheReadAnswersWithTheAdminPlaneOff
    {
        // Given Admin:Enabled=false and a minted token, When GET /api/announcements/now-playing.
        [Fact(Skip = "pending T351 (STORY-366 AC1)")]
        public void TheResponseIsTwoHundred() => Assert.Fail("pending T351");

        [Fact(Skip = "pending T351 (STORY-366 AC1)")]
        public void TheBodyIsTheNowPlayingSnapshot() => Assert.Fail("pending T351");
    }

    public sealed class ScenarioSubmitStaysAdminSurface
    {
        // Same station and token, When POST /api/announcements.
        [Fact(Skip = "pending T351 (STORY-366 AC2)")]
        public void TheResponseIsFourOhFour() => Assert.Fail("pending T351");
    }

    public sealed class ScenarioTheTokenEndpointsStayAdminSurface
    {
        [Fact(Skip = "pending T351 (STORY-366 AC3)")]
        public void PostTokenIsFourOhFour() => Assert.Fail("pending T351");

        [Fact(Skip = "pending T351 (STORY-366 AC3)")]
        public void DeleteTokenIsFourOhFour() => Assert.Fail("pending T351");
    }

    public sealed class ScenarioTheReadStillWorksWithTheAdminPlaneOn
    {
        // Given Admin:Enabled=true, When the read is called with the token, and again with a cookie.
        [Fact(Skip = "pending T351 (STORY-366 AC4)")]
        public void TheTokenReadIsTwoHundred() => Assert.Fail("pending T351");

        [Fact(Skip = "pending T351 (STORY-366 AC4)")]
        public void TheCookieReadIsTwoHundred() => Assert.Fail("pending T351");
    }

    // ---------------------------------------------------------------------
    // SAD PATH — no token, no read
    // ---------------------------------------------------------------------

    public sealed class ScenarioNoTokenRowNoRead
    {
        // Given Admin:Enabled=false and no token ever minted, When any Bearer value is sent.
        [Fact(Skip = "pending T351 (STORY-366 AC6)")]
        public void TheResponseIsFourOhOne() => Assert.Fail("pending T351");
    }

    public sealed class ScenarioARevokedTokenIsRefusedOnTheReadToo
    {
        // Given a token minted then revoked (DELETE /api/announcements/token with admin on).
        [Fact(Skip = "pending T351 (STORY-366 AC7)")]
        public void TheResponseIsFourOhOne() => Assert.Fail("pending T351");
    }
}

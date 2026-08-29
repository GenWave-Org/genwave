// STORY-369 — Listeners can thumb the track that's playing (SPEC F149.4, F150.2–F150.7, F150.10 ·
// PLAN T358, T366, T368)
//
// BDD specification — xUnit. PENDING until T358/T366/T368. Entry-point discipline: every fact
// drives the REAL production binary (WebApplicationFactory<Program>, the Story345/Story366
// factory idiom over an ephemeral station+library Postgres — tests/GenWave.Host.Tests/Support/
// EphemeralStationDatabase) — two factories on the SAME db where a scenario needs it (one to seed
// a real airing via TrackAired and read the minted `airing` token off /spectator/api/now-playing,
// a second bare client to post /spectator/api/thumbs against it), the `thumbs` route limiter
// chain (per-IP cooldown + per-IP daily cap, plus the per-listener daily cap enforced in the
// action) is never bypassed, and cookie assertions read the raw Set-Cookie header (HttpOnly/
// SameSite/Path/Max-Age) rather than the client's cookie container. AC8 (T368) is the one
// exception: the spectator SPA is hand-written JS served by the production binary, so its fact
// reads the served app.js/index.html text for the thumbs pair + the localStorage "your thumbs"
// strip — the house pattern of Gh258_SpectatorStationLogo.cs — never a browser.
namespace GenWave.Host.Tests.Specs;

public static class FeatureListenersThumbTheTrackPlaying
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the token rides now-playing, a thumb lands, the page reflects it
    // ---------------------------------------------------------------------

    public sealed class ScenarioNowPlayingCarriesTheAiringToken
    {
        // Given a music track on air, When GET /spectator/api/now-playing is called.
        [Fact(Skip = "pending T358 (STORY-369 AC1)")]
        public void ThePayloadCarriesABase64UrlAiringToken() => Assert.Fail("pending T358");

        [Fact(Skip = "pending T358 (STORY-369 AC1)")]
        public void TheDisclosureContractsPropertySetGrowsByExactlyThatField() => Assert.Fail("pending T358");
    }

    public sealed class ScenarioNonMusicAiringHasANullToken
    {
        // Given an ident on air, When GET /spectator/api/now-playing is called.
        [Fact(Skip = "pending T358 (STORY-369 AC2)")]
        public void AiringIsNull() => Assert.Fail("pending T358");
    }

    public sealed class ScenarioAThumbOnTheCurrentAiringIsRecorded
    {
        // Given Station:Thumbs:Enabled true and a music track on air with token X, When
        // POST /spectator/api/thumbs {airing: X, direction: "up"} is called.
        [Fact(Skip = "pending T366 (STORY-369 AC3)")]
        public void TheResponseIsTwoOhTwoWithTheFixedBody() => Assert.Fail("pending T366");

        [Fact(Skip = "pending T366 (STORY-369 AC3)")]
        public void MediaThumbHoldsOneRowForThatMediaAndAiring() => Assert.Fail("pending T366");
    }

    public sealed class ScenarioThePreviousAiringStillResolves
    {
        // Given the track with token X just ended and the next track carries token Y, When
        // POST /spectator/api/thumbs {airing: X, direction: "down"} is called.
        [Fact(Skip = "pending T366 (STORY-369 AC4)")]
        public void TheResponseIsTwoOhTwo() => Assert.Fail("pending T366");

        [Fact(Skip = "pending T366 (STORY-369 AC4)")]
        public void TheThumbIsRecordedAgainstXsMedia() => Assert.Fail("pending T366");
    }

    public sealed class ScenarioTheListenerCookieIsMintedOnTheFirstThumb
    {
        // Given a caller with no genwave-listener cookie, When it thumbs.
        [Fact(Skip = "pending T366 (STORY-369 AC5)")]
        public void TheResponseSetsGenwaveListenerHttpOnlySameSiteLaxPathSpectatorMaxAge365Days() => Assert.Fail("pending T366");

        [Fact(Skip = "pending T366 (STORY-369 AC5)")]
        public void TheRowsListenerKeyIsTheSha256OfTheCookieToken() => Assert.Fail("pending T366");
    }

    public sealed class ScenarioARepeatThumbIsIdempotent
    {
        // Given a listener who thumbed X up, When the same listener thumbs X up again.
        [Fact(Skip = "pending T366 (STORY-369 AC6)")]
        public void MediaThumbStillHoldsOneRowForMediaAiringListener() => Assert.Fail("pending T366");
    }

    public sealed class ScenarioAFlipUpdatesTheDirection
    {
        // Given a listener who thumbed X up, When the same listener thumbs X down.
        [Fact(Skip = "pending T366 (STORY-369 AC7)")]
        public void TheOneRowsDirectionIsDown() => Assert.Fail("pending T366");

        [Fact(Skip = "pending T366 (STORY-369 AC7)")]
        public void TheAggregateReflectsMinusOneNotPlusOne() => Assert.Fail("pending T366");
    }

    public sealed class ScenarioThePageShowsThePairAndTheStrip
    {
        // Given the spectator page with thumbs enabled and a track on air, When the listener
        // presses 👍 — read from the served app.js (Gh258 house pattern), not a browser.
        [Fact(Skip = "pending T368 (STORY-369 AC8)")]
        public void TheServedAppJsRendersAYourThumbsStripFromLocalStorage() => Assert.Fail("pending T368");

        [Fact(Skip = "pending T368 (STORY-369 AC8)")]
        public void TheServedAppJsResetsThePairOnTheNextTrack() => Assert.Fail("pending T368");
    }

    // ---------------------------------------------------------------------
    // SAD PATH — off, stale, throttled, and silent about who's listening
    // ---------------------------------------------------------------------

    public sealed class ScenarioSurfaceOff
    {
        // Given Station:Thumbs:Enabled false, When POST /spectator/api/thumbs is called.
        [Fact(Skip = "pending T366 (STORY-369 AC9)")]
        public void TheResponseIsTheStandardFourOhFour() => Assert.Fail("pending T366");

        [Fact(Skip = "pending T366 (STORY-369 AC9)")]
        public void ThePageRendersNoThumbs() => Assert.Fail("pending T366");
    }

    public sealed class ScenarioAStaleOrGarbageTokenIsASilentTwoOhTwo
    {
        // Given a token older than the previous airing, or random bytes, When it is posted.
        [Fact(Skip = "pending T366 (STORY-369 AC10)")]
        public void TheResponseIsTheSameTwoOhTwoBody() => Assert.Fail("pending T366");

        [Fact(Skip = "pending T366 (STORY-369 AC10)")]
        public void NoRowIsWritten() => Assert.Fail("pending T366");
    }

    public sealed class ScenarioThePerIpCooldown
    {
        // Given a caller who thumbed 10 seconds ago, When it thumbs again from the same IP.
        [Fact(Skip = "pending T366 (STORY-369 AC11)")]
        public void TheResponseIsFourTwoNine() => Assert.Fail("pending T366");
    }

    public sealed class ScenarioThePerListenerDailyCap
    {
        // Given a listener at 60 thumbs today (cooldown respected), When it thumbs once more.
        [Fact(Skip = "pending T366 (STORY-369 AC12)")]
        public void TheResponseIsFourTwoNine() => Assert.Fail("pending T366");

        [Fact(Skip = "pending T366 (STORY-369 AC12)")]
        public void NoRowIsWritten() => Assert.Fail("pending T366");
    }

    public sealed class ScenarioNothingAboutTheListenerReachesTheLogs
    {
        // Given any thumb request, When the api log for it is read at every level.
        [Fact(Skip = "pending T366 (STORY-369 AC13)")]
        public void TheRawCookieTokenNeverAppears() => Assert.Fail("pending T366");

        [Fact(Skip = "pending T366 (STORY-369 AC13)")]
        public void TheHashedListenerKeyNeverAppears() => Assert.Fail("pending T366");
    }
}

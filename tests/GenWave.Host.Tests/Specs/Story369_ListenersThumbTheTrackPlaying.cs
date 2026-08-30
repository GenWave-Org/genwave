// STORY-369 — Listeners can thumb the track that's playing (SPEC F149.4, F150.2–F150.7, F150.10 ·
// PLAN T358, T366, T368)
//
// BDD specification — xUnit. AC1/AC2 WIRED at T358; AC3-AC13 PENDING until T366/T368. Entry-point
// discipline: every T358 fact drives the REAL production binary (WebApplicationFactory<Program>,
// the Story168_SpectatorNowPlaying.cs "unreachable ConnectionStrings:Library, no real Postgres
// needed for the in-memory now-playing path" idiom — AiringTokenRing itself has no DB dependency
// at all) and raises a genuine TrackAired through the REAL, container-resolved IStationEventSink
// (the Story367 arcs' "highest honestly reachable seam"), reading the minted token back off the
// REAL, container-resolved IAiringTokenResolver rather than fabricating one — "the token must come
// from the same publish." The now-playing SNAPSHOT itself is populated the same honest way
// Story168/Gh258/Issue160 already do it — a direct NowPlayingService.Update call — since
// PlayoutFeederService's own tick (the production wiring that stamps AiringTokenRing.Current onto
// a fresh NowPlayingSnapshot) needs a live Liquidsoap connection no unit-level spec here has; the
// snapshot is built WITH the real minted token attached, never a placeholder string. T366/T368
// facts stay exactly as PLAN T358 found them, to be wired against a real ephemeral station+library
// Postgres (tests/GenWave.Host.Tests/Support/EphemeralStationDatabase) when the thumbs write path
// and the served SPA exist.

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Host.Api;
using GenWave.Host.Playout;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureListenersThumbTheTrackPlaying
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the token rides now-playing, a thumb lands, the page reflects it
    // ---------------------------------------------------------------------

    public sealed class ScenarioNowPlayingCarriesTheAiringToken
    {
        // Given a music track on air, When GET /spectator/api/now-playing is called.
        [Fact]
        public async Task ThePayloadCarriesABase64UrlAiringToken()
        {
            var (_, body, token) = await AiringTokenTestSupport.FetchWithMusicOnAirAsync();

            var airing = body.GetProperty("airing").GetString();
            Assert.Equal(token, airing);
            // 128 bits, unpadded base64url: ceil(16 * 4 / 3) = 22 chars, url-safe alphabet only
            // (never '+'/'/'/'=' — SPEC F149.4's own "never plain base64" contract).
            Assert.Matches("^[A-Za-z0-9_-]{22}$", airing);
        }

        [Fact]
        public async Task TheDisclosureContractsPropertySetGrowsByExactlyThatField()
        {
            var (_, body, _) = await AiringTokenTestSupport.FetchWithMusicOnAirAsync();

            var actual = body.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            // The pre-T358 pinned set (Story183_DisclosureContractCompleteness.cs) plus exactly one
            // new member — the same table this fact would fail against if a SECOND field had
            // sneaked in alongside `airing`.
            var expected = new HashSet<string>(StringComparer.Ordinal)
            {
                "title", "artist", "startedAt", "durationMs", "listeners", "dj", "djAvatarUrl",
                "show", "upNext", "artworkUrl", "airing", "state", "kind",
            };

            Assert.True(actual.SetEquals(expected),
                $"expected exactly {{{string.Join(", ", expected)}}}, got {{{string.Join(", ", actual)}}}");
        }
    }

    public sealed class ScenarioNonMusicAiringHasANullToken
    {
        // Given an ident on air, When GET /spectator/api/now-playing is called.
        [Fact]
        public async Task AiringIsNull()
        {
            await using var factory = new AiringTokenWebFactory();
            var sink = factory.Services.GetRequiredService<IStationEventSink>();

            // The REAL production sink declines to mint (non-null SegmentKind) — the same TrackAired
            // shape Story367's own RotationNonMusicArc raises for an ident (SegmentKind.StationId).
            sink.Publish(new TrackAired(
                "tts:ident-1", null, null, 0.0, AiringTokenTestSupport.StartedAt, 1_200,
                SegmentKind: SegmentKind.StationId));

            var store = factory.Services.GetRequiredService<NowPlayingService>();
            store.Update(SingleStation.IdString, new NowPlayingSnapshot(
                MediaId: "tts:ident-1", Title: "Ident text — operator content", Artist: null,
                GainDb: 0, StartedAt: AiringTokenTestSupport.StartedAt, DurationMs: 1_200, IsDrain: false));

            var client = factory.CreateClient();
            var response = await client.GetAsync("/spectator/api/now-playing");
            var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

            // SpectatorPatterNowPlaying (the shape a "tts:*" MediaId routes to) simply has no
            // `airing` member — absence-by-construction, the same F62.9 posture "no title field" and
            // "no artist field" already use for patter (Story168_SpectatorNowPlaying.cs).
            Assert.False(body.TryGetProperty("airing", out _));
        }
    }

    // ---------------------------------------------------------------------
    // THE RING — current + previous only (F150.4 grace), never a third
    // ---------------------------------------------------------------------

    /// <summary>
    /// One extra fact-set beyond STORY-369's own acceptance criteria (PLAN T358's own build note):
    /// two consecutive music airings mint two different tokens, the first still resolves once the
    /// second is current (SPEC F150.4's grace across a track change), and a third airing evicts it.
    /// Drives <see cref="IAiringTokenResolver"/> directly, container-resolved off the REAL
    /// production binary — a lower seam than the HTTP now-playing surface (which only ever exposes
    /// the CURRENT token, never the previous one), but still the real, composed
    /// <see cref="AiringTokenRing"/> singleton, never a hand-built instance.
    /// </summary>
    public sealed class ScenarioTheRingKeepsExactlyCurrentAndPrevious : IDisposable
    {
        readonly WebApplicationFactory<Program> factory = new AiringTokenWebFactory();
        readonly IStationEventSink sink;
        readonly IAiringTokenResolver resolver;
        readonly string tokenOne;
        readonly string tokenTwo;
        readonly bool firstResolvesAfterSecond;

        public ScenarioTheRingKeepsExactlyCurrentAndPrevious()
        {
            sink = factory.Services.GetRequiredService<IStationEventSink>();
            resolver = factory.Services.GetRequiredService<IAiringTokenResolver>();

            sink.Publish(new TrackAired("101", "Song One", "Artist One", 0.0, AiringTokenTestSupport.StartedAt, 180_000));
            tokenOne = resolver.Current
                ?? throw new InvalidOperationException("expected AiringTokenRing to mint a token for the first airing");

            sink.Publish(new TrackAired("102", "Song Two", "Artist Two", 0.0, AiringTokenTestSupport.StartedAt, 180_000));
            tokenTwo = resolver.Current
                ?? throw new InvalidOperationException("expected AiringTokenRing to mint a token for the second airing");
            firstResolvesAfterSecond = resolver.TryResolve(tokenOne, out _, out _);
        }

        // Given two consecutive music airings, When their tokens are compared.
        [Fact]
        public void TheSecondAiringMintsADifferentTokenThanTheFirst() => Assert.NotEqual(tokenOne, tokenTwo);

        // Given the second airing already happened, When the first token is resolved (F150.4 grace).
        [Fact]
        public void TheFirstTokenStillResolvesAfterTheSecondAirs() => Assert.True(firstResolvesAfterSecond);

        // Given a third consecutive airing, When the first (now two airings back) token is resolved.
        [Fact]
        public void AThirdAiringEvictsTheFirstToken()
        {
            sink.Publish(new TrackAired("103", "Song Three", "Artist Three", 0.0, AiringTokenTestSupport.StartedAt, 180_000));

            Assert.False(resolver.TryResolve(tokenOne, out _, out _));
        }

        public void Dispose() => factory.Dispose();
    }

    /// <summary>
    /// PLAN T358 review MED-1: <see cref="IAiringTokenResolver.Current"/> alone survives an
    /// intervening non-music item (SPEC F150.4's grace — see that property's own remarks), but the
    /// SNAPSHOT built for that non-music item must never carry it. This scenario hand-builds the
    /// ident's own <see cref="NowPlayingSnapshot"/> the SAME gated way
    /// <c>PlayoutFeederService.PublishSnapshot</c> now does (<see cref="MusicAiring.IsMusicMediaId"/>),
    /// since this scenario drives no live feeder tick — proving the record's "null for non-music"
    /// contract holds by construction, not merely because SpectatorController happens to route
    /// idents to a member-less DTO.
    /// </summary>
    public sealed class ScenarioTheSnapshotSuppressesTheStaleTokenAcrossANonMusicItem : IDisposable
    {
        const string IdentMediaId = "tts:ident-1";

        readonly WebApplicationFactory<Program> factory = new AiringTokenWebFactory();
        readonly IAiringTokenResolver resolver;
        readonly string musicToken;
        readonly NowPlayingSnapshot identSnapshot;

        public ScenarioTheSnapshotSuppressesTheStaleTokenAcrossANonMusicItem()
        {
            var sink = factory.Services.GetRequiredService<IStationEventSink>();
            resolver = factory.Services.GetRequiredService<IAiringTokenResolver>();

            sink.Publish(new TrackAired("201", "Song One", "Artist One", 0.0, AiringTokenTestSupport.StartedAt, 180_000));
            musicToken = resolver.Current
                ?? throw new InvalidOperationException("expected AiringTokenRing to mint a token for the music airing");

            // The ident that follows: the REAL sink declines to mint (non-null SegmentKind), so
            // resolver.Current is untouched by this Publish — still musicToken, by design.
            sink.Publish(new TrackAired(
                IdentMediaId, null, null, 0.0, AiringTokenTestSupport.StartedAt, 1_200,
                SegmentKind: SegmentKind.StationId));

            identSnapshot = new NowPlayingSnapshot(
                MediaId: IdentMediaId, Title: "Ident text — operator content", Artist: null,
                GainDb: 0, StartedAt: AiringTokenTestSupport.StartedAt, DurationMs: 1_200, IsDrain: false,
                Airing: MusicAiring.IsMusicMediaId(IdentMediaId) ? resolver.Current : null);

            var store = factory.Services.GetRequiredService<NowPlayingService>();
            store.Update(SingleStation.IdString, identSnapshot);
        }

        // Given a music airing followed by an ident, When the ident's own snapshot is inspected.
        [Fact]
        public void TheSnapshotsAiringIsNull() => Assert.Null(identSnapshot.Airing);

        // Given the same sequence, When the earlier music token is resolved (F150.4 grace holds
        // even though the ident's own snapshot no longer carries it).
        [Fact]
        public void TheEarlierMusicTokenStillResolves() => Assert.True(resolver.TryResolve(musicToken, out _, out _));

        public void Dispose() => factory.Dispose();
    }

    [Collection(ThumbsWriteCollection.Name)]
    public sealed class ScenarioAThumbOnTheCurrentAiringIsRecorded(ThumbsWriteArc arc)
    {
        // Given Station:Thumbs:Enabled true and a music track on air with token X, When
        // POST /spectator/api/thumbs {airing: X, direction: "up"} is called.
        [Fact]
        public void TheResponseIsTwoOhTwoWithTheFixedBody()
        {
            Assert.Equal(HttpStatusCode.Accepted, arc.Status3);
            Assert.Equal(
                JsonSerializer.Serialize(new SpectatorThumbAccepted(), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                arc.Body3Text);
        }

        [Fact]
        public void MediaThumbHoldsOneRowForThatMediaAndAiring()
        {
            Assert.True(arc.Row3Exists);
            Assert.Equal("up", arc.Row3Direction);
        }
    }

    [Collection(ThumbsWriteCollection.Name)]
    public sealed class ScenarioThePreviousAiringStillResolves(ThumbsWriteArc arc)
    {
        // Given the track with token X just ended and the next track carries token Y, When
        // POST /spectator/api/thumbs {airing: X, direction: "down"} is called.
        [Fact]
        public void TheResponseIsTwoOhTwo() => Assert.Equal(HttpStatusCode.Accepted, arc.Status4);

        [Fact]
        public void TheThumbIsRecordedAgainstXsMedia()
        {
            Assert.True(arc.Row4Exists);
            Assert.Equal("down", arc.Row4Direction);
        }
    }

    [Collection(ThumbsWriteCollection.Name)]
    public sealed class ScenarioTheListenerCookieIsMintedOnTheFirstThumb(ThumbsWriteArc arc)
    {
        // Given a caller with no genwave-listener cookie, When it thumbs.
        [Fact]
        public void TheResponseSetsGenwaveListenerHttpOnlySameSiteLaxPathSpectatorMaxAge365Days()
        {
            var setCookie = arc.SetCookieHeader ?? throw new InvalidOperationException("expected a Set-Cookie header");
            var cookie = ThumbsWriteArc.ParseSetCookie(setCookie);

            Assert.True(cookie.HttpOnly);
            // ASP.NET Core's own cookie writer lowercases the SameSite token on the wire — the
            // attribute's case carries no semantic weight (RFC 6265bis treats it as an
            // case-insensitive token), so this compares case-insensitively rather than pinning an
            // implementation detail of the framework's own writer.
            Assert.Equal("Lax", cookie.SameSite, ignoreCase: true);
            Assert.Equal("/spectator", cookie.Path);
            Assert.Equal(365L * 24 * 3600, cookie.MaxAgeSeconds);
            // TestServer talks plain http — Secure is only ever stamped when Request.IsHttps.
            Assert.False(cookie.Secure);
        }

        [Fact]
        public void TheRowsListenerKeyIsTheSha256OfTheCookieToken() =>
            Assert.Equal(arc.ExpectedListenerKey, arc.Row3ListenerKeyColumn);
    }

    [Collection(ThumbsWriteCollection.Name)]
    public sealed class ScenarioARepeatThumbIsIdempotent(ThumbsWriteArc arc)
    {
        // Given a listener who thumbed X up, When the same listener thumbs X up again.
        [Fact]
        public void MediaThumbStillHoldsOneRowForMediaAiringListener()
        {
            Assert.Equal(1, arc.RepeatRowCount);
            Assert.Equal("up", arc.RepeatDirection);
        }
    }

    [Collection(ThumbsWriteCollection.Name)]
    public sealed class ScenarioAFlipUpdatesTheDirection(ThumbsWriteArc arc)
    {
        // Given a listener who thumbed X up, When the same listener thumbs X down.
        [Fact]
        public void TheOneRowsDirectionIsDown()
        {
            Assert.Equal(1, arc.FlipRowCount);
            Assert.Equal("down", arc.FlipDirection);
        }

        [Fact]
        public void TheAggregateReflectsMinusOneNotPlusOne() => Assert.True(arc.FlipNudge < 0,
            $"expected a negative nudge after the up->down flip, got {arc.FlipNudge}");
    }

    public sealed class ScenarioThePageShowsThePairAndTheStrip
    {
        // Given the spectator page with thumbs enabled and a track on air, When the listener
        // presses 👍 — read from the served app.js (Gh258/Issue160 house pattern: pin the SOURCE,
        // never drive a browser here — the orchestrator's own Playwright pass owns the real click).
        [Fact]
        public async Task TheServedAppJsRendersAYourThumbsStripFromLocalStorage()
        {
            await using var factory = new AiringTokenWebFactory();
            var client = factory.CreateClient();

            var js = await client.GetStringAsync("/spectator/app.js");

            // Distinctive identifiers — the exact selectors the orchestrator's Playwright smoke
            // targets: the pair's container id, the two buttons' accessible names, the throttle
            // message's id, and the strip's storage key/container/clear-control ids.
            Assert.Contains("\"now-playing-thumbs\"", js, StringComparison.Ordinal);
            Assert.Contains("\"Thumbs up\"", js, StringComparison.Ordinal);
            Assert.Contains("\"Thumbs down\"", js, StringComparison.Ordinal);
            Assert.Contains("\"thumbs-message\"", js, StringComparison.Ordinal);
            Assert.Contains("\"genwave-thumbs\"", js, StringComparison.Ordinal);
            Assert.Contains("\"thumbs-strip\"", js, StringComparison.Ordinal);
            Assert.Contains("\"thumbs-strip-list\"", js, StringComparison.Ordinal);
            Assert.Contains("\"thumbs-strip-clear\"", js, StringComparison.Ordinal);
            Assert.Contains("localStorage", js, StringComparison.Ordinal);

            // The house no-innerHTML-from-data rule: the served script never assigns to
            // .innerHTML anywhere — every node (the pair, the strip's rows) is built with
            // createElement/textContent instead (SPEC F150.10, renderHistory's own precedent).
            // The word "innerHTML" DOES appear in this file's own remarks explaining that rule —
            // this only fails on a genuine assignment.
            Assert.DoesNotMatch(new Regex(@"\.innerHTML\s*="), js);
        }

        [Fact]
        public async Task TheServedAppJsResetsThePairOnTheNextTrack()
        {
            await using var factory = new AiringTokenWebFactory();
            var client = factory.CreateClient();

            var js = await client.GetStringAsync("/spectator/app.js");

            // The reset-on-track-change logic (SPEC F150.10): a changed airing token clears both
            // buttons' pressed state.
            Assert.Contains("airing !== thumbsAiring", js, StringComparison.Ordinal);
            Assert.Contains("thumbsPressedDirection = null", js, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — off, stale, throttled, and silent about who's listening
    // ---------------------------------------------------------------------

    public sealed class ScenarioSurfaceOff
    {
        // Given Station:Thumbs:Enabled false, When POST /spectator/api/thumbs is called.
        [Fact]
        public async Task TheResponseIsTheStandardFourOhFour()
        {
            // AiringTokenWebFactory (below) never sets Station:Thumbs:Enabled — the deployment
            // default (false, StationThumbsOptions) applies, so this reuses the T358 harness as-is.
            await using var factory = new AiringTokenWebFactory();
            var client = factory.CreateClient();

            var response = await client.PostAsJsonAsync(
                "/spectator/api/thumbs", new { airing = "AAAAAAAAAAAAAAAAAAAAAA", direction = "up" });

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        // T368 — AC9's page half: the served index.html carries NO thumbs markup at all,
        // regardless of Station:Thumbs:Enabled — the pair only ever exists once app.js's presence
        // probe (ensureThumbsSection) builds it at runtime. This is therefore true unconditionally
        // (a static-HTML fact, the Gh258 house pattern), not merely "when off": there is nothing
        // pre-rendered for the probe to have to un-render either way.
        [Fact]
        public async Task ThePageRendersNoThumbs()
        {
            await using var factory = new AiringTokenWebFactory(); // Station:Thumbs:Enabled defaults false
            var client = factory.CreateClient();

            var html = await client.GetStringAsync("/spectator");

            Assert.DoesNotContain("thumbs-up", html, StringComparison.Ordinal);
            Assert.DoesNotContain("thumbs-down", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Thumbs up", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Thumbs down", html, StringComparison.Ordinal);
            Assert.DoesNotContain("thumbs-strip", html, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// T368 review fix (orchestrator ruling, 2026-08-30): the original build found that a bare GET
    /// on the POST-only route always answered 405 via ASP.NET Core's own method-mismatch fallback,
    /// regardless of <c>Station:Thumbs:Enabled</c> — that fallback endpoint carries none of
    /// <c>SpectatorThumbsController</c>'s attribute metadata, so <see cref="SurfaceGateMiddleware"/>
    /// never got a chance to gate it. The fix, <c>SpectatorThumbsController.ProbeThumbsPresence</c>,
    /// maps a REAL <c>[HttpGet("thumbs")]</c> action carrying the same
    /// <c>[SpectatorSurface]</c>/<c>[ThumbsSurface]</c> tags <c>PostThumb</c> carries, so the surface
    /// gate now correctly sees it: 404 when off, 204 with no body when on — see that action's own
    /// remarks for why it also overrides the class-level <see cref="RateLimiterPolicies.Thumbs"/>
    /// policy down to <see cref="RateLimiterPolicies.Spectator"/> for GET specifically (a 5-minute
    /// re-probe must never spend the write path's per-IP daily cap).
    /// </summary>
    public sealed class ScenarioThePresenceProbe
    {
        // Given Station:Thumbs:Enabled false, When a bare GET is made on this route.
        [Fact]
        public async Task AGetAnswersFourOhFourWhenThumbsIsOff()
        {
            await using var factory = new AiringTokenWebFactory(); // Thumbs:Enabled defaults false
            var client = factory.CreateClient();

            var response = await client.GetAsync("/spectator/api/thumbs");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        // Given Station:Thumbs:Enabled true, When the same bare GET is made.
        [Fact]
        public async Task AGetAnswersTwoOhFourWhenThumbsIsOn()
        {
            await using var factory = new ThumbsPresenceOnWebFactory();
            var client = factory.CreateClient();

            var response = await client.GetAsync("/spectator/api/thumbs");

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        // T368 review LOW-1: a bare 204/404 with no explicit Cache-Control is heuristically
        // cacheable with no bound, so a fronting cache could pin the ON state past app.js's own
        // ~5-minute re-probe. Compared directly against a live now-playing GET rather than a
        // hardcoded string, so this never drifts from GetNowPlaying's own [SpectatorCacheControl(5)]
        // if that tier ever changes.
        [Fact]
        public async Task TheOnResponseCarriesTheSameCacheControlTierAsNowPlaying()
        {
            await using var factory = new ThumbsPresenceOnWebFactory();
            var client = factory.CreateClient();

            var probe = await client.GetAsync("/spectator/api/thumbs");
            var nowPlaying = await client.GetAsync("/spectator/api/now-playing");

            Assert.Equal(nowPlaying.Headers.CacheControl?.ToString(), probe.Headers.CacheControl?.ToString());
            Assert.Equal("public, max-age=5", probe.Headers.CacheControl?.ToString());
        }

        // Given Station:Thumbs:Enabled true, When the probe is made, Then it discloses nothing
        // (SPEC F150.2 — no read endpoint for thumbs themselves) and never mints the
        // listener-identity cookie (SPEC F150.6): only POST ever calls ResolveListenerKey, so a
        // caller who only ever probes never becomes a counted listener.
        [Fact]
        public async Task TheProbeCarriesNoBodyAndMintsNoListenerCookie()
        {
            await using var factory = new ThumbsPresenceOnWebFactory();
            var client = factory.CreateClient();

            var response = await client.GetAsync("/spectator/api/thumbs");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Empty(body);
            Assert.False(
                response.Headers.TryGetValues("Set-Cookie", out _),
                "the presence probe must never mint genwave-listener — only POST does");
        }
    }

    [Collection(ThumbsWriteCollection.Name)]
    public sealed class ScenarioAStaleOrGarbageTokenIsASilentTwoOhTwo(ThumbsWriteArc arc)
    {
        // Given a token older than the previous airing, or random bytes, When it is posted.
        [Fact]
        public void TheResponseIsTheSameTwoOhTwoBody()
        {
            Assert.Equal(HttpStatusCode.Accepted, arc.Status10);
            Assert.Equal(arc.Body3Text, arc.Body10Text);
        }

        [Fact]
        public void NoRowIsWritten() => Assert.Equal(arc.ThumbCountBeforeAc10, arc.ThumbCountAfterAc10);
    }

    [Collection(ThumbsWriteCollection.Name)]
    public sealed class ScenarioThePerIpCooldown(ThumbsWriteArc arc)
    {
        // Given a caller who thumbed 10 seconds ago, When it thumbs again from the same IP.
        [Fact]
        public void TheResponseIsFourTwoNine()
        {
            Assert.Equal(HttpStatusCode.Accepted, arc.Status11First);
            Assert.Equal(HttpStatusCode.TooManyRequests, arc.Status11Second);
        }
    }

    [Collection(ThumbsWriteCollection.Name)]
    public sealed class ScenarioThePerListenerDailyCap(ThumbsWriteArc arc)
    {
        // Given a listener at the daily cap already (cooldown respected — a fresh factory per
        // call), When it thumbs once more.
        [Fact]
        public void TheResponseIsFourTwoNine() => Assert.Equal(HttpStatusCode.TooManyRequests, arc.Status12);

        [Fact]
        public void NoRowIsWritten() => Assert.Equal(arc.SeededDailyCap, arc.RowCountForCappedListenerAfter);
    }

    [Collection(ThumbsWriteCollection.Name)]
    public sealed class ScenarioNothingAboutTheListenerReachesTheLogs(ThumbsWriteArc arc)
    {
        // Given any thumb request, When the api log for it is read at every level.
        [Fact]
        public void TheRawCookieTokenNeverAppears() =>
            Assert.DoesNotContain(arc.LogMessages, message => message.Contains(arc.ListenerToken, StringComparison.Ordinal));

        [Fact]
        public void TheHashedListenerKeyNeverAppears() =>
            Assert.DoesNotContain(arc.LogMessages, message => message.Contains(arc.ExpectedListenerKey, StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------
    // T366 review MED-3 — the listener cookie's Secure flag follows Request.IsHttps, which only
    // ever reflects the edge's real scheme when X-Forwarded-Proto arrives from a hop inside
    // Proxy:TrustedNetworks (Program.cs). Neither fact below touches Postgres: a garbage token
    // never reaches the per-listener DB read (T366 review MED-1's own reorder), so the cookie mint
    // is all either fact needs to observe.
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheSecureCookieFlagFollowsAnXForwardedProtoFromATrustedHop
    {
        const string TrustedCidr = "10.10.10.0/24";
        const string TrustedHopAddress = "10.10.10.5";
        const string UntrustedHopAddress = "8.8.8.8";

        static Task<HttpResponseMessage> PostWithSimulatedHopAsync(WebApplicationFactory<Program> factory, string remoteIp)
        {
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            client.DefaultRequestHeaders.Add(RemoteIpStartupFilter.TestIpHeaderName, remoteIp);
            client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
            return client.PostAsJsonAsync(
                "/spectator/api/thumbs", new { airing = "AAAAAAAAAAAAAAAAAAAAAA", direction = "up" });
        }

        // Given X-Forwarded-Proto: https from an address INSIDE Proxy:TrustedNetworks, When it thumbs.
        [Fact]
        public async Task ASecureFlagIsStampedWhenTheProxyHopIsTrusted()
        {
            await using var factory = new ForwardedProtoThumbsWebFactory(TrustedCidr);

            var response = await PostWithSimulatedHopAsync(factory, TrustedHopAddress);

            Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies), "expected a Set-Cookie header");
            Assert.Contains("Secure", cookies!.First(), StringComparison.OrdinalIgnoreCase);
        }

        // Given the SAME header from an address OUTSIDE Proxy:TrustedNetworks — the middleware never
        // applies an untrusted hop's X-Forwarded-Proto, so Request.Scheme (and therefore Request.IsHttps)
        // stays exactly what TestServer's own plain-http connection reports.
        [Fact]
        public async Task NoSecureFlagWhenTheProxyHopIsUntrusted()
        {
            await using var factory = new ForwardedProtoThumbsWebFactory(TrustedCidr);

            var response = await PostWithSimulatedHopAsync(factory, UntrustedHopAddress);

            Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies), "expected a Set-Cookie header");
            Assert.DoesNotContain("Secure", cookies!.First(), StringComparison.OrdinalIgnoreCase);
        }
    }
}

// ── T358 test harness ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Boots the real production composition root against an UNREACHABLE library connection string —
/// the Story168_SpectatorNowPlaying.cs precedent: nothing on the now-playing read path (in-memory
/// <see cref="NowPlayingService"/>, the in-memory <see cref="AiringTokenRing"/>) ever opens it, so no
/// ephemeral Postgres is provisioned for these facts at all.
/// </summary>
file sealed class AiringTokenWebFactory() : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Station:SpectatorMode", "true");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-story369-airing");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
        });
    }
}

/// <summary>The AiringTokenWebFactory precedent immediately above, with
/// <c>Station:Thumbs:Enabled</c> forced true instead of left at its false default — the "on" half
/// of <see cref="ScenarioThePresenceProbe"/>. Unreachable <c>ConnectionStrings:Library</c> still
/// suffices: <c>ProbeThumbsPresence</c> is a bare <c>NoContent()</c> that never resolves
/// <see cref="IThumbStore"/>/<see cref="IAiringTokenResolver"/>, so this needs no real Postgres
/// either.</summary>
file sealed class ThumbsPresenceOnWebFactory() : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Station:SpectatorMode", "true");
        builder.UseSetting("Station:Thumbs:Enabled", "true");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-story369-presence-on");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
        });
    }
}

/// <summary>Shared arrange helper for the AC1 facts — raises a real music <see cref="TrackAired"/>
/// through the REAL, container-resolved <see cref="IStationEventSink"/>, reads the token it minted
/// back off the REAL, container-resolved <see cref="IAiringTokenResolver"/> ("the token must come
/// from the same publish"), then publishes a matching <see cref="NowPlayingSnapshot"/> carrying that
/// SAME token — the honest stand-in for what <c>PlayoutFeederService</c>'s own tick would do with a
/// live engine connection (see this file's own header remarks).</summary>
file static class AiringTokenTestSupport
{
    public static readonly DateTimeOffset StartedAt = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);

    public static async Task<(HttpStatusCode Status, JsonElement Body, string Token)> FetchWithMusicOnAirAsync()
    {
        await using var factory = new AiringTokenWebFactory();
        var sink = factory.Services.GetRequiredService<IStationEventSink>();
        var resolver = factory.Services.GetRequiredService<IAiringTokenResolver>();

        sink.Publish(new TrackAired("42", "Night Drive", "The Waveforms", -2.5, StartedAt, 214_000));
        var token = resolver.Current
            ?? throw new InvalidOperationException("expected AiringTokenRing to mint a token for a music TrackAired");

        var store = factory.Services.GetRequiredService<NowPlayingService>();
        store.Update(SingleStation.IdString, new NowPlayingSnapshot(
            MediaId: "42", Title: "Night Drive", Artist: "The Waveforms",
            GainDb: -2.5, StartedAt: StartedAt, DurationMs: 214_000, IsDrain: false, Airing: token));

        var client = factory.CreateClient();
        var response = await client.GetAsync("/spectator/api/now-playing");
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return (response.StatusCode, body, token);
    }
}

// ── T366 write-path harness — every DB-backed fact (AC3, AC4, AC5, AC6, AC7, AC10, AC11, AC12, AC13)
// shares ONE ephemeral station+library Postgres (the Story345/Story366 EphemeralStationDatabase
// idiom) across MULTIPLE WebApplicationFactory instances — the Story366 "two factories on one db"
// idiom, needed here because AiringTokenRing and the RateLimiterPolicies.Thumbs chain are BOTH
// per-factory in-memory state: a fresh factory instance buys a fresh, un-throttled no-remote-ip
// partition for a scenario that needs its own independent POST, while every factory still points at
// the SAME database so rows written by an earlier factory are visible to a later one's own reads. ──

[CollectionDefinition(Name)]
public sealed class ThumbsWriteCollection : ICollectionFixture<ThumbsWriteArc>
{
    public const string Name = "Story369ThumbsWrite";
}

/// <summary>
/// Arranges ONE ephemeral Postgres and drives every T366 write-path fact through the REAL
/// production binary, capturing the values each Scenario class above reads (the Story366/367 "Arc
/// does the arranging, Facts just assert" shape, IAsyncLifetime.InitializeAsync).
/// </summary>
public sealed class ThumbsWriteArc : IAsyncLifetime
{
    const string Route = "/spectator/api/thumbs";

    // AC3 / AC5 / AC13
    public HttpStatusCode Status3 { get; private set; }
    public string Body3Text { get; private set; } = "";
    public bool Row3Exists { get; private set; }
    public string Row3Direction { get; private set; } = "";
    public string? SetCookieHeader { get; private set; }
    public string? Row3ListenerKeyColumn { get; private set; }
    public string ListenerToken { get; private set; } = "";
    public string ExpectedListenerKey { get; private set; } = "";
    public IReadOnlyList<string> LogMessages { get; private set; } = [];

    // AC4
    public HttpStatusCode Status4 { get; private set; }
    public bool Row4Exists { get; private set; }
    public string Row4Direction { get; private set; } = "";

    // AC6 + AC7
    public int RepeatRowCount { get; private set; }
    public string RepeatDirection { get; private set; } = "";
    public int FlipRowCount { get; private set; }
    public string FlipDirection { get; private set; } = "";
    public double FlipNudge { get; private set; }

    // AC10
    public HttpStatusCode Status10 { get; private set; }
    public string Body10Text { get; private set; } = "";
    public int ThumbCountBeforeAc10 { get; private set; }
    public int ThumbCountAfterAc10 { get; private set; }

    // AC11
    public HttpStatusCode Status11First { get; private set; }
    public HttpStatusCode Status11Second { get; private set; }

    // AC12
    public int SeededDailyCap { get; private set; }
    public HttpStatusCode Status12 { get; private set; }
    public int RowCountForCappedListenerAfter { get; private set; }

    public async Task InitializeAsync()
    {
        await using var db = await ThumbsStationDatabase.StartAsync();

        // ── AC3 + AC5 + AC13: the first-ever thumb from this listener, no cookie sent — the server
        // must mint one (SPEC F150.6). A dedicated logging provider captures every reachable level
        // so AC13 can prove neither the raw token nor the derived key ever reaches a log line.
        var logs = new CapturingAllLevelsLoggerProvider();
        var mediaOne = await GardenerSeedFixtures.InsertMediaRowAsync(db.LibraryConnectionString, "/test/thumbs-ac3.flac");
        var startedOne = DateTimeOffset.Parse("2026-08-01T10:00:00Z");

        await using (var factory1 = new ThumbsWebFactory(db, logs: logs))
        {
            var sink = factory1.Services.GetRequiredService<IStationEventSink>();
            var resolver = factory1.Services.GetRequiredService<IAiringTokenResolver>();
            sink.Publish(new TrackAired(mediaOne.ToString(), "AC3 Song", "AC3 Artist", 0.0, startedOne, 180_000));
            var tokenOne = resolver.Current ?? throw new InvalidOperationException("expected a minted token");

            var client1 = factory1.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            var response1 = await client1.PostAsJsonAsync(Route, new { airing = tokenOne, direction = "up" });
            Status3 = response1.StatusCode;
            Body3Text = await response1.Content.ReadAsStringAsync();

            if (response1.Headers.TryGetValues("Set-Cookie", out var cookies))
                SetCookieHeader = cookies.First();
            var setCookie = SetCookieHeader ?? throw new InvalidOperationException("expected a Set-Cookie: genwave-listener header");

            // The server-minted cookie value IS this arc's listener token from here on — a real
            // client would store and resend exactly this (production behavior), so every later POST
            // in this arc authenticates as the SAME listener via the SAME raw value.
            ListenerToken = ThumbsWriteArc.ParseSetCookie(setCookie).Value;
            ExpectedListenerKey = ComputeListenerKey(ListenerToken);

            var row3 = await ThumbTestFixtures.ReadThumbRowAsync(db.LibraryConnectionString, mediaOne, startedOne, ExpectedListenerKey);
            Row3Exists = row3 is not null;
            Row3Direction = row3?.Direction ?? "";
            Row3ListenerKeyColumn = await ThumbTestFixtures.ReadListenerKeyAsync(db.LibraryConnectionString, mediaOne, startedOne);

            LogMessages = logs.Messages;
        }

        // ── AC4: two consecutive airings, then a thumb on the FIRST (now the previous) token.
        var mediaTwo = await GardenerSeedFixtures.InsertMediaRowAsync(db.LibraryConnectionString, "/test/thumbs-ac4-first.flac");
        var mediaThree = await GardenerSeedFixtures.InsertMediaRowAsync(db.LibraryConnectionString, "/test/thumbs-ac4-second.flac");
        var startedTwo = DateTimeOffset.Parse("2026-08-01T11:00:00Z");
        var startedThree = DateTimeOffset.Parse("2026-08-01T11:03:00Z");

        await using (var factory2 = new ThumbsWebFactory(db))
        {
            var sink = factory2.Services.GetRequiredService<IStationEventSink>();
            var resolver = factory2.Services.GetRequiredService<IAiringTokenResolver>();
            sink.Publish(new TrackAired(mediaTwo.ToString(), "AC4 First", "AC4 Artist", 0.0, startedTwo, 180_000));
            var tokenX = resolver.Current ?? throw new InvalidOperationException("expected a minted token");
            sink.Publish(new TrackAired(mediaThree.ToString(), "AC4 Second", "AC4 Artist", 0.0, startedThree, 180_000));

            var client2 = ClientWithListenerCookie(factory2, ListenerToken);
            var response2 = await client2.PostAsJsonAsync(Route, new { airing = tokenX, direction = "down" });
            Status4 = response2.StatusCode;

            var row4 = await ThumbTestFixtures.ReadThumbRowAsync(db.LibraryConnectionString, mediaTwo, startedTwo, ExpectedListenerKey);
            Row4Exists = row4 is not null;
            Row4Direction = row4?.Direction ?? "";
        }

        // ── AC6 (idempotent repeat) + AC7 (flip): three POSTs against the SAME token from the SAME
        // factory — the token ring is per-factory in-memory, so all three must share one factory. A
        // short cooldown plus a real wall-clock wait between calls lets each POST clear the SAME
        // factory's own Thumbs limiter honestly — the arc still drives the REAL rate-limiter chain,
        // never bypassed.
        var mediaFour = await GardenerSeedFixtures.InsertMediaRowAsync(db.LibraryConnectionString, "/test/thumbs-ac6-ac7.flac");
        var startedFour = DateTimeOffset.Parse("2026-08-01T12:00:00Z");

        await using (var factory3 = new ThumbsWebFactory(db, cooldownSeconds: 1))
        {
            var sink = factory3.Services.GetRequiredService<IStationEventSink>();
            var resolver = factory3.Services.GetRequiredService<IAiringTokenResolver>();
            sink.Publish(new TrackAired(mediaFour.ToString(), "AC6 Song", "AC6 Artist", 0.0, startedFour, 180_000));
            var tokenZ = resolver.Current ?? throw new InvalidOperationException("expected a minted token");

            var client3 = ClientWithListenerCookie(factory3, ListenerToken);
            await client3.PostAsJsonAsync(Route, new { airing = tokenZ, direction = "up" });

            await Task.Delay(TimeSpan.FromSeconds(1.2));
            await client3.PostAsJsonAsync(Route, new { airing = tokenZ, direction = "up" });

            RepeatRowCount = await ThumbTestFixtures.CountThumbRowsAsync(db.LibraryConnectionString, mediaFour, startedFour, ExpectedListenerKey);
            var repeatRow = await ThumbTestFixtures.ReadThumbRowAsync(db.LibraryConnectionString, mediaFour, startedFour, ExpectedListenerKey);
            RepeatDirection = repeatRow?.Direction ?? "";

            await Task.Delay(TimeSpan.FromSeconds(1.2));
            await client3.PostAsJsonAsync(Route, new { airing = tokenZ, direction = "down" });

            FlipRowCount = await ThumbTestFixtures.CountThumbRowsAsync(db.LibraryConnectionString, mediaFour, startedFour, ExpectedListenerKey);
            var flipRow = await ThumbTestFixtures.ReadThumbRowAsync(db.LibraryConnectionString, mediaFour, startedFour, ExpectedListenerKey);
            FlipDirection = flipRow?.Direction ?? "";
            FlipNudge = await ThumbTestFixtures.ReadNudgeAsync(db.LibraryConnectionString, mediaFour);
        }

        // ── AC10: a syntactically valid but never-minted token — the same silent 202, no row.
        await using (var factory4 = new ThumbsWebFactory(db))
        {
            ThumbCountBeforeAc10 = await ThumbTestFixtures.CountAllThumbRowsAsync(db.LibraryConnectionString);

            var client4 = ClientWithListenerCookie(factory4, ListenerToken);
            var response4 = await client4.PostAsJsonAsync(Route, new { airing = "AAAAAAAAAAAAAAAAAAAAAA", direction = "up" });
            Status10 = response4.StatusCode;
            Body10Text = await response4.Content.ReadAsStringAsync();

            ThumbCountAfterAc10 = await ThumbTestFixtures.CountAllThumbRowsAsync(db.LibraryConnectionString);
        }

        // ── AC11: per-IP cooldown — two rapid calls from the same (shared, no-remote-ip) TestServer
        // partition (RateLimiterPolicies' own documented, intentional fallback behavior).
        await using (var factory5 = new ThumbsWebFactory(db))
        {
            var client5 = factory5.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            var first = await client5.PostAsJsonAsync(Route, new { airing = "BBBBBBBBBBBBBBBBBBBBBB", direction = "up" });
            var second = await client5.PostAsJsonAsync(Route, new { airing = "CCCCCCCCCCCCCCCCCCCCCC", direction = "up" });
            Status11First = first.StatusCode;
            Status11Second = second.StatusCode;
        }

        // ── AC12: the F150.5 PER-LISTENER daily cap — seeded directly against library.media_thumb
        // (the PLAN T366 "seed N rows for the listener_key directly" instruction), with the cap
        // overridden down from the default 60 so this fact seeds a handful of rows, not sixty.
        const int dailyCap = 3;
        SeededDailyCap = dailyCap;
        var mediaFive = await GardenerSeedFixtures.InsertMediaRowAsync(db.LibraryConnectionString, "/test/thumbs-ac12.flac");
        var cappedToken = MintRawListenerToken();
        var cappedListenerKey = ComputeListenerKey(cappedToken);
        for (var i = 0; i < dailyCap; i++)
        {
            await ThumbTestFixtures.SeedThumbRowAsync(
                db.LibraryConnectionString, mediaFive, DateTimeOffset.Parse("2026-08-01T13:00:00Z").AddMinutes(i),
                cappedListenerKey, "up", "spectator");
        }

        await using (var factory6 = new ThumbsWebFactory(db, dailyCap: dailyCap))
        {
            // T366 review MED-1's own reorder means an UNRESOLVABLE token now short-circuits BEFORE
            // the per-listener DB read (by design — see SpectatorThumbsController's own remarks), so
            // this fact needs a REAL, resolvable token to actually exercise the cap gate at all.
            var sink = factory6.Services.GetRequiredService<IStationEventSink>();
            var resolver = factory6.Services.GetRequiredService<IAiringTokenResolver>();
            var startedSix = DateTimeOffset.Parse("2026-08-01T13:30:00Z");
            sink.Publish(new TrackAired(mediaFive.ToString(), "AC12 Song", "AC12 Artist", 0.0, startedSix, 180_000));
            var tokenSix = resolver.Current ?? throw new InvalidOperationException("expected a minted token");

            var client6 = ClientWithListenerCookie(factory6, cappedToken);
            var response6 = await client6.PostAsJsonAsync(Route, new { airing = tokenSix, direction = "up" });
            Status12 = response6.StatusCode;

            RowCountForCappedListenerAfter = await ThumbTestFixtures.CountThumbRowsForListenerAsync(db.LibraryConnectionString, cappedListenerKey);
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    static string MintRawListenerToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16));

    static string ComputeListenerKey(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    static HttpClient ClientWithListenerCookie(WebApplicationFactory<Program> factory, string token)
    {
        // HandleCookies=false — a client managing its own CookieContainer both hides Set-Cookie
        // from HttpResponseMessage.Headers (the reason factory1's own client below needs it) and
        // would otherwise fight the manually-set Cookie header this helper adds.
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", $"genwave-listener={token}");
        return client;
    }

    /// <summary>Minimal Set-Cookie parser — just the attributes SPEC F150.6 pins.</summary>
    internal static (string Value, bool HttpOnly, string? SameSite, string? Path, long? MaxAgeSeconds, bool Secure) ParseSetCookie(string raw)
    {
        var parts = raw.Split(';').Select(p => p.Trim()).ToArray();
        var nameValue = parts[0].Split('=', 2);
        var value = nameValue.Length > 1 ? nameValue[1] : "";
        var httpOnly = parts.Any(p => p.Equals("HttpOnly", StringComparison.OrdinalIgnoreCase));
        var secure = parts.Any(p => p.Equals("Secure", StringComparison.OrdinalIgnoreCase));
        var sameSite = parts.FirstOrDefault(p => p.StartsWith("SameSite=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
        var path = parts.FirstOrDefault(p => p.StartsWith("Path=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
        long? maxAge = null;
        var maxAgePart = parts.FirstOrDefault(p => p.StartsWith("Max-Age=", StringComparison.OrdinalIgnoreCase));
        if (maxAgePart is not null && long.TryParse(maxAgePart.Split('=', 2)[1], out var seconds))
            maxAge = seconds;
        return (value, httpOnly, sameSite, path, maxAge, secure);
    }
}

/// <summary>This file's own <see cref="EphemeralStationDatabase"/> subclass (Support/EphemeralStationDatabase.cs
/// — the Story366/367 hoist) — supplies only the compose project-name prefix.</summary>
file sealed class ThumbsStationDatabase : EphemeralStationDatabase
{
    ThumbsStationDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<ThumbsStationDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-thumbswrite");
        var db = new ThumbsStationDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>
/// Boots the real production composition root against a real ephemeral Postgres, with
/// <c>Station:Thumbs:Enabled</c> forced on and <c>Gardener:ThumbCooldownSeconds</c>/
/// <c>Gardener:ThumbDailyCap</c> overridable per instance (mirrors Story380's own
/// <c>GardenerKnobsWebFactory</c> override shape) — a FRESH instance per DB-backed fact in
/// <see cref="ThumbsWriteArc"/> buys a fresh, un-throttled rate-limiter partition (class remarks).
/// </summary>
file sealed class ThumbsWebFactory(
    ThumbsStationDatabase db, int? cooldownSeconds = null, int? dailyCap = null,
    CapturingAllLevelsLoggerProvider? logs = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", db.LibraryConnectionString);
        builder.UseSetting("ConnectionStrings:Station", db.StationConnectionString);
        builder.UseSetting("Admin:Password", "test-password-story369-thumbs-write");
        builder.UseSetting("Station:SpectatorMode", "true");
        builder.UseSetting("Station:Thumbs:Enabled", "true");

        // The exact four Station:* keys compose.yaml itself overrides in production (Story366/367's
        // own precedent) — every other Station:* leaf rides appsettings.json's own shipped default.
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");
        // gh-#99: every media row this file seeds lands in the DEFAULT library (id 1,
        // db/01-library.sh's own `library_id ... default 1`) — Station:SafeScope:LibraryIds
        // defaults to [1] too (appsettings.json), which would silently exclude every one of
        // them from IThumbStore.RecordAsync as "safe scope" (ThumbWriteResult.Ignored). Point
        // the safe scope at a library id nothing here ever uses instead (the Story367
        // RotationNonMusicArc/Story355WebFactory own `safeLibraryId` precedent).
        builder.UseSetting("Station:SafeScope:LibraryIds:0", "999999");

        if (cooldownSeconds is { } cooldown)
            builder.UseSetting("Gardener:ThumbCooldownSeconds", cooldown.ToString(CultureInfo.InvariantCulture));
        if (dailyCap is { } cap)
            builder.UseSetting("Gardener:ThumbDailyCap", cap.ToString(CultureInfo.InvariantCulture));

        if (logs is not null)
        {
            // T366 review MED-2: CapturingAllLevelsLoggerProvider.IsEnabled=>true does NOT, on its
            // own, widen what reaches the provider — LoggerFilterOptions (appsettings.Development.json
            // pins Logging:LogLevel:Default to Information) decides whether Log() is even CALLED
            // BEFORE any provider's own IsEnabled is consulted. SetMinimumLevel + a wildcard AddFilter
            // are what actually lower that floor to Trace for this factory's own logging pipeline —
            // without both, AC13 below would only ever see Information+ lines and could never catch a
            // genuine Debug-level leak (proven red-then-green in this class's own remarks/commit
            // history — a temporary LogDebug of the raw cookie token in SpectatorThumbsController).
            builder.ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddFilter(_ => true);
                logging.AddProvider(logs);
            });
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
        });
    }
}

/// <summary>Captures every log entry this provider is asked to log — STORY-369 AC13's own
/// log-capture idiom (mirrors Story186_CorrectionsObservability.cs's own
/// <c>CapturingDebugLoggerProvider</c>, widened to every level rather than Debug-and-above, since
/// AC13 asks for "at every level"). <see cref="Logger.IsEnabled"/> returning <see langword="true"/>
/// unconditionally is necessary but NOT sufficient on its own (T366 review MED-2): the FRAMEWORK's
/// own <c>LoggerFilterOptions</c> decides whether <c>Log()</c> is even invoked for a given
/// (category, level) pair before this type's <see cref="Logger.IsEnabled"/> is ever consulted — see
/// <see cref="ThumbsWebFactory"/>'s own <c>SetMinimumLevel(LogLevel.Trace)</c> +
/// <c>AddFilter(_ => true)</c> pairing, which is what actually lowers that floor for the one
/// factory instance that constructs this provider.</summary>
file sealed class CapturingAllLevelsLoggerProvider : ILoggerProvider
{
    readonly List<string> messages = [];

    public IReadOnlyList<string> Messages { get { lock (messages) return messages.ToList(); } }

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
    public void Dispose() { }

    void Add(string message) { lock (messages) messages.Add(message); }

    sealed class Logger(CapturingAllLevelsLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            var full = exception is null ? message : $"{message} {exception}";
            owner.Add($"[{category}] {full}");
        }
    }
}

/// <summary>Raw SQL reads/writes against <c>library.media_thumb</c>/<c>library.media_rotation</c> —
/// an independent read of what actually landed in Postgres (the GardenerSeedFixtures precedent one
/// seam over: that shared class owns the generic media/library/ledger helpers every rotation spec
/// reuses; these are genuinely thumbs-specific, so they stay local to this file rather than growing
/// that shared class for a single caller).</summary>
file static class ThumbTestFixtures
{
    public readonly record struct ThumbRow(string Direction, string Source);

    public static async Task<ThumbRow?> ReadThumbRowAsync(
        string libraryConnectionString, long mediaId, DateTimeOffset airingStartedAt, string listenerKey)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            select direction::text, source::text from library.media_thumb
            where media_id = @mediaId and airing_started_at = @startedAt and listener_key = @listenerKey
            """;
        cmd.Parameters.AddWithValue("mediaId", mediaId);
        cmd.Parameters.AddWithValue("startedAt", airingStartedAt);
        cmd.Parameters.AddWithValue("listenerKey", listenerKey);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new ThumbRow(reader.GetString(0), reader.GetString(1));
    }

    public static async Task<string?> ReadListenerKeyAsync(string libraryConnectionString, long mediaId, DateTimeOffset airingStartedAt)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select listener_key from library.media_thumb where media_id = @mediaId and airing_started_at = @startedAt";
        cmd.Parameters.AddWithValue("mediaId", mediaId);
        cmd.Parameters.AddWithValue("startedAt", airingStartedAt);
        return (string?)await cmd.ExecuteScalarAsync();
    }

    public static async Task<int> CountThumbRowsAsync(
        string libraryConnectionString, long mediaId, DateTimeOffset airingStartedAt, string listenerKey)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            select count(*) from library.media_thumb
            where media_id = @mediaId and airing_started_at = @startedAt and listener_key = @listenerKey
            """;
        cmd.Parameters.AddWithValue("mediaId", mediaId);
        cmd.Parameters.AddWithValue("startedAt", airingStartedAt);
        cmd.Parameters.AddWithValue("listenerKey", listenerKey);
        return (int)(long)(await cmd.ExecuteScalarAsync() ?? 0L);
    }

    public static async Task<int> CountAllThumbRowsAsync(string libraryConnectionString)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select count(*) from library.media_thumb";
        return (int)(long)(await cmd.ExecuteScalarAsync() ?? 0L);
    }

    public static async Task<int> CountThumbRowsForListenerAsync(string libraryConnectionString, string listenerKey)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select count(*) from library.media_thumb where listener_key = @listenerKey";
        cmd.Parameters.AddWithValue("listenerKey", listenerKey);
        return (int)(long)(await cmd.ExecuteScalarAsync() ?? 0L);
    }

    public static async Task SeedThumbRowAsync(
        string libraryConnectionString, long mediaId, DateTimeOffset airingStartedAt, string listenerKey,
        string direction, string source)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            insert into library.media_thumb (media_id, airing_started_at, listener_key, direction, source)
            values (@mediaId, @startedAt, @listenerKey, @direction::library.thumb_direction, @source::library.thumb_source)
            """;
        cmd.Parameters.AddWithValue("mediaId", mediaId);
        cmd.Parameters.AddWithValue("startedAt", airingStartedAt);
        cmd.Parameters.AddWithValue("listenerKey", listenerKey);
        cmd.Parameters.AddWithValue("direction", direction);
        cmd.Parameters.AddWithValue("source", source);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<double> ReadNudgeAsync(string libraryConnectionString, long mediaId)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select nudge from library.media_rotation where media_id = @mediaId";
        cmd.Parameters.AddWithValue("mediaId", mediaId);
        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? 0.0 : Convert.ToDouble(result);
    }
}

// ── T366 review MED-3 harness ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Stamps <see cref="HttpContext.Connection"/>'s <see cref="IPAddress"/> from a test-only request
/// header — <c>TestServer</c> opens no real sockets, so <c>Connection.RemoteIpAddress</c> is null by
/// default, which would make <c>ForwardedHeadersMiddleware</c>'s own trust check permissive for
/// every simulated hop (its "remote address unknown" branch never refuses a header). Mirrors
/// Story344_AnnouncementDoorRateLimit.cs's own <c>RemoteIpStartupFilter</c> (redefined here, `file`
/// scoped there too — no shared test-support project, that file's own precedent) — registered as an
/// <see cref="IStartupFilter"/> so its middleware wraps AROUND the entire production pipeline,
/// running before <c>Program.cs</c>'s own <c>UseForwardedHeaders()</c> ever sees the request.
/// </summary>
file sealed class RemoteIpStartupFilter : IStartupFilter
{
    public const string TestIpHeaderName = "X-Test-Remote-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            if (context.Request.Headers.TryGetValue(TestIpHeaderName, out var value)
                && IPAddress.TryParse(value.ToString(), out var ip))
            {
                context.Connection.RemoteIpAddress = ip;
            }
            return nextMiddleware(context);
        });
        next(app);
    };
}

/// <summary>
/// Boots the real production composition root with <c>Proxy:TrustedNetworks</c> set to
/// <paramref name="trustedCidr"/> and <see cref="RemoteIpStartupFilter"/> registered so a caller can
/// simulate arriving from inside or outside that network (T366 review MED-3). Unreachable
/// <c>ConnectionStrings:Library</c> — the AiringTokenWebFactory precedent one seam over: a garbage
/// <c>airing</c> token never reaches a database read (T366 review MED-1's own reorder), so the two
/// MED-3 facts never need a real Postgres either.
/// </summary>
file sealed class ForwardedProtoThumbsWebFactory(string trustedCidr) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-story369-forwarded-proto");
        builder.UseSetting("Station:SpectatorMode", "true");
        builder.UseSetting("Station:Thumbs:Enabled", "true");
        builder.UseSetting("Proxy:TrustedNetworks:0", trustedCidr);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
            services.AddSingleton<IStartupFilter>(new RemoteIpStartupFilter());
        });
    }
}

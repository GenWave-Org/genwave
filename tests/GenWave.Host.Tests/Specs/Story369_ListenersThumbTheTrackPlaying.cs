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

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Host.Playout;

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

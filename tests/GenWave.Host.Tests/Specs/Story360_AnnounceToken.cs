// STORY-360 — The smart home holds a key, not the keys (SPEC F145.3/.4 · PLAN T340)
//
// BDD specification — xUnit. WIRED T340 — every Fact below drives the real production
// POST/DELETE /api/announcements/token and POST/GET /api/announcements[/now-playing] routes through
// WebApplicationFactory<Program> with real cookie auth AND the real "AnnounceToken" Bearer scheme
// (mirrors Story357_AnnouncementEndpoint.cs's own idiom), against FakeAnnouncementStore/
// FakeAnnounceTokenStore doubles — no live Postgres, this project has none for Host.Tests. The
// round trip against a REAL Postgres-backed AnnounceTokenStore/station.settings row is proven by the
// manual wire-proof transcript (PLAN T340's own acceptance), not by this in-process suite.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Auth;
using GenWave.Host.Playout;
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAnnounceToken
{
    public sealed class ScenarioGeneratedOnceHashedAtRest
    {
        [Fact]
        public async Task GenerationReturnsThePlaintextExactlyOnce()
        {
            // Given a logged-in operator
            await using var factory = new AnnounceTokenApiWebFactory(new FakeAnnouncementStore(), new FakeAnnounceTokenStore());
            var client = await AnnounceTokenApiWebFactory.LoggedInClientAsync(factory);

            // When two successive generate calls each reveal a token
            var first = await AnnounceTokenApiWebFactory.GenerateTokenAsync(client);
            var second = await AnnounceTokenApiWebFactory.GenerateTokenAsync(client);

            // Then each reveal is a real, sufficiently random value (SPEC F145.3's ≥32-byte floor,
            // url-safe base64 encoded), and a fresh generate reveals a DIFFERENT one — the prior
            // reveal is never handed out again
            Assert.True(
                first.Length >= 32 && second.Length >= 32 && first != second,
                $"expected two distinct, sufficiently long reveals; first: {first.Length} chars, second: {second.Length} chars, equal: {first == second}");
        }

        [Fact]
        public async Task OnlyTheHashIsStoredInSettings()
        {
            // Given a logged-in operator and a fresh token store
            var tokenStore = new FakeAnnounceTokenStore();
            await using var factory = new AnnounceTokenApiWebFactory(new FakeAnnouncementStore(), tokenStore);
            var client = await AnnounceTokenApiWebFactory.LoggedInClientAsync(factory);

            // When a token is generated
            var plaintext = await AnnounceTokenApiWebFactory.GenerateTokenAsync(client);

            // Then the store holds the SHA-256 hex hash of the plaintext, never the plaintext itself
            var expectedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)));
            Assert.Equal(
                (StoredIsHash: true, StoredIsPlaintext: false),
                (StoredIsHash: tokenStore.Hash == expectedHash, StoredIsPlaintext: tokenStore.Hash == plaintext));
        }

        [Fact]
        public async Task NoLaterReadBackOrApiResponseContainsThePlaintext()
        {
            // Given a logged-in operator and a freshly generated token
            var announcementStore = new FakeAnnouncementStore();
            await using var factory = new AnnounceTokenApiWebFactory(announcementStore, new FakeAnnounceTokenStore());
            var client = await AnnounceTokenApiWebFactory.LoggedInClientAsync(factory);
            var plaintext = await AnnounceTokenApiWebFactory.GenerateTokenAsync(client);

            // When the same session drives every other in-scope announcements-family response afterward
            // — including the T344 status read (T340's own carried reveal-once-read-back note, closed
            // here rather than left open for a later cycle)
            var postResponse = await client.PostAsJsonAsync("/api/announcements", new { message = "Dinner's ready" });
            var nowPlayingResponse = await client.GetAsync("/api/announcements/now-playing");
            var statusResponse = await client.GetAsync("/api/announcements/token/status");
            var postBody = await postResponse.Content.ReadAsStringAsync();
            var nowPlayingBody = await nowPlayingResponse.Content.ReadAsStringAsync();
            var statusBody = await statusResponse.Content.ReadAsStringAsync();

            // Then neither response ever echoes the plaintext back
            Assert.True(
                !postBody.Contains(plaintext, StringComparison.Ordinal)
                    && !nowPlayingBody.Contains(plaintext, StringComparison.Ordinal)
                    && !statusBody.Contains(plaintext, StringComparison.Ordinal),
                $"plaintext leaked into a later API response; post: {postBody}, nowPlaying: {nowPlayingBody}, status: {statusBody}");
        }
    }

    public sealed class ScenarioScopeIsExactlyTwoSurfaces
    {
        [Fact]
        public async Task TheTokenAuthorizesTheAnnouncementsFamily()
        {
            // Given a station with a configured token and NO cookie session at all
            var announcementStore = new FakeAnnouncementStore();
            var tokenStore = new FakeAnnounceTokenStore();
            await using var factory = new AnnounceTokenApiWebFactory(announcementStore, tokenStore);
            var plaintext = await AnnounceTokenApiWebFactory.GenerateTokenAsync(
                await AnnounceTokenApiWebFactory.LoggedInClientAsync(factory));
            var bearerOnlyClient = AnnounceTokenApiWebFactory.BearerClient(factory, plaintext);

            // When that Bearer-only client posts an announcement
            var response = await bearerOnlyClient.PostAsJsonAsync("/api/announcements", new { message = "From the smart speaker" });

            // Then it is accepted, and the store recorded the TOKEN submitter — never Session
            Assert.Equal(
                (Status: HttpStatusCode.OK, Submitter: AnnouncementSubmitter.Token),
                (Status: response.StatusCode, Submitter: announcementStore.InsertCalls.Single().Submitter));
        }

        [Fact]
        public async Task TheTokenAuthorizesTheNowPlayingRead()
        {
            // Given a station with a configured token and NO cookie session at all
            await using var factory = new AnnounceTokenApiWebFactory(new FakeAnnouncementStore(), new FakeAnnounceTokenStore());
            var plaintext = await AnnounceTokenApiWebFactory.GenerateTokenAsync(
                await AnnounceTokenApiWebFactory.LoggedInClientAsync(factory));
            var bearerOnlyClient = AnnounceTokenApiWebFactory.BearerClient(factory, plaintext);

            // When that Bearer-only client reads now-playing
            var response = await bearerOnlyClient.GetAsync("/api/announcements/now-playing");

            // Then it succeeds
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task AnyOtherAuthenticatedRouteRefusesTheToken()
        {
            // Given a station with a configured token and NO cookie session at all
            await using var factory = new AnnounceTokenApiWebFactory(new FakeAnnouncementStore(), new FakeAnnounceTokenStore());
            var plaintext = await AnnounceTokenApiWebFactory.GenerateTokenAsync(
                await AnnounceTokenApiWebFactory.LoggedInClientAsync(factory));
            var bearerOnlyClient = AnnounceTokenApiWebFactory.BearerClient(factory, plaintext);

            // When that Bearer-only client calls a real Operator-plane route OUTSIDE the announcements
            // family (the settings PUT — never in AnnounceTokenAuthenticationDefaults.InScopeSchemes)
            var response = await bearerOnlyClient.PutAsJsonAsync("/api/settings", Array.Empty<object>());

            // Then it is refused — the "AnnounceToken" scheme is never even consulted for a route
            // that never named it, so this Bearer header authenticates nothing here
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task AdminSessionAuthStillWorksOnTheSameRoutes()
        {
            // Given a logged-in operator (cookie session, no Bearer token anywhere) and a configured
            // token that is never presented
            var announcementStore = new FakeAnnouncementStore();
            await using var factory = new AnnounceTokenApiWebFactory(announcementStore, new FakeAnnounceTokenStore());
            var client = await AnnounceTokenApiWebFactory.LoggedInClientAsync(factory);

            // When the cookie-only session posts an announcement and reads now-playing
            var postResponse = await client.PostAsJsonAsync("/api/announcements", new { message = "From the booth" });
            var nowPlayingResponse = await client.GetAsync("/api/announcements/now-playing");

            // Then both still succeed — widening the route to accept a Bearer token never narrowed
            // what the admin cookie session can already do
            Assert.Equal(
                (Post: HttpStatusCode.OK, NowPlaying: HttpStatusCode.OK),
                (Post: postResponse.StatusCode, NowPlaying: nowPlayingResponse.StatusCode));
        }
    }

    public sealed class ScenarioSchemeMatchIsCaseInsensitive
    {
        [Fact]
        public async Task ALowercaseBearerHeaderAuthenticates()
        {
            // Given a configured token and NO cookie session
            await using var factory = new AnnounceTokenApiWebFactory(new FakeAnnouncementStore(), new FakeAnnounceTokenStore());
            var plaintext = await AnnounceTokenApiWebFactory.GenerateTokenAsync(
                await AnnounceTokenApiWebFactory.LoggedInClientAsync(factory));

            // When a spec-compliant client presents the auth-scheme token in lowercase (RFC 7235 §2.1:
            // the scheme token is case-insensitive) rather than the canonical "Bearer" casing
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"bearer {plaintext}");
            var response = await client.GetAsync("/api/announcements/now-playing");

            // Then it authenticates exactly as the canonical casing would — the scheme match is
            // case-insensitive, not merely tolerant of one specific casing
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    public sealed class ScenarioRevocationFailsClosed
    {
        [Fact]
        public async Task ARevokedTokenIsRefusedOnItsNextRequest()
        {
            // Given a token that has already authenticated successfully once
            var announcementStore = new FakeAnnouncementStore();
            await using var factory = new AnnounceTokenApiWebFactory(announcementStore, new FakeAnnounceTokenStore());
            var loggedInClient = await AnnounceTokenApiWebFactory.LoggedInClientAsync(factory);
            var plaintext = await AnnounceTokenApiWebFactory.GenerateTokenAsync(loggedInClient);
            var bearerOnlyClient = AnnounceTokenApiWebFactory.BearerClient(factory, plaintext);
            var firstUse = await bearerOnlyClient.GetAsync("/api/announcements/now-playing");

            // When the operator revokes it via the session-only door
            var revoke = await loggedInClient.DeleteAsync("/api/announcements/token");

            // Then the SAME plaintext is refused on its very next request
            var secondUse = await bearerOnlyClient.GetAsync("/api/announcements/now-playing");
            Assert.Equal(
                (FirstUse: HttpStatusCode.OK, Revoke: HttpStatusCode.NoContent, SecondUse: HttpStatusCode.Unauthorized),
                (FirstUse: firstUse.StatusCode, Revoke: revoke.StatusCode, SecondUse: secondUse.StatusCode));
        }

        [Fact]
        public async Task ARegeneratedTokenRefusesTheOldPlaintext()
        {
            // Given an already-generated token
            await using var factory = new AnnounceTokenApiWebFactory(new FakeAnnouncementStore(), new FakeAnnounceTokenStore());
            var loggedInClient = await AnnounceTokenApiWebFactory.LoggedInClientAsync(factory);
            var oldPlaintext = await AnnounceTokenApiWebFactory.GenerateTokenAsync(loggedInClient);

            // When the operator regenerates it
            var newPlaintext = await AnnounceTokenApiWebFactory.GenerateTokenAsync(loggedInClient);

            // Then the OLD plaintext is refused and the NEW one works, on the same route
            var oldResponse = await AnnounceTokenApiWebFactory.BearerClient(factory, oldPlaintext).GetAsync("/api/announcements/now-playing");
            var newResponse = await AnnounceTokenApiWebFactory.BearerClient(factory, newPlaintext).GetAsync("/api/announcements/now-playing");
            Assert.Equal(
                (Old: HttpStatusCode.Unauthorized, New: HttpStatusCode.OK),
                (Old: oldResponse.StatusCode, New: newResponse.StatusCode));
        }

        [Fact]
        public async Task WithNoHashRowConfiguredEveryBearerTokenIsRefused()
        {
            // Given a station where no token has ever been generated (a fresh FakeAnnounceTokenStore
            // — Hash starts null, the SPEC F145.4 "no hash row" state)
            await using var factory = new AnnounceTokenApiWebFactory(new FakeAnnouncementStore(), new FakeAnnounceTokenStore());
            var bearerOnlyClient = AnnounceTokenApiWebFactory.BearerClient(factory, "any-value-whatsoever");

            // When that client presents any Bearer value at all
            var response = await bearerOnlyClient.GetAsync("/api/announcements/now-playing");

            // Then it is refused — there is no "any token works" fallback
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    // ── Extended scenarios (PLAN T340's own additions — behavior the pending skeleton didn't name) ──

    public sealed class ScenarioTheMintingDoorIsSessionOnly
    {
        [Fact]
        public async Task ATokenCannotGenerateOrRegenerateATokenForItself()
        {
            // Given a configured token and NO cookie session
            await using var factory = new AnnounceTokenApiWebFactory(new FakeAnnouncementStore(), new FakeAnnounceTokenStore());
            var plaintext = await AnnounceTokenApiWebFactory.GenerateTokenAsync(
                await AnnounceTokenApiWebFactory.LoggedInClientAsync(factory));
            var bearerOnlyClient = AnnounceTokenApiWebFactory.BearerClient(factory, plaintext);

            // When that Bearer-only client tries to mint a replacement
            var response = await bearerOnlyClient.PostAsync("/api/announcements/token", content: null);

            // Then it is refused — AnnouncementTokenController never lists the "AnnounceToken" scheme
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task ATokenCannotRevokeItself()
        {
            // Given a configured token and NO cookie session
            var tokenStore = new FakeAnnounceTokenStore();
            await using var factory = new AnnounceTokenApiWebFactory(new FakeAnnouncementStore(), tokenStore);
            var plaintext = await AnnounceTokenApiWebFactory.GenerateTokenAsync(
                await AnnounceTokenApiWebFactory.LoggedInClientAsync(factory));
            var bearerOnlyClient = AnnounceTokenApiWebFactory.BearerClient(factory, plaintext);

            // When that Bearer-only client tries to revoke itself
            var response = await bearerOnlyClient.DeleteAsync("/api/announcements/token");

            // Then it is refused, and the hash row is untouched — the token still works afterward
            var stillWorks = await bearerOnlyClient.GetAsync("/api/announcements/now-playing");
            Assert.Equal(
                (Revoke: HttpStatusCode.Unauthorized, StillWorks: HttpStatusCode.OK),
                (Revoke: response.StatusCode, StillWorks: stillWorks.StatusCode));
        }

        [Fact]
        public async Task ATokenCannotReadItsOwnStatus()
        {
            // Given a configured token and NO cookie session
            await using var factory = new AnnounceTokenApiWebFactory(new FakeAnnouncementStore(), new FakeAnnounceTokenStore());
            var plaintext = await AnnounceTokenApiWebFactory.GenerateTokenAsync(
                await AnnounceTokenApiWebFactory.LoggedInClientAsync(factory));
            var bearerOnlyClient = AnnounceTokenApiWebFactory.BearerClient(factory, plaintext);

            // When that Bearer-only client tries to read its own status (T344 review finding F3)
            var response = await bearerOnlyClient.GetAsync("/api/announcements/token/status");

            // Then it is refused — the status route is session-only, same as generate/revoke above: a
            // caller holding only a token has no more business introspecting the credential that scopes
            // it than minting or revoking one.
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    public sealed class ScenarioLastUsedIsStamped
    {
        [Fact]
        public async Task ASuccessfulBearerAuthenticationStampsLastUsed()
        {
            // Given a configured token
            var tokenStore = new FakeAnnounceTokenStore();
            await using var factory = new AnnounceTokenApiWebFactory(new FakeAnnouncementStore(), tokenStore);
            var plaintext = await AnnounceTokenApiWebFactory.GenerateTokenAsync(
                await AnnounceTokenApiWebFactory.LoggedInClientAsync(factory));

            // When it authenticates a request successfully
            await AnnounceTokenApiWebFactory.BearerClient(factory, plaintext).GetAsync("/api/announcements/now-playing");

            // Then the store's last-used stamp fired exactly once
            Assert.Equal(1, tokenStore.StampCalls);
        }

        [Fact]
        public async Task AFailedBearerAttemptNeverStampsLastUsed()
        {
            // Given a station with no token configured
            var tokenStore = new FakeAnnounceTokenStore();
            await using var factory = new AnnounceTokenApiWebFactory(new FakeAnnouncementStore(), tokenStore);

            // When a Bearer request is refused
            await AnnounceTokenApiWebFactory.BearerClient(factory, "wrong").GetAsync("/api/announcements/now-playing");

            // Then last-used was never stamped
            Assert.Equal(0, tokenStore.StampCalls);
        }

        [Fact]
        public async Task ARegenerateClearsLastUsedUntilTheNewTokenAuthenticates()
        {
            // Given a token that has already authenticated once — status carries a last-used stamp...
            var tokenStore = new FakeAnnounceTokenStore();
            await using var factory = new AnnounceTokenApiWebFactory(new FakeAnnouncementStore(), tokenStore);
            var loggedInClient = await AnnounceTokenApiWebFactory.LoggedInClientAsync(factory);
            var oldPlaintext = await AnnounceTokenApiWebFactory.GenerateTokenAsync(loggedInClient);
            await AnnounceTokenApiWebFactory.BearerClient(factory, oldPlaintext).GetAsync("/api/announcements/now-playing");
            var beforeRegenerate = await loggedInClient.GetFromJsonAsync<AnnounceTokenStatusDto>("/api/announcements/token/status");
            Assert.NotNull(beforeRegenerate?.LastUsedAt);

            // When the operator regenerates...
            var newPlaintext = await AnnounceTokenApiWebFactory.GenerateTokenAsync(loggedInClient);

            // Then status shows lastUsed null — the prior token's stamp must die with its credential
            // (T344 review finding F2), not survive to misreport the brand-new plaintext as already used.
            var afterRegenerate = await loggedInClient.GetFromJsonAsync<AnnounceTokenStatusDto>("/api/announcements/token/status");
            Assert.Null(afterRegenerate?.LastUsedAt);

            // And once the NEW token itself authenticates, it stamps again — the timestamp isn't gone
            // forever, only reset to reflect the new credential's own history.
            await AnnounceTokenApiWebFactory.BearerClient(factory, newPlaintext).GetAsync("/api/announcements/now-playing");
            var afterNewTokenUse = await loggedInClient.GetFromJsonAsync<AnnounceTokenStatusDto>("/api/announcements/token/status");
            Assert.NotNull(afterNewTokenUse?.LastUsedAt);
        }
    }

    public sealed class ScenarioNowPlayingReadIsMinimal
    {
        [Fact]
        public async Task AnOnAirTrackReportsTitleArtistAndDjNameOnly()
        {
            // Given a logged-in operator and a track on-air
            await using var factory = new AnnounceTokenApiWebFactory(new FakeAnnouncementStore(), new FakeAnnounceTokenStore());
            var client = await AnnounceTokenApiWebFactory.LoggedInClientAsync(factory);
            var nowPlaying = factory.Services.GetRequiredService<NowPlayingService>();
            nowPlaying.Update(SingleStation.IdString, new NowPlayingSnapshot(
                MediaId: "42", Title: "A Song", Artist: "An Artist", GainDb: 0, StartedAt: DateTimeOffset.UtcNow,
                DurationMs: 180_000, IsDrain: false, DjName: "Flip"));

            // When the now-playing read is called
            var dto = await client.GetFromJsonAsync<AnnouncementNowPlayingDto>("/api/announcements/now-playing");

            // Then it carries exactly title/artist/djName off that snapshot — nothing more
            Assert.Equal(
                new AnnouncementNowPlayingDto("A Song", "An Artist", "Flip"),
                dto);
        }

        [Fact]
        public async Task NoSnapshotYetReportsStandbyAsThreeNulls()
        {
            // Given a logged-in operator and a feeder that has not ticked yet (no snapshot published)
            await using var factory = new AnnounceTokenApiWebFactory(new FakeAnnouncementStore(), new FakeAnnounceTokenStore());
            var client = await AnnounceTokenApiWebFactory.LoggedInClientAsync(factory);

            // When the now-playing read is called
            var dto = await client.GetFromJsonAsync<AnnouncementNowPlayingDto>("/api/announcements/now-playing");

            // Then every field is null — the standby state
            Assert.Equal(new AnnouncementNowPlayingDto(null, null, null), dto);
        }
    }
}

// ── Test harness ───────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for this file's own Facts — mirrors
/// <c>AnnouncementsApiWebFactory</c>'s (Story357_AnnouncementEndpoint.cs) idiom: <see cref="IAnnouncementStore"/>
/// and <see cref="IAnnounceTokenStore"/> both replaced by stateful fakes, real cookie AND real
/// "AnnounceToken" Bearer auth, no live Postgres/Liquidsoap/Kokoro reached.
/// </summary>
file sealed class AnnounceTokenApiWebFactory(FakeAnnouncementStore announcementStore, FakeAnnounceTokenStore tokenStore)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story360-announce-token";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IAnnouncementStore>();
            services.AddSingleton<IAnnouncementStore>(announcementStore);

            services.RemoveAll<IAnnounceTokenStore>();
            services.AddSingleton<IAnnounceTokenStore>(tokenStore);
        });
    }

    /// <summary>Logs in via the real POST /api/auth/login round trip (mirrors Story305_ShowsApi.cs's own helper) and returns the cookie-bearing client.</summary>
    public static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    /// <summary>A fresh client carrying ONLY an <c>Authorization: Bearer</c> header — no cookie ever set.</summary>
    public static HttpClient BearerClient(WebApplicationFactory<Program> factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Drives the real generate/regenerate endpoint on an already logged-in client and returns the revealed plaintext.</summary>
    public static async Task<string> GenerateTokenAsync(HttpClient loggedInClient)
    {
        var response = await loggedInClient.PostAsync("/api/announcements/token", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AnnounceTokenGeneratedDto>();
        Assert.NotNull(dto);
        return dto.Token;
    }
}

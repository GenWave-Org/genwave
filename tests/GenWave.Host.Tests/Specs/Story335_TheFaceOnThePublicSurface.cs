// STORY-335 — The face on the public surface (SPEC F129.1/.2/.3 · PLAN T298 route + T299 payload)
//
// BDD specification — xUnit. The spectator DJ card itself (AC4) is the static page —
// browser acceptance at the T301 wire per the T92 precedent (no JS test rig by design).
//
// T298 route facts below drive GET /spectator/api/artwork/dj/{token} through the production
// pipeline (WebApplicationFactory<Program>) against FakePersonaAvatarStore/FakeStationImageStore
// — this project has no Postgres fixture; the real station.persona_avatar/station_image SQL is
// T290's own coverage (GenWave.MediaLibrary.Tests). Mirrors Gh258_SpectatorStationLogo.cs's own
// StationLogoWebFactory and Story222_ArtworkEndpoint.cs's own ArtworkWebFactory for the DI shape.

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

/// <summary>Boots the real Program.cs graph on the spectator surface with
/// <see cref="IPersonaAvatarStore"/> and <see cref="IStationImageStore"/> replaced by seedable
/// doubles — the same DI-swap shape <c>Story333_TheWornFace.cs</c>'s own
/// <c>PersonaAvatarWebFactory</c> uses, plus the spectator-mode arrangement
/// <c>Gh258_SpectatorStationLogo.cs</c>'s own <c>StationLogoWebFactory</c> already establishes for
/// this exact controller.</summary>
file sealed class DjArtworkWebFactory(
    IPersonaAvatarStore? personaAvatarStore = null,
    IStationImageStore? stationImageStore = null) : WebApplicationFactory<Program>
{
    readonly IPersonaAvatarStore personaAvatarStore = personaAvatarStore ?? new FakePersonaAvatarStore();
    readonly IStationImageStore stationImageStore = stationImageStore ?? new FakeStationImageStore();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Station:SpectatorMode", "true");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-x7z");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());

            services.RemoveAll<IPersonaAvatarStore>();
            services.AddSingleton(personaAvatarStore);

            services.RemoveAll<IStationImageStore>();
            services.AddSingleton(stationImageStore);
        });
    }
}

public static class FeatureTheFaceOnThePublicSurface
{
    // 32 lowercase hex chars — the real PersonaAvatarController.GenerateToken shape (128-bit
    // CSPRNG hex) — though FakePersonaAvatarStore itself never validates format, only looks up.
    const string CurrentToken = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4";
    const string StaleToken = "b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5";
    const string RandomToken = "c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6";

    // NOT 32 lowercase hex — the shape GenWave.Core.Domain.ArtworkToken.IsWellFormed rejects before
    // any store call (ScenarioMalformedTokensNeverReachTheStore below).
    const string MalformedToken = "not-a-token";

    static readonly byte[] FaceBytes = [0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02];
    static readonly byte[] StationImageBytes = [0xFA, 0xCE, 0x00, 0x99];

    /// <summary>A <see cref="FakePersonaAvatarStore"/> seeded with exactly one worn face, serving
    /// under <see cref="CurrentToken"/> — mirrors <c>Story333_TheWornFace.cs</c>'s own
    /// <c>PersonaAvatarFixtures</c> "seed before constructing the factory" idiom.</summary>
    static async Task<FakePersonaAvatarStore> SeededPersonaAvatarStoreAsync()
    {
        var store = new FakePersonaAvatarStore();
        await store.UpsertAsync(
            new PersonaAvatarInput(1, FaceBytes, "sha256-stub", CurrentToken, PersonaAvatarSource.Upload, null),
            CancellationToken.None);
        return store;
    }

    /// <summary>A <see cref="FakeStationImageStore"/> seeded with an owner-customized station
    /// image — the bytes every dj-token miss (stale or random) must fall through to.</summary>
    static FakeStationImageStore SeededStationImageStore()
    {
        var store = new FakeStationImageStore();
        store.Seed(new StationImage(StationImageBytes, StationImageBytes.Length, "sha256-stub", "station-token", DateTime.UtcNow));
        return store;
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the route (T298)
    // ---------------------------------------------------------------------

    public sealed class ScenarioAWornFaceServesAnonymouslyAndImmutably
    {
        [Fact]
        public async Task TheCurrentTokenReturnsTheFaceBytes()
        {
            var personaAvatarStore = await SeededPersonaAvatarStoreAsync();
            await using var factory = new DjArtworkWebFactory(personaAvatarStore: personaAvatarStore);
            var client = factory.CreateClient();

            // No session cookie is ever set up — the Spectator authorization policy always
            // succeeds (STORY-171/172's own precedent), proving this route is reachable
            // anonymously by construction, not merely by omission of a check here.
            var response = await client.GetAsync($"/spectator/api/artwork/dj/{CurrentToken}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsByteArrayAsync();
            Assert.Equal(FaceBytes, body);
        }

        [Fact]
        public async Task TheResponseCarriesTheImmutableYearCache()
        {
            var personaAvatarStore = await SeededPersonaAvatarStoreAsync();
            await using var factory = new DjArtworkWebFactory(personaAvatarStore: personaAvatarStore);
            var client = factory.CreateClient();

            var response = await client.GetAsync($"/spectator/api/artwork/dj/{CurrentToken}");

            // Cache-Control: public, max-age=31536000, immutable — safe because rotation re-URLs.
            var cache = response.Headers.CacheControl;
            Assert.NotNull(cache);
            Assert.True(cache!.Public);
            Assert.Equal(TimeSpan.FromSeconds(31536000), cache.MaxAge);
            Assert.Contains(cache.Extensions, ext => ext.Name == "immutable");
        }

        [Fact]
        public async Task TheFaceServesAsPng()
        {
            // Mirrors Gh258_SpectatorStationLogo.cs's own LogoServesAsPng — the response's
            // Content-Type pinned directly, not merely assumed from the controller's own constant.
            var personaAvatarStore = await SeededPersonaAvatarStoreAsync();
            await using var factory = new DjArtworkWebFactory(personaAvatarStore: personaAvatarStore);
            var client = factory.CreateClient();

            var response = await client.GetAsync($"/spectator/api/artwork/dj/{CurrentToken}");

            Assert.Equal(("image/png", true),
                (response.Content.Headers.ContentType?.MediaType, response.IsSuccessStatusCode));
        }

        [Fact]
        public async Task TheRouteExistsOnlyOnTheSpectatorSurface()
        {
            // The dedicated public listener serves it (SpectatorSurfaceAttribute is what makes a
            // route public-port-reachable, SurfaceGateMiddleware's own contract); the admin
            // listener's own surface set gains nothing — this endpoint carries no
            // AdminSurfaceAttribute at all, mirroring Story196_LlmCallInspector.cs's own inverse
            // proof for an admin-only route.
            await using var factory = new DjArtworkWebFactory();
            _ = factory.CreateClient(); // force host build so the route table is populated

            // GET and HEAD are two distinct endpoints sharing one route pattern (one per
            // HttpMethodAttribute, MVC attribute-routing's own selector-per-attribute shape) — both
            // must carry the identical class-level surface tagging, so this asserts over the whole
            // set rather than a single endpoint.
            var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
                .OfType<RouteEndpoint>()
                .Where(e => e.RoutePattern.RawText == "spectator/api/artwork/dj/{token}")
                .ToList();

            Assert.NotEmpty(endpoints);
            Assert.All(endpoints, endpoint =>
            {
                Assert.NotNull(endpoint.Metadata.GetMetadata<SpectatorSurfaceAttribute>());
                Assert.Null(endpoint.Metadata.GetMetadata<AdminSurfaceAttribute>());
            });

            // gh-#160 parity: the [HttpGet]/[HttpHead] pair on GetDjArtwork must actually produce
            // both verbs across the endpoint set above — a fact that inspects only ONE endpoint's
            // tagging could pass even if the HttpHead attribute were ever dropped from the method.
            var methods = endpoints
                .SelectMany(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .ToHashSet();
            Assert.Equal(new HashSet<string> { "GET", "HEAD" }, methods);
        }
    }

    public sealed class ScenarioRotationRevokes
    {
        [Fact]
        public async Task TheOldTokenServesTheStationImageBytesWith200()
        {
            // Given a persona whose CURRENT face serves under CurrentToken — StaleToken names no
            // row at all (the real IPersonaAvatarStore.UpsertAsync replaces the whole row on every
            // write, PLAN T295's own TOKEN ENTROPY remarks: a prior token is never left resolvable
            // once rotated).
            var personaAvatarStore = await SeededPersonaAvatarStoreAsync();
            var stationImageStore = SeededStationImageStore();
            await using var factory = new DjArtworkWebFactory(personaAvatarStore, stationImageStore);
            var client = factory.CreateClient();

            var response = await client.GetAsync($"/spectator/api/artwork/dj/{StaleToken}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsByteArrayAsync();
            Assert.Equal(StationImageBytes, body);
        }

        [Fact]
        public async Task TheOldTokenCarriesTheFallbackCacheControlNeverImmutable()
        {
            // The gh-#258 invariant, pinned for the dj route too — mirrors
            // Gh258_SpectatorStationLogo.cs's own TheFallbackIsCacheableButNeverImmutable.
            // SetFallbackCacheControl runs at the miss site, never SetImmutableCacheControl: a miss
            // here is a MUTABLE asset reachable under infinitely many stale/unminted token URLs, so
            // pinning it immutable would let a browser that hit a since-rotated token keep
            // rendering stale bytes for a year after the persona's face actually changed — the same
            // regression gh-#258 already taught this codebase to avoid, now for the worn face too.
            var personaAvatarStore = await SeededPersonaAvatarStoreAsync();
            var stationImageStore = SeededStationImageStore();
            await using var factory = new DjArtworkWebFactory(personaAvatarStore, stationImageStore);
            var client = factory.CreateClient();

            var response = await client.GetAsync($"/spectator/api/artwork/dj/{StaleToken}");

            var cache = response.Headers.CacheControl;
            Assert.NotNull(cache);
            Assert.True(cache!.Public);
            Assert.Equal(TimeSpan.FromDays(1), cache.MaxAge);
            Assert.DoesNotContain(cache.Extensions, ext => ext.Name == "immutable");
        }

        [Fact]
        public async Task TheOldTokenServesTheShippedLogoWhenNoStationImageIsCustomized()
        {
            // The production-reachable branch when the owner has never uploaded a station image:
            // default FakeStationImageStore reports no row (no Seed call), so
            // ServeStationImageAsync falls through to ServeStationIcon — the shipped
            // /spectator/logo.png bytes (the Gh258:127 TheArtworkEndpointFallbackServesTheLogoBytes
            // idiom, extended to the dj route).
            var personaAvatarStore = await SeededPersonaAvatarStoreAsync();
            await using var factory = new DjArtworkWebFactory(personaAvatarStore: personaAvatarStore);
            var client = factory.CreateClient();

            var fallback = await client.GetByteArrayAsync($"/spectator/api/artwork/dj/{StaleToken}");
            var logo = await client.GetByteArrayAsync("/spectator/logo.png");

            Assert.Equal(logo, fallback);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the payload (T299)
    // ---------------------------------------------------------------------

    public sealed class ScenarioThePayloadNamesTheFace
    {
        [Fact(Skip = "Pending T299 — see docs/PLAN.md")]
        public void DjAvatarUrlCarriesTheOnAirPersonasTokenUrl()
        {
            Assert.Fail("pending T299");
        }

        [Fact(Skip = "Pending T299 — see docs/PLAN.md")]
        public void DjAvatarUrlIsNullWhenTheOnAirPersonaIsFaceless()
        {
            Assert.Fail("pending T299");
        }

        [Fact(Skip = "Pending T299 — see docs/PLAN.md")]
        public void TheDisclosureContractPinsTheCompletePropertySet()
        {
            // F93.5/F67.5 amendment: the suite's complete-set assertion includes djAvatarUrl,
            // so an unblessed field still fails the build.
            Assert.Fail("pending T299");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — no oracle
    // ---------------------------------------------------------------------

    public sealed class ScenarioUnknownTokensAreNotAProbe
    {
        [Fact]
        public async Task ARandomTokenServesTheStationImageBytesWith200()
        {
            // Indistinguishable from a stale token (ScenarioRotationRevokes above): the SAME 200,
            // the SAME bytes, from a token nobody ever minted — the F88.3 idiom extended to faces.
            var personaAvatarStore = await SeededPersonaAvatarStoreAsync();
            var stationImageStore = SeededStationImageStore();
            await using var factory = new DjArtworkWebFactory(personaAvatarStore, stationImageStore);
            var client = factory.CreateClient();

            var response = await client.GetAsync($"/spectator/api/artwork/dj/{RandomToken}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsByteArrayAsync();
            Assert.Equal(StationImageBytes, body);
        }
    }

    public sealed class ScenarioMalformedTokensNeverReachTheStore
    {
        [Fact]
        public async Task AMalformedTokenServesTheStationImageBytesWith200()
        {
            // Same 200, same bytes as a stale/random token (F88.2's own non-enumerability guard
            // extended to faces) — proving the SHAPE it fails on serves the identical fallback the
            // store-level MISS already serves; AMalformedTokenNeverReachesThePersonaAvatarStore
            // below is what proves the guard actually short-circuits rather than merely coinciding
            // with the same answer.
            var stationImageStore = SeededStationImageStore();
            await using var factory = new DjArtworkWebFactory(stationImageStore: stationImageStore);
            var client = factory.CreateClient();

            var response = await client.GetAsync($"/spectator/api/artwork/dj/{MalformedToken}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsByteArrayAsync();
            Assert.Equal(StationImageBytes, body);
        }

        [Fact]
        public async Task AMalformedTokenCarriesTheFallbackCacheControlNeverImmutable()
        {
            await using var factory = new DjArtworkWebFactory();
            var client = factory.CreateClient();

            var response = await client.GetAsync($"/spectator/api/artwork/dj/{MalformedToken}");

            var cache = response.Headers.CacheControl;
            Assert.NotNull(cache);
            Assert.True(cache!.Public);
            Assert.Equal(TimeSpan.FromDays(1), cache.MaxAge);
            Assert.DoesNotContain(cache.Extensions, ext => ext.Name == "immutable");
        }

        [Fact]
        public async Task AMalformedTokenNeverReachesThePersonaAvatarStore()
        {
            // The mutation-proof fact the two above cannot be: ServeStationImageAsync's own
            // fallback produces the SAME bytes/headers whether GetDjArtwork's well-formedness
            // guard runs or the store is merely called and misses — only a call-count assertion
            // proves the guard short-circuits BEFORE the store (a malformed token must never buy
            // a round trip against the real, Postgres-backed PersonaAvatarRepository).
            var personaAvatarStore = await SeededPersonaAvatarStoreAsync();
            await using var factory = new DjArtworkWebFactory(personaAvatarStore: personaAvatarStore);
            var client = factory.CreateClient();

            await client.GetAsync($"/spectator/api/artwork/dj/{MalformedToken}");

            Assert.Equal(0, personaAvatarStore.GetByTokenCallCount);
        }
    }
}

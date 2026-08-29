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
//
// T299 payload facts drive GET /spectator/api/now-playing through the SAME production pipeline —
// the factory below additionally swaps IScheduleStore/IScheduleSpecialStore/TimeProvider (mirrors
// Story311_SpectatorShowFields.cs's own ShowFieldsWebFactory) whenever a scenario needs a real
// on-air persona id for CachingScheduleResolver to resolve; the T298 route facts above never
// exercise that resolver at all, so those construct the factory without them.

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Playout;
using GenWave.Host.Tests.Fakes;
using GenWave.Orchestration;

namespace GenWave.Host.Tests.Specs;

/// <summary>Boots the real Program.cs graph on the spectator surface with
/// <see cref="IPersonaAvatarStore"/> and <see cref="IStationImageStore"/> replaced by seedable
/// doubles — the same DI-swap shape <c>Story333_TheWornFace.cs</c>'s own
/// <c>PersonaAvatarWebFactory</c> uses, plus the spectator-mode arrangement
/// <c>Gh258_SpectatorStationLogo.cs</c>'s own <c>StationLogoWebFactory</c> already establishes for
/// this exact controller.
/// <para>
/// PLAN T299 extension: <paramref name="scheduleStore"/>/<paramref name="timeProvider"/> are ALSO
/// swappable (null = leave the real, Postgres-backed registrations in place, exactly as the T298
/// route facts need — they never call <see cref="CachingScheduleResolver.ResolveAsync"/>, so the
/// real store is never actually reached). <paramref name="publicBaseUrl"/> mirrors
/// <c>Story223_ArtworkEmission.cs</c>'s own "Station:PublicBaseUrl set" arrangement — required for
/// <c>SpectatorController.ResolveDjAvatarUrlAsync</c> to ever compose a URL at all (F129.2's own
/// gate).
/// </para>
/// <para>
/// PLAN T299 fix-round extension: <paramref name="activePersonaAccessor"/> is ALSO swappable
/// (default: a fresh <see cref="FakeActivePersonaAccessor"/>, empty <c>Names</c>) — needed once
/// <c>SpectatorController.DjIdentityAgrees</c> started gating <c>djAvatarUrl</c> on whether this
/// accessor's cached name for the on-air persona id agrees with the item-truth <c>dj</c> the
/// snapshot carries: a fact wanting to prove the URL-composition path (not the suppression gate)
/// must seed a name here that matches the snapshot's own <c>DjName</c>.
/// </para>
/// </summary>
file sealed class DjArtworkWebFactory(
    IPersonaAvatarStore? personaAvatarStore = null,
    IStationImageStore? stationImageStore = null,
    IScheduleStore? scheduleStore = null,
    TimeProvider? timeProvider = null,
    IActivePersonaAccessor? activePersonaAccessor = null,
    string publicBaseUrl = "") : WebApplicationFactory<Program>
{
    readonly IPersonaAvatarStore personaAvatarStore = personaAvatarStore ?? new FakePersonaAvatarStore();
    readonly IStationImageStore stationImageStore = stationImageStore ?? new FakeStationImageStore();
    readonly IActivePersonaAccessor activePersonaAccessor = activePersonaAccessor ?? new FakeActivePersonaAccessor();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Station:SpectatorMode", "true");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-x7z");
        if (!string.IsNullOrEmpty(publicBaseUrl))
            builder.UseSetting("Station:PublicBaseUrl", publicBaseUrl);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton(activePersonaAccessor);

            services.RemoveAll<IPersonaAvatarStore>();
            services.AddSingleton(personaAvatarStore);

            services.RemoveAll<IStationImageStore>();
            services.AddSingleton(stationImageStore);

            if (scheduleStore is not null)
            {
                services.RemoveAll<IScheduleStore>();
                services.AddSingleton(scheduleStore);
                // CachingScheduleResolver.ResolveAsync reads this on every call alongside
                // IScheduleStore (Story311's own T260 note) — an empty fake is enough since none
                // of these facts author a special.
                services.RemoveAll<IScheduleSpecialStore>();
                services.AddSingleton<IScheduleSpecialStore>(new FakeScheduleSpecialStore());
            }

            if (timeProvider is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(timeProvider);
            }
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
        // UTC, no DST/timezone concern rides these facts. Day() below derives the segment's own
        // DayOfWeek from this instant directly — never a separate hardcoded literal — so the two
        // can never silently drift apart the way a copy-pasted fixture date can (the bug this
        // exact mismatch produced while writing this fact: 2026-08-16 is a Sunday, not the
        // Wednesday Story311's own fixture comment names for its own, different date).
        static readonly DateTimeOffset Now = new(2026, 8, 16, 10, 0, 0, TimeSpan.Zero);
        const int Midnight = 24 * 60;
        const string PublicBaseUrl = "https://example.test";

        /// <summary>One all-day segment covering <see cref="Now"/>'s own day-of-week, persona id 1
        /// — the SAME id <see cref="SeededPersonaAvatarStoreAsync"/> seeds a face for, so the
        /// resolver's on-air answer and the avatar store agree on who is on air without threading
        /// an id through both separately.</summary>
        static ScheduleSegment[] Persona1AllDay() =>
        [
            new(Id: 1, Day: Now.DayOfWeek, StartMinute: 0, EndMinute: Midnight,
                PersonaId: 1, Genres: null, EnergyMin: null, EnergyMax: null),
        ];

        /// <param name="djName">The item-truth <c>dj</c> stamp (PLAN T299 fix round default: null,
        /// unchanged for every fact that predates <c>SpectatorController.DjIdentityAgrees</c>) — a
        /// fact proving <c>djAvatarUrl</c> actually composes a URL must pass the SAME name it also
        /// seeds onto <see cref="FakeActivePersonaAccessor.Names"/>, or the RIGHT FACE OR NO FACE
        /// gate suppresses it.</param>
        static NowPlayingSnapshot TrackSnapshot(string? djName = null) =>
            new(MediaId: "42", Title: "Night Drive", Artist: "The Waveforms", GainDb: -2.5,
                StartedAt: Now.AddMinutes(-5), DurationMs: 214_000, IsDrain: false, DjName: djName);

        /// <summary>Warms <see cref="CachingScheduleResolver"/>'s cached week snapshot exactly once
        /// — mirrors Story311's own <c>WarmScheduleAsync</c>: <see cref="CachingScheduleResolver.TryGetCurrent"/>
        /// answers null until this has run once.</summary>
        static Task WarmScheduleAsync(IServiceProvider services) =>
            services.GetRequiredService<CachingScheduleResolver>().ResolveAsync(CancellationToken.None);

        static async Task<JsonElement> FetchNowPlayingAsync(WebApplicationFactory<Program> factory, NowPlayingSnapshot snapshot)
        {
            await WarmScheduleAsync(factory.Services);
            factory.Services.GetRequiredService<NowPlayingService>().Update("1", snapshot); // SingleStation.IdString

            var client = factory.CreateClient();
            var response = await client.GetAsync("/spectator/api/now-playing");
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        }

        [Fact]
        public async Task DjAvatarUrlCarriesTheOnAirPersonasTokenUrl()
        {
            // RIGHT FACE OR NO FACE (SPEC F129.6, PLAN T299 fix round, SpectatorController.DjIdentityAgrees)
            // demands the item-truth dj name agree with the resolver's own cached name before it
            // will ever compose a URL — seed both to the SAME value so this fact proves the
            // URL-composition path itself, not the suppression gate
            // (DjAvatarUrlIsNullWhenTheCachedNameDisagreesWithTheItemTruthDj below proves that one).
            const string djName = "Nova";
            var personaAvatarStore = await SeededPersonaAvatarStoreAsync();
            var scheduleStore = new FakeScheduleStore(new ScheduleWeekSnapshot(Persona1AllDay()));
            var activePersonaAccessor = new FakeActivePersonaAccessor();
            activePersonaAccessor.Names[1] = djName;
            await using var factory = new DjArtworkWebFactory(
                personaAvatarStore: personaAvatarStore, scheduleStore: scheduleStore,
                timeProvider: new FakeTimeProvider(Now), activePersonaAccessor: activePersonaAccessor,
                publicBaseUrl: PublicBaseUrl);

            var body = await FetchNowPlayingAsync(factory, TrackSnapshot(djName));

            // SAME composition SpectatorArtworkController.GetDjArtwork's own route pattern serves —
            // {PublicBaseUrl}/spectator/api/artwork/dj/{token}, the token this persona's seeded face
            // (SeededPersonaAvatarStoreAsync) actually carries.
            Assert.Equal($"{PublicBaseUrl}/spectator/api/artwork/dj/{CurrentToken}",
                body.GetProperty("djAvatarUrl").GetString());
        }

        [Fact]
        public async Task DjAvatarUrlIsNullWhenTheCachedNameDisagreesWithTheItemTruthDj()
        {
            // RIGHT FACE OR NO FACE (SPEC F129.6, PLAN T299 fix round): persona 1 genuinely wears a
            // face (SeededPersonaAvatarStoreAsync) and IS who the resolver names on air
            // (Persona1AllDay), but the item-truth dj the snapshot carries names someone else — the
            // exact shape a boundary mid-drain produces (GetNowPlaying's own BOUNDARY SKEW remarks).
            // djAvatarUrl must suppress to null rather than pair persona 1's real face with the
            // wrong name.
            var personaAvatarStore = await SeededPersonaAvatarStoreAsync();
            var scheduleStore = new FakeScheduleStore(new ScheduleWeekSnapshot(Persona1AllDay()));
            var activePersonaAccessor = new FakeActivePersonaAccessor();
            activePersonaAccessor.Names[1] = "Nova";
            await using var factory = new DjArtworkWebFactory(
                personaAvatarStore: personaAvatarStore, scheduleStore: scheduleStore,
                timeProvider: new FakeTimeProvider(Now), activePersonaAccessor: activePersonaAccessor,
                publicBaseUrl: PublicBaseUrl);

            var body = await FetchNowPlayingAsync(factory, TrackSnapshot("Outgoing Dj"));

            Assert.True(body.TryGetProperty("djAvatarUrl", out var djAvatarUrl));
            Assert.Equal(JsonValueKind.Null, djAvatarUrl.ValueKind);
        }

        [Fact]
        public async Task DjAvatarUrlIsNullWhenTheOnAirPersonaIsFaceless()
        {
            // No SeededPersonaAvatarStoreAsync call here — the default FakePersonaAvatarStore has
            // no rows at all, so GetTokenByPersonaIdAsync(1, …) reports null: an honest "no face",
            // never an error, mirroring IPersonaAvatarStore's own contract.
            var scheduleStore = new FakeScheduleStore(new ScheduleWeekSnapshot(Persona1AllDay()));
            await using var factory = new DjArtworkWebFactory(
                scheduleStore: scheduleStore, timeProvider: new FakeTimeProvider(Now),
                publicBaseUrl: PublicBaseUrl);

            var body = await FetchNowPlayingAsync(factory, TrackSnapshot());

            // A present key with a null value (F93.3's own "present key, absent value" idiom for
            // ArtworkUrl) — never an absent property, which would instead be a disclosure-contract
            // violation (TheDisclosureContractPinsTheCompletePropertySet below).
            Assert.True(body.TryGetProperty("djAvatarUrl", out var djAvatarUrl));
            Assert.Equal(JsonValueKind.Null, djAvatarUrl.ValueKind);
        }

        [Fact]
        public async Task TheDisclosureContractPinsTheCompletePropertySet()
        {
            // F93.5/F67.5 amendment: Story183_DisclosureContractCompleteness.cs (and its Story230/
            // Story248 census copies) own the canonical blessed-shape table, amended alongside this
            // task to bless djAvatarUrl. This fact is this suite's OWN complementary proof, off the
            // SHIPPED wire response from the real production pipeline (not an in-memory instance) —
            // an unblessed/missing field on the actual wire still fails here, independently of
            // whatever those tables already assert.
            var personaAvatarStore = await SeededPersonaAvatarStoreAsync();
            var scheduleStore = new FakeScheduleStore(new ScheduleWeekSnapshot(Persona1AllDay()));
            await using var factory = new DjArtworkWebFactory(
                personaAvatarStore: personaAvatarStore, scheduleStore: scheduleStore,
                timeProvider: new FakeTimeProvider(Now), publicBaseUrl: PublicBaseUrl);

            var body = await FetchNowPlayingAsync(factory, TrackSnapshot());

            var properties = body.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            Assert.Equal(
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "title", "artist", "startedAt", "durationMs", "listeners",
                    "dj", "djAvatarUrl", "show", "upNext", "artworkUrl", "airing", "state", "kind",
                },
                properties);
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

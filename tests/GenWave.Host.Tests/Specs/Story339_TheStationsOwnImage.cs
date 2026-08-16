// STORY-339 — The station's own image (SPEC F131, gh-#15 · PLAN T307)
//
// BDD specification — xUnit. The write paths (PUT/DELETE/GET /api/station/image) and every
// consumer route drive the real production pipeline through WebApplicationFactory<Program> against
// a FakeStationImageStore — this project has no Postgres fixture; the real station.station_image
// SQL (including StationImageInput's own derived byte size) is T290's own coverage against real
// Postgres, GenWave.MediaLibrary.Tests (Story333_VisualLayerStores.cs). Mirrors
// Story333_TheWornFace.cs's own WIRED-T295 posture one-for-one.
//
// StationImageCache's own memo-TTL/never-throws/cancellation-immunity facts drive
// ArtworkUrlResolver.ResolveAsync directly (Story336_TheFaceOnTheStream.cs's own idiom for the
// SAME class of proof over PersonaAvatarTokenCache) rather than through HTTP: a deterministic
// gated-fetch race is far more reliably orchestrated against a direct method call than a real
// client-cancels-mid-request race over TestServer.

namespace GenWave.Host.Tests.Specs;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Artwork;
using GenWave.Host.Auth;
using GenWave.Host.Engine;
using GenWave.Host.Images;
using GenWave.Host.Options;
using GenWave.Host.Tests.Fakes;

public static class FeatureTheStationsOwnImage
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the pipeline + write paths (T291 pipeline via T307's own controller)
    // ---------------------------------------------------------------------

    public sealed class ScenarioUploadNormalizesIntoTheSingletonRow
    {
        [Fact]
        public async Task TheStoredBytesAreAFresh512SquareMetadataFreePng()
        {
            // Given a real, plausibly-sized, non-square JPEG (any accepted input — the T291
            // pipeline's own gate/crop/re-encode correctness is Story333's own exhaustive coverage;
            // this fact's job is proving the WRITE PATH persists that real output),
            var stationImageStore = new FakeStationImageStore();
            await using var factory = new StationImageWebFactory(stationImageStore);
            var client = await StationImageWebFactory.LoggedInClientAsync(factory);
            using var content = StationImageFixtures.ImageBody(TestImages.CreateJpeg(400, 300));

            // When PUT /api/station/image is called (the real production route, real ffmpeg),
            var response = await client.PutAsync("/api/station/image", content);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

            // Then the stored row is a fresh 512×512 metadata-free PNG.
            var stored = await stationImageStore.GetAsync(CancellationToken.None);
            Assert.NotNull(stored);
            Assert.True(PngImageHeader.HasSignature(stored!.Bytes));
            Assert.True(PngImageHeader.TryReadDimensions(stored.Bytes, out var width, out var height));
            Assert.Equal((512, 512), (width, height));
        }

        [Fact]
        public async Task TheTokenRotatesOnEveryWrite()
        {
            // Given a station that has already customized its image once,
            var stationImageStore = new FakeStationImageStore();
            await using var factory = new StationImageWebFactory(stationImageStore);
            var client = await StationImageWebFactory.LoggedInClientAsync(factory);
            var bytes = TestImages.CreatePng(512, 512);

            async Task<string> PutAndReadTokenAsync()
            {
                using var content = StationImageFixtures.ImageBody(bytes);
                var response = await client.PutAsync("/api/station/image", content);
                Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
                var dto = await response.Content.ReadFromJsonAsync<StationImageDto>();
                return dto?.Token ?? throw new InvalidOperationException("PUT succeeded with no token in its own response.");
            }

            // When it is written to twice in a row — two independent uploads of the exact SAME
            // bytes both times, so only the controller's own fresh mint could explain a difference,
            var firstToken = await PutAndReadTokenAsync();
            var secondToken = await PutAndReadTokenAsync();

            // Then the second write's token is never the first's — the row was genuinely rotated.
            Assert.NotEqual(firstToken, secondToken);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — token entropy (T290/T295/T307 rider: shape + rotation + uniqueness by construction)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheTokenIsCryptographicallyRandom
    {
        [Fact]
        public async Task EveryWriteMintsA128BitLowercaseHexTokenDistinctFromTheLast()
        {
            // Given a station with no customized image yet,
            var stationImageStore = new FakeStationImageStore();
            await using var factory = new StationImageWebFactory(stationImageStore);
            var client = await StationImageWebFactory.LoggedInClientAsync(factory);
            var bytes = TestImages.CreatePng(512, 512);

            async Task<string> PutAndReadTokenAsync()
            {
                using var content = StationImageFixtures.ImageBody(bytes);
                var response = await client.PutAsync("/api/station/image", content);
                Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
                var dto = await response.Content.ReadFromJsonAsync<StationImageDto>();
                return dto?.Token ?? throw new InvalidOperationException("PUT succeeded with no token in its own response.");
            }

            // When the SAME image is written twice in a row,
            var firstToken = await PutAndReadTokenAsync();
            var secondToken = await PutAndReadTokenAsync();

            // Then both tokens are shaped as 128-bit lowercase hex (the F131.1/F88 opaque-token
            // idiom — uniqueness of any ONE such value is by construction, never a database round
            // trip: StationImageController's own TOKEN ENTROPY remarks, mirroring
            // PersonaAvatarController's) AND the second write's token is never the first's.
            Assert.True(ArtworkToken.IsWellFormed(firstToken), $"expected 32 lowercase hex chars, got \"{firstToken}\"");
            Assert.True(ArtworkToken.IsWellFormed(secondToken), $"expected 32 lowercase hex chars, got \"{secondToken}\"");
            Assert.NotEqual(firstToken, secondToken);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — every slot follows, live (SPEC F131.2/F131.3, AC2)
    // ---------------------------------------------------------------------

    public sealed class ScenarioEverySlotFollowsLive
    {
        [Fact]
        public async Task TheArtworkFallbackServesTheRowBytesWhenSet()
        {
            // Given a station image just uploaded,
            var stationImageStore = new FakeStationImageStore();
            await using var factory = new StationImageWebFactory(stationImageStore);
            var client = await StationImageWebFactory.LoggedInClientAsync(factory);
            using var content = StationImageFixtures.ImageBody(TestImages.CreatePng(512, 512));
            var putResponse = await client.PutAsync("/api/station/image", content);
            Assert.True(putResponse.IsSuccessStatusCode, await putResponse.Content.ReadAsStringAsync());
            var stored = await stationImageStore.GetAsync(CancellationToken.None);

            // When the F88 no-art fallback is hit anonymously (a malformed cover-art token —
            // SpectatorArtworkController.GetArtwork's own generic route, unified onto the
            // row-else-shipped-logo ladder at T307),
            var anonymous = factory.CreateClient();
            var fallback = await anonymous.GetByteArrayAsync("/spectator/api/artwork/not-a-token");

            // Then it serves the uploaded image's own bytes — no restart required.
            Assert.Equal(stored!.Bytes, fallback);
        }

        [Fact]
        public async Task TheFeederStampsTheTokenVersionedStationUrlWhenCustomized()
        {
            // Given a station image just uploaded,
            var stationImageStore = new FakeStationImageStore();
            await using var factory = new StationImageWebFactory(stationImageStore);
            var client = await StationImageWebFactory.LoggedInClientAsync(factory);
            using var content = StationImageFixtures.ImageBody(TestImages.CreatePng(512, 512));
            var putResponse = await client.PutAsync("/api/station/image", content);
            var dto = await putResponse.Content.ReadFromJsonAsync<StationImageDto>();

            // When the SAME running process's own ArtworkUrlResolver (the feeder push path) resolves
            // a station-voiced TTS item — no restart, the identical DI singleton the PUT just wrote
            // through,
            var resolver = factory.Services.GetRequiredService<ArtworkUrlResolver>();
            var item = new MediaItem("tts:ident1", "/tts/ident1.wav", "GenWave", DefaultLoudness,
                SegmentKind: SegmentKind.StationId);
            var url = await resolver.ResolveAsync(item, CancellationToken.None);

            // Then it stamps the TOKEN-VERSIONED station URL, not the shipped constant.
            Assert.Equal($"{StationImageWebFactory.PublicBaseUrl}/spectator/api/artwork/station/{dto!.Token}", url);
        }

        [Fact]
        public async Task TheSpectatorFaviconServesTheRowBytesWithShortCache()
        {
            // Given a station image just uploaded,
            var stationImageStore = new FakeStationImageStore();
            await using var factory = new StationImageWebFactory(stationImageStore);
            var client = await StationImageWebFactory.LoggedInClientAsync(factory);
            using var content = StationImageFixtures.ImageBody(TestImages.CreatePng(512, 512));
            await client.PutAsync("/api/station/image", content);
            var stored = await stationImageStore.GetAsync(CancellationToken.None);

            // When the spectator favicon is fetched anonymously,
            var anonymous = factory.CreateClient();
            var response = await anonymous.GetAsync("/spectator/favicon.ico");

            // Then it serves the row's own bytes — SHORT cache, ETag'd, never immutable (F131.3's
            // own "stable URL" posture — the SAME cadence every other spectator asset here uses).
            Assert.Equal(stored!.Bytes, await response.Content.ReadAsByteArrayAsync());
            var cache = response.Headers.CacheControl;
            Assert.NotNull(cache);
            Assert.True(cache!.Public);
            Assert.Equal(TimeSpan.FromSeconds(300), cache.MaxAge);
            Assert.DoesNotContain(cache.Extensions, ext => ext.Name == "immutable");
            Assert.NotNull(response.Headers.ETag);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the ladder unification holds (PLAN T307 review rider — regression-critical)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheLadderIsUnified
    {
        [Fact]
        public async Task GetArtworkAndGetDjArtworkFallbacksAreByteIdenticalWhenCustomized()
        {
            // Given a station image just uploaded,
            var stationImageStore = new FakeStationImageStore();
            await using var factory = new StationImageWebFactory(stationImageStore);
            var client = await StationImageWebFactory.LoggedInClientAsync(factory);
            using var content = StationImageFixtures.ImageBody(TestImages.CreatePng(512, 512));
            await client.PutAsync("/api/station/image", content);

            // When BOTH no-oracle fallbacks are hit anonymously (malformed tokens on each of the
            // two distinct routes),
            var anonymous = factory.CreateClient();
            var fromCoverArt = await anonymous.GetAsync("/spectator/api/artwork/not-a-token");
            var fromDjArt = await anonymous.GetAsync("/spectator/api/artwork/dj/not-a-token");

            // Then the two ladders no longer diverge — byte-identical bodies and Content-Type,
            // proving ONE shared ServeStationImageAsync path, not two independent implementations
            // that merely happen to agree today.
            Assert.Equal(await fromCoverArt.Content.ReadAsByteArrayAsync(), await fromDjArt.Content.ReadAsByteArrayAsync());
            Assert.Equal(fromCoverArt.Content.Headers.ContentType, fromDjArt.Content.Headers.ContentType);
        }

        [Fact]
        public async Task GetArtworkAndGetDjArtworkFallbacksAreByteIdenticalWhenNotCustomized()
        {
            // Given a station that has never customized its image,
            await using var factory = new StationImageWebFactory();
            var client = factory.CreateClient();

            var fromCoverArt = await client.GetAsync("/spectator/api/artwork/not-a-token");
            var fromDjArt = await client.GetAsync("/spectator/api/artwork/dj/not-a-token");

            Assert.Equal(await fromCoverArt.Content.ReadAsByteArrayAsync(), await fromDjArt.Content.ReadAsByteArrayAsync());
            Assert.Equal(fromCoverArt.Content.Headers.ContentType, fromDjArt.Content.Headers.ContentType);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — deletion reverts everything (SPEC F131.1/F131.5, AC3)
    // ---------------------------------------------------------------------

    public sealed class ScenarioDeletionReverts
    {
        [Fact]
        public async Task EverySlotReturnsToTheShippedLogoBytes()
        {
            // Given a station image uploaded, then removed,
            var stationImageStore = new FakeStationImageStore();
            await using var factory = new StationImageWebFactory(stationImageStore);
            var client = await StationImageWebFactory.LoggedInClientAsync(factory);
            using var content = StationImageFixtures.ImageBody(TestImages.CreatePng(512, 512));
            await client.PutAsync("/api/station/image", content);

            // When DELETE /api/station/image is called,
            var deleteResponse = await client.DeleteAsync("/api/station/image");
            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

            // Then every slot reverts — byte-identical to a station that never uploaded at all
            // (a wholly separate, never-touched factory/store).
            await using var neverUploadedFactory = new StationImageWebFactory();
            var neverUploadedClient = neverUploadedFactory.CreateClient();

            var anonymous = factory.CreateClient();
            var fallbackAfterDelete = await anonymous.GetByteArrayAsync("/spectator/api/artwork/not-a-token");
            var faviconAfterDelete = await anonymous.GetByteArrayAsync("/spectator/favicon.ico");
            var logoAfterDelete = await anonymous.GetByteArrayAsync("/spectator/logo.png");

            var neverUploadedFallback = await neverUploadedClient.GetByteArrayAsync("/spectator/api/artwork/not-a-token");
            var neverUploadedFavicon = await neverUploadedClient.GetByteArrayAsync("/spectator/favicon.ico");
            var neverUploadedLogo = await neverUploadedClient.GetByteArrayAsync("/spectator/logo.png");

            Assert.Equal(neverUploadedFallback, fallbackAfterDelete);
            Assert.Equal(neverUploadedFavicon, faviconAfterDelete);
            Assert.Equal(neverUploadedLogo, logoAfterDelete);

            // And the feeder stamp reverts to the SAME shipped constant URL a never-uploaded station
            // emits — never a stale token URL from the deleted row.
            var resolver = factory.Services.GetRequiredService<ArtworkUrlResolver>();
            var item = new MediaItem("tts:ident2", "/tts/ident2.wav", "GenWave", DefaultLoudness,
                SegmentKind: SegmentKind.StationId);
            var url = await resolver.ResolveAsync(item, CancellationToken.None);
            Assert.Equal($"{StationImageWebFactory.PublicBaseUrl}{StationArtworkPaths.ShippedFallbackPath}", url);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the write invalidates the shared memo immediately (PLAN T307 fix round R1)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheCacheInvalidatesImmediatelyOnWrite
    {
        [Fact]
        public async Task AWriteIsVisibleToTheNextCacheReadWithNoTtlWait()
        {
            // Given a station image already uploaded AND already warm in StationImageCache — an
            // anonymous read through the SAME shared no-oracle fallback that ScenarioEverySlotFollowsLive
            // exercises, so this run's DI-singleton StationImageCache is holding the FIRST image's
            // bytes in its memo before anything else happens.
            var stationImageStore = new FakeStationImageStore();
            await using var factory = new StationImageWebFactory(stationImageStore);
            var client = await StationImageWebFactory.LoggedInClientAsync(factory);
            using (var firstContent = StationImageFixtures.ImageBody(TestImages.CreatePng(512, 512)))
                await client.PutAsync("/api/station/image", firstContent);
            var firstStored = await stationImageStore.GetAsync(CancellationToken.None);

            var anonymous = factory.CreateClient();
            var warm = await anonymous.GetByteArrayAsync("/spectator/api/artwork/not-a-token");
            Assert.Equal(firstStored!.Bytes, warm);

            // When a SECOND, different image replaces it — the same running process, no restart,
            // and StationImageCache.StalenessBound (30s) is nowhere close to elapsing,
            using (var secondContent = StationImageFixtures.ImageBody(TestImages.CreatePng(400, 300)))
            {
                var putResponse = await client.PutAsync("/api/station/image", secondContent);
                Assert.True(putResponse.IsSuccessStatusCode, await putResponse.Content.ReadAsStringAsync());
            }
            var secondStored = await stationImageStore.GetAsync(CancellationToken.None);
            Assert.NotEqual(firstStored.Token, secondStored!.Token);

            // Then the VERY NEXT read already serves the second image — StationImageController's own
            // StationImageCache.Invalidate() call busted the warm memo, rather than this reader having
            // to wait out the TTL to stop seeing the first image's now-stale bytes.
            var afterSecondWrite = await anonymous.GetByteArrayAsync("/spectator/api/artwork/not-a-token");
            Assert.Equal(secondStored.Bytes, afterSecondWrite);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the admin snapshot carries the token, bytes-free (PLAN T307 fix round F1)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheAdminSnapshotCarriesTheToken
    {
        [Fact]
        public async Task ApiStationsReportsNullUntilCustomizedThenTheFreshTokenWithNoRestart()
        {
            // Given a station that has never customized its image,
            var stationImageStore = new FakeStationImageStore();
            await using var factory = new StationImageWebFactory(stationImageStore);
            var client = await StationImageWebFactory.LoggedInClientAsync(factory);

            // Then GET /api/stations (the authed shell's own per-navigation snapshot, AuthController's
            // GetTokenAsync read) carries a null token — an honest "no customization".
            var before = await client.GetFromJsonAsync<List<StationDto>>("/api/stations");
            Assert.NotNull(before);
            Assert.Null(Assert.Single(before!).StationImageToken);

            // When a station image is uploaded,
            using var content = StationImageFixtures.ImageBody(TestImages.CreatePng(512, 512));
            var putResponse = await client.PutAsync("/api/station/image", content);
            var dto = await putResponse.Content.ReadFromJsonAsync<StationImageDto>();

            // Then the very next GET /api/stations reports the SAME freshly-rotated token — no api
            // restart, and never a bytes fetch: this read goes through
            // IStationImageStore.GetTokenAsync directly, not StationImageCache's ≤30s memo (this
            // controller's own remarks — a stale favicon href is harmless, but this read is cheap
            // enough it never has to accept the staleness anyway).
            var after = await client.GetFromJsonAsync<List<StationDto>>("/api/stations");
            Assert.Equal(dto!.Token, Assert.Single(after!).StationImageToken);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — no oracle (SPEC F131.4, AC4)
    // ---------------------------------------------------------------------

    public sealed class ScenarioUnknownStationTokensAreNotAProbe
    {
        [Fact]
        public async Task AnUnknownStationTokenServesTheCurrentBytesWith200()
        {
            // Given a station image with a KNOWN current token,
            var stationImageStore = new FakeStationImageStore();
            await using var factory = new StationImageWebFactory(stationImageStore);
            var client = await StationImageWebFactory.LoggedInClientAsync(factory);
            using var content = StationImageFixtures.ImageBody(TestImages.CreatePng(512, 512));
            await client.PutAsync("/api/station/image", content);
            var stored = await stationImageStore.GetAsync(CancellationToken.None);

            // When a DIFFERENT, well-formed (never-minted) token is requested on the station token
            // route,
            const string unknownButWellFormedToken = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4";
            Assert.NotEqual(unknownButWellFormedToken, stored!.Token);
            var anonymous = factory.CreateClient();
            var response = await anonymous.GetAsync($"/spectator/api/artwork/station/{unknownButWellFormedToken}");

            // Then it serves the CURRENT bytes with 200 — never a 404, never the old/no image
            // (F131.4: no oracle, no history).
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(stored.Bytes, await response.Content.ReadAsByteArrayAsync());
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — StationImageCache never throws into any reader (PLAN T307 rider, mirroring
    // Story336_TheFaceOnTheStream.cs's own T300 fix-round proofs, now for the station image)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheStoreNeverThrowsIntoTheResolver
    {
        [Fact]
        public async Task AFaultingStoreDegradesToTheShippedFallbackThenRecoversOnceItRecovers()
        {
            // The never-throws contract + recovery: a faulting store degrades ResolveAsync to the
            // shipped station URL, never a thrown exception into the push path — and because a
            // fault memoizes only a permanently-stale sentinel that can never evaluate as fresh,
            // the very next call still retries the store immediately, with no StalenessBound wait
            // required, and the customized image returns.
            var stationImageStore = SeededStationImageStore();
            stationImageStore.ThrowOnCallNumber = 1; // only the first store call faults
            var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var resolver = Resolver(stationImageStore, timeProvider);
            var item = new MediaItem("tts:crosstalk1", "/tts/crosstalk1.wav", "GenWave", DefaultLoudness,
                SegmentKind: SegmentKind.Crosstalk);

            var whileFaulting = await resolver.ResolveAsync(item, CancellationToken.None);
            Assert.Equal($"{PublicBaseUrl}{StationArtworkPaths.ShippedFallbackPath}", whileFaulting);

            var recovered = await resolver.ResolveAsync(item, CancellationToken.None);
            Assert.Equal($"{PublicBaseUrl}{StationArtworkPaths.PathPrefix}{StationImageToken}", recovered);

            // Stays memoized like any ordinary successful fetch across a further tick inside the
            // same staleness window — the recovered answer is not a one-shot fluke.
            timeProvider.Advance(StationImageCache.StalenessBound - TimeSpan.FromSeconds(1));
            var stillGood = await resolver.ResolveAsync(item, CancellationToken.None);
            Assert.Equal($"{PublicBaseUrl}{StationArtworkPaths.PathPrefix}{StationImageToken}", stillGood);
            Assert.Equal(2, stationImageStore.GetCallCount);
        }
    }

    public sealed class ScenarioACancelledCallersColdFetchNeverPoisonsTheSharedMemo
    {
        [Fact]
        public async Task ACancelledCallerStillLeavesLaterResolvesAnswering()
        {
            // Mirrors Story336's own F1 pin exactly, now for StationImageCache: FetchAsync's own
            // CancellationToken.None binding means the shared fetch is never bound to any one
            // caller's token, so a first caller's own cancellation can never wedge the memo for
            // every later caller.
            var stationImageStore = SeededStationImageStore();
            stationImageStore.Gate = new TaskCompletionSource<StationImage?>();
            var resolver = Resolver(stationImageStore);
            var item = new MediaItem("tts:crosstalk2", "/tts/crosstalk2.wav", "GenWave", DefaultLoudness,
                SegmentKind: SegmentKind.Crosstalk);

            using var cts = new CancellationTokenSource();
            var poisonedCall = resolver.ResolveAsync(item, cts.Token);
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => poisonedCall);

            // The store "recovers" (or simply finishes) — releases whichever fetch(es) are gated,
            // proving the shared memo was never wedged by the first caller's own cancellation.
            stationImageStore.Gate.SetResult(SeededStationImage());
            var url = await resolver.ResolveAsync(item, CancellationToken.None);

            Assert.Equal($"{PublicBaseUrl}{StationArtworkPaths.PathPrefix}{StationImageToken}", url);
        }
    }

    public sealed class ScenarioTheHotPathStaysCold
    {
        [Fact]
        public async Task StationImageResolutionIssuesNoPerTickRead()
        {
            // Pins StationImageCache's own ≤30s TTL memo (SPEC F131.2, PLAN T307 rider) — the ONE
            // shared memo every station-image reader shares, proven here on the push path exactly
            // as Story336 proves PersonaAvatarTokenCache's own bound.
            var stationImageStore = SeededStationImageStore();
            var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var resolver = Resolver(stationImageStore, timeProvider);
            var item = new MediaItem("tts:crosstalk3", "/tts/crosstalk3.wav", "GenWave", DefaultLoudness,
                SegmentKind: SegmentKind.Crosstalk);

            await resolver.ResolveAsync(item, CancellationToken.None);
            await resolver.ResolveAsync(item, CancellationToken.None);
            await resolver.ResolveAsync(item, CancellationToken.None);

            Assert.Equal(1, stationImageStore.GetCallCount);

            timeProvider.Advance(StationImageCache.StalenessBound + TimeSpan.FromSeconds(1));
            await resolver.ResolveAsync(item, CancellationToken.None);

            Assert.Equal(2, stationImageStore.GetCallCount);
        }
    }

    // ── Shared fixtures for the direct-resolver StationImageCache proofs above ─────────────────────

    const string PublicBaseUrl = "https://example.test";
    const string StationImageToken = "e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2";

    static readonly Loudness DefaultLoudness = new(-16.0, -1.0, Measurable: true);

    static StationImage SeededStationImage() =>
        new([0xFA, 0xCE], 1, "sha256-stub", StationImageToken, DateTime.UtcNow);

    static FakeStationImageStore SeededStationImageStore()
    {
        var store = new FakeStationImageStore();
        store.Seed(SeededStationImage());
        return store;
    }

    static ArtworkUrlResolver Resolver(IStationImageStore stationImageStore, TimeProvider? timeProvider = null) => new(
        new FakeOptionsMonitor<StationOptions>(new StationOptions { PublicBaseUrl = PublicBaseUrl }),
        new FakeArtworkTokenStore(), new FakeActivePersonaAccessor(),
        new PersonaAvatarTokenCache(
            new FakePersonaAvatarStore(), timeProvider ?? TimeProvider.System, NullLogger<PersonaAvatarTokenCache>.Instance),
        new StationImageCache(
            stationImageStore, timeProvider ?? TimeProvider.System, NullLogger<StationImageCache>.Instance));
}

// ── Test harness ───────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for this file's own T307 write-path/consumer
/// facts — boots the real Program.cs graph with <see cref="IStationImageStore"/> replaced by a
/// <see cref="FakeStationImageStore"/> (mirrors <c>Story333_TheWornFace.cs</c>'s own
/// <c>PersonaAvatarWebFactory</c>). <see cref="ImageNormalizeService"/> is left WIRED to its real
/// production registration (real ffmpeg) — never faked. <c>Station:PublicBaseUrl</c> is always set
/// (<see cref="PublicBaseUrl"/>) so the SAME running process's own <see cref="ArtworkUrlResolver"/>
/// singleton can be resolved and driven from <c>factory.Services</c> for this file's own "no
/// restart" facts, mirroring how <see cref="Story335_TheFaceOnThePublicSurface"/>'s own
/// <c>DjArtworkWebFactory</c> arranges the same for <c>SpectatorController</c>.
/// </summary>
file sealed class StationImageWebFactory(IStationImageStore? stationImageStore = null) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story339-stationimage";

    /// <summary>The base URL every "no restart" fact resolves <see cref="ArtworkUrlResolver"/>
    /// annotations against.</summary>
    public const string PublicBaseUrl = "https://example.test";

    readonly IStationImageStore stationImageStore = stationImageStore ?? new FakeStationImageStore();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Station:SpectatorMode", "true");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Station:PublicBaseUrl", PublicBaseUrl);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());

            services.RemoveAll<IStationImageStore>();
            services.AddSingleton(stationImageStore);
        });
    }

    public static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }
}

/// <summary>Fixture builders shared across this file's own write-path facts — <c>file</c>-scoped,
/// mirrors <c>PersonaAvatarFixtures</c>'s own established idiom.</summary>
file static class StationImageFixtures
{
    public static ByteArrayContent ImageBody(byte[] bytes, string contentType = "image/png")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return content;
    }
}

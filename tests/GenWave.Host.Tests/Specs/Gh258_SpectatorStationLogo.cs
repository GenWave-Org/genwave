// gh-#258 — GenWave logo as DJ-break album art renders fuzzy
//
// BDD specification — xUnit. Root cause: the spectator page's station-art slot (DJ break, no-art
// track, standby, and the artwork endpoint's F88.3 no-oracle fallback) served
// /spectator/favicon.ico — an .ico whose largest frame is 32px — upscaled into the 72px CSS
// (2-3x device-pixel) now-playing art box, while real cover art arrives as a ≤500px jpeg. The fix
// serves a card-sized station mark instead: wwwroot/spectator/logo.png, byte-identical to
// admin-ui/app/icon.png (the 256px derivation of the operator's repo-root GenWave-logo.png) —
// the exact provenance discipline Story180 pinned for the favicon. The favicon stays: it is the
// TAB icon; the art slot just stops borrowing it.

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

/// <summary>
/// PLAN T307 (ladder unification): <see cref="IStationImageStore"/> is now ALSO swapped for a
/// seedable-but-unseeded double — mirrors <c>Story335_TheFaceOnThePublicSurface.cs</c>'s own
/// <c>DjArtworkWebFactory</c>. Needed because <c>SpectatorArtworkController.GetArtwork</c>'s own
/// fallback (this file's own <c>TheArtworkEndpointFallbackServesTheLogoBytes</c>) and
/// <see cref="Api.SpectatorPageEndpoints"/>'s own <c>logo.png</c> route now BOTH read
/// <see cref="IStationImageStore"/> through <c>StationImageCache</c> — this project has no Postgres
/// fixture, so an un-swapped store would otherwise attempt a real connection on every fact here.
/// </summary>
file sealed class StationLogoWebFactory(bool spectatorMode = true) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Station:SpectatorMode", spectatorMode ? "true" : "false");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-x7z");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
            services.RemoveAll<IStationImageStore>();
            services.AddSingleton<IStationImageStore>(new FakeStationImageStore());
        });
    }
}

public static class FeatureSpectatorStationLogo
{
    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent!;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioLogoServedForTheArtSlot
    {
        [Fact]
        public async Task ThePageUsesTheLogoNotTheFaviconForNowPlayingArt()
        {
            await using var factory = new StationLogoWebFactory();
            var client = factory.CreateClient();

            var html = await client.GetStringAsync("/spectator");

            // The <img> art slot references logo.png; the favicon remains only as the tab icon's
            // <link rel="icon">.
            Assert.Contains("id=\"now-playing-art\" src=\"/spectator/logo.png\"", html, StringComparison.Ordinal);
        }

        [Fact]
        public async Task TheScriptFallsBackToTheLogoNotTheFavicon()
        {
            await using var factory = new StationLogoWebFactory();
            var client = factory.CreateClient();

            var js = await client.GetStringAsync("/spectator/app.js");

            Assert.Contains("STATION_ICON_PATH = \"/spectator/logo.png\"", js, StringComparison.Ordinal);
        }

        [Fact]
        public async Task LogoServesAsPng()
        {
            await using var factory = new StationLogoWebFactory();
            var client = factory.CreateClient();

            var response = await client.GetAsync("/spectator/logo.png");

            Assert.Equal(("image/png", true),
                (response.Content.Headers.ContentType?.MediaType, response.IsSuccessStatusCode));
        }

        [Fact]
        public async Task LogoBytesMatchTheAdminUiIcon()
        {
            // Same provenance rule as Story180's favicon parity: the one station identity is the
            // operator's GenWave-logo.png, and this asset is its existing 256px derivation —
            // never an independently regenerated binary that could drift.
            await using var factory = new StationLogoWebFactory();
            var client = factory.CreateClient();

            var served = await client.GetByteArrayAsync("/spectator/logo.png");
            var adminUi = await File.ReadAllBytesAsync(
                Path.Combine(RepoRoot(), "admin-ui", "app", "icon.png"));

            Assert.Equal(adminUi, served);
        }

        [Fact]
        public async Task LogoIsLargeEnoughForTheCardSlot()
        {
            // The art box renders 72px CSS — up to ~216 device pixels at 3x. The mark must beat
            // that on its long side (the favicon's 32px is the fuzz this issue is about). PNG
            // IHDR: width/height are the two big-endian u32s at byte offsets 16 and 20.
            await using var factory = new StationLogoWebFactory();
            var client = factory.CreateClient();

            var bytes = await client.GetByteArrayAsync("/spectator/logo.png");
            var width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
            var height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];

            Assert.True(Math.Max(width, height) >= 216,
                $"station mark is {width}x{height}; the 72px art slot needs ≥216px on the long side to stay sharp at 3x");
        }

        [Fact]
        public async Task TheArtworkEndpointFallbackServesTheLogoBytes()
        {
            // The F88.3 no-oracle fallback must serve the SAME card-sized mark — it was the other
            // serving path for the fuzzy favicon bytes. A malformed token is used deliberately:
            // the real IArtworkTokenStore rejects it before any database round trip, so this runs
            // through the production pipeline with no DB (Story222 covers the resolvable shapes
            // against a scripted store).
            await using var factory = new StationLogoWebFactory();
            var client = factory.CreateClient();

            var fallback = await client.GetByteArrayAsync("/spectator/api/artwork/not-a-token");
            var logo = await client.GetByteArrayAsync("/spectator/logo.png");

            Assert.Equal(logo, fallback);
        }

        [Fact]
        public async Task TheFallbackIsCacheableButNeverImmutable()
        {
            // The v2.8.10 field regression: the fallback used to ride the cover jpegs' year-long
            // `immutable` policy, so browsers that cached the pre-gh-#258 fuzzy favicon bytes under
            // a token URL kept rendering them after the server was fixed — safe-loop cards stayed
            // fuzzy while DJ-break cards (the never-cached /spectator/logo.png path) went sharp.
            // The station icon is a mutable asset: one day, no immutable. Story222 still pins the
            // real-cover response as year-long immutable — that contract is unchanged.
            await using var factory = new StationLogoWebFactory();
            var client = factory.CreateClient();

            var response = await client.GetAsync("/spectator/api/artwork/not-a-token");

            var cache = response.Headers.CacheControl;
            Assert.NotNull(cache);
            Assert.True(cache!.Public);
            Assert.Equal(TimeSpan.FromDays(1), cache.MaxAge);
            Assert.DoesNotContain(cache.Extensions, ext => ext.Name == "immutable");
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioLogoIsSurfaceGated
    {
        [Fact]
        public async Task LogoIs404WhenSpectatorModeOff()
        {
            await using var factory = new StationLogoWebFactory(spectatorMode: false);
            var client = factory.CreateClient();

            var response = await client.GetAsync("/spectator/logo.png");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}

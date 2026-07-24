// STORY-222 — Tracks get an artwork door that reveals nothing (SPEC F88.2–F88.3, PLAN T84)
//
// BDD specification — xUnit. Entry-point discipline: every scenario drives
// GET /spectator/api/artwork/{token} through the production pipeline (WebApplicationFactory),
// never an internal extractor call. Only IArtworkTokenStore is scripted; ArtworkService runs the
// real ffmpeg extraction against a small mp3 generated on the fly (ffmpeg is required to run this
// suite, same precondition GenWave.MediaLibrary.Tests' TestMedia already carries).

using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Specs;

/// <summary>Generates small real mp3 fixtures for the artwork extraction facts below — the same
/// ffmpeg-via-lavfi idiom as <c>GenWave.MediaLibrary.Tests.TestMedia</c> (a different assembly,
/// hence its own copy here rather than a cross-project reference for one helper).</summary>
file static class TestArtworkMedia
{
    /// <summary>An mp3 with a solid-color <paramref name="coverSizePx"/>-square PNG embedded as
    /// its attached picture (the same shape TagLib/ffprobe/most players recognise as cover art).</summary>
    public static string CreateWithEmbeddedCover(string dir, string fileName, int coverSizePx = 600) =>
        CreateWithEmbeddedCover(dir, fileName, coverSizePx, coverSizePx);

    /// <summary>Same as the square overload, but with independent width/height — for proving a
    /// non-square cover is downscaled on both axes rather than just its width.</summary>
    public static string CreateWithEmbeddedCover(string dir, string fileName, int coverWidthPx, int coverHeightPx)
    {
        var coverPath = Path.Combine(dir, "cover.png");
        RunFfmpeg(["-y", "-f", "lavfi", "-i", $"color=c=red:s={coverWidthPx}x{coverHeightPx}", "-frames:v", "1", coverPath]);

        var mediaPath = Path.Combine(dir, fileName);
        RunFfmpeg([
            "-nostdin", "-y",
            "-f", "lavfi", "-i", "anullsrc=r=44100:cl=stereo",
            "-i", coverPath,
            "-map", "0:a", "-map", "1:v",
            "-c:a", "libmp3lame", "-c:v", "mjpeg",
            "-id3v2_version", "3", "-disposition:v", "attached_pic",
            "-t", "1", "-shortest",
            mediaPath,
        ]);
        return mediaPath;
    }

    /// <summary>An mp3 with no video stream at all — ffmpeg's extraction map has nothing to find.</summary>
    public static string CreateWithoutCover(string dir, string fileName)
    {
        var mediaPath = Path.Combine(dir, fileName);
        RunFfmpeg([
            "-nostdin", "-y",
            "-f", "lavfi", "-i", "anullsrc=r=44100:cl=stereo",
            "-t", "1", "-c:a", "libmp3lame",
            mediaPath,
        ]);
        return mediaPath;
    }

    public static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gw-artworktest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Reads back an image's pixel dimensions via ffprobe, proving the endpoint's own
    /// ffmpeg downscale actually ran rather than trusting a byte count.</summary>
    public static (int Width, int Height) ProbeDimensions(byte[] imageBytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"gw-artworktest-probe-{Guid.NewGuid():N}.jpg");
        File.WriteAllBytes(path, imageBytes);
        try
        {
            var psi = new ProcessStartInfo("ffprobe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                ArgumentList =
                {
                    "-v", "error", "-select_streams", "v:0",
                    "-show_entries", "stream=width,height", "-of", "csv=p=0:s=x",
                    path,
                },
            };
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("failed to start ffprobe");
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0)
                throw new InvalidOperationException($"ffprobe failed: {stderr}");

            var parts = stdout.Trim().Split('x');
            return (int.Parse(parts[0]), int.Parse(parts[1]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    static void RunFfmpeg(IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("failed to start ffmpeg");
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"ffmpeg failed: {stderr.Result}");
    }
}

/// <summary>Scripts <see cref="IArtworkTokenStore.ResolveAsync"/> only — <see cref="GetOrCreateTokenAsync"/>
/// is never exercised by an artwork GET, so it deliberately has no scriptable behavior.</summary>
file sealed class FakeArtworkTokenStore : IArtworkTokenStore
{
    readonly Dictionary<string, ArtworkTokenResolution> resolutions = [];

    public void Register(string token, long mediaId, string path) =>
        resolutions[token] = new ArtworkTokenResolution(mediaId, path);

    public Task<string> GetOrCreateTokenAsync(long mediaId, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by the artwork endpoint's own specs (STORY-222/T84).");

    public Task<ArtworkTokenResolution?> ResolveAsync(string token, CancellationToken ct) =>
        Task.FromResult(resolutions.TryGetValue(token, out var resolution) ? resolution : null);
}

file sealed class ArtworkWebFactory(string cacheDir, FakeArtworkTokenStore tokenStore) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Station:SpectatorMode", "true");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-x7z");
        builder.UseSetting("Artwork:CacheDir", cacheDir);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
            services.RemoveAll<IArtworkTokenStore>();
            services.AddSingleton<IArtworkTokenStore>(tokenStore);
        });
    }
}

public static class FeatureArtworkEndpoint
{
    // Well-formed (32 lowercase hex) so IArtworkTokenStore.ResolveAsync's malformed-token guard
    // never short-circuits these — only the fake store's own script decides known vs unknown.
    const string KnownToken = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4";
    const string ArtlessToken = "b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5";
    const string UnknownToken = "c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6";

    static async Task<byte[]> StationIconBytesAsync(HttpClient client) =>
        await client.GetByteArrayAsync("/spectator/favicon.ico");

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioArtServed
    {
        [Fact]
        public async Task AKnownTokenReturnsAJpegOfTheEmbeddedCover()
        {
            // Given a track with embedded 600px art and its artwork token
            var mediaDir = TestArtworkMedia.NewTempDir();
            var cacheDir = TestArtworkMedia.NewTempDir();
            try
            {
                var mediaPath = TestArtworkMedia.CreateWithEmbeddedCover(mediaDir, "track.mp3");
                var tokenStore = new FakeArtworkTokenStore();
                tokenStore.Register(KnownToken, mediaId: 1, mediaPath);
                await using var factory = new ArtworkWebFactory(cacheDir, tokenStore);
                var client = factory.CreateClient();

                // When GET /spectator/api/artwork/{token} through the production pipeline
                var response = await client.GetAsync($"/spectator/api/artwork/{KnownToken}");

                // Then the body is a real JPEG derived from that cover, downscaled to ≤500px —
                // never the station-icon fallback.
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
                var body = await response.Content.ReadAsByteArrayAsync();
                var (width, height) = TestArtworkMedia.ProbeDimensions(body);
                Assert.True(width <= 500 && height <= 500, $"expected ≤500px, got {width}x{height}");
                Assert.NotEqual(await StationIconBytesAsync(client), body);
            }
            finally
            {
                Directory.Delete(mediaDir, recursive: true);
                Directory.Delete(cacheDir, recursive: true);
            }
        }

        [Fact]
        public async Task APortraitCoverIsDownscaledOnBothDimensionsPreservingAspect()
        {
            // Given a track with a 300x1000 portrait embedded cover — long side (height) already
            // exceeds 500px while width does not, so a width-only bound would let it straight
            // through untouched (SPEC F88.3 regression: 300x1000 must not survive as 300x1000).
            var mediaDir = TestArtworkMedia.NewTempDir();
            var cacheDir = TestArtworkMedia.NewTempDir();
            try
            {
                var mediaPath = TestArtworkMedia.CreateWithEmbeddedCover(mediaDir, "track.mp3", coverWidthPx: 300, coverHeightPx: 1000);
                var tokenStore = new FakeArtworkTokenStore();
                tokenStore.Register(KnownToken, mediaId: 1, mediaPath);
                await using var factory = new ArtworkWebFactory(cacheDir, tokenStore);
                var client = factory.CreateClient();

                var response = await client.GetAsync($"/spectator/api/artwork/{KnownToken}");

                // Then both dimensions are ≤500px ...
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var body = await response.Content.ReadAsByteArrayAsync();
                var (width, height) = TestArtworkMedia.ProbeDimensions(body);
                Assert.True(width <= 500 && height <= 500, $"expected ≤500px on both axes, got {width}x{height}");

                // ... and the original 300:1000 aspect ratio survives the downscale.
                Assert.Equal(300.0 / 1000.0, (double)width / height, precision: 2);
            }
            finally
            {
                Directory.Delete(mediaDir, recursive: true);
                Directory.Delete(cacheDir, recursive: true);
            }
        }

        [Fact]
        public async Task TheResponseCarriesImmutablePublicCacheHeaders()
        {
            var mediaDir = TestArtworkMedia.NewTempDir();
            var cacheDir = TestArtworkMedia.NewTempDir();
            try
            {
                var mediaPath = TestArtworkMedia.CreateWithEmbeddedCover(mediaDir, "track.mp3");
                var tokenStore = new FakeArtworkTokenStore();
                tokenStore.Register(KnownToken, mediaId: 1, mediaPath);
                await using var factory = new ArtworkWebFactory(cacheDir, tokenStore);
                var client = factory.CreateClient();

                var response = await client.GetAsync($"/spectator/api/artwork/{KnownToken}");

                // Then Cache-Control is public, max-age=31536000, immutable.
                var cache = response.Headers.CacheControl;
                Assert.NotNull(cache);
                Assert.True(cache.Public);
                Assert.Equal(TimeSpan.FromSeconds(31536000), cache.MaxAge);
                Assert.Contains(cache.Extensions, ext => ext.Name == "immutable");
            }
            finally
            {
                Directory.Delete(mediaDir, recursive: true);
                Directory.Delete(cacheDir, recursive: true);
            }
        }

        [Fact]
        public async Task ASecondFetchServesFromTheDiskCache()
        {
            var mediaDir = TestArtworkMedia.NewTempDir();
            var cacheDir = TestArtworkMedia.NewTempDir();
            try
            {
                var mediaPath = TestArtworkMedia.CreateWithEmbeddedCover(mediaDir, "track.mp3");
                var tokenStore = new FakeArtworkTokenStore();
                tokenStore.Register(KnownToken, mediaId: 1, mediaPath);
                await using var factory = new ArtworkWebFactory(cacheDir, tokenStore);
                var client = factory.CreateClient();

                var first = await client.GetAsync($"/spectator/api/artwork/{KnownToken}");
                var firstBody = await first.Content.ReadAsByteArrayAsync();
                Assert.Equal("image/jpeg", first.Content.Headers.ContentType?.MediaType);

                // Remove the source file: a second, un-cached extraction is now impossible — if
                // the endpoint still returns the same jpeg, it can only have come from disk cache.
                File.Delete(mediaPath);

                var second = await client.GetAsync($"/spectator/api/artwork/{KnownToken}");
                var secondBody = await second.Content.ReadAsByteArrayAsync();

                // Then the extraction ran once — the second request never re-invokes ffmpeg.
                Assert.Equal("image/jpeg", second.Content.Headers.ContentType?.MediaType);
                Assert.Equal(firstBody, secondBody);
            }
            finally
            {
                Directory.Delete(mediaDir, recursive: true);
                Directory.Delete(cacheDir, recursive: true);
            }
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    public sealed class SadPathNoOracle
    {
        [Fact]
        public async Task AnUnknownTokenServesTheStationIconWith200()
        {
            // Given no track ever resolves UnknownToken
            var cacheDir = TestArtworkMedia.NewTempDir();
            try
            {
                var tokenStore = new FakeArtworkTokenStore();
                await using var factory = new ArtworkWebFactory(cacheDir, tokenStore);
                var client = factory.CreateClient();

                // When GET /spectator/api/artwork/{token} for a token nobody minted
                var response = await client.GetAsync($"/spectator/api/artwork/{UnknownToken}");

                // Then it serves the station icon with 200 — token guessing learns nothing (F88.3).
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var body = await response.Content.ReadAsByteArrayAsync();
                Assert.Equal(await StationIconBytesAsync(client), body);
            }
            finally
            {
                Directory.Delete(cacheDir, recursive: true);
            }
        }

        [Fact]
        public async Task AnArtlessTrackTokenServesTheStationIconWith200()
        {
            var mediaDir = TestArtworkMedia.NewTempDir();
            var cacheDir = TestArtworkMedia.NewTempDir();
            try
            {
                var mediaPath = TestArtworkMedia.CreateWithoutCover(mediaDir, "no-cover.mp3");
                var tokenStore = new FakeArtworkTokenStore();
                tokenStore.Register(ArtlessToken, mediaId: 2, mediaPath);
                await using var factory = new ArtworkWebFactory(cacheDir, tokenStore);
                var client = factory.CreateClient();

                var response = await client.GetAsync($"/spectator/api/artwork/{ArtlessToken}");

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var body = await response.Content.ReadAsByteArrayAsync();
                Assert.Equal(await StationIconBytesAsync(client), body);
            }
            finally
            {
                Directory.Delete(mediaDir, recursive: true);
                Directory.Delete(cacheDir, recursive: true);
            }
        }

        [Fact]
        public async Task NoSpectatorPayloadOrUrlEverCarriesANumericMediaId()
        {
            // Given a distinctive numeric media id behind a resolvable-but-artless token, and an
            // unresolvable token — the two reasons F88.3 says must be indistinguishable.
            const long distinctiveMediaId = 918_273_645;
            var mediaDir = TestArtworkMedia.NewTempDir();
            var cacheDir = TestArtworkMedia.NewTempDir();
            try
            {
                var mediaPath = TestArtworkMedia.CreateWithoutCover(mediaDir, "no-cover.mp3");
                var tokenStore = new FakeArtworkTokenStore();
                tokenStore.Register(ArtlessToken, distinctiveMediaId, mediaPath);
                await using var factory = new ArtworkWebFactory(cacheDir, tokenStore);
                var client = factory.CreateClient();

                var unknown = await client.GetAsync($"/spectator/api/artwork/{UnknownToken}");
                var artless = await client.GetAsync($"/spectator/api/artwork/{ArtlessToken}");

                // Then both responses are byte-for-byte identical (F62.9 stays intact with
                // artwork live) ...
                var unknownBody = await unknown.Content.ReadAsByteArrayAsync();
                var artlessBody = await artless.Content.ReadAsByteArrayAsync();
                Assert.Equal(unknownBody, artlessBody);

                // ... and neither response's headers ever carry the numeric media id behind the
                // resolvable token.
                var idText = distinctiveMediaId.ToString();
                Assert.DoesNotContain(idText, unknown.ToString(), StringComparison.Ordinal);
                Assert.DoesNotContain(idText, artless.ToString(), StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(mediaDir, recursive: true);
                Directory.Delete(cacheDir, recursive: true);
            }
        }
    }
}

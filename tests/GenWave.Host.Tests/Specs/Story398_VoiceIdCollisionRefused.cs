// STORY-398 — Two voice packs with the same voice id can't coexist (SPEC F166.4 · PLAN T413)
//
// BDD specification — xUnit. Drives the real production route through WebApplicationFactory<Program>
// against a fake catalog origin (mirrors Story282_FontPackInstall.cs's own idiom) and a
// FakeVoicePackStore. Every install here — even one destined to fail on an ALREADY-INSTALLED-pack
// collision — still calls Kokoro's own GET /v1/audio/voices (VoicePackController's collision check
// awaits both the store lookup and the stock lister unconditionally, SPEC F166.4), so this file's
// fake Kokoro-voices route is DYNAMIC: it lists whatever .pt files the test's own temp voices root
// actually holds at request time, unioned with a fixed stock roster — the same "kokoro rescans, no
// restart" contract the real appliance relies on (SPEC F166.3), which is exactly what AC4's own fact
// below (a rename installs, then GET /api/voices proves BOTH ids live) is here to prove end to end.

using System.Net;
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
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureVoicePacksWithTheSameVoiceIdCannotCoexist
{
    // ---------------------------------------------------------------------
    // SAD PATH — the whole feature is refusal
    // ---------------------------------------------------------------------

    public sealed class ScenarioInstalledPackCollisionRefuses
    {
        [Fact]
        public async Task ASecondPackWithAnAlreadyInstalledVoiceIdReturns409()
        {
            // Given an incumbent pack already installed (voice id "af_shared"),
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackCollisionWebFactory(store);
            var client = await VoicePackCollisionWebFactory.LoggedInClientAsync(factory);
            var first = await client.PostAsync("/api/voice-packs/incumbent-pack/install", null);
            Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());

            // When a second pack declaring the SAME voice id is installed,
            var second = await client.PostAsync("/api/voice-packs/colliding-pack/install", null);

            // Then it is refused 409 (AC1).
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        }

        [Fact]
        public async Task The409ProblemDetailsListsTheCollidedIds()
        {
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackCollisionWebFactory(store);
            var client = await VoicePackCollisionWebFactory.LoggedInClientAsync(factory);
            await client.PostAsync("/api/voice-packs/incumbent-pack/install", null);

            var second = await client.PostAsync("/api/voice-packs/colliding-pack/install", null);
            var body = await second.Content.ReadAsStringAsync();

            Assert.Contains("af_shared", body, StringComparison.Ordinal);
        }

        [Fact]
        public async Task The409ProblemDetailsNamesTheIncumbentPackSlug()
        {
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackCollisionWebFactory(store);
            var client = await VoicePackCollisionWebFactory.LoggedInClientAsync(factory);
            await client.PostAsync("/api/voice-packs/incumbent-pack/install", null);

            var second = await client.PostAsync("/api/voice-packs/colliding-pack/install", null);
            var body = await second.Content.ReadAsStringAsync();

            Assert.Contains("incumbent-pack", body, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioStockCollisionRefuses
    {
        [Fact]
        public async Task APackReUsingAStockVoiceIdReturns409()
        {
            // Given a pack declaring a voice id kokoro already ships as stock ("af_bella"),
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackCollisionWebFactory(store);
            var client = await VoicePackCollisionWebFactory.LoggedInClientAsync(factory);

            // When it is installed,
            var response = await client.PostAsync("/api/voice-packs/stock-colliding-pack/install", null);

            // Then it is refused 409 (AC2) — no pack has to be installed first for a stock collision.
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task The409ProblemDetailsNamesStockAsIncumbent()
        {
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackCollisionWebFactory(store);
            var client = await VoicePackCollisionWebFactory.LoggedInClientAsync(factory);

            var response = await client.PostAsync("/api/voice-packs/stock-colliding-pack/install", null);
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("stock", body, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioNoBytesOnCollision
    {
        [Fact]
        public async Task NoBytesAreWrittenForTheSecondPackOnCollision()
        {
            // Given the incumbent's own file already landed under the shared voices root,
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackCollisionWebFactory(store);
            var client = await VoicePackCollisionWebFactory.LoggedInClientAsync(factory);
            await client.PostAsync("/api/voice-packs/incumbent-pack/install", null);

            // When a second pack, declaring the SAME voice id (so the same target file name), is
            // refused,
            await client.PostAsync("/api/voice-packs/colliding-pack/install", null);

            // Then the file on disk still holds the INCUMBENT's own original bytes — the colliding
            // pack's own (different) bytes never landed, proving the collision check runs before any
            // asset is fetched, not merely before the final upsert.
            var writtenBytes = await File.ReadAllBytesAsync(Path.Combine(factory.VoicesRoot, "af_shared.pt"));
            Assert.Equal(VoicePackCollisionFixtures.IncumbentPtBytes, writtenBytes);
        }

        [Fact]
        public async Task StationVoicePackVoiceIsUnchangedOnCollision()
        {
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackCollisionWebFactory(store);
            var client = await VoicePackCollisionWebFactory.LoggedInClientAsync(factory);
            await client.PostAsync("/api/voice-packs/incumbent-pack/install", null);

            await client.PostAsync("/api/voice-packs/colliding-pack/install", null);

            // Then no second row was ever upserted — the store still holds only the incumbent.
            Assert.Equal(1, store.PackCount);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — a rename installs
    // ---------------------------------------------------------------------

    public sealed class ScenarioARenameInstallsCleanly
    {
        [Fact]
        public async Task BothVoiceIdsAppearInGetApiVoicesAfterASuccessfulRename()
        {
            // Given an incumbent pack already installed (voice id "af_shared"),
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackCollisionWebFactory(store);
            var client = await VoicePackCollisionWebFactory.LoggedInClientAsync(factory);
            var first = await client.PostAsync("/api/voice-packs/incumbent-pack/install", null);
            Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());

            // When a differently-named pack (voice id "af_second" — no collision) installs,
            var renamed = await client.PostAsync("/api/voice-packs/renamed-pack/install", null);
            Assert.True(renamed.IsSuccessStatusCode, await renamed.Content.ReadAsStringAsync());

            // Then it succeeds, and the live voice listing reflects BOTH ids without a restart (AC4) —
            // kokoro rescans its (fake, but directory-backed) voices directory per request.
            var response = await client.GetAsync("/api/voices");
            var voices = await response.Content.ReadFromJsonAsync<string[]>() ?? [];

            Assert.Contains("af_shared", voices);
            Assert.Contains("af_second", voices);
        }
    }
}

/// <summary>
/// A four-entry fake catalog origin (mirrors <c>FontPackInstallWebFactory</c>'s own shape) serving:
/// an incumbent pack, a pack that collides with it, a pack that collides with a STOCK id, and a
/// non-colliding "renamed" pack — plus a fresh, per-instance temp <c>Packs:VoicesRoot</c>, and a
/// Kokoro <c>GET /v1/audio/voices</c> route that dynamically lists whatever <c>.pt</c> files that
/// root actually holds (see <see cref="FeatureVoicePacksWithTheSameVoiceIdCannotCoexist"/>'s own
/// remarks on why this must be dynamic, not a fixed canned list).
/// </summary>
file sealed class VoicePackCollisionWebFactory : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story398-voicepack-collision";

    readonly FakeVoicePackStore store;
    readonly FakeHttpMessageHandler handler;
    readonly TempDir voicesRootDir = new();

    public string VoicesRoot => voicesRootDir.Path;

    public VoicePackCollisionWebFactory(FakeVoicePackStore store)
    {
        this.store = store;
        handler = VoicePackCollisionFixtures.BuildRoutedHandler(VoicesRoot);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Community:CatalogIndexUrl", VoicePackCollisionFixtures.IndexUrl);
        builder.UseSetting("Packs:VoicesRoot", VoicesRoot);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(handler));

            services.RemoveAll<IVoicePackStore>();
            services.AddSingleton<IVoicePackStore>(store);
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            voicesRootDir.Dispose();
        }

        base.Dispose(disposing);
    }

    public static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }
}

file static class VoicePackCollisionFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/voice-collision-index.json";
    const string Origin = "https://catalog.test/repo/";
    // Tts:Endpoint under the "Development" ASP.NET environment this factory uses (see
    // appsettings.Development.json) is "http://localhost:8880", not the production default.
    const string KokoroVoicesUrl = "http://localhost:8880/v1/audio/voices";

    /// <summary>Kokoro's own fixed stock roster for this file's facts (SPEC F166.2 — this repo does
    /// not pin kokoro's real stock count; these are just two plausible stock ids).</summary>
    static readonly string[] StockVoiceIds = ["af_bella", "af_heart"];

    public static readonly byte[] IncumbentPtBytes = Encoding.UTF8.GetBytes("incumbent-original-bytes");

    static readonly (string Slug, string VoiceId, byte[] PtBytes)[] Packs =
    [
        ("incumbent-pack", "af_shared", IncumbentPtBytes),
        ("colliding-pack", "af_shared", Encoding.UTF8.GetBytes("collider-bytes-should-never-land")),
        ("stock-colliding-pack", "af_bella", Encoding.UTF8.GetBytes("stock-collision-bytes-should-never-land")),
        ("renamed-pack", "af_second", Encoding.UTF8.GetBytes("renamed-pack-bytes")),
    ];

    static readonly byte[] PreviewBytes = Encoding.UTF8.GetBytes("fake-preview-bytes-for-story398");

    static byte[] PtBytesFor(string slug) => Packs.First(p => string.Equals(p.Slug, slug, StringComparison.Ordinal)).PtBytes;

    static string ManifestJson(string slug, string voiceId) => $$"""
        { "packName": "Test Pack", "engine": "kokoro", "synthetic": true, "sourceRef": null,
          "preview": "{{slug}}.preview.mp3",
          "voices": [ { "voiceId": "{{voiceId}}" } ] }
        """;

    const string MetaJson = """
        {"author":"Test Fixture","description":"A voice pack for the collision specs.","audience":"everyone"}
        """;

    static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    static string EntryJson(string slug, string voiceId) => $$"""
        { "slug": "{{slug}}", "kind": "voice-pack", "audience": "everyone",
          "manifest": { "path": "entries/{{slug}}/{{slug}}.voice-pack.json", "sha256": "{{Sha256Hex(ManifestJson(slug, voiceId))}}" },
          "meta": { "path": "entries/{{slug}}/{{slug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" },
          "assets": [
            { "path": "entries/{{slug}}/{{voiceId}}.pt", "sha256": "{{Sha256Hex(PtBytesFor(slug))}}", "bytes": {{PtBytesFor(slug).Length}} },
            { "path": "entries/{{slug}}/{{slug}}.preview.mp3", "sha256": "{{Sha256Hex(PreviewBytes)}}", "bytes": {{PreviewBytes.Length}} }
          ] }
        """;

    static string IndexJson() => $$"""
        { "generatedAt": "2026-09-06", "entries": [ {{string.Join(", ", Packs.Select(p => EntryJson(p.Slug, p.VoiceId)))}} ] }
        """;

    /// <summary>
    /// Serves every pack entry's own documents/assets, plus a Kokoro <c>GET /v1/audio/voices</c> that
    /// unions the fixed stock roster with whatever <c>.pt</c> files <paramref name="voicesRoot"/>
    /// actually holds at request time — see this file's own header remarks for why that must be
    /// dynamic rather than a fixed canned list.
    /// </summary>
    public static FakeHttpMessageHandler BuildRoutedHandler(string voicesRoot)
    {
        var routes = new Dictionary<string, string>(StringComparer.Ordinal) { [IndexUrl] = IndexJson() };
        var assetBytesByUrl = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var pack in Packs)
        {
            routes[Origin + $"entries/{pack.Slug}/{pack.Slug}.voice-pack.json"] = ManifestJson(pack.Slug, pack.VoiceId);
            routes[Origin + $"entries/{pack.Slug}/{pack.Slug}.meta.json"] = MetaJson;
            assetBytesByUrl[Origin + $"entries/{pack.Slug}/{pack.VoiceId}.pt"] = pack.PtBytes;
            assetBytesByUrl[Origin + $"entries/{pack.Slug}/{pack.Slug}.preview.mp3"] = PreviewBytes;
        }

        return new((request, _) =>
        {
            var absoluteUri = request.RequestUri!.AbsoluteUri;

            if (string.Equals(absoluteUri, KokoroVoicesUrl, StringComparison.Ordinal))
            {
                var installedVoiceIds = System.IO.Directory.Exists(voicesRoot)
                    ? System.IO.Directory.EnumerateFiles(voicesRoot, "*.pt").Select(f => Path.GetFileNameWithoutExtension(f))
                    : [];
                var voices = StockVoiceIds.Concat(installedVoiceIds).ToArray();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { voices }),
                });
            }

            if (assetBytesByUrl.TryGetValue(absoluteUri, out var assetBytes))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(assetBytes) });

            return Task.FromResult(
                routes.TryGetValue(absoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
    }
}

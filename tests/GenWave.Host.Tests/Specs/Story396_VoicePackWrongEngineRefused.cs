// STORY-396 — A voice pack for the wrong engine is refused before bytes hit disk (SPEC F164.2 · PLAN T413)
//
// BDD specification — xUnit. Drives the real production route (POST /api/voice-packs/{slug}/install)
// through WebApplicationFactory<Program> against a fake catalog origin (mirrors
// Story282_FontPackInstall.cs's own FontPackInstallWebFactory idiom) and a FakeVoicePackStore — this
// file has no Postgres fixture of its own; the real cross-table DELETE guard SQL is proven against real
// Postgres in Story401_PackUninstallGuards.cs instead. The HAPPY PATH (a kokoro pack on a kokoro-primary
// station) still reaches the voice-id collision check (SPEC F166.4, runs AFTER the engine check but
// before any asset fetch) — so the shared fake HTTP handler also answers Kokoro's real
// GET /v1/audio/voices route (ITtsVoiceLister is always Kokoro-HTTP-backed regardless of
// PrimaryVoiceEngine) with an empty stock roster, so the happy path never collides. The SAD PATH (a
// piper-only station, via Tts:PiperPrimaryEndpoint) never reaches that check at all — the engine
// mismatch is the very first gate past manifest parsing, strictly before any byte is fetched.

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
using GenWave.Host.Api;
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

public static class FeatureVoicePackForWrongEngineIsRefusedBeforeBytesHitDisk
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioMatchingEngineInstalls
    {
        [Fact]
        public async Task KokoroStationAcceptsAKokoroPack()
        {
            // Given a kokoro-primary station (the default PrimaryVoiceEngine — no
            // Tts:PiperPrimaryEndpoint) and a pack declaring engine:"kokoro",
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackEngineWebFactory(store);
            var client = await VoicePackEngineWebFactory.LoggedInClientAsync(factory);

            // When install is attempted (the real production route),
            var response = await client.PostAsync($"/api/voice-packs/{VoicePackEngineFixtures.Slug}/install", null);

            // Then it succeeds (AC1) — the engine matches, so the install runs to completion.
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task TheVoiceFilesLandOnTheVolumeAsUsual()
        {
            // Given the same kokoro-station install,
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackEngineWebFactory(store);
            var client = await VoicePackEngineWebFactory.LoggedInClientAsync(factory);

            // When install completes,
            var response = await client.PostAsync($"/api/voice-packs/{VoicePackEngineFixtures.Slug}/install", null);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

            // Then its voice file lands under the voices root exactly like any other install (AC1) —
            // the engine check changes nothing about the write path once it passes.
            Assert.True(File.Exists(Path.Combine(factory.VoicesRoot, $"{VoicePackEngineFixtures.VoiceId}.pt")));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioMismatchedEngineRefuses
    {
        [Fact]
        public async Task ThePiperOnlyStationRefusesAKokoroPackWith400()
        {
            // Given a piper-only station (Tts:PiperPrimaryEndpoint set) and the SAME kokoro pack,
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackEngineWebFactory(store, piperPrimaryEndpoint: "http://piper:5001");
            var client = await VoicePackEngineWebFactory.LoggedInClientAsync(factory);

            // When install is attempted,
            var response = await client.PostAsync($"/api/voice-packs/{VoicePackEngineFixtures.Slug}/install", null);

            // Then it is refused 400 (AC2) — before any asset is fetched.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task TheProblemDetailsTypeNamesTheEngineMismatch()
        {
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackEngineWebFactory(store, piperPrimaryEndpoint: "http://piper:5001");
            var client = await VoicePackEngineWebFactory.LoggedInClientAsync(factory);

            var response = await client.PostAsync($"/api/voice-packs/{VoicePackEngineFixtures.Slug}/install", null);
            var body = await response.Content.ReadAsStringAsync();

            // Then the ProblemDetails carries the one machine-readable discriminator this controller
            // sets (SPEC F164.2, VoicePackController's NotSupportedEngineProblem).
            Assert.Contains("\"not_supported_engine\"", body, StringComparison.Ordinal);
        }

        [Fact]
        public async Task TheProblemDetailsBodyNamesBothStationAndPackEngines()
        {
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackEngineWebFactory(store, piperPrimaryEndpoint: "http://piper:5001");
            var client = await VoicePackEngineWebFactory.LoggedInClientAsync(factory);

            var response = await client.PostAsync($"/api/voice-packs/{VoicePackEngineFixtures.Slug}/install", null);
            var body = await response.Content.ReadAsStringAsync();

            // Then the body names both the pack's own declared engine and the station's actual one.
            Assert.Contains("kokoro", body, StringComparison.Ordinal);
            Assert.Contains("piper", body, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioNoBytesOnRefusal
    {
        [Fact]
        public async Task NoPtFileAppearsUnderTheVoicesRootOnRefusal()
        {
            // Given the same piper-only refusal,
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackEngineWebFactory(store, piperPrimaryEndpoint: "http://piper:5001");
            var client = await VoicePackEngineWebFactory.LoggedInClientAsync(factory);

            // When install is refused,
            await client.PostAsync($"/api/voice-packs/{VoicePackEngineFixtures.Slug}/install", null);

            // Then nothing was ever written under the voices root (AC3) — the engine check runs
            // strictly before any asset is fetched.
            Assert.Empty(Directory.EnumerateFiles(factory.VoicesRoot));
        }

        [Fact]
        public async Task StationVoicePackHasNoRowForTheSlugOnRefusal()
        {
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackEngineWebFactory(store, piperPrimaryEndpoint: "http://piper:5001");
            var client = await VoicePackEngineWebFactory.LoggedInClientAsync(factory);

            await client.PostAsync($"/api/voice-packs/{VoicePackEngineFixtures.Slug}/install", null);

            // Then no row for the slug was ever upserted (AC3) — all-or-nothing, the refusal never
            // reaches the store at all.
            Assert.Equal(0, store.PackCount);
        }
    }

    /// <summary>
    /// STORY-396 AC4 — "the shelf entry is unchanged": <c>Given the refused install, When the shelf
    /// refreshes, Then the pack still appears with the "install" button (no half-state)</c>. The
    /// shelf (admin-ui's <c>PersonaCatalogClient</c>, PLAN T418) decides Install-vs-Installed off
    /// <c>GET /api/voice-packs</c>'s own <c>installedVoicePackSlugSet</c> — this controller's own
    /// <see cref="VoicePackController.List"/>. The fact below proves the SERVER half of that
    /// contract (SPEC F164.2): a refusal leaves that listing exactly as it found it. The UI half —
    /// that a refusal never even asks the shelf to re-read it, so it keeps rendering "Install" — is
    /// the jest spec's own claim (<c>voice-jingle-pack-shelf-install.spec.tsx</c>).
    /// </summary>
    public sealed class ScenarioShelfEntryUnchanged
    {
        [Fact]
        public async Task TheShelfStillOffersTheInstallButtonAfterRefusal()
        {
            // Given ONE unrelated pack already installed — seeded straight through the store,
            // Story395's own ScenarioTheInstalledPacksListing idiom — so the before/after comparison
            // below is a real equality over a non-empty listing, not two empty arrays matching by
            // coincidence, plus the shelf's own pre-attempt read of that listing (GET
            // /api/voice-packs, the ONE input installedVoicePackSlugSet.has(slug) depends on),
            const string otherSlug = "other-already-installed-pack";
            var store = new FakeVoicePackStore();
            await store.UpsertAsync(otherSlug, "kokoro", "{}", otherSlug, [], CancellationToken.None);
            await using var factory = new VoicePackEngineWebFactory(store, piperPrimaryEndpoint: "http://piper:5001");
            var client = await VoicePackEngineWebFactory.LoggedInClientAsync(factory);
            var before = await client.GetAsync("/api/voice-packs");
            var beforeSummaries = await before.Content.ReadFromJsonAsync<InstalledPackSummaryDto[]>() ?? [];
            Assert.Single(beforeSummaries, summary => summary.Slug == otherSlug);

            // When install is refused (400 not_supported_engine, the piper-only station against the
            // same kokoro pack ScenarioMismatchedEngineRefuses already proves 400s),
            var install = await client.PostAsync($"/api/voice-packs/{VoicePackEngineFixtures.Slug}/install", null);
            Assert.Equal(HttpStatusCode.BadRequest, install.StatusCode);

            var after = await client.GetAsync("/api/voice-packs");
            var afterSummaries = await after.Content.ReadFromJsonAsync<InstalledPackSummaryDto[]>() ?? [];

            // Then the listing this button reads is unchanged — the unrelated pack is still the ONLY
            // row, the refused slug never having joined it — so installedVoicePackSlugSet.has(slug)
            // is false both times and the card keeps rendering "Install", never a half-installed
            // "Installed"/"Uninstall" state.
            Assert.Equal(HttpStatusCode.OK, after.StatusCode);
            Assert.Equal(beforeSummaries, afterSummaries);
            Assert.DoesNotContain(afterSummaries, summary => summary.Slug == VoicePackEngineFixtures.Slug);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — engine outside the closed set entirely (T413 review round 2 finding B2)
    // ---------------------------------------------------------------------

    /// <summary>
    /// The DEFAULT kokoro-primary station (no <c>Tts:PiperPrimaryEndpoint</c>) against a pack
    /// declaring <c>"engine": "piper"</c> — distinct from <see cref="ScenarioMismatchedEngineRefuses"/>,
    /// which flips the STATION to piper against a kokoro PACK. This is the other half of
    /// <c>VoicePackController.SupportedEngines</c>'s two-reason refusal (T413 review round 2 finding
    /// B2): the closed-set membership check (<see cref="CatalogVoicePackManifestSerializer"/> no
    /// longer narrows "piper" to a parse-time <see langword="null"/> — see that type's own remarks),
    /// not the primary-engine-mismatch check <see cref="ScenarioMismatchedEngineRefuses"/> already
    /// covers.
    /// </summary>
    public sealed class ScenarioEngineOutsideTheClosedSetRefuses
    {
        [Fact]
        public async Task AKokoroStationRefusesAPiperDeclaredPackWith400()
        {
            // Given the DEFAULT kokoro-primary station and a pack declaring engine:"piper" — an
            // engine this database's own CHECK constraint (db/45) will never even hold,
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackEngineWebFactory(store);
            var client = await VoicePackEngineWebFactory.LoggedInClientAsync(factory);

            // When install is attempted,
            var response = await client.PostAsync($"/api/voice-packs/{VoicePackEngineFixtures.PiperSlug}/install", null);

            // Then it is refused 400 — the manifest itself parsed cleanly (B2 reverses F2's own
            // parse-time narrowing), so this refusal can only come from the controller's own
            // closed-set gate.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task TheProblemDetailsTypeNamesTheUnsupportedEngine()
        {
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackEngineWebFactory(store);
            var client = await VoicePackEngineWebFactory.LoggedInClientAsync(factory);

            var response = await client.PostAsync($"/api/voice-packs/{VoicePackEngineFixtures.PiperSlug}/install", null);
            var body = await response.Content.ReadAsStringAsync();

            // Then it carries the SAME machine-readable discriminator a primary-engine mismatch
            // gets (SPEC F164.2) — "this database can't hold it" and "this station doesn't run it"
            // are both instances of "this station cannot install this pack".
            Assert.Contains("\"not_supported_engine\"", body, StringComparison.Ordinal);
        }

        [Fact]
        public async Task TheProblemDetailsBodyNamesBothTheDeclaredAndTheStationEngine()
        {
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackEngineWebFactory(store);
            var client = await VoicePackEngineWebFactory.LoggedInClientAsync(factory);

            var response = await client.PostAsync($"/api/voice-packs/{VoicePackEngineFixtures.PiperSlug}/install", null);
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("piper", body, StringComparison.Ordinal);
            Assert.Contains("kokoro", body, StringComparison.Ordinal);
        }

        [Fact]
        public async Task NoBytesAreFetchedOrWrittenForAnUnsupportedEngine()
        {
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackEngineWebFactory(store);
            var client = await VoicePackEngineWebFactory.LoggedInClientAsync(factory);

            await client.PostAsync($"/api/voice-packs/{VoicePackEngineFixtures.PiperSlug}/install", null);

            // Then nothing was ever written under the voices root, and no row was upserted — the
            // closed-set gate runs before any asset is fetched, exactly like the primary-engine
            // mismatch gate beside it.
            Assert.Empty(Directory.EnumerateFiles(factory.VoicesRoot));
            Assert.Equal(0, store.PackCount);
        }

        [Fact]
        public async Task APiperPrimaryStationStillRefusesAPiperDeclaredPack()
        {
            // T413 review round 3 finding F2 — the engine check has TWO independent arms: closed-set
            // membership (this database's own db/45 CHECK never holds anything but "kokoro") and
            // primary-engine equality (the pack's declared engine must match THIS station's). This
            // fact IS the closed-set arm's own coverage, so it is the third edit gh-#614's future
            // engine widening must touch, beside VoicePackController.SupportedEngines and db/45's own
            // CHECK constraint (T413 review round 4 finding gh-#614 naming). Every
            // other fact in this file pairs a pack/station engine pair that DIFFER, so the equality
            // arm alone refuses them — removing the closed-set arm entirely left the suite green. Here
            // the station's own primary engine is flipped to "piper" (Tts:PiperPrimaryEndpoint) AND the
            // pack ALSO declares "piper" — the equality arm now trivially PASSES (packEngine ==
            // stationEngine), so only the closed-set arm can still refuse this install.
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackEngineWebFactory(store, piperPrimaryEndpoint: "http://piper:5001");
            var client = await VoicePackEngineWebFactory.LoggedInClientAsync(factory);

            var response = await client.PostAsync($"/api/voice-packs/{VoicePackEngineFixtures.PiperSlug}/install", null);
            var body = await response.Content.ReadAsStringAsync();

            // Then it is still refused 400 with the same discriminator, and the body names the closed
            // set itself ("kokoro") — proof this came from the closed-set arm, not the (here-passing)
            // equality arm.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("\"not_supported_engine\"", body, StringComparison.Ordinal);
            Assert.Contains("kokoro", body, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFiles(factory.VoicesRoot));
            Assert.Equal(0, store.PackCount);
        }
    }
}

/// <summary>
/// A single-entry fake catalog origin serving one kokoro voice pack (mirrors
/// <c>FontPackInstallWebFactory</c>'s own shape) plus a fresh, per-instance temp <c>Packs:VoicesRoot</c>
/// — cleaned up on dispose. Optionally flips the station to a piper-only primary engine
/// (<c>Tts:PiperPrimaryEndpoint</c>) to drive the mismatched-engine sad path.
/// </summary>
file sealed class VoicePackEngineWebFactory : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story396-voicepack-engine";

    readonly FakeVoicePackStore store;
    readonly FakeHttpMessageHandler handler;
    readonly string? piperPrimaryEndpoint;

    public string VoicesRoot { get; } = Directory.CreateTempSubdirectory("t413-story396-voices-").FullName;

    public VoicePackEngineWebFactory(FakeVoicePackStore store, string? piperPrimaryEndpoint = null)
    {
        this.store = store;
        this.piperPrimaryEndpoint = piperPrimaryEndpoint;
        handler = VoicePackEngineFixtures.BuildRoutedHandler();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Community:CatalogIndexUrl", VoicePackEngineFixtures.IndexUrl);
        builder.UseSetting("Packs:VoicesRoot", VoicesRoot);
        if (!string.IsNullOrEmpty(piperPrimaryEndpoint))
            builder.UseSetting("Tts:PiperPrimaryEndpoint", piperPrimaryEndpoint);

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
            try { Directory.Delete(VoicesRoot, recursive: true); }
            catch (IOException) { /* best-effort cleanup */ }
            catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
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

file static class VoicePackEngineFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/voice-engine-index.json";
    const string Origin = "https://catalog.test/repo/";
    // Tts:Endpoint under the "Development" ASP.NET environment this factory uses (see
    // appsettings.Development.json) is "http://localhost:8880", not the production default.
    const string KokoroVoicesUrl = "http://localhost:8880/v1/audio/voices";

    public const string Slug = "engine-test-pack";
    public const string VoiceId = "af_enginetest";

    /// <summary>T413 review round 2 finding B2 — a SECOND catalog entry declaring an engine outside
    /// the closed set (<c>VoicePackController.SupportedEngines</c>) entirely, distinct from
    /// <see cref="Slug"/>'s own kokoro pack: drives <see cref="ScenarioEngineOutsideTheClosedSetRefuses"/>
    /// against the DEFAULT kokoro-primary station, never the piper-flipped station
    /// <see cref="ScenarioMismatchedEngineRefuses"/> uses.</summary>
    public const string PiperSlug = "piper-declared-pack";
    public const string PiperVoiceId = "af_piperdeclared";

    static readonly byte[] PtBytes = Encoding.UTF8.GetBytes("fake-pt-bytes-for-story396");
    static readonly byte[] PreviewBytes = Encoding.UTF8.GetBytes("fake-preview-bytes-for-story396");
    static readonly byte[] PiperPtBytes = Encoding.UTF8.GetBytes("fake-piper-pt-bytes-for-story396");
    static readonly byte[] PiperPreviewBytes = Encoding.UTF8.GetBytes("fake-piper-preview-bytes-for-story396");

    static string ManifestJson => $$"""
        { "packName": "Engine Test Pack", "engine": "kokoro", "synthetic": true, "sourceRef": null,
          "preview": "{{Slug}}.preview.mp3",
          "voices": [ { "voiceId": "{{VoiceId}}" } ] }
        """;

    static string PiperManifestJson => $$"""
        { "packName": "Piper Declared Pack", "engine": "piper", "synthetic": true, "sourceRef": null,
          "preview": "{{PiperSlug}}.preview.mp3",
          "voices": [ { "voiceId": "{{PiperVoiceId}}" } ] }
        """;

    const string MetaJson = """
        {"author":"Test Fixture","description":"A voice pack for the engine-check specs.","audience":"everyone"}
        """;

    static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    static string IndexJson() => $$"""
        { "generatedAt": "2026-09-06", "entries": [
          { "slug": "{{Slug}}", "kind": "voice-pack", "audience": "everyone",
            "manifest": { "path": "entries/{{Slug}}/{{Slug}}.voice-pack.json", "sha256": "{{Sha256Hex(ManifestJson)}}" },
            "meta": { "path": "entries/{{Slug}}/{{Slug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" },
            "assets": [
              { "path": "entries/{{Slug}}/{{VoiceId}}.pt", "sha256": "{{Sha256Hex(PtBytes)}}", "bytes": {{PtBytes.Length}} },
              { "path": "entries/{{Slug}}/{{Slug}}.preview.mp3", "sha256": "{{Sha256Hex(PreviewBytes)}}", "bytes": {{PreviewBytes.Length}} }
            ] },
          { "slug": "{{PiperSlug}}", "kind": "voice-pack", "audience": "everyone",
            "manifest": { "path": "entries/{{PiperSlug}}/{{PiperSlug}}.voice-pack.json", "sha256": "{{Sha256Hex(PiperManifestJson)}}" },
            "meta": { "path": "entries/{{PiperSlug}}/{{PiperSlug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" },
            "assets": [
              { "path": "entries/{{PiperSlug}}/{{PiperVoiceId}}.pt", "sha256": "{{Sha256Hex(PiperPtBytes)}}", "bytes": {{PiperPtBytes.Length}} },
              { "path": "entries/{{PiperSlug}}/{{PiperSlug}}.preview.mp3", "sha256": "{{Sha256Hex(PiperPreviewBytes)}}", "bytes": {{PiperPreviewBytes.Length}} }
            ] } ] }
        """;

    /// <summary>
    /// Serves this fixture's own catalog documents/assets, plus a fake Kokoro
    /// <c>GET /v1/audio/voices</c> (SPEC F166.4's own collision-check dependency, <c>ITtsVoiceLister</c>
    /// — reached only on the HAPPY PATH facts, since a piper-only station's install never gets past the
    /// engine check to call it, and neither does the closed-set refusal against <see cref="PiperSlug"/>)
    /// returning an empty stock roster, so the happy path never collides.
    /// </summary>
    public static FakeHttpMessageHandler BuildRoutedHandler()
    {
        var routes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [IndexUrl] = IndexJson(),
            [Origin + $"entries/{Slug}/{Slug}.voice-pack.json"] = ManifestJson,
            [Origin + $"entries/{Slug}/{Slug}.meta.json"] = MetaJson,
            [Origin + $"entries/{PiperSlug}/{PiperSlug}.voice-pack.json"] = PiperManifestJson,
            [Origin + $"entries/{PiperSlug}/{PiperSlug}.meta.json"] = MetaJson,
            [KokoroVoicesUrl] = """{"voices":[]}""",
        };
        var assetBytesByUrl = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [Origin + $"entries/{Slug}/{VoiceId}.pt"] = PtBytes,
            [Origin + $"entries/{Slug}/{Slug}.preview.mp3"] = PreviewBytes,
            [Origin + $"entries/{PiperSlug}/{PiperVoiceId}.pt"] = PiperPtBytes,
            [Origin + $"entries/{PiperSlug}/{PiperSlug}.preview.mp3"] = PiperPreviewBytes,
        };

        return new((request, _) =>
        {
            var absoluteUri = request.RequestUri!.AbsoluteUri;

            if (assetBytesByUrl.TryGetValue(absoluteUri, out var assetBytes))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(assetBytes) });

            return Task.FromResult(
                routes.TryGetValue(absoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
    }
}

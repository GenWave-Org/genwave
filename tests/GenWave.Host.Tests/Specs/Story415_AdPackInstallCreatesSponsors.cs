// STORY-415 — Installing an ad pack creates its sponsors (SPEC F172.3 · PLAN T437)
//
// INSTALL itself shipped at T432 (AdPackController.Install already calls
// ISponsorStore.UpsertPackSponsorsAsync then IAdBriefStore.UpsertAllAsync) — this file is the SPEC
// re-pin T437 owns: driving the REAL production route through WebApplicationFactory<Program> against
// a REAL ephemeral Postgres, the Story393_AdPackKind.cs AdPackKindArc idiom one file over ("arrange
// once, many read-only Scenarios"; two fresh app instances against the SAME database for the
// install/reinstall pair, so a genuinely cold CatalogProxyService cache forces the reinstall to
// actually re-fetch — see that type's own remarks for why). AC2 (the manifest serializer's own
// `brand` field) needs no DB or HTTP at all — it drives CatalogAdPackManifestSerializer.Deserialize
// directly, the ScenarioTheManifestSerializerCapsRejectHonestly idiom one file over.

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using GenWave.Host.Catalog;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureInstallingAnAdPackCreatesItsSponsors
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    [Collection(AdPackInstallCollection.Name)]
    public sealed class ScenarioInstallUpsertsSponsorsThenBriefs(AdPackInstallArc arc)
    {
        [Fact]
        public void InstallIs200()
            => Assert.Equal(HttpStatusCode.OK, arc.FirstInstallStatus);

        [Fact]
        public void ThirtyPackOwnedSponsorsExist()
            => Assert.Equal(AdPackInstallFixtures.BrandCount, arc.SponsorRowCountAfterFirstInstall);

        [Fact]
        public void EveryBriefPointsAtTheSponsorNamedInTheManifest()
        {
            // Every brief's own sponsor_id resolves to a station.sponsor row whose name is the SAME
            // brand this manifest declared it under — no brief left pointing at a mismatched or
            // missing sponsor.
            Assert.Equal(AdPackInstallFixtures.BrandCount, arc.BriefSponsorNamesAfterFirstInstall.Count);
            Assert.Equal(
                AdPackInstallFixtures.Brands.OrderBy(b => b, StringComparer.Ordinal),
                arc.BriefSponsorNamesAfterFirstInstall.OrderBy(b => b, StringComparer.Ordinal));
        }
    }

    /// <summary>Pure, in-process, no DB/HTTP — mirrors Story393_AdPackKind.cs's own
    /// ScenarioTheManifestSerializerCapsRejectHonestly idiom: drives
    /// CatalogAdPackManifestSerializer.Deserialize directly.</summary>
    public sealed class ScenarioTheCatalogManifestStillSaysBrand
    {
        const string OneBriefManifest = """{ "packName": "Test Pack", "briefs": [ { "brand": "Acme Filing Co", "premise": "Bureaucracy, but faster" } ] }""";

        [Fact]
        public void TheAdPackManifestKeepsItsBrandField()
        {
            // AC2's first claim: a manifest brief's own "brand" text round-trips onto
            // CatalogAdPackBrief.Brand verbatim.
            var manifest = CatalogAdPackManifestSerializer.Deserialize(OneBriefManifest);

            Assert.NotNull(manifest);
            var brief = Assert.Single(manifest!.Briefs);
            Assert.Equal("Acme Filing Co", brief.Brand);
        }

        [Fact]
        public void AManifestBriefWithoutABrandFieldIsRefused()
        {
            // AC2's second claim — a NEW Fact, not a rename of the stub above (the brief's own
            // "implement every stub ... byte-for-byte" instruction forbids renaming
            // TheAdPackManifestKeepsItsBrandField itself): a brief whose JSON carries no "brand" key
            // at all refuses the WHOLE manifest, the same all-or-nothing posture
            // CatalogAdPackManifestSerializer's own class remarks document for a blank one.
            const string noBrandManifest = """{ "packName": "Test Pack", "briefs": [ { "premise": "No brand here" } ] }""";

            var manifest = CatalogAdPackManifestSerializer.Deserialize(noBrandManifest);

            Assert.Null(manifest);
        }
    }

    [Collection(AdPackInstallCollection.Name)]
    public sealed class ScenarioReinstallIsIdempotent(AdPackInstallArc arc)
    {
        [Fact]
        public void SponsorRowCountIsUnchanged()
        {
            Assert.Equal(HttpStatusCode.OK, arc.SecondInstallStatus);
            Assert.Equal(arc.SponsorRowCountAfterFirstInstall, arc.SponsorRowCountAfterSecondInstall);
        }

        [Fact]
        public void EveryBriefKeepsItsSponsorId()
            // Same (pack_slug, sponsor_id) key across both installs (T405 review F2's own "content
            // refreshes, operator state persists" ruling, PLAN T432's own sponsor-keyed widening) —
            // the id map captured after each install is identical, brief for brief.
            => Assert.Equal(arc.BriefSponsorIdMapAfterFirstInstall, arc.BriefSponsorIdMapAfterSecondInstall);
    }
}

// ── The install/DB-backed arc — one Postgres, two fresh app instances (one per install) ────────────

[CollectionDefinition(Name)]
public sealed class AdPackInstallCollection : ICollectionFixture<AdPackInstallArc>
{
    public const string Name = "Story415AdPackInstall";
}

/// <summary>
/// Arranges every DB-backed fact this file's own Scenarios read (the
/// <see cref="AdPackKindArc"/>-in-Story393_AdPackKind.cs "arrange once, many read-only Scenarios"
/// idiom, reused rather than duplicated): boots ONE real ephemeral Postgres, then installs a
/// THIRTY-brand pack TWICE through TWO SEPARATE, fresh <see cref="WebApplicationFactory{TEntryPoint}"/>
/// instances against the SAME database — a genuinely cold <c>CatalogProxyService</c> cache per
/// install, <see cref="AdPackKindArc"/>'s own reasoning for why two app boots rather than one plus a
/// fake clock.
/// </summary>
public sealed class AdPackInstallArc : IAsyncLifetime
{
    public HttpStatusCode FirstInstallStatus { get; private set; }
    public HttpStatusCode SecondInstallStatus { get; private set; }
    public int SponsorRowCountAfterFirstInstall { get; private set; }
    public int SponsorRowCountAfterSecondInstall { get; private set; }
    public IReadOnlyList<string> BriefSponsorNamesAfterFirstInstall { get; private set; } = [];
    public IReadOnlyDictionary<string, long> BriefSponsorIdMapAfterFirstInstall { get; private set; } =
        new Dictionary<string, long>();
    public IReadOnlyDictionary<string, long> BriefSponsorIdMapAfterSecondInstall { get; private set; } =
        new Dictionary<string, long>();

    public async Task InitializeAsync()
    {
        // A LOCAL, not a field — Story415AdPackDatabase is file-local (CS9051), the identical reason
        // Story405AdPackDatabase's own arc gives one file over.
        await using var database = await Story415AdPackDatabase.StartAsync();

        await using (var factory1 = new AdPackInstallWebFactory(
                         database, AdPackInstallFixtures.BuildRoutedHandler(AdPackInstallFixtures.ManifestJsonV1)))
        {
            var client1 = await AdPackInstallWebFactory.LoggedInClientAsync(factory1);

            var first = await client1.PostAsync($"/api/ad-packs/{AdPackInstallFixtures.PackSlug}/install", null);
            FirstInstallStatus = first.StatusCode;
            SponsorRowCountAfterFirstInstall = await CountSponsorRowsAsync(database.StationConnectionString, AdPackInstallFixtures.PackSlug);
            BriefSponsorNamesAfterFirstInstall = await ReadBriefSponsorNamesAsync(database.StationConnectionString, AdPackInstallFixtures.PackSlug);
            BriefSponsorIdMapAfterFirstInstall = await ReadBriefSponsorIdMapAsync(database.StationConnectionString, AdPackInstallFixtures.PackSlug);
        }

        // ── Reinstall: the SAME thirty brands, a refreshed premise for one of them — a fresh app
        // instance for the identical cold-cache reasoning AdPackKindArc's own remarks give. ──
        await using (var factory2 = new AdPackInstallWebFactory(
                         database, AdPackInstallFixtures.BuildRoutedHandler(AdPackInstallFixtures.ManifestJsonV2)))
        {
            var client2 = await AdPackInstallWebFactory.LoggedInClientAsync(factory2);

            var second = await client2.PostAsync($"/api/ad-packs/{AdPackInstallFixtures.PackSlug}/install", null);
            SecondInstallStatus = second.StatusCode;
            SponsorRowCountAfterSecondInstall = await CountSponsorRowsAsync(database.StationConnectionString, AdPackInstallFixtures.PackSlug);
            BriefSponsorIdMapAfterSecondInstall = await ReadBriefSponsorIdMapAsync(database.StationConnectionString, AdPackInstallFixtures.PackSlug);
        }
    }

    static async Task<int> CountSponsorRowsAsync(string stationConnectionString, string packSlug)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            "select count(*)::int from station.sponsor where pack_slug = @packSlug", new { packSlug });
    }

    static async Task<IReadOnlyList<string>> ReadBriefSponsorNamesAsync(string stationConnectionString, string packSlug)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        var names = await conn.QueryAsync<string>(
            """
            select s.name from station.ad_brief b
            join station.sponsor s on s.id = b.sponsor_id
            where b.pack_slug = @packSlug
            """,
            new { packSlug });
        return names.AsList();
    }

    /// <summary>Keyed on the sponsor's own name, not the brief's row id — the id map this file's own
    /// EveryBriefKeepsItsSponsorId fact reads must compare the SAME brand across two separate
    /// installs, and a brief row's own id is stable only because the reinstall targets one
    /// (pack_slug, sponsor_id) slot per T405 review F2, not something this arc should assume ahead of
    /// the fact it is arranging.</summary>
    static async Task<IReadOnlyDictionary<string, long>> ReadBriefSponsorIdMapAsync(string stationConnectionString, string packSlug)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<(string Name, long SponsorId)>(
            """
            select s.name as Name, b.sponsor_id as SponsorId from station.ad_brief b
            join station.sponsor s on s.id = b.sponsor_id
            where b.pack_slug = @packSlug
            """,
            new { packSlug });
        return rows.ToDictionary(r => r.Name, r => r.SponsorId, StringComparer.Ordinal);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Test harnesses ───────────────────────────────────────────────────────────────────────────────

/// <summary>This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/>
/// harness — see that type's own remarks. Supplies only the <c>"genwave-t437a"</c> compose
/// project-name prefix this file's own arc needs (the brief's own project-prefix instruction).</summary>
file sealed class Story415AdPackDatabase : EphemeralStationDatabase
{
    Story415AdPackDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story415AdPackDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t437a");
        var db = new Story415AdPackDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for <see cref="AdPackInstallArc"/>'s own installs —
/// mirrors <c>AdPackInstallWebFactory</c> in Story393_AdPackKind.cs almost verbatim (that file's own
/// class remarks explain every setting below); re-declared here (rather than shared) because that
/// type is <see langword="file"/>-scoped there, per this project's own per-file harness convention.
/// </summary>
file sealed class AdPackInstallWebFactory(Story415AdPackDatabase database, FakeHttpMessageHandler handler)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story415-adpack-install";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", database.LibraryConnectionString);
        builder.UseSetting("ConnectionStrings:Station", database.StationConnectionString);
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");
        builder.UseSetting("Community:CatalogIndexUrl", AdPackInstallFixtures.IndexUrl);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(handler));
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

/// <summary>
/// Fixture documents + a routed fake HTTP double for this file's own Facts — slug
/// <c>brought-to-you-by</c> (STORY-415's own AC1 literal example), THIRTY distinct fictional brands
/// GENERATED below rather than hand-typed (the brief's own "generate them in the fixture; keep the
/// JSON literal readable" instruction). The station's shipped catalog pack of the identical name lives
/// in the catalog repo, not here — this is a same-shape stand-in served by a fake origin, exactly the
/// way every other pack-kind spec in this project fakes the catalog door.
/// <see cref="ManifestJsonV1"/>/<see cref="ManifestJsonV2"/> are the SAME thirty brands with one
/// premise refreshed — the reinstall content-change fixture pair, <c>ThreeBriefManifestJsonV1/V2</c>'s
/// own idiom in Story393_AdPackKind.cs, thirty brands wide.
/// </summary>
file static class AdPackInstallFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/ad-pack-install-index.json";
    const string DirectoryUrl = "https://catalog.test/repo/";

    public const string PackSlug = "brought-to-you-by";
    public const int BrandCount = 30;

    /// <summary>Thirty distinct fictional brand names — readable, not a "Brand 01".."Brand 30"
    /// placeholder run, since <see cref="ScenarioInstallUpsertsSponsorsThenBriefs.EveryBriefPointsAtTheSponsorNamedInTheManifest"/>
    /// asserts these round-trip verbatim as station.sponsor.name.</summary>
    public static readonly IReadOnlyList<string> Brands =
    [
        "Al's Diner", "Bramble & Fitch Hardware", "Ceylon Tea Co", "Driftwood Books",
        "Echo Ridge Outfitters", "Fenwick Auto Repair", "Gable & Stone Realty", "Harbor Light Coffee",
        "Ironclad Roofing", "Juniper Lane Florist", "Kettlebrook Farms", "Lantern Hill Bakery",
        "Maple & Main Hardware", "Northgate Fitness", "Oakwood Dental", "Pinecrest Veterinary",
        "Quarry Street Brewing", "Redbrick Pizzeria", "Silverton Cycles", "Thistle & Thorn Nursery",
        "Union Square Diner", "Verdant Yoga Studio", "Westbrook Plumbing", "Xanadu Travel Agency",
        "Yellowbird Laundromat", "Zephyr Auto Glass", "Amberlight Antiques", "Briarwood Insurance",
        "Cobblestone Cafe", "Dovetail Furniture",
    ];

    static string BriefJson(string brand, string premise) => $$"""{ "brand": "{{brand}}", "premise": "{{premise}}" }""";

    public static readonly string ManifestJsonV1 = $$"""
        { "packName": "Brought To You By",
          "briefs": [ {{string.Join(", ", Brands.Select(b => BriefJson(b, $"{b}'s own local spot")))}} ] }
        """;

    /// <summary>The SAME thirty brands, ONE premise refreshed (the FIRST brand's own) — the reinstall
    /// content-change fixture, mirrors <c>ThreeBriefManifestJsonV2</c>'s own idiom, thirty wide.</summary>
    public static readonly string ManifestJsonV2 = $$"""
        { "packName": "Brought To You By",
          "briefs": [ {{string.Join(", ", Brands.Select((b, i) => BriefJson(b, i == 0 ? $"{b}'s own local spot, now family owned" : $"{b}'s own local spot")))}} ] }
        """;

    static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    const string MetaJson = """
        {"author":"Test Fixture","description":"An ad-pack for the install-endpoint specs.","audience":"everyone","added":"2026-09-08"}
        """;

    static string IndexJson(string manifestJson) => $$"""
        { "generatedAt": "2026-09-08", "entries": [
          { "slug": "{{PackSlug}}", "kind": "ad-pack", "audience": "everyone",
            "manifest": { "path": "entries/ad-packs/{{PackSlug}}/{{PackSlug}}.ad-pack.json", "sha256": "{{Sha256Hex(manifestJson)}}" },
            "meta": { "path": "entries/ad-packs/{{PackSlug}}/{{PackSlug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" } } ] }
        """;

    /// <summary>Serves every fixture document at its own resolved URL, 404 for anything else — the
    /// <c>AdPackFixtures.BuildRoutedHandler</c> idiom, one file over.</summary>
    public static FakeHttpMessageHandler BuildRoutedHandler(string manifestJson)
    {
        var routes = new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(manifestJson),
            [DirectoryUrl + "entries/ad-packs/" + PackSlug + "/" + PackSlug + ".ad-pack.json"] = manifestJson,
            [DirectoryUrl + "entries/ad-packs/" + PackSlug + "/" + PackSlug + ".meta.json"] = MetaJson,
        };

        return new((request, _) =>
        {
            var absoluteUri = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(
                routes.TryGetValue(absoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
    }
}

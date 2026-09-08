// STORY-404 — I browse installed pack credits from one endpoint (SPEC F169.2 · PLAN T419)
//
// AttributionsArc seeds ONE ephemeral Postgres (AttributionsPopulatedDatabase) with a jingle pack, a
// font pack, and a voice pack — installed through the REAL IJinglePackStore/IFontPackStore/
// IVoicePackStore UpsertAsync methods resolved straight from the running app's own DI container,
// never HTTP install + a fake catalog origin (the heavier Story399/401 idiom this endpoint's own
// read path doesn't need: GET /api/attributions reads ONLY each pack row's own `definition` jsonb —
// never library.media, station.voice_pack_voice, or station.font_pack_face rows — so every child
// collection UpsertAsync takes below is deliberately empty; all three repositories' own UpsertAsync
// bodies tolerate a zero-length asset/voice/face list cleanly). A second ephemeral Postgres
// (AttributionsSparseDatabase) installs ONLY a font pack, proving AC2's "an empty group never
// appears" the other way round (no jingle pack installed at all).
//
// The AC6 sad-path Scenario needs no real Postgres at all — it mirrors the Story392/Gh008 DB-less
// factory precedent: SurfaceGateMiddleware/the authorization filter both refuse before any store is
// ever touched, so a bogus ConnectionStrings:Library is enough.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GenWave.Core.Abstractions;
using GenWave.Host.Api;
using GenWave.Host.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAttributionsEndpointProjectsInstalledPackCredits
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    [Collection(AttributionsCollection.Name)]
    public sealed class ScenarioEndpointExists(AttributionsArc arc)
    {
        [Fact]
        public void GetApiAttributionsReturns200UnderTheCurationPolicy()
            => Assert.Equal(HttpStatusCode.OK, arc.PopulatedStatus);

        [Fact]
        public void TheResponseBodyIsAJsonObjectWithAGroupsArray()
            => Assert.NotEmpty(arc.PopulatedResponse.Groups);

        [Fact]
        public void TheResponseBodyUsesTheTenF1692WireNamesByteForByte()
        {
            // Deserializing through GenWave.Host.Api.AttributionsResponse (as every other fact in
            // this file does) binds by C# member under System.Text.Json's case-insensitive web
            // defaults — it would still pass a renamed [JsonPropertyName] on any DTO. Only reading
            // the raw body text observes the actual wire names F169.2 publishes to gh-#16.
            foreach (var propertyName in new[]
                     {
                         "groups", "kind", "packs", "slug", "name",
                         "attributions", "title", "creator", "sourceUrl", "license",
                     })
                Assert.Contains($"\"{propertyName}\":", arc.PopulatedBodyJson, StringComparison.Ordinal);
        }

        [Fact]
        public void GroupsAppearInTheFixedJingleThenFontThenVoiceOrder()
            => Assert.Equal(
                ["jingle-pack", "font-pack", "voice-pack"],
                arc.PopulatedResponse.Groups.Select(g => g.Kind).ToArray());
    }

    [Collection(AttributionsCollection.Name)]
    public sealed class ScenarioKindGroupsAppearOnlyWhenPopulated(AttributionsArc arc)
    {
        [Fact]
        public void GroupsContainsAFontPackEntryWhenAFontPackIsInstalled()
            => Assert.Contains(arc.SparseResponse.Groups, g => g.Kind == "font-pack");

        [Fact]
        public void GroupsOmitsAJinglePackEntryWhenNoJinglePackIsInstalled()
            => Assert.DoesNotContain(arc.SparseResponse.Groups, g => g.Kind == "jingle-pack");
    }

    [Collection(AttributionsCollection.Name)]
    public sealed class ScenarioCcByAssetsProjectStructured(AttributionsArc arc)
    {
        [Fact]
        public void EveryCcByAssetSurfacesWithACreatorField()
        {
            Assert.Equal(AttributionsFixtures.CcByOneCreator, arc.CcByLineOne.Creator);
            Assert.Equal(AttributionsFixtures.CcByTwoCreator, arc.CcByLineTwo.Creator);
        }

        [Fact]
        public void EveryCcByAssetSurfacesWithASourceUrlField()
        {
            Assert.Equal(AttributionsFixtures.CcByOneSourceUrl, arc.CcByLineOne.SourceUrl);
            Assert.Equal(AttributionsFixtures.CcByTwoSourceUrl, arc.CcByLineTwo.SourceUrl);
        }

        [Fact]
        public void EveryCcByAssetSurfacesWithALicenseField()
        {
            Assert.Equal("CC-BY", arc.CcByLineOne.License);
            Assert.Equal("CC-BY", arc.CcByLineTwo.License);
        }

        [Fact]
        public void EveryCcByAssetSurfacesWithATitleField()
        {
            Assert.Equal(AttributionsFixtures.CcByOneTitle, arc.CcByLineOne.Title);
            Assert.Equal(AttributionsFixtures.CcByTwoTitle, arc.CcByLineTwo.Title);
        }
    }

    [Collection(AttributionsCollection.Name)]
    public sealed class ScenarioCc0PacksContributeOneAggregateLine(AttributionsArc arc)
    {
        [Fact]
        public void AJinglePackOfThreeCc0AssetsProjectsAsOneAggregateCreditLine()
        {
            // Three CC0 assets were seeded on this pack (id-three/id-four/id-five) — they collapse to
            // exactly one aggregate line, named after the pack, with no per-asset creator/source.
            var line = Assert.Single(arc.Cc0Lines);
            Assert.Equal(AttributionsFixtures.JinglePackName, line.Title);
            Assert.Null(line.Creator);
            Assert.Null(line.SourceUrl);
            Assert.Equal("CC0", line.License);
        }
    }

    [Collection(AttributionsCollection.Name)]
    public sealed class ScenarioVoicePacksSurfaceAsSynthetic(AttributionsArc arc)
    {
        [Fact]
        public void AnInstalledVoicePackAppearsInTheVoicePackGroupWithASyntheticBlendNote()
        {
            var line = Assert.Single(arc.VoicePack.Attributions);
            Assert.Equal(AttributionsFixtures.VoicePackName, arc.VoicePack.Name);
            Assert.Equal("Synthetic blend", line.License);
        }

        [Fact]
        public void NoPerVoiceCreatorStringAppearsOnAVoicePackEntry()
        {
            var line = Assert.Single(arc.VoicePack.Attributions);
            Assert.Null(line.Creator);
            Assert.Null(line.SourceUrl);
        }
    }

    [Collection(AttributionsCollection.Name)]
    public sealed class ScenarioMalformedDefinitionsAreSkippedNotThrown(AttributionsArc arc)
    {
        [Fact]
        public void TheMalformedJinglePackIsOmittedFromTheGroupRatherThanFailingTheWholeRequest()
        {
            // R6: a stored `definition` that fails to re-parse (here `{}` — no packName, no assets)
            // drops ONLY that one pack; PopulatedStatus's own 200 (asserted elsewhere in this file)
            // already proves the request as a whole never 500s over it.
            var jingleGroup = arc.PopulatedResponse.Groups.Single(g => g.Kind == "jingle-pack");
            Assert.DoesNotContain(jingleGroup.Packs, p => p.Slug == AttributionsFixtures.MalformedJingleSlug);
        }

        [Fact]
        public void AWarningNamingTheKindAndSlugIsLogged()
        {
            Assert.Contains(arc.PopulatedLogs, m => m.Contains("jingle-pack") && m.Contains(AttributionsFixtures.MalformedJingleSlug));
        }

        [Fact]
        public void TheMalformedFontPackIsAlsoOmittedRatherThanFailingTheWholeRequest()
        {
            // R6's skip-not-throw guard is identical code shape across all three Build*GroupAsync
            // branches; the jingle branch above proves it once, this proves the font branch is not
            // a silent throw-instead-of-skip regression waiting for its own fixture.
            var fontGroup = arc.PopulatedResponse.Groups.Single(g => g.Kind == "font-pack");
            Assert.DoesNotContain(fontGroup.Packs, p => p.Slug == AttributionsFixtures.MalformedFontSlug);
        }

        [Fact]
        public void AWarningNamingTheFontPackKindAndSlugIsLogged()
        {
            Assert.Contains(arc.PopulatedLogs, m => m.Contains("font-pack") && m.Contains(AttributionsFixtures.MalformedFontSlug));
        }
    }

    [Collection(AttributionsCollection.Name)]
    public sealed class ScenarioPacksWithinAGroupAreOrderedBySlug(AttributionsArc arc)
    {
        [Fact]
        public void FontPacksAppearInSlugOrderEvenThoughTheStoreItselfImposesNoOrdering()
        {
            // FontPackRepository.GetAllAsync carries no `order by` at all (unlike the jingle/voice
            // ListSql text pins) — the controller's own OrderBy(Slug, Ordinal) is the ONLY thing
            // ordering this group. sample-font-pack is seeded FIRST and a-font-pack SECOND, so a
            // sorted result can only come from that OrderBy, never from insertion order.
            var fontGroup = arc.PopulatedResponse.Groups.Single(g => g.Kind == "font-pack");
            Assert.Equal(
                [AttributionsFixtures.SecondFontSlug, AttributionsFixtures.FontSlug],
                fontGroup.Packs.Select(p => p.Slug).ToArray());
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioEndpoint404sUnderPublicPosture
    {
        [Fact]
        public async Task GetApiAttributionsReturns404WhenAdminEnabledIsFalse()
        {
            // Admin:Enabled=false: /api/attributions 404s like every admin route (F162.1/AC6) — no
            // real database needed, SurfaceGateMiddleware refuses before any store is ever touched
            // (the Story392/Gh008 DB-less-factory precedent).
            await using var factory = new AttributionsAdminOffWebFactory();
            var client = factory.CreateClient();

            var response = await client.GetAsync("/api/attributions");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task GetApiAttributionsReturns401WhenAdminIsEnabledButTheCallerHasNoSession()
        {
            // AC6 is a "never 401/403 while public" claim, not "this route never 401s at all" — with
            // the admin surface reachable but no session cookie, the Curation policy refuses like
            // every other admin-plane route (Gh008's own "WithoutTheCookieEveryPlaneStillDeniesEntry"
            // precedent).
            await using var factory = new AttributionsAdminOnWebFactory();
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.GetAsync("/api/attributions");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}

// ── Fixture manifests — hand-authored `definition` JSON, one per pack kind, each shaped to round-trip
// cleanly through that kind's own hardened CatalogXxxManifestSerializer.Deserialize. ──

static class AttributionsFixtures
{
    public const string JingleSlug = "mixed-credits";
    public const string FontSlug = "sample-font-pack";
    public const string VoiceSlug = "sample-voice-pack";

    /// <summary>R6's own fixture — a jingle-pack row whose <c>definition</c> fails
    /// <c>CatalogJinglePackManifestSerializer.Deserialize</c> (no <c>packName</c>, no <c>assets</c>),
    /// proving that ONE bad row is skipped rather than 500ing the whole request.</summary>
    public const string MalformedJingleSlug = "malformed-pack";
    public const string MalformedDefinitionJson = "{}";

    /// <summary>The font-branch sibling of <see cref="MalformedJingleSlug"/> — same malformed
    /// <see cref="MalformedDefinitionJson"/> body, re-parsed by
    /// <c>CatalogFontManifestSerializer.Deserialize</c> instead, proving the font branch's
    /// skip-not-throw guard is exercised too (not just the jingle branch).</summary>
    public const string MalformedFontSlug = "bad-typeface-pack";

    /// <summary>A SECOND font pack, seeded AFTER <see cref="FontSlug"/> (whose own slug,
    /// <c>sample-font-pack</c>, sorts AFTER this one, <c>a-font-pack</c>) — proves the font
    /// group's slug order comes from the controller's own <c>OrderBy(Slug, Ordinal)</c>, not from
    /// insertion order (<c>FontPackRepository.GetAllAsync</c> carries no SQL <c>order by</c>).</summary>
    public const string SecondFontSlug = "a-font-pack";
    public const string SecondFontFamily = "Alt Grotesk";
    public const string SecondFontLicense = "OFL-1.1";
    public const string SecondFontSourceUrl = "https://fonts.example.com/alt-grotesk";

    public const string JinglePackName = "Mixed Credits Pack";
    public const string CcByOneTitle = "Bed One";
    public const string CcByOneCreator = "Jane Doe";
    public const string CcByOneSourceUrl = "https://example.com/bed-one";
    public const string CcByTwoTitle = "Sting Two";
    public const string CcByTwoCreator = "John Roe";
    public const string CcByTwoSourceUrl = "https://example.com/sting-two";

    public const string FontFamily = "Sample Grotesk";
    public const string FontLicense = "OFL-1.1";
    public const string FontSourceUrl = "https://fonts.example.com/sample-grotesk";

    public const string VoicePackName = "Sample Voices Pack";
    // Two voices, not one: a single-voice pack can't tell "one aggregate line per pack" (AC5) apart
    // from a regression that emits one line per voice — Assert.Single would pass either way.
    public const string VoiceId = "af_nova";
    public const string SecondVoiceId = "af_sky";

    static readonly string CcByOneSha = new('a', 64);
    static readonly string CcByTwoSha = new('b', 64);
    static readonly string Cc0ThreeSha = new('c', 64);
    static readonly string Cc0FourSha = new('d', 64);
    static readonly string Cc0FiveSha = new('e', 64);

    public static readonly string JingleDefinitionJson = $$"""
        {
          "packName": "{{JinglePackName}}",
          "assets": [
            { "file": "bed-one.wav", "sha256": "{{CcByOneSha}}", "role": "bed", "title": "{{CcByOneTitle}}", "license": "CC-BY",
              "attribution": { "creator": "{{CcByOneCreator}}", "sourceUrl": "{{CcByOneSourceUrl}}", "license": "CC-BY" } },
            { "file": "sting-two.wav", "sha256": "{{CcByTwoSha}}", "role": "sting", "title": "{{CcByTwoTitle}}", "license": "CC-BY",
              "attribution": { "creator": "{{CcByTwoCreator}}", "sourceUrl": "{{CcByTwoSourceUrl}}", "license": "CC-BY" } },
            { "file": "id-three.wav", "sha256": "{{Cc0ThreeSha}}", "role": "station_id", "title": "Id Three", "license": "CC0" },
            { "file": "id-four.wav", "sha256": "{{Cc0FourSha}}", "role": "bed", "title": "Id Four", "license": "CC0" },
            { "file": "id-five.wav", "sha256": "{{Cc0FiveSha}}", "role": "sting", "title": "Id Five", "license": "CC0" }
          ]
        }
        """;

    public static readonly string FontDefinitionJson = $$"""
        {
          "family": "{{FontFamily}}",
          "files": [ { "role": "regular", "file": "sample-grotesk-regular.woff2", "weight": "400", "style": "normal", "bytes": 12345 } ],
          "license": "{{FontLicense}}",
          "sourceUrl": "{{FontSourceUrl}}",
          "subset": "latin"
        }
        """;

    public static readonly string SecondFontDefinitionJson = $$"""
        {
          "family": "{{SecondFontFamily}}",
          "files": [ { "role": "regular", "file": "alt-grotesk-regular.woff2", "weight": "400", "style": "normal", "bytes": 12345 } ],
          "license": "{{SecondFontLicense}}",
          "sourceUrl": "{{SecondFontSourceUrl}}",
          "subset": "latin"
        }
        """;

    public static readonly string VoiceDefinitionJson = $$"""
        {
          "packName": "{{VoicePackName}}",
          "engine": "kokoro",
          "synthetic": true,
          "preview": "{{VoiceSlug}}.preview.mp3",
          "voices": [ { "voiceId": "{{VoiceId}}" }, { "voiceId": "{{SecondVoiceId}}" } ]
        }
        """;
}

// ── The DB-backed arc — two real Postgres instances (populated + sparse), two running apps, packs
// seeded straight through the REAL stores' UpsertAsync (never HTTP install). ──

[CollectionDefinition(Name)]
public sealed class AttributionsCollection : ICollectionFixture<AttributionsArc>
{
    public const string Name = "Story404Attributions";
}

/// <summary>
/// Arranges every DB-backed fact STORY-404's happy-path Scenarios read (the Story393_AdPackKind.cs
/// AdPackKindArc "arrange once, many read-only Scenarios" idiom). <see cref="PopulatedResponse"/>
/// comes from a database carrying a mixed CC-BY/CC0 jingle pack, a font pack, and a voice pack;
/// <see cref="SparseResponse"/> comes from a SEPARATE database carrying only a font pack, proving the
/// empty-group-omission claim (AC2) the other way round.
/// </summary>
public sealed class AttributionsArc : IAsyncLifetime
{
    /// <summary>Web-default options only for re-parsing the raw body <see cref="PopulatedResponse"/>
    /// was already deserialized from once (case-insensitive member binding) — NOT how
    /// <see cref="PopulatedBodyJson"/> itself is read; that field is the untouched wire text.</summary>
    static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    public HttpStatusCode PopulatedStatus { get; private set; }

    /// <summary>The populated response's raw wire body, captured BEFORE deserialization — the only
    /// thing that can observe the actual JSON property names (SPEC F169.2), since deserializing
    /// through <see cref="AttributionsResponse"/> binds by C# member under
    /// <see cref="JsonSerializerDefaults.Web"/>'s case-insensitive rules regardless of what the wire
    /// name actually is.</summary>
    public string PopulatedBodyJson { get; private set; } = "";

    public AttributionsResponse PopulatedResponse { get; private set; } = new([]);
    public AttributionsResponse SparseResponse { get; private set; } = new([]);
    public IReadOnlyList<string> PopulatedLogs { get; private set; } = [];

    public AttributionPackDto JinglePack => PopulatedResponse.Groups
        .Single(g => g.Kind == "jingle-pack").Packs
        .Single(p => p.Slug == AttributionsFixtures.JingleSlug);

    public AttributionPackDto VoicePack => PopulatedResponse.Groups
        .Single(g => g.Kind == "voice-pack").Packs
        .Single(p => p.Slug == AttributionsFixtures.VoiceSlug);

    public AttributionLineDto CcByLineOne => JinglePack.Attributions.Single(l => l.Title == AttributionsFixtures.CcByOneTitle);
    public AttributionLineDto CcByLineTwo => JinglePack.Attributions.Single(l => l.Title == AttributionsFixtures.CcByTwoTitle);
    public IReadOnlyList<AttributionLineDto> Cc0Lines => JinglePack.Attributions.Where(l => l.License == "CC0").ToList();

    public async Task InitializeAsync()
    {
        await using (var populatedDb = await AttributionsPopulatedDatabase.StartAsync())
        await using (var factory = new AttributionsWebFactory(populatedDb))
        {
            _ = factory.CreateClient();
            var jingleStore = factory.Services.GetRequiredService<IJinglePackStore>();
            var fontStore = factory.Services.GetRequiredService<IFontPackStore>();
            var voiceStore = factory.Services.GetRequiredService<IVoicePackStore>();

            // Every child collection is deliberately empty (see this file's own header remarks) —
            // GET /api/attributions reads only each row's own `definition` text.
            await jingleStore.UpsertAsync(
                AttributionsFixtures.JingleSlug, AttributionsFixtures.JingleDefinitionJson, "test-fixture",
                [], CancellationToken.None);
            await fontStore.UpsertAsync(
                AttributionsFixtures.FontSlug, AttributionsFixtures.FontFamily, AttributionsFixtures.FontDefinitionJson,
                "test-fixture", [], CancellationToken.None);
            await voiceStore.UpsertAsync(
                AttributionsFixtures.VoiceSlug, "kokoro", AttributionsFixtures.VoiceDefinitionJson, "test-fixture",
                [], CancellationToken.None);

            // A SECOND font pack, seeded AFTER the one above — its slug (a-font-pack) sorts BEFORE
            // sample-font-pack, so ScenarioPacksWithinAGroupAreOrderedBySlug can tell the
            // controller's own OrderBy(Slug, Ordinal) apart from insertion order (see
            // AttributionsFixtures.SecondFontSlug's own remarks).
            await fontStore.UpsertAsync(
                AttributionsFixtures.SecondFontSlug, AttributionsFixtures.SecondFontFamily,
                AttributionsFixtures.SecondFontDefinitionJson, "test-fixture", [], CancellationToken.None);

            // R6's own rows — definitions that fail their own kind's hardened Deserialize (see
            // AttributionsFixtures.MalformedDefinitionJson's own remarks); ScenarioMalformed…
            // asserts each is skipped, not thrown on, for BOTH branches that carry the guard.
            await jingleStore.UpsertAsync(
                AttributionsFixtures.MalformedJingleSlug, AttributionsFixtures.MalformedDefinitionJson,
                "test-fixture", [], CancellationToken.None);
            await fontStore.UpsertAsync(
                AttributionsFixtures.MalformedFontSlug, "Malformed Family", AttributionsFixtures.MalformedDefinitionJson,
                "test-fixture", [], CancellationToken.None);

            var client = await AttributionsWebFactory.LoggedInClientAsync(factory);
            var response = await client.GetAsync("/api/attributions");
            PopulatedStatus = response.StatusCode;
            PopulatedBodyJson = await response.Content.ReadAsStringAsync();
            PopulatedResponse = JsonSerializer.Deserialize<AttributionsResponse>(PopulatedBodyJson, WebJsonOptions) ?? new([]);
            PopulatedLogs = factory.Logs.Messages;
        }

        await using (var sparseDb = await AttributionsSparseDatabase.StartAsync())
        await using (var factory = new AttributionsWebFactory(sparseDb))
        {
            _ = factory.CreateClient();
            var fontStore = factory.Services.GetRequiredService<IFontPackStore>();
            await fontStore.UpsertAsync(
                AttributionsFixtures.FontSlug, AttributionsFixtures.FontFamily, AttributionsFixtures.FontDefinitionJson,
                "test-fixture", [], CancellationToken.None);

            var client = await AttributionsWebFactory.LoggedInClientAsync(factory);
            var response = await client.GetAsync("/api/attributions");
            SparseResponse = await response.Content.ReadFromJsonAsync<AttributionsResponse>() ?? new([]);
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Test harnesses ───────────────────────────────────────────────────────────────────────────────

/// <summary>This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/>
/// harness — see that type's own remarks. Supplies only the <c>"gw-t419-full"</c> compose
/// project-name prefix for the populated fixture set — short enough that
/// <c>EphemeralStationDatabase.Provision</c>'s own <c>[..24]</c> truncation still leaves real GUID
/// entropy on the compose project name (O4: a 24-character-exactly prefix would leave none).</summary>
file sealed class AttributionsPopulatedDatabase : EphemeralStationDatabase
{
    AttributionsPopulatedDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<AttributionsPopulatedDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("gw-t419-full");
        var db = new AttributionsPopulatedDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/>
/// harness — a SEPARATE database from <see cref="AttributionsPopulatedDatabase"/>, carrying only a
/// font pack, so <c>GroupsOmitsAJinglePackEntryWhenNoJinglePackIsInstalled</c> proves the omission
/// against a database that genuinely never saw a jingle pack row, not merely an unread one.</summary>
file sealed class AttributionsSparseDatabase : EphemeralStationDatabase
{
    AttributionsSparseDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<AttributionsSparseDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("gw-t419-sparse");
        var db = new AttributionsSparseDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for <see cref="AttributionsArc"/> — boots the real
/// Program.cs graph against a REAL ephemeral Postgres (<paramref name="database"/>) with every hosted
/// service removed (no background reach into the pack tables racing this arc's own seeding), and the
/// REAL <c>IJinglePackStore</c>/<c>IFontPackStore</c>/<c>IVoicePackStore</c> — never swapped for the
/// fakes, since this file's whole point is proving the REAL <c>AttributionsController</c> against real
/// stored <c>definition</c> rows.
/// </summary>
file sealed class AttributionsWebFactory(EphemeralStationDatabase database) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-t419-attributions";

    /// <summary>R6's own capture — <see cref="CapturingLoggerProvider"/>'s shared idiom (see that
    /// type's own remarks), read by <see cref="AttributionsArc"/> before this factory disposes.</summary>
    internal CapturingLoggerProvider Logs { get; } = new();

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

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.AddSingleton<ILoggerProvider>(Logs);
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
/// STORY-404 AC6's own DB-less factory — a bogus <c>ConnectionStrings:Library</c> (never actually
/// reached: <c>Admin:Enabled=false</c> 404s in <c>SurfaceGateMiddleware</c>, before routing ever
/// reaches <c>AttributionsController</c>'s constructor) — no real ephemeral Postgres needed just to
/// prove a 404 (the Story392/Gh008 precedent).
/// </summary>
file sealed class AttributionsAdminOffWebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Admin:Enabled", "false");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-t419-attributions-admin-off");
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");

        builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
    }
}

/// <summary>
/// The admin-ON sibling of <see cref="AttributionsAdminOffWebFactory"/> — proves AC6's "never
/// 401/403" claim is scoped to the public-off posture, not a blanket "this route never 401s": with
/// the admin surface reachable but no session cookie, the Curation policy still refuses (the Gh008
/// "WithoutTheCookieEveryPlaneStillDeniesEntry" precedent). Same bogus connection string — the
/// Curation authorization filter refuses before any store is ever touched.
/// </summary>
file sealed class AttributionsAdminOnWebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-t419-attributions-admin-on");
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");

        builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
    }
}

// STORY-474 — About (gh-#16 · SPEC F207 · PLAN T561)
//
// AboutArc seeds ONE ephemeral Postgres (AboutDatabase) with a font pack — installed through the
// REAL IFontPackStore.UpsertAsync resolved straight from the running app's own DI container (the
// Story404_AttributionsEndpoint.cs precedent) — so AC3's comparison can't pass vacuously on
// {groups:[]}. The WebApplicationFactory also sets Station:Tagline so AC1/the tagline fact prove
// the live setting, not just the default. AC6's sad path needs no real Postgres at all — mirrors
// Story404's own DB-less factory precedent: the Curation policy refuses before any store is ever
// touched. AC3 compares the RAW wire JSON of the two endpoints' `attributions`/body text rather than
// deserialized-record equality: IReadOnlyList<T> record members compare by reference under the
// compiler-generated Equals, so a naive Assert.Equal on two independently-deserialized DTOs would be
// unreliable — comparing the untouched JSON text sidesteps that trap entirely.

using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Configuration;
using GenWave.Host.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAbout
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    [Collection(AboutCollection.Name)]
    public sealed class ScenarioGetAboutWithASession(AboutArc arc)
    {
        /// <summary>AC1 — version, stationName, tagline, libraryCount, uptimeSeconds, attributions —
        /// exactly those six root properties, one assertion: the root's own property-name set
        /// against the expected six-name set, rather than six separate Contains checks that could
        /// never catch an unexpected SEVENTH property.</summary>
        [Fact]
        public void ServesEveryField()
        {
            using var doc = JsonDocument.Parse(arc.AboutBodyJson);

            Assert.Equal(
                new HashSet<string> { "version", "stationName", "tagline", "libraryCount", "uptimeSeconds", "attributions" },
                doc.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet());
        }

        /// <summary>AC2 — version equals the assembly informational version</summary>
        [Fact]
        public void MatchesTheAssemblyVersion()
        {
            var expected =
                typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? "unknown";

            Assert.Equal(expected, arc.Response.Version);
        }

        /// <summary>AC3 — attributions equal GET /api/attributions</summary>
        [Fact]
        public void ReusesTheAttributionList()
        {
            using var aboutDoc = JsonDocument.Parse(arc.AboutBodyJson);
            using var attributionsDoc = JsonDocument.Parse(arc.AttributionsBodyJson);

            Assert.Equal(
                attributionsDoc.RootElement.GetRawText(),
                aboutDoc.RootElement.GetProperty("attributions").GetRawText());
        }

        /// <summary>The seeded font pack proves AC3's comparison isn't vacuous on {groups:[]}.</summary>
        [Fact]
        public void TheAttributionsListIsNotEmpty() => Assert.NotEmpty(arc.Response.Attributions.Groups);

        /// <summary>Station:Tagline (Dean's ruling 2026-09-23) — read live and served verbatim.</summary>
        [Fact]
        public void TaglineIsServedFromTheLiveStationSetting() =>
            Assert.Equal(AboutWebFactory.StationTagline, arc.Response.Tagline);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioGetAboutWithoutASession
    {
        /// <summary>AC6 — no cookie gives 401 (the Curation policy refuses before any store is ever
        /// touched, the Story404/Gh008 "WithoutTheCookieEveryPlaneStillDeniesEntry" precedent — no
        /// real database needed).</summary>
        [Fact]
        public async Task RequiresASession()
        {
            await using var factory = new AboutAdminOnWebFactory();
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.GetAsync("/api/about");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

}

// ---------------------------------------------------------------------
// Station:Tagline — SettingValidator (Dean's ruling 2026-09-23)
// ---------------------------------------------------------------------

public static class FeatureStationTaglineValidation
{
    const string Key = "Station:Tagline";

    static SettingValidator BuildSettingValidator() => new(new ConfigurationBuilder().Build());

    public sealed class ScenarioBlankAndInBudgetAreValid
    {
        [Fact]
        public void BlankIsValid() => Assert.Null(BuildSettingValidator().Validate(Key, ""));

        [Fact]
        public void ExactlyTheMaxCharBudgetIsValid() =>
            Assert.Null(BuildSettingValidator().Validate(Key, new string('a', ShowBudgets.TaglineMaxChars)));
    }

    public sealed class ScenarioOverBudgetIsRejected
    {
        [Fact]
        public void OneCharOverTheMaxIsRejected() =>
            Assert.NotNull(BuildSettingValidator().Validate(Key, new string('a', ShowBudgets.TaglineMaxChars + 1)));
    }
}

// ── The DB-backed arc ────────────────────────────────────────────────────────────────────────────

[CollectionDefinition(Name)]
public sealed class AboutCollection : ICollectionFixture<AboutArc>
{
    public const string Name = "Story474About";
}

/// <summary>
/// Arranges every DB-backed fact STORY-474's happy-path Scenario reads (the Story404_
/// AttributionsEndpoint.cs AttributionsArc "arrange once, many read-only facts" idiom).
/// </summary>
public sealed class AboutArc : IAsyncLifetime
{
    static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    public const string FontSlug = "about-font-pack";
    public const string FontFamily = "About Grotesk";
    public const string FontLicense = "OFL-1.1";
    public const string FontSourceUrl = "https://fonts.example.com/about-grotesk";

    public static readonly string FontDefinitionJson = $$"""
        {
          "family": "{{FontFamily}}",
          "files": [ { "role": "regular", "file": "about-grotesk-regular.woff2", "weight": "400", "style": "normal", "bytes": 12345 } ],
          "license": "{{FontLicense}}",
          "sourceUrl": "{{FontSourceUrl}}",
          "subset": "latin"
        }
        """;

    /// <summary>The <c>/api/about</c> response's raw wire body — the only thing AC3's
    /// <c>ReusesTheAttributionList</c> can safely compare (see this file's own header remarks).</summary>
    public string AboutBodyJson { get; private set; } = "";

    /// <summary>The <c>/api/attributions</c> response's raw wire body, captured from the SAME
    /// logged-in client in the SAME arrange.</summary>
    public string AttributionsBodyJson { get; private set; } = "";

    public AboutResponse Response { get; private set; } = new("", "", "", 0, 0, new([]));

    public async Task InitializeAsync()
    {
        await using var db = await AboutDatabase.StartAsync();
        await using var factory = new AboutWebFactory(db);

        _ = factory.CreateClient();
        var fontStore = factory.Services.GetRequiredService<IFontPackStore>();
        await fontStore.UpsertAsync(FontSlug, FontFamily, FontDefinitionJson, "test-fixture", [], CancellationToken.None);

        var client = await AboutWebFactory.LoggedInClientAsync(factory);

        var aboutHttpResponse = await client.GetAsync("/api/about");
        AboutBodyJson = await aboutHttpResponse.Content.ReadAsStringAsync();
        Response = JsonSerializer.Deserialize<AboutResponse>(AboutBodyJson, WebJsonOptions) ?? Response;

        var attributionsHttpResponse = await client.GetAsync("/api/attributions");
        AttributionsBodyJson = await attributionsHttpResponse.Content.ReadAsStringAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Test harnesses ───────────────────────────────────────────────────────────────────────────────

/// <summary>This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/>
/// harness — see that type's own remarks.</summary>
file sealed class AboutDatabase : EphemeralStationDatabase
{
    AboutDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<AboutDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("gw-t561-about");
        var db = new AboutDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for <see cref="AboutArc"/> — boots the real
/// Program.cs graph against a REAL ephemeral Postgres (<paramref name="database"/>) with every hosted
/// service removed, and a non-blank <c>Station:Tagline</c> so <c>TaglineIsServedFromTheLiveStationSetting</c>
/// proves the live setting, not merely the compiled-in default.
/// </summary>
file sealed class AboutWebFactory(EphemeralStationDatabase database) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-t561-about";
    internal const string StationTagline = "Your late-night companion";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", database.LibraryConnectionString);
        builder.UseSetting("ConnectionStrings:Station", database.StationConnectionString);
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Tagline", StationTagline);
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");

        builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
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
/// STORY-474 AC6's own DB-less factory (the Story404/Gh008 precedent) — a bogus
/// <c>ConnectionStrings:Library</c>, never actually reached: with the admin surface reachable but no
/// session cookie, the Curation authorization filter refuses before any store is ever touched.
/// </summary>
file sealed class AboutAdminOnWebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-t561-about-admin-on");
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");

        builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
    }
}

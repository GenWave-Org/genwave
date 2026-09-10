// STORY-407 — List sponsors and read one (SPEC F171.2 · PLAN T434)
//
// BDD specification — xUnit through the deployed entry point (WebApplicationFactory<Program> against
// a real ephemeral Postgres — the Story392_AdBriefsApi.cs arc idiom): every fact drives
// GET /api/sponsors* over HTTP with an authed admin session, never ISponsorStore/SponsorsController
// directly. One arc (Story407Arc) arranges everything every HAPPY-PATH Scenario below reads; the
// anonymous-posture Scenario needs no arranged state (a bare unauthenticated GET), so it stands alone.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureListSponsorsAndReadOne
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    [Collection(Story407Collection.Name)]
    public sealed class ScenarioTheListShape(Story407Arc arc)
    {
        [Fact]
        public void TheListIsOrderedByName()
            => Assert.Equal(["Alpha", "Beta", "Gamma"], arc.ListNames);

        [Fact]
        public void EveryRowCarriesTheTenFactsAndPausedAndPackSlug()
        {
            Assert.Equal(Story407Arc.AlphaTaglineValue, arc.AlphaTagline);
            Assert.Equal(Story407Arc.AlphaAboutValue, arc.AlphaAbout);
            Assert.Equal(Story407Arc.AlphaPhoneValue, arc.AlphaPhone);
            Assert.Equal(Story407Arc.AlphaAddressValue, arc.AlphaAddress);
            Assert.Equal(Story407Arc.AlphaWebsiteValue, arc.AlphaWebsite);
            Assert.Equal(Story407Arc.AlphaToneValue, arc.AlphaTone);
            Assert.False(arc.AlphaPaused);
            Assert.Null(arc.AlphaPackSlug);
        }

        [Fact]
        public void EveryRowCarriesBriefsSpotsByStateAndShowsCounts()
        {
            Assert.Equal(0, arc.AlphaBriefs);
            Assert.Empty(arc.AlphaSpots);
            Assert.Equal(0, arc.AlphaShows);
        }
    }

    [Collection(Story407Collection.Name)]
    public sealed class ScenarioTheFoldedFilter(Story407Arc arc)
    {
        [Fact]
        public void QMatchesLikeNameKey()
            => Assert.Contains(Story407Arc.NorthsideBakery, arc.FilteredNames);
    }

    [Collection(Story407Collection.Name)]
    public sealed class ScenarioTheDetailCarriesAWeakETag(Story407Arc arc)
    {
        [Fact]
        public void GetByIdIs200()
            => Assert.Equal(HttpStatusCode.OK, arc.DetailStatus);

        [Fact]
        public void TheETagIsWeakDigits()
            => Assert.Matches("""^W/"\d+"$""", arc.DetailETag);
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnonymousIsRefused
    {
        [Fact]
        public async Task NoCookieIs401()
        {
            // No real database needed — [Authorize(Policy = Curation)] refuses BEFORE routing ever
            // reaches SponsorsController's constructor (the Story166/Story374/Story392(T403) DB-less-
            // factory precedent, here with Admin:Enabled left at its true default so the surface
            // itself exists and the 401 — not a 404 — is what's under test).
            await using var factory = new Story407AnonymousWebFactory();
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.GetAsync("/api/sponsors");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}

// ── Collection definition — one ephemeral Postgres/factory shared by every happy-path Scenario above
// (the Story392_AdBriefsApi.cs "arrange once, many read-only Scenarios" idiom). ──

[CollectionDefinition(Name)]
public sealed class Story407Collection : ICollectionFixture<Story407Arc>
{
    public const string Name = "Story407SponsorListAndDetail";
}

/// <summary>
/// Arranges every fact STORY-407's Scenarios read, entirely over the REAL production HTTP pipeline
/// with a real admin session — no <c>SponsorRepository</c> call, no <c>SponsorsController</c> call,
/// anywhere in this class.
/// </summary>
public sealed class Story407Arc : IAsyncLifetime
{
    // A real space between "North" and "Side" (unlike "Northside") — the query below deliberately
    // uses excess whitespace + mixed case, so a match here actually proves station.sponsor_fold's own
    // collapse-whitespace-then-lowercase behaviour, not a same-string comparison.
    public const string NorthsideBakery = "North Side Bakery";

    public const string AlphaTaglineValue = "Fresh every morning";
    public const string AlphaAboutValue = "A neighborhood coffee shop";
    public const string AlphaPhoneValue = "(406) 111-0100";
    public const string AlphaAddressValue = "1 Main St";
    public const string AlphaWebsiteValue = "https://alpha.example.com";
    public const string AlphaToneValue = "warm";

    public IReadOnlyList<string> ListNames { get; private set; } = [];

    public string? AlphaTagline { get; private set; }
    public string? AlphaAbout { get; private set; }
    public string? AlphaPhone { get; private set; }
    public string? AlphaAddress { get; private set; }
    public string? AlphaWebsite { get; private set; }
    public string? AlphaTone { get; private set; }
    public string? AlphaPackSlug { get; private set; }
    public bool AlphaPaused { get; private set; }
    public int AlphaBriefs { get; private set; }
    public IReadOnlyDictionary<string, int> AlphaSpots { get; private set; } = new Dictionary<string, int>();
    public int AlphaShows { get; private set; }

    public IReadOnlyList<string> FilteredNames { get; private set; } = [];

    public HttpStatusCode DetailStatus { get; private set; }
    public string DetailETag { get; private set; } = "";

    public async Task InitializeAsync()
    {
        // A LOCAL, not a field — Story407Database is file-local (CS9051), the Story392AdBriefsDatabase
        // precedent.
        await using var database = await Story407Database.StartAsync();
        await using var factory = new Story407WebFactory(database);
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new { password = Story407WebFactory.Password });
        if (login.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login unexpectedly returned {login.StatusCode}");

        // ── Seed three sponsors, one ("Alpha") with every optional fact set. ──
        var alphaCreate = await client.PostAsJsonAsync("/api/sponsors", new
        {
            name = "Alpha",
            tagline = AlphaTaglineValue,
            about = AlphaAboutValue,
            phone = AlphaPhoneValue,
            address = AlphaAddressValue,
            website = AlphaWebsiteValue,
            tone = AlphaToneValue,
        });
        var alphaId = (await JsonDocument.ParseAsync(await alphaCreate.Content.ReadAsStreamAsync()))
            .RootElement.GetProperty("id").GetInt64();

        await client.PostAsJsonAsync("/api/sponsors", new { name = "Gamma" });
        await client.PostAsJsonAsync("/api/sponsors", new { name = "Beta" });
        await client.PostAsJsonAsync("/api/sponsors", new { name = NorthsideBakery });

        // ── GET /api/sponsors — the list shape, ordered by name. ──
        var listResponse = await client.GetAsync("/api/sponsors");
        var listBody = await JsonDocument.ParseAsync(await listResponse.Content.ReadAsStreamAsync());
        var rows = listBody.RootElement.EnumerateArray().ToList();
        ListNames = rows.Take(3).Select(r => r.GetProperty("name").GetString() ?? "").ToList();

        var alphaRow = rows.Single(r => r.GetProperty("name").GetString() == "Alpha");
        AlphaTagline = alphaRow.GetProperty("tagline").GetString();
        AlphaAbout = alphaRow.GetProperty("about").GetString();
        AlphaPhone = alphaRow.GetProperty("phone").GetString();
        AlphaAddress = alphaRow.GetProperty("address").GetString();
        AlphaWebsite = alphaRow.GetProperty("website").GetString();
        AlphaTone = alphaRow.GetProperty("tone").GetString();
        AlphaPackSlug = alphaRow.GetProperty("packSlug").ValueKind == JsonValueKind.Null
            ? null : alphaRow.GetProperty("packSlug").GetString();
        AlphaPaused = alphaRow.GetProperty("paused").GetBoolean();
        AlphaBriefs = alphaRow.GetProperty("briefs").GetInt32();
        AlphaSpots = alphaRow.GetProperty("spots").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetInt32());
        AlphaShows = alphaRow.GetProperty("shows").GetInt32();

        // ── GET /api/sponsors?q= — the folded filter. Excess whitespace (%20%20%20, collapsed by
        // station.sponsor_fold) and mixed case, proving the fold — not a literal substring match. %20,
        // not '+': ASP.NET Core's own query-string parsing (unlike classic ASP.NET's
        // HttpUtility.ParseQueryString) does NOT decode '+' as a space, so a literal '+' would fold to
        // "north+side" and match nothing. ──
        var filterResponse = await client.GetAsync("/api/sponsors?q=nORTH%20%20%20sIDE");
        var filterBody = await JsonDocument.ParseAsync(await filterResponse.Content.ReadAsStreamAsync());
        FilteredNames = filterBody.RootElement.EnumerateArray()
            .Select(r => r.GetProperty("name").GetString() ?? "").ToList();

        // ── GET /api/sponsors/{id} — the detail carries a weak ETag. EntityTagHeaderValue.Tag is only
        // the quoted-string part (its own IsWeak is a separate bool) — ToString() is what round-trips
        // the full W/"..." wire form this fact is actually pinning. ──
        var detailResponse = await client.GetAsync($"/api/sponsors/{alphaId}");
        DetailStatus = detailResponse.StatusCode;
        DetailETag = detailResponse.Headers.ETag?.ToString() ?? "";
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Test harness — WebApplicationFactory + ephemeral Postgres subclasses (the Story392_AdBriefsApi.cs
// "`file`-scoped types cannot cross files" precedent — this file supplies its own). ──

file sealed class Story407WebFactory(Story407Database db) : WebApplicationFactory<Program>
{
    public const string Password = "test-password-t434a-sponsors-list";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", db.LibraryConnectionString);
        builder.UseSetting("ConnectionStrings:Station", db.StationConnectionString);
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
        });
    }
}

/// <summary>
/// STORY-407's own DB-less factory — a bogus <c>ConnectionStrings:*</c> (never actually reached: the
/// deny-ALL-without-a-named-policy-match posture refuses BEFORE routing ever reaches
/// <see cref="SponsorsController"/>'s constructor) — no real ephemeral Postgres needed just to prove a
/// 401 (the <c>Story374.GardenerSurfaceWebFactory</c>/<c>Story166.KillSwitchWebFactory</c>/
/// <c>Story392(T403).AdsAdminOffWebFactory</c> precedent, here proving 401 rather than 404 since
/// <c>Admin:Enabled</c> is left at its true default).
/// </summary>
file sealed class Story407AnonymousWebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-t434a-sponsors-anonymous");
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
        });
    }
}

/// <summary>This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/> harness
/// — see that type's own remarks. Supplies only the <c>"genwave-t434a"</c> compose project-name prefix
/// this file's own arc needs.</summary>
file sealed class Story407Database : EphemeralStationDatabase
{
    Story407Database(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story407Database> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t434a");
        var db = new Story407Database(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

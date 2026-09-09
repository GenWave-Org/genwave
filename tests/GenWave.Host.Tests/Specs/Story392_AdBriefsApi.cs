// STORY-392 — I manage the Ads library (the Briefs half · F162.1 · F162.2 · PLAN T403b)
// Re-targeted at the sponsor-first wire contract (SPEC F171.6, STORY-411, PLAN T435): every brief now
// belongs to a sponsor id, never a free-text brand — see the Arc's own remarks below for what changed.
// The Briefs tab's page half (AC5 in a browser) lives outside this repo's server-side specs.
//
// BDD specification — xUnit through the deployed entry point (WebApplicationFactory<Program> against
// a real ephemeral Postgres — the Story374/Story382/Story392(T403) arc idiom): every fact drives
// GET/POST/PATCH /api/ad-briefs* and POST /api/sponsors over HTTP with an authed admin session, never
// AdBriefRepository/AdBriefsController/SponsorRepository/SponsorsController directly. One arc
// (AdBriefsApiArc) arranges everything every HAPPY-PATH/sad-path Scenario below reads (the SAME
// "arrange once, many read-only Scenarios" idiom Story392's own AdsApiArc already establishes one
// controller over); the admin-surface posture Scenario needs no real database at all
// (SurfaceGateMiddleware 404s before any store is ever touched), so it gets its own, DB-less factory
// (the Story166/Story374/Story392(T403) precedent).

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAdBriefsApi
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — through the production surface (WebApplicationFactory)
    // ---------------------------------------------------------------------

    [Collection(AdBriefsApiCollection.Name)]
    public sealed class ScenarioTheListShowsEveryBrief(AdBriefsApiArc arc)
    {
        [Fact]
        public void BothPackAndOwnerRowsAppear()
        {
            // GET /api/ad-briefs (F162.1's Briefs tab) — the SEEDED pack row and the POSTed owner row
            // both come back, bare array, no paging envelope (T403b's own YAGNI call). Each row's own
            // sponsor name (SPEC F171.6's sponsor object, not a top-level brand field) is what this
            // fact reads.
            Assert.Equal(HttpStatusCode.OK, arc.ListStatus);
            Assert.Contains(arc.ListBrands, b => b is (AdBriefsApiArc.SeededPackBrand, false, AdBriefsApiArc.SeededPackSlug));
            Assert.Contains(arc.ListBrands, b => b is (AdBriefsApiArc.CleanOwnerBrand, false, null));
            // review MED-1: every sponsor in THIS arc is unpaused, so `paused` is false for every row
            // here — Story411's ABriefUnderAPausedSponsorReportsPausedTrue holds the true case (a
            // paused sponsor's own brief), just not through the LIST endpoint.
            Assert.All(arc.ListBrands, b => Assert.False(b.Paused));
        }
    }

    [Collection(AdBriefsApiCollection.Name)]
    public sealed class ScenarioPostCreatesAnOwnerBrief(AdBriefsApiArc arc)
    {
        [Fact]
        public void ACleanCreatePostsAndReadsBackEveryFieldVerbatim()
        {
            // POST /api/ad-briefs {sponsorId,premise,tone,structure,enabled} → 201, every field
            // round-trips byte-for-byte (the sponsor's own name, under "sponsor", stands in for the
            // former top-level "brand"), pack_slug is null (owner-only creation).
            Assert.Equal(HttpStatusCode.Created, arc.CleanOwnerPostStatus);
            Assert.Equal(AdBriefsApiArc.CleanOwnerBrand, arc.RoundTripSponsorName);
            Assert.Equal(AdBriefsApiArc.CleanOwnerPremise, arc.RoundTripPremise);
            Assert.Equal(AdBriefsApiArc.CleanOwnerTone, arc.RoundTripTone);
            Assert.Equal(AdBriefsApiArc.CleanOwnerStructure, arc.RoundTripStructure);
            Assert.True(arc.RoundTripEnabled);
            Assert.Null(arc.RoundTripPackSlug);
        }

        [Fact]
        public void ADuplicateOwnerBrandIs409()
        {
            // A second POST for the SAME sponsor and the SAME premise — the ratified per-sponsor-angle
            // cap (SPEC F171.6, PLAN T431/T432/T435 — station.ad_brief's own
            // ad_brief_sponsor_id_premise_key) surfaces as 409 duplicate_brief naming the premise field,
            // never a silent update.
            Assert.Equal(HttpStatusCode.Conflict, arc.DuplicateOwnerPostStatus);
            Assert.Equal("premise", arc.DuplicateOwnerPostField);
            Assert.Equal("duplicate_brief", arc.DuplicateOwnerPostType);
        }

        [Fact]
        public void ABrandThatOnlyHasAPackBriefCoexists()
        {
            // A sponsor NAME already carrying a PACK-owned sponsor row — creating an OWNER sponsor
            // (pack_slug NULL) with the SAME folded name now succeeds at the SPONSOR level (SPEC
            // F171.6 moved the "(pack_slug, brand)" scoping from station.ad_brief onto
            // station.sponsor's own sponsor_pack_slug_name_key: NULLS NOT DISTINCT makes (NULL,
            // name_key) distinct from ('another-pack-slug', name_key) — verified against the REAL
            // constraint, not asserted from memory). A brief for that new owner sponsor then creates
            // cleanly, since it names a real, distinct sponsor id.
            Assert.Equal(HttpStatusCode.Created, arc.CoexistingOwnerPostStatus);
        }

        [Fact]
        public void AnOmittedEnabledDefaultsToTrue()
        {
            // review F1: the SAME coexisting-sponsor brief POST omits `enabled` entirely — the add
            // form's own "new briefs are live by default" posture (AdBriefCreateRequest's own remarks)
            // pinned against the real response body, not merely asserted from the controller's own doc
            // comment.
            Assert.True(arc.CoexistingOwnerEnabled);
        }

        [Fact]
        public void AWhitespaceOnlyPremiseFoldsToNull()
        {
            // review F3: the SAME coexisting-sponsor brief POST also sends a whitespace-only premise —
            // reads back null, never the literal spaces.
            Assert.Null(arc.CoexistingOwnerPremise);
        }

        [Fact]
        public void ABlankBrandIs400()
        {
            // Re-targeted (SPEC F171.6, PLAN T435): there is no free-text brand to blank anymore — a
            // missing sponsorId is what this fact now pins, refused as 400 sponsor_required.
            Assert.Equal(HttpStatusCode.BadRequest, arc.MissingSponsorIdPostStatus);
        }
    }

    [Collection(AdBriefsApiCollection.Name)]
    public sealed class ScenarioPatchTogglesAnyBrief(AdBriefsApiArc arc)
    {
        [Fact]
        public void PatchDisablesAnOwnerBrief()
        {
            Assert.Equal(HttpStatusCode.OK, arc.PatchOwnerStatus);
            Assert.False(arc.PatchOwnerResultEnabled);
            // review MED-1: the PATCH response's own sponsor object, pinned by name — this arc's owner
            // sponsor is never paused, so asserting `paused` here too would be a tautology a hardcoded
            // `paused: false` projection could never fail; Story411's NEW fact
            // (PatchOnAPausedSponsorsBriefStillReportsPausedTrue) pins the true case instead.
            Assert.Equal(AdBriefsApiArc.CleanOwnerBrand, arc.PatchOwnerResultSponsorName);
        }

        [Fact]
        public void PatchEnablesAPackBrief()
        {
            // The toggle is the operator's own lever over pack content too — only CREATE is
            // owner-only (PLAN T403b's own reading of F162.1).
            Assert.Equal(HttpStatusCode.OK, arc.PatchPackStatus);
            Assert.True(arc.PatchPackResultEnabled);
            Assert.Equal(AdBriefsApiArc.SeededPackSlug, arc.PatchPackResultPackSlug);
        }

        [Fact]
        public void PatchWithNoEnabledFieldIs400()
        {
            // The 400 names the field and carries no type: `field` pins "enabled" (never a bare status
            // a wrong-field bug could still pass), and the absent `type` key confirms
            // RequiredFieldProblem's PATCH call site leaves ProblemDetails.Type null (the converter
            // omits it) rather than merely happening to serialize a null value.
            Assert.Equal(HttpStatusCode.BadRequest, arc.PatchMissingEnabledStatus);
            Assert.Equal("enabled", arc.PatchMissingEnabledField);
            Assert.False(arc.PatchMissingEnabledHasType);
        }

        [Fact]
        public void PatchOnAnUnknownIdIs404()
        {
            Assert.Equal(HttpStatusCode.NotFound, arc.PatchUnknownIdStatus);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the admin surface gates the Briefs tab too
    // ---------------------------------------------------------------------

    public sealed class ScenarioAdminSurfacePosture
    {
        [Fact]
        public async Task EveryAdBriefsRouteIs404WhileAdminIsDisabled()
        {
            // Admin:Enabled=false: /api/ad-briefs* 404s like every admin route (F162.1). No real
            // database needed — SurfaceGateMiddleware refuses before any store is ever touched (the
            // Story166/Story374/Story392(T403) DB-less-factory precedent).
            await using var factory = new AdBriefsAdminOffWebFactory();
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var routes = new (HttpMethod Method, string Path, HttpContent? Body)[]
            {
                (HttpMethod.Get, "/api/ad-briefs", null),
                (HttpMethod.Post, "/api/ad-briefs", JsonContent.Create(new { })),
                (HttpMethod.Patch, "/api/ad-briefs/1", JsonContent.Create(new { })),
            };

            foreach (var (method, path, body) in routes)
            {
                var request = new HttpRequestMessage(method, path) { Content = body };
                var response = await client.SendAsync(request);
                Assert.True(
                    response.StatusCode == HttpStatusCode.NotFound,
                    $"{method} {path} returned {(int)response.StatusCode} with Admin:Enabled=false.");
            }
        }
    }
}

// ── Collection definition — one ephemeral Postgres/factory shared by every happy-path/sad-path
// Scenario above (the Story374/Story382/Story392(T403) "arrange once, many read-only Scenarios"
// idiom). ──

[CollectionDefinition(Name)]
public sealed class AdBriefsApiCollection : ICollectionFixture<AdBriefsApiArc>
{
    public const string Name = "Story392AdBriefsApi";
}

/// <summary>
/// Arranges every fact STORY-392's Briefs-API Scenarios read, entirely over the REAL production HTTP
/// pipeline with a real admin session — no <c>AdBriefRepository</c> call, no
/// <c>AdBriefsController</c> call, anywhere in this class. The pack brief is seeded directly via raw
/// SQL (the <c>GardenerRotFixtures</c>/<c>AdsWireFixtures</c> precedent): no pack-install endpoint
/// exists yet, so a pack row can only ever be arranged independently of the API under test. Every
/// OWNER sponsor this arc needs, though, is created through the real <c>POST /api/sponsors</c> surface
/// (PLAN T434) — the sponsor-first contract (SPEC F171.6, PLAN T435) means a brief create needs a real
/// sponsor id, not a free-text brand a compile bridge used to resolve.
/// </summary>
public sealed class AdBriefsApiArc : IAsyncLifetime
{
    public const string SeededPackSlug = "genwave-catalog";
    public const string SeededPackBrand = "Widget Bros";

    public const string CleanOwnerBrand = "Cravin's Diner";
    public const string CleanOwnerPremise = "A cozy neighborhood diner";
    public const string CleanOwnerTone = "warm";
    public const string CleanOwnerStructure = "hook-offer-cta";

    const string CoexistingBrand = "Shared Brand Co";

    public HttpStatusCode ListStatus { get; private set; }
    public IReadOnlyList<(string Brand, bool Paused, string? PackSlug)> ListBrands { get; private set; } = [];

    public HttpStatusCode CleanOwnerPostStatus { get; private set; }
    public string RoundTripSponsorName { get; private set; } = "";
    public string? RoundTripPremise { get; private set; }
    public string? RoundTripTone { get; private set; }
    public string? RoundTripStructure { get; private set; }
    public bool RoundTripEnabled { get; private set; }
    public string? RoundTripPackSlug { get; private set; }

    public HttpStatusCode DuplicateOwnerPostStatus { get; private set; }
    public string? DuplicateOwnerPostField { get; private set; }
    public string? DuplicateOwnerPostType { get; private set; }

    public HttpStatusCode CoexistingOwnerPostStatus { get; private set; }
    public bool CoexistingOwnerEnabled { get; private set; }
    public string? CoexistingOwnerPremise { get; private set; }

    public HttpStatusCode MissingSponsorIdPostStatus { get; private set; }

    public HttpStatusCode PatchOwnerStatus { get; private set; }
    public bool PatchOwnerResultEnabled { get; private set; }
    public string PatchOwnerResultSponsorName { get; private set; } = "";

    public HttpStatusCode PatchPackStatus { get; private set; }
    public bool PatchPackResultEnabled { get; private set; }
    public string? PatchPackResultPackSlug { get; private set; }

    public HttpStatusCode PatchMissingEnabledStatus { get; private set; }
    public string? PatchMissingEnabledField { get; private set; }
    public bool PatchMissingEnabledHasType { get; private set; }
    public HttpStatusCode PatchUnknownIdStatus { get; private set; }

    public async Task InitializeAsync()
    {
        // A LOCAL, not a field — Story392AdBriefsDatabase is file-local (CS9051), the identical
        // reason Story374's/Story382's/Story392(T403)'s own arcs give for the same shape.
        await using var database = await Story392AdBriefsDatabase.StartAsync();

        var packId = await AdBriefWireFixtures.InsertPackBriefAsync(
            database.StationConnectionString, SeededPackSlug, SeededPackBrand);

        await using var factory = new Story392AdBriefsWebFactory(database);
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new { password = Story392AdBriefsWebFactory.Password });
        if (login.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login unexpectedly returned {login.StatusCode}");

        // ── The clean owner sponsor, created through the real POST /api/sponsors surface (PLAN T434)
        // — the sponsor-first contract needs a real sponsor id before a brief for it can exist. Pure
        // arrange, not a fact under test — an unexpected status throws loudly here rather than crashing
        // confusingly on the GetProperty("id") below (review LOW-2, the coexisting sponsor create's own
        // guard a few dozen lines down). ──
        var cleanOwnerSponsorCreate = await client.PostAsJsonAsync(
            "/api/sponsors", new { name = CleanOwnerBrand });
        if (cleanOwnerSponsorCreate.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"arrange: POST /api/sponsors for the clean owner sponsor unexpectedly returned " +
                $"{cleanOwnerSponsorCreate.StatusCode}");
        }
        var cleanOwnerSponsor = await JsonDocument.ParseAsync(await cleanOwnerSponsorCreate.Content.ReadAsStreamAsync());
        var cleanOwnerSponsorId = cleanOwnerSponsor.RootElement.GetProperty("id").GetInt64();

        // ── The clean owner create: the editor round-trip fact. ──
        var createPayload = new
        {
            sponsorId = cleanOwnerSponsorId,
            premise = CleanOwnerPremise,
            tone = CleanOwnerTone,
            structure = CleanOwnerStructure,
            enabled = true,
        };
        var createResponse = await client.PostAsJsonAsync("/api/ad-briefs", createPayload);
        CleanOwnerPostStatus = createResponse.StatusCode;
        var created = await JsonDocument.ParseAsync(await createResponse.Content.ReadAsStreamAsync());
        var cleanOwnerId = created.RootElement.GetProperty("id").GetInt64();
        RoundTripSponsorName = created.RootElement.GetProperty("sponsor").GetProperty("name").GetString() ?? "";
        RoundTripPremise = created.RootElement.GetProperty("premise").GetString();
        RoundTripTone = created.RootElement.GetProperty("tone").GetString();
        RoundTripStructure = created.RootElement.GetProperty("structure").GetString();
        RoundTripEnabled = created.RootElement.GetProperty("enabled").GetBoolean();
        RoundTripPackSlug = created.RootElement.GetProperty("packSlug").ValueKind == JsonValueKind.Null
            ? null : created.RootElement.GetProperty("packSlug").GetString();

        // ── The list: both the seeded pack row and the just-created owner row appear. ──
        var listResponse = await client.GetAsync("/api/ad-briefs");
        ListStatus = listResponse.StatusCode;
        var listBody = await JsonDocument.ParseAsync(await listResponse.Content.ReadAsStreamAsync());
        ListBrands = listBody.RootElement.EnumerateArray()
            .Select(item => (
                item.GetProperty("sponsor").GetProperty("name").GetString() ?? "",
                item.GetProperty("sponsor").GetProperty("paused").GetBoolean(),
                item.GetProperty("packSlug").ValueKind == JsonValueKind.Null
                    ? (string?)null : item.GetProperty("packSlug").GetString()))
            .ToList();

        // ── A duplicate owner create for the SAME sponsor AND the SAME folded premise — refused, 409
        // duplicate_brief naming the premise field, never a silent update (CreateOwnerAsync's own
        // (sponsor_id, premise_key) cap, SPEC F171.6: a DIFFERENT premise for the same sponsor is a
        // legal second angle, not a duplicate — this call must match CleanOwnerPremise verbatim to
        // actually land on the same key). ──
        var duplicateResponse = await client.PostAsJsonAsync("/api/ad-briefs", new
        {
            sponsorId = cleanOwnerSponsorId,
            premise = CleanOwnerPremise,
        });
        DuplicateOwnerPostStatus = duplicateResponse.StatusCode;
        var duplicateBody = await JsonDocument.ParseAsync(await duplicateResponse.Content.ReadAsStreamAsync());
        DuplicateOwnerPostField = duplicateBody.RootElement.TryGetProperty("field", out var fieldProperty)
            ? fieldProperty.GetString() : null;
        DuplicateOwnerPostType = duplicateBody.RootElement.TryGetProperty("type", out var typeProperty)
            ? typeProperty.GetString() : null;

        // ── A PACK-owned sponsor already named CoexistingBrand, seeded by SQL, ahead of the owner
        // sponsor create below — so the "a pack sponsor already holds this name" precondition is
        // independent of the API under test. An OWNER sponsor (pack_slug NULL) for the SAME folded
        // name then creates cleanly at the SPONSOR level (see ABrandThatOnlyHasAPackBriefCoexists's own
        // remarks) — that sponsor create is pure arrange, so it is asserted 201 here and thrown on any
        // other status (arrange failures fail loudly as arrange, not as a fact); CoexistingOwnerPostStatus
        // itself reads the BRIEF create below — the actual "coexists" claim ABrandThatOnlyHasAPackBriefCoexists
        // pins. A brief for that new owner sponsor is what review findings F1/F3 pin (folded in, not a
        // separate call): `enabled` is OMITTED entirely — pins the "omitted defaults to true" ruling
        // against the real response body, not merely the controller's own doc comment — and `premise`
        // is whitespace-only — pins that it folds to null on the wire, never the literal spaces. ──
        await AdBriefWireFixtures.InsertPackBriefAsync(
            database.StationConnectionString, "another-pack-slug", CoexistingBrand);
        var coexistingSponsorCreate = await client.PostAsJsonAsync(
            "/api/sponsors", new { name = CoexistingBrand });
        if (coexistingSponsorCreate.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"arrange: POST /api/sponsors for the coexisting sponsor unexpectedly returned " +
                $"{coexistingSponsorCreate.StatusCode}");
        }
        var coexistingSponsor = await JsonDocument.ParseAsync(await coexistingSponsorCreate.Content.ReadAsStreamAsync());
        var coexistingSponsorId = coexistingSponsor.RootElement.GetProperty("id").GetInt64();

        var coexistingResponse = await client.PostAsJsonAsync(
            "/api/ad-briefs", new { sponsorId = coexistingSponsorId, premise = "   " });
        CoexistingOwnerPostStatus = coexistingResponse.StatusCode;

        // The status itself IS the fact ABrandThatOnlyHasAPackBriefCoexists pins, so — unlike the pure
        // arrange steps above — a non-201 here is not thrown as an arrange failure. The body is only
        // ever a created row's own shape on 201; on any other status it's a ProblemDetails with no
        // `enabled`/`premise` fields, so reading those unconditionally would crash InitializeAsync and
        // take the whole collection's facts down with it. AnOmittedEnabledDefaultsToTrue/
        // AWhitespaceOnlyPremiseFoldsToNull simply keep reading these two fields' own declared
        // defaults (false/null) when the create did not, in fact, succeed.
        if (coexistingResponse.StatusCode == HttpStatusCode.Created)
        {
            var coexistingBody = await JsonDocument.ParseAsync(await coexistingResponse.Content.ReadAsStreamAsync());
            CoexistingOwnerEnabled = coexistingBody.RootElement.GetProperty("enabled").GetBoolean();
            CoexistingOwnerPremise = coexistingBody.RootElement.GetProperty("premise").ValueKind == JsonValueKind.Null
                ? null : coexistingBody.RootElement.GetProperty("premise").GetString();
        }

        // ── A missing sponsorId is refused — there is no free-text brand to blank anymore (SPEC
        // F171.6, PLAN T435). ──
        var missingSponsorIdResponse = await client.PostAsJsonAsync(
            "/api/ad-briefs", new { sponsorId = (long?)null });
        MissingSponsorIdPostStatus = missingSponsorIdResponse.StatusCode;

        // ── PATCH disables the owner brief just created. ──
        var patchOwnerResponse = await client.PatchAsync(
            $"/api/ad-briefs/{cleanOwnerId}", JsonContent.Create(new { enabled = false }));
        PatchOwnerStatus = patchOwnerResponse.StatusCode;
        var patchOwnerBody = await JsonDocument.ParseAsync(await patchOwnerResponse.Content.ReadAsStreamAsync());
        PatchOwnerResultEnabled = patchOwnerBody.RootElement.GetProperty("enabled").GetBoolean();
        PatchOwnerResultSponsorName = patchOwnerBody.RootElement.GetProperty("sponsor").GetProperty("name").GetString() ?? "";

        // ── PATCH enables the SEEDED pack brief. ──
        var patchPackResponse = await client.PatchAsync(
            $"/api/ad-briefs/{packId}", JsonContent.Create(new { enabled = true }));
        PatchPackStatus = patchPackResponse.StatusCode;
        var patchPackBody = await JsonDocument.ParseAsync(await patchPackResponse.Content.ReadAsStreamAsync());
        PatchPackResultEnabled = patchPackBody.RootElement.GetProperty("enabled").GetBoolean();
        PatchPackResultPackSlug = patchPackBody.RootElement.GetProperty("packSlug").GetString();

        // ── PATCH with no `enabled` field is a 400, naming the field and carrying no `type` (a null
        // ProblemDetails.Type is omitted by the converter — RequiredFieldProblem's own remarks). ──
        var patchMissingResponse = await client.PatchAsync(
            $"/api/ad-briefs/{cleanOwnerId}", JsonContent.Create(new { }));
        PatchMissingEnabledStatus = patchMissingResponse.StatusCode;
        var patchMissingBody = await JsonDocument.ParseAsync(await patchMissingResponse.Content.ReadAsStreamAsync());
        PatchMissingEnabledField = patchMissingBody.RootElement.TryGetProperty("field", out var patchMissingFieldProperty)
            ? patchMissingFieldProperty.GetString() : null;
        PatchMissingEnabledHasType = patchMissingBody.RootElement.TryGetProperty("type", out _);

        // ── PATCH against an unknown id is a 404. ──
        var patchUnknownResponse = await client.PatchAsync(
            "/api/ad-briefs/999999", JsonContent.Create(new { enabled = true }));
        PatchUnknownIdStatus = patchUnknownResponse.StatusCode;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Test harness — WebApplicationFactory + ephemeral Postgres subclasses (the Story374/Story382/
// Story392(T403) "`file`-scoped types cannot cross files" precedent — this file supplies its own). ──

/// <summary>
/// Boots the real production composition root against a real ephemeral Postgres with every hosted
/// service removed — no background reach into <c>station.ad_brief</c>, so this arc's own seeded/posted
/// rows are never raced by a background tick. Every <c>AdBriefsController</c> endpoint is still
/// reachable — only the BACKGROUND loops are removed, the same <c>Story392AdsWebFactory</c> idiom one
/// controller over.
/// </summary>
file sealed class Story392AdBriefsWebFactory(Story392AdBriefsDatabase db) : WebApplicationFactory<Program>
{
    public const string Password = "test-password-t403b-briefs-api";

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
/// STORY-392's own DB-less factory — a bogus <c>ConnectionStrings:*</c> (never actually reached:
/// <c>Admin:Enabled=false</c> 404s in <c>SurfaceGateMiddleware</c>, BEFORE routing ever reaches
/// <c>AdBriefsController</c>'s constructor) — no real ephemeral Postgres needed just to prove a 404
/// (the <c>Story374.GardenerSurfaceWebFactory</c>/<c>Story166.KillSwitchWebFactory</c>/
/// <c>Story392(T403).AdsAdminOffWebFactory</c> precedent).
/// </summary>
file sealed class AdBriefsAdminOffWebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Admin:Enabled", "false");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-t403b-briefs-admin-off");
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
/// This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/> harness — see
/// that type's own remarks for the full "which compose file, why a unique project name + OS-assigned
/// port" rationale. Supplies only the <c>"genwave-t403b"</c> compose project-name prefix this file's
/// own arc needs.
/// </summary>
file sealed class Story392AdBriefsDatabase : EphemeralStationDatabase
{
    Story392AdBriefsDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story392AdBriefsDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t403b");
        var db = new Story392AdBriefsDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>Arrange helpers this file's own arc uses — raw SQL against the ephemeral database's own
/// connection string, never through <c>AdBriefRepository</c> (the <c>GardenerRotFixtures</c>/
/// <c>AdsWireFixtures</c> precedent): no pack-install endpoint exists yet, so a pack row is seeded
/// independently of the API under test. Inserts a PACK-owned <c>station.sponsor</c> row (SPEC F171.6,
/// PLAN T431/T432) alongside the brief, keyed by (<paramref name="packSlug"/>, folded
/// <paramref name="brand"/>) — the brief's own <c>sponsor_id</c> then points at that row, never a
/// free-text brand column (dropped, db/46).</summary>
public static class AdBriefWireFixtures
{
    public static async Task<long> InsertPackBriefAsync(string stationConnectionString, string packSlug, string brand)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            with sponsor as (
              insert into station.sponsor (name, pack_slug) values (@brand, @packSlug)
              on conflict on constraint sponsor_pack_slug_name_key do update set name = excluded.name
              returning id
            )
            insert into station.ad_brief (pack_slug, sponsor_id, premise, tone, structure, enabled)
            select @packSlug, sponsor.id, 'Seeded pack premise', 'dry', null, false
            from sponsor
            returning id
            """;
        cmd.Parameters.AddWithValue("packSlug", packSlug);
        cmd.Parameters.AddWithValue("brand", brand);
        return (long)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("insert returned no id"));
    }
}

// STORY-408 — Create and edit a sponsor (SPEC F171.3 · PLAN T434)
//
// BDD specification — xUnit through the deployed entry point (WebApplicationFactory<Program> against
// a real ephemeral Postgres — the Story392_AdBriefsApi.cs arc idiom): every fact drives
// POST/PATCH /api/sponsors* over HTTP with an authed admin session, never ISponsorStore/
// SponsorsController directly. One arc (Story408Arc) arranges everything every Scenario below reads;
// the pack-owned sponsor is seeded directly via raw SQL (the AdBriefWireFixtures precedent): no
// pack-install endpoint exists yet, so a pack row can only ever be arranged independently of the API
// under test.

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

public static class FeatureCreateAndEditASponsor
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    [Collection(Story408Collection.Name)]
    public sealed class ScenarioCreateWithJustAName(Story408Arc arc)
    {
        [Fact]
        public void PostIs201()
            => Assert.Equal(HttpStatusCode.Created, arc.CreateStatus);

        [Fact]
        public void EveryFactIsNullExceptName()
        {
            Assert.Equal("Halvorsen Ice Rink", arc.CreatedName);
            Assert.Null(arc.CreatedTagline);
            Assert.Null(arc.CreatedAbout);
            Assert.Null(arc.CreatedPhone);
            Assert.Null(arc.CreatedAddress);
            Assert.Null(arc.CreatedWebsite);
            Assert.Null(arc.CreatedTone);
            Assert.Null(arc.CreatedPackSlug);
            Assert.False(arc.CreatedPaused);
        }
    }

    [Collection(Story408Collection.Name)]
    public sealed class ScenarioEditAnyFactIncludingTheName(Story408Arc arc)
    {
        [Fact]
        public void PatchWithIfMatchIs200()
            => Assert.Equal(HttpStatusCode.OK, arc.PatchStatus);

        [Fact]
        public void TheTwoFieldsAreSet()
        {
            Assert.Equal("Open at six", arc.PatchedTagline);
            Assert.Equal("(406) 222-0100", arc.PatchedPhone);
        }
    }

    [Collection(Story408Collection.Name)]
    public sealed class ScenarioAPackOwnedSponsorsFactsStayEditable(Story408Arc arc)
    {
        [Fact]
        public void PatchingTaglineOnAPackOwnedSponsorIs200()
        {
            Assert.Equal(HttpStatusCode.OK, arc.PackOwnedTaglinePatchStatus);
            Assert.Equal("A rink with real details", arc.PackOwnedTaglineAfterPatch);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    [Collection(Story408Collection.Name)]
    public sealed class ScenarioRejectingInvalidInput(Story408Arc arc)
    {
        [Fact]
        public void PackSlugInARequestIs400SponsorPackSlugForbidden()
        {
            Assert.Equal(HttpStatusCode.BadRequest, arc.PackSlugInRequestStatus);
            Assert.Equal("sponsor_pack_slug_forbidden", arc.PackSlugInRequestType);
        }

        [Fact]
        public void RenamingAPackOwnedSponsorIs400SponsorNamePackOwned()
        {
            Assert.Equal(HttpStatusCode.BadRequest, arc.RenamePackOwnedStatus);
            Assert.Equal("sponsor_name_pack_owned", arc.RenamePackOwnedType);
        }

        [Fact]
        public void ANonUrlWebsiteIs400NamingTheField()
        {
            Assert.Equal(HttpStatusCode.BadRequest, arc.BadWebsiteStatus);
            Assert.Equal("website", arc.BadWebsiteField);
        }

        // ── T434 round-2 review finding F2: the six error branches every prior fact left unpinned. ──

        [Fact]
        public void CreatingADuplicateNameIs409SponsorNameTaken()
        {
            Assert.Equal(HttpStatusCode.Conflict, arc.DuplicateNameStatus);
            Assert.Equal("sponsor_name_taken", arc.DuplicateNameType);
        }

        [Fact]
        public void PatchingWithoutIfMatchIs428()
            => Assert.Equal(HttpStatusCode.PreconditionRequired, arc.MissingIfMatchStatus);

        [Fact]
        public void PatchingWithAMalformedIfMatchIs400()
            => Assert.Equal(HttpStatusCode.BadRequest, arc.MalformedIfMatchStatus);

        [Fact]
        public void PatchingWithAStaleIfMatchIs409SponsorVersionConflict()
        {
            Assert.Equal(HttpStatusCode.Conflict, arc.StaleIfMatchStatus);
            Assert.Equal("sponsor_version_conflict", arc.StaleIfMatchType);
        }

        [Fact]
        public void RenamingToAnExistingNameIs409SponsorNameTaken()
        {
            Assert.Equal(HttpStatusCode.Conflict, arc.RenameCollisionStatus);
            Assert.Equal("sponsor_name_taken", arc.RenameCollisionType);
        }

        [Fact]
        public void PatchingAnUnknownIdIs404()
            => Assert.Equal(HttpStatusCode.NotFound, arc.UnknownIdPatchStatus);
    }
}

// ── Collection definition — one ephemeral Postgres/factory shared by every Scenario above (the
// Story392_AdBriefsApi.cs "arrange once, many read-only Scenarios" idiom). ──

[CollectionDefinition(Name)]
public sealed class Story408Collection : ICollectionFixture<Story408Arc>
{
    public const string Name = "Story408SponsorCreateAndEdit";
}

/// <summary>
/// Arranges every fact STORY-408's Scenarios read, entirely over the REAL production HTTP pipeline
/// with a real admin session — no <c>SponsorRepository</c> call, no <c>SponsorsController</c> call,
/// anywhere in this class (except the raw-SQL pack-sponsor seed, which has no API of its own yet).
/// </summary>
public sealed class Story408Arc : IAsyncLifetime
{
    public HttpStatusCode CreateStatus { get; private set; }
    public string CreatedName { get; private set; } = "";
    public string? CreatedTagline { get; private set; }
    public string? CreatedAbout { get; private set; }
    public string? CreatedPhone { get; private set; }
    public string? CreatedAddress { get; private set; }
    public string? CreatedWebsite { get; private set; }
    public string? CreatedTone { get; private set; }
    public string? CreatedPackSlug { get; private set; }
    public bool CreatedPaused { get; private set; }

    public HttpStatusCode PatchStatus { get; private set; }
    public string? PatchedTagline { get; private set; }
    public string? PatchedPhone { get; private set; }

    public HttpStatusCode PackOwnedTaglinePatchStatus { get; private set; }
    public string? PackOwnedTaglineAfterPatch { get; private set; }

    public HttpStatusCode PackSlugInRequestStatus { get; private set; }
    public string? PackSlugInRequestType { get; private set; }

    public HttpStatusCode RenamePackOwnedStatus { get; private set; }
    public string? RenamePackOwnedType { get; private set; }

    public HttpStatusCode BadWebsiteStatus { get; private set; }
    public string? BadWebsiteField { get; private set; }

    // ── T434 round-2 review finding F2. ──
    public HttpStatusCode DuplicateNameStatus { get; private set; }
    public string? DuplicateNameType { get; private set; }

    public HttpStatusCode MissingIfMatchStatus { get; private set; }
    public HttpStatusCode MalformedIfMatchStatus { get; private set; }

    public HttpStatusCode StaleIfMatchStatus { get; private set; }
    public string? StaleIfMatchType { get; private set; }

    public HttpStatusCode RenameCollisionStatus { get; private set; }
    public string? RenameCollisionType { get; private set; }

    public HttpStatusCode UnknownIdPatchStatus { get; private set; }

    public async Task InitializeAsync()
    {
        // A LOCAL, not a field — Story408Database is file-local (CS9051), the Story392AdBriefsDatabase
        // precedent.
        await using var database = await Story408Database.StartAsync();

        var packSponsorId = await Story408WireFixtures.InsertPackSponsorAsync(
            database.StationConnectionString, "test-pack", "Al's Rink Supply");

        await using var factory = new Story408WebFactory(database);
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new { password = Story408WebFactory.Password });
        if (login.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login unexpectedly returned {login.StatusCode}");

        // ── AC1: create with just a name. ──
        var createResponse = await client.PostAsJsonAsync("/api/sponsors", new { name = "Halvorsen Ice Rink" });
        CreateStatus = createResponse.StatusCode;
        var created = await JsonDocument.ParseAsync(await createResponse.Content.ReadAsStreamAsync());
        var ownerId = created.RootElement.GetProperty("id").GetInt64();
        CreatedName = created.RootElement.GetProperty("name").GetString() ?? "";
        CreatedTagline = created.RootElement.GetProperty("tagline").GetString();
        CreatedAbout = created.RootElement.GetProperty("about").GetString();
        CreatedPhone = created.RootElement.GetProperty("phone").GetString();
        CreatedAddress = created.RootElement.GetProperty("address").GetString();
        CreatedWebsite = created.RootElement.GetProperty("website").GetString();
        CreatedTone = created.RootElement.GetProperty("tone").GetString();
        CreatedPackSlug = created.RootElement.GetProperty("packSlug").ValueKind == JsonValueKind.Null
            ? null : created.RootElement.GetProperty("packSlug").GetString();
        CreatedPaused = created.RootElement.GetProperty("paused").GetBoolean();
        var ownerETag = createResponse.Headers.ETag?.Tag
            ?? throw new InvalidOperationException("POST /api/sponsors returned no ETag");

        // ── AC2: edit any fact, including a second edit later — this call sets tagline + phone. ──
        var patchRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/sponsors/{ownerId}")
        {
            Content = JsonContent.Create(new { tagline = "Open at six", phone = "(406) 222-0100" }),
        };
        patchRequest.Headers.TryAddWithoutValidation("If-Match", ownerETag);
        var patchResponse = await client.SendAsync(patchRequest);
        PatchStatus = patchResponse.StatusCode;
        var patched = await JsonDocument.ParseAsync(await patchResponse.Content.ReadAsStreamAsync());
        PatchedTagline = patched.RootElement.GetProperty("tagline").GetString();
        PatchedPhone = patched.RootElement.GetProperty("phone").GetString();

        // ── AC4: a pack-owned sponsor's OTHER facts stay editable (tagline, not name). ──
        var packGetResponse = await client.GetAsync($"/api/sponsors/{packSponsorId}");
        var packETag = packGetResponse.Headers.ETag?.Tag
            ?? throw new InvalidOperationException("GET /api/sponsors/{id} returned no ETag");

        var packTaglineRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/sponsors/{packSponsorId}")
        {
            Content = JsonContent.Create(new { tagline = "A rink with real details" }),
        };
        packTaglineRequest.Headers.TryAddWithoutValidation("If-Match", packETag);
        var packTaglineResponse = await client.SendAsync(packTaglineRequest);
        PackOwnedTaglinePatchStatus = packTaglineResponse.StatusCode;
        var packTaglineBody = await JsonDocument.ParseAsync(await packTaglineResponse.Content.ReadAsStreamAsync());
        PackOwnedTaglineAfterPatch = packTaglineBody.RootElement.GetProperty("tagline").GetString();
        var packETagAfterTagline = packTaglineResponse.Headers.ETag?.Tag
            ?? throw new InvalidOperationException("PATCH /api/sponsors/{id} returned no ETag");

        // ── AC3: packSlug in a POST request is refused outright. ──
        var packSlugPostResponse = await client.PostAsJsonAsync(
            "/api/sponsors", new { name = "Al's Diner", packSlug = "brought-to-you-by" });
        PackSlugInRequestStatus = packSlugPostResponse.StatusCode;
        var packSlugPostBody = await JsonDocument.ParseAsync(await packSlugPostResponse.Content.ReadAsStreamAsync());
        PackSlugInRequestType = packSlugPostBody.RootElement.TryGetProperty("type", out var typeProperty)
            ? typeProperty.GetString() : null;

        // ── AC4 (the refusal half): renaming the pack-owned sponsor is refused. ──
        var renameRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/sponsors/{packSponsorId}")
        {
            Content = JsonContent.Create(new { name = "Renamed" }),
        };
        renameRequest.Headers.TryAddWithoutValidation("If-Match", packETagAfterTagline);
        var renameResponse = await client.SendAsync(renameRequest);
        RenamePackOwnedStatus = renameResponse.StatusCode;
        var renameBody = await JsonDocument.ParseAsync(await renameResponse.Content.ReadAsStreamAsync());
        RenamePackOwnedType = renameBody.RootElement.TryGetProperty("type", out var renameTypeProperty)
            ? renameTypeProperty.GetString() : null;

        // ── AC5: website shape enforced. ──
        var badWebsiteResponse = await client.PostAsJsonAsync(
            "/api/sponsors", new { name = "Rusty Nail", website = "not-a-url" });
        BadWebsiteStatus = badWebsiteResponse.StatusCode;
        var badWebsiteBody = await JsonDocument.ParseAsync(await badWebsiteResponse.Content.ReadAsStreamAsync());
        BadWebsiteField = badWebsiteBody.RootElement.TryGetProperty("field", out var fieldProperty)
            ? fieldProperty.GetString() : null;

        // ── T434 round-2 review finding F2: a folded-name collision on POST is refused. ──
        var duplicateNameResponse = await client.PostAsJsonAsync("/api/sponsors", new { name = "Halvorsen Ice Rink" });
        DuplicateNameStatus = duplicateNameResponse.StatusCode;
        var duplicateNameBody = await JsonDocument.ParseAsync(await duplicateNameResponse.Content.ReadAsStreamAsync());
        DuplicateNameType = duplicateNameBody.RootElement.TryGetProperty("type", out var duplicateNameTypeProperty)
            ? duplicateNameTypeProperty.GetString() : null;

        // ── PATCH with no If-Match header at all is refused (428) — no ETag to strip/parse yet. ──
        var missingIfMatchResponse = await client.PatchAsJsonAsync(
            $"/api/sponsors/{ownerId}", new { tagline = "No If-Match" });
        MissingIfMatchStatus = missingIfMatchResponse.StatusCode;

        // ── PATCH with a well-formed-but-not-a-weak-etag If-Match ("abc" is not a uint) is refused (400)
        // BEFORE any store call. ──
        var malformedIfMatchRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/sponsors/{ownerId}")
        {
            Content = JsonContent.Create(new { tagline = "Malformed If-Match" }),
        };
        malformedIfMatchRequest.Headers.TryAddWithoutValidation("If-Match", "W/\"abc\"");
        var malformedIfMatchResponse = await client.SendAsync(malformedIfMatchRequest);
        MalformedIfMatchStatus = malformedIfMatchResponse.StatusCode;

        // ── Re-PATCH with ownerETag — the FIRST ETag, from the original POST — now stale since AC2's
        // own PATCH above already moved the row's version forward. ──
        var staleIfMatchRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/sponsors/{ownerId}")
        {
            Content = JsonContent.Create(new { tagline = "Stale If-Match" }),
        };
        staleIfMatchRequest.Headers.TryAddWithoutValidation("If-Match", ownerETag);
        var staleIfMatchResponse = await client.SendAsync(staleIfMatchRequest);
        StaleIfMatchStatus = staleIfMatchResponse.StatusCode;
        var staleIfMatchBody = await JsonDocument.ParseAsync(await staleIfMatchResponse.Content.ReadAsStreamAsync());
        StaleIfMatchType = staleIfMatchBody.RootElement.TryGetProperty("type", out var staleIfMatchTypeProperty)
            ? staleIfMatchTypeProperty.GetString() : null;

        // ── A second, distinct owner sponsor — purely so the rename-collision fact below has a real
        // name already taken to collide with. ──
        var secondOwnerCreateResponse = await client.PostAsJsonAsync("/api/sponsors", new { name = "Sunridge Hardware" });
        var secondOwnerCreated = await JsonDocument.ParseAsync(await secondOwnerCreateResponse.Content.ReadAsStreamAsync());
        var secondOwnerName = secondOwnerCreated.RootElement.GetProperty("name").GetString() ?? "";

        // ── Renaming ownerId to the second sponsor's own name collides (409) — the failed stale-If-
        // Match attempt above never advanced ownerId's version, so patchResponse's own ETag (from AC2)
        // is still the current one. ──
        var ownerETagAfterPatch = patchResponse.Headers.ETag?.Tag
            ?? throw new InvalidOperationException("PATCH /api/sponsors/{id} returned no ETag");
        var renameCollisionRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/sponsors/{ownerId}")
        {
            Content = JsonContent.Create(new { name = secondOwnerName }),
        };
        renameCollisionRequest.Headers.TryAddWithoutValidation("If-Match", ownerETagAfterPatch);
        var renameCollisionResponse = await client.SendAsync(renameCollisionRequest);
        RenameCollisionStatus = renameCollisionResponse.StatusCode;
        var renameCollisionBody = await JsonDocument.ParseAsync(await renameCollisionResponse.Content.ReadAsStreamAsync());
        RenameCollisionType = renameCollisionBody.RootElement.TryGetProperty("type", out var renameCollisionTypeProperty)
            ? renameCollisionTypeProperty.GetString() : null;

        // ── PATCH on an unknown id, with an otherwise well-formed If-Match, is 404 — existence is
        // checked before the version compare (SponsorRepository.UpdateAsync's own remarks). ──
        var unknownIdRequest = new HttpRequestMessage(HttpMethod.Patch, "/api/sponsors/999999")
        {
            Content = JsonContent.Create(new { tagline = "Ghost" }),
        };
        unknownIdRequest.Headers.TryAddWithoutValidation("If-Match", ownerETagAfterPatch);
        var unknownIdResponse = await client.SendAsync(unknownIdRequest);
        UnknownIdPatchStatus = unknownIdResponse.StatusCode;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Test harness — WebApplicationFactory + ephemeral Postgres subclasses (the Story392_AdBriefsApi.cs
// "`file`-scoped types cannot cross files" precedent — this file supplies its own). ──

file sealed class Story408WebFactory(Story408Database db) : WebApplicationFactory<Program>
{
    public const string Password = "test-password-t434b-sponsors-create-edit";

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

/// <summary>This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/> harness
/// — see that type's own remarks. Supplies only the <c>"genwave-t434b"</c> compose project-name prefix
/// this file's own arc needs.</summary>
file sealed class Story408Database : EphemeralStationDatabase
{
    Story408Database(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story408Database> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t434b");
        var db = new Story408Database(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>Arrange helpers this file's own arc uses — raw SQL against the ephemeral database's own
/// connection string, never through <c>SponsorRepository</c> (the <c>AdBriefWireFixtures</c>
/// precedent): no pack-install endpoint exists yet, so a pack-owned sponsor is seeded independently of
/// the API under test.</summary>
public static class Story408WireFixtures
{
    public static async Task<long> InsertPackSponsorAsync(string stationConnectionString, string packSlug, string name)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "insert into station.sponsor (name, pack_slug) values (@name, @packSlug) returning id";
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("packSlug", packSlug);
        return (long)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("insert returned no id"));
    }
}

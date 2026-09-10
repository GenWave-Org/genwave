// STORY-411 — A brief belongs to exactly one sponsor (SPEC F171.6 · PLAN T435)
//
// BDD specification — xUnit through the deployed entry point (WebApplicationFactory<Program> against
// a real ephemeral Postgres — the Story392_AdBriefsApi.cs/Story407_SponsorListAndDetail.cs arc idiom):
// every fact drives POST /api/ad-briefs (and POST /api/sponsors to arrange the one sponsor it needs)
// over HTTP with an authed admin session, never AdBriefRepository/AdBriefsController/SponsorRepository/
// SponsorsController directly. One arc (Story411Arc) arranges everything every HAPPY-PATH/sad-path
// Scenario below reads (the SAME "arrange once, many read-only Scenarios" idiom one controller over).

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

public static class FeatureABriefBelongsToExactlyOneSponsor
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    [Collection(Story411Collection.Name)]
    public sealed class ScenarioTheBriefCarriesTheSponsorObject(Story411Arc arc)
    {
        [Fact]
        public void PostWithASponsorIs201()
            => Assert.Equal(HttpStatusCode.Created, arc.FirstCreateStatus);

        [Fact]
        public void TheResponseCarriesSponsorIdNamePaused()
        {
            Assert.Equal(arc.SponsorId, arc.ResponseSponsorId);
            Assert.Equal(Story411Arc.SponsorName, arc.ResponseSponsorName);
            Assert.False(arc.ResponseSponsorPaused);
        }

        [Fact]
        public void TheResponseHasNoBrandField()
            => Assert.False(arc.ResponseHasBrandProperty);

        // ── NEW fact (not part of the pending stubs above — never rename an existing one): the
        // unpaused half above (TheResponseCarriesSponsorIdNamePaused) only ever exercises an unpaused
        // sponsor, so Assert.False there is a tautology a hardcoded `paused: false` projection would
        // never fail. This fact drives the SAME sponsor.paused wire field for a sponsor the Arc has
        // actually paused through the real POST /api/sponsors/{id}/pause verb (PLAN T434), so a
        // hardcoded false in the controller's SponsorRefDto projection turns THIS fact red. ──

        [Fact]
        public void ABriefUnderAPausedSponsorReportsPausedTrue()
            => Assert.True(arc.PausedSponsorBriefPaused);

        // ── NEW fact (review MED-1): the fact above pins sponsor.paused==true on the CREATE response;
        // AdBriefsController.SetEnabled builds its own sponsor DTO independently (a SEPARATE GetAsync
        // call, PLAN T435's own PATCH remarks) — a hardcoded `paused: false` there would slip past every
        // OTHER fact in this suite (Story392's own PATCH fact pins only `name`, since its owner sponsor
        // is never paused). This fact PATCHes that SAME paused sponsor's brief and reads the PATCH
        // response's own sponsor.paused instead. ──
        [Fact]
        public void PatchOnAPausedSponsorsBriefStillReportsPausedTrue()
            => Assert.True(arc.PausedSponsorBriefPatchSponsorPaused);
    }

    [Collection(Story411Collection.Name)]
    public sealed class ScenarioADifferentAngleUnderTheSameSponsorIsAllowed(Story411Arc arc)
    {
        [Fact]
        public void ASecondAngleIs201()
            => Assert.Equal(HttpStatusCode.Created, arc.SecondAngleStatus);
    }

    [Collection(Story411Collection.Name)]
    public sealed class ScenarioTheBrandColumnIsGone(Story411Arc arc)
    {
        [Fact]
        public void AdBriefHasNoBrandColumn()
            => Assert.False(arc.AdBriefHasBrandColumn);

        [Fact]
        public void SponsorIdIsNotNull()
            => Assert.Equal("NO", arc.AdBriefSponsorIdIsNullable);
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    [Collection(Story411Collection.Name)]
    public sealed class ScenarioRejectingInvalidInput(Story411Arc arc)
    {
        [Fact]
        public void MissingSponsorIdIs400SponsorRequired()
        {
            Assert.Equal(HttpStatusCode.BadRequest, arc.MissingSponsorIdStatus);
            Assert.Equal("sponsor_required", arc.MissingSponsorIdType);
            // review LOW-1: the SAME claim as the type check above ("the 400 names sponsorId"), pinned
            // on the `field` extension too — a mutation that names some other field (e.g. the old
            // free-text "brand") in this 400's body would otherwise survive unnoticed.
            Assert.Equal("sponsorId", arc.MissingSponsorIdField);
        }

        [Fact]
        public void TheSameAngleTwiceIs409DuplicateBrief()
        {
            Assert.Equal(HttpStatusCode.Conflict, arc.DuplicateStatus);
            Assert.Equal("duplicate_brief", arc.DuplicateType);
        }

        // ── NEW facts (not part of the pending stubs above — never rename an existing one) ──

        [Fact]
        public void UnknownSponsorIdIs404SponsorNotFound()
        {
            Assert.Equal(HttpStatusCode.NotFound, arc.UnknownSponsorIdStatus);
            Assert.Equal("sponsor_not_found", arc.UnknownSponsorIdType);
        }

        [Fact]
        public void AnonymousPostIs401()
            => Assert.Equal(HttpStatusCode.Unauthorized, arc.AnonymousPostStatus);
    }
}

// ── Collection definition — one ephemeral Postgres/factory shared by every happy-path/sad-path
// Scenario above (the Story392_AdBriefsApi.cs/Story407_SponsorListAndDetail.cs "arrange once, many
// read-only Scenarios" idiom). ──

[CollectionDefinition(Name)]
public sealed class Story411Collection : ICollectionFixture<Story411Arc>
{
    public const string Name = "Story411BriefBelongsToSponsor";
}

/// <summary>
/// Arranges every fact STORY-411's Scenarios read, entirely over the REAL production HTTP pipeline with
/// a real admin session — no <c>AdBriefRepository</c>/<c>AdBriefsController</c>/<c>SponsorRepository</c>/
/// <c>SponsorsController</c> call anywhere in this class. The one sponsor every brief-create call needs
/// is created through the real <c>POST /api/sponsors</c> surface (PLAN T434), never seeded by SQL — this
/// story is about the FK between a brief and a real sponsor row, so the sponsor itself must be real too.
/// </summary>
public sealed class Story411Arc : IAsyncLifetime
{
    public const string SponsorName = "Cravin's Diner";
    public const string FirstPremise = "Open at six";

    // A real space between words, mixed case — the SAME station.sponsor_fold collapse-whitespace-
    // then-lowercase behaviour Story407Arc's own folded-filter fact already proves one seam over; this
    // folds to the SAME premise_key as FirstPremise ("open at six").
    public const string DuplicatePremiseVariant = "  Open  at  Six  ";

    public const string SecondPremise = "Weekend special";

    // A SECOND owner sponsor, paused through the real T434 verb — the ONLY way
    // ABriefUnderAPausedSponsorReportsPausedTrue's own claim can be a fact rather than a tautology (see
    // that fact's own remarks).
    public const string PausedSponsorName = "Sleepy Diner";
    public const string PausedSponsorPremise = "Closed for a nap";

    public long SponsorId { get; private set; }

    public HttpStatusCode MissingSponsorIdStatus { get; private set; }
    public string? MissingSponsorIdType { get; private set; }
    public string? MissingSponsorIdField { get; private set; }

    public HttpStatusCode FirstCreateStatus { get; private set; }
    public long ResponseSponsorId { get; private set; }
    public string ResponseSponsorName { get; private set; } = "";
    public bool ResponseSponsorPaused { get; private set; }
    public bool ResponseHasBrandProperty { get; private set; }

    public HttpStatusCode DuplicateStatus { get; private set; }
    public string? DuplicateType { get; private set; }

    public HttpStatusCode SecondAngleStatus { get; private set; }

    public HttpStatusCode UnknownSponsorIdStatus { get; private set; }
    public string? UnknownSponsorIdType { get; private set; }

    public HttpStatusCode AnonymousPostStatus { get; private set; }

    public bool AdBriefHasBrandColumn { get; private set; }
    public string AdBriefSponsorIdIsNullable { get; private set; } = "";

    public bool PausedSponsorBriefPaused { get; private set; }
    public bool PausedSponsorBriefPatchSponsorPaused { get; private set; }

    public async Task InitializeAsync()
    {
        // A LOCAL, not a field — Story411Database is file-local (CS9051), the Story392AdBriefsDatabase/
        // Story407Database precedent.
        await using var database = await Story411Database.StartAsync();
        await using var factory = new Story411WebFactory(database);
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new { password = Story411WebFactory.Password });
        if (login.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login unexpectedly returned {login.StatusCode}");

        // ── Arrange: one owner sponsor through the real T434 surface, not SQL. ──
        var sponsorCreate = await client.PostAsJsonAsync("/api/sponsors", new { name = SponsorName });
        var sponsorBody = await JsonDocument.ParseAsync(await sponsorCreate.Content.ReadAsStreamAsync());
        SponsorId = sponsorBody.RootElement.GetProperty("id").GetInt64();

        // ── AC1 — sponsorId is required on create. ──
        var missingResponse = await client.PostAsJsonAsync(
            "/api/ad-briefs", new { sponsorId = (long?)null, premise = "Nothing to see here" });
        MissingSponsorIdStatus = missingResponse.StatusCode;
        var missingBody = await JsonDocument.ParseAsync(await missingResponse.Content.ReadAsStreamAsync());
        MissingSponsorIdType = missingBody.RootElement.TryGetProperty("type", out var missingType)
            ? missingType.GetString() : null;
        MissingSponsorIdField = missingBody.RootElement.TryGetProperty("field", out var missingField)
            ? missingField.GetString() : null;

        // ── AC2 — the brief carries the sponsor object in the response, no top-level brand. ──
        var firstCreateResponse = await client.PostAsJsonAsync(
            "/api/ad-briefs", new { sponsorId = SponsorId, premise = FirstPremise });
        FirstCreateStatus = firstCreateResponse.StatusCode;
        var firstCreateBody = await JsonDocument.ParseAsync(await firstCreateResponse.Content.ReadAsStreamAsync());
        var sponsorElement = firstCreateBody.RootElement.GetProperty("sponsor");
        ResponseSponsorId = sponsorElement.GetProperty("id").GetInt64();
        ResponseSponsorName = sponsorElement.GetProperty("name").GetString() ?? "";
        ResponseSponsorPaused = sponsorElement.GetProperty("paused").GetBoolean();
        ResponseHasBrandProperty = firstCreateBody.RootElement.TryGetProperty("brand", out _);

        // ── AC3 — the same angle under the same sponsor twice is a conflict. ──
        var duplicateResponse = await client.PostAsJsonAsync(
            "/api/ad-briefs", new { sponsorId = SponsorId, premise = DuplicatePremiseVariant });
        DuplicateStatus = duplicateResponse.StatusCode;
        var duplicateBody = await JsonDocument.ParseAsync(await duplicateResponse.Content.ReadAsStreamAsync());
        DuplicateType = duplicateBody.RootElement.TryGetProperty("type", out var duplicateType)
            ? duplicateType.GetString() : null;

        // ── AC4 — a different angle under the same sponsor is allowed. ──
        var secondAngleResponse = await client.PostAsJsonAsync(
            "/api/ad-briefs", new { sponsorId = SponsorId, premise = SecondPremise });
        SecondAngleStatus = secondAngleResponse.StatusCode;

        // ── NEW: a SECOND owner sponsor, paused through the real POST /api/sponsors/{id}/pause verb
        // (PLAN T434) — a paused sponsor still accepts a new brief (pausing withholds a sponsor's spots
        // from air, it does not lock its briefs from editing), and that brief's own response.sponsor.paused
        // is what ABriefUnderAPausedSponsorReportsPausedTrue pins. Every arrange step here is pure
        // arrange, not a fact under test — an unexpected status throws loudly rather than surfacing as a
        // Fact failure (the login-check precedent a few lines up). ──
        var pausedSponsorCreate = await client.PostAsJsonAsync("/api/sponsors", new { name = PausedSponsorName });
        if (pausedSponsorCreate.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"arrange: POST /api/sponsors for the paused sponsor unexpectedly returned {pausedSponsorCreate.StatusCode}");
        }
        var pausedSponsorBody = await JsonDocument.ParseAsync(await pausedSponsorCreate.Content.ReadAsStreamAsync());
        var pausedSponsorId = pausedSponsorBody.RootElement.GetProperty("id").GetInt64();

        var pauseResponse = await client.PostAsync($"/api/sponsors/{pausedSponsorId}/pause", content: null);
        if (pauseResponse.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"arrange: POST /api/sponsors/{pausedSponsorId}/pause unexpectedly returned {pauseResponse.StatusCode}");
        }

        var pausedSponsorBriefResponse = await client.PostAsJsonAsync(
            "/api/ad-briefs", new { sponsorId = pausedSponsorId, premise = PausedSponsorPremise });
        if (pausedSponsorBriefResponse.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"arrange: POST /api/ad-briefs for the paused sponsor unexpectedly returned {pausedSponsorBriefResponse.StatusCode}");
        }
        var pausedSponsorBriefBody = await JsonDocument.ParseAsync(await pausedSponsorBriefResponse.Content.ReadAsStreamAsync());
        PausedSponsorBriefPaused = pausedSponsorBriefBody.RootElement.GetProperty("sponsor").GetProperty("paused").GetBoolean();
        var pausedSponsorBriefId = pausedSponsorBriefBody.RootElement.GetProperty("id").GetInt64();

        // ── NEW (review MED-1): PATCHing that SAME paused sponsor's brief still reports
        // sponsor.paused==true on the PATCH response body, not just the CREATE response —
        // AdBriefsController.SetEnabled builds its own sponsor DTO via a separate GetAsync call. Pure
        // arrange, not the fact under test itself — an unexpected status throws loudly (the same
        // precedent a few lines up). ──
        var pausedSponsorBriefPatchResponse = await client.PatchAsync(
            $"/api/ad-briefs/{pausedSponsorBriefId}", JsonContent.Create(new { enabled = false }));
        if (pausedSponsorBriefPatchResponse.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"arrange: PATCH /api/ad-briefs/{pausedSponsorBriefId} unexpectedly returned " +
                $"{pausedSponsorBriefPatchResponse.StatusCode}");
        }
        var pausedSponsorBriefPatchBody = await JsonDocument.ParseAsync(
            await pausedSponsorBriefPatchResponse.Content.ReadAsStreamAsync());
        PausedSponsorBriefPatchSponsorPaused =
            pausedSponsorBriefPatchBody.RootElement.GetProperty("sponsor").GetProperty("paused").GetBoolean();

        // ── NEW: an unknown sponsorId is refused as 404, checked BEFORE any insert is attempted. ──
        var unknownSponsorResponse = await client.PostAsJsonAsync(
            "/api/ad-briefs", new { sponsorId = 999_999L, premise = "A brief for nobody" });
        UnknownSponsorIdStatus = unknownSponsorResponse.StatusCode;
        var unknownSponsorBody = await JsonDocument.ParseAsync(await unknownSponsorResponse.Content.ReadAsStreamAsync());
        UnknownSponsorIdType = unknownSponsorBody.RootElement.TryGetProperty("type", out var unknownType)
            ? unknownType.GetString() : null;

        // ── NEW: an anonymous POST is refused as 401 — a fresh, never-logged-in client off the SAME
        // factory (Admin:Enabled left at its true default, so 401 — not 404 — is what's under test, the
        // Story407AnonymousWebFactory precedent's own reasoning, here without needing a second,
        // DB-less factory since this factory's real database is already up). ──
        var anonymousClient = factory.CreateClient();
        var anonymousResponse = await anonymousClient.PostAsJsonAsync(
            "/api/ad-briefs", new { sponsorId = SponsorId, premise = "An anonymous attempt" });
        AnonymousPostStatus = anonymousResponse.StatusCode;

        // ── AC5 — the brand column is gone; sponsor_id is NOT NULL. ──
        var (brandExists, sponsorIdNullable) = await ReadAdBriefColumnFactsAsync(database.StationConnectionString);
        AdBriefHasBrandColumn = brandExists;
        AdBriefSponsorIdIsNullable = sponsorIdNullable;
    }

    static async Task<(bool BrandExists, string SponsorIdIsNullable)> ReadAdBriefColumnFactsAsync(
        string stationConnectionString)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            select column_name, is_nullable
            from information_schema.columns
            where table_schema = 'station' and table_name = 'ad_brief'
            """;
        await using var reader = await cmd.ExecuteReaderAsync();

        var brandExists = false;
        var sponsorIdIsNullable = "";
        while (await reader.ReadAsync())
        {
            var columnName = reader.GetString(0);
            if (columnName == "brand")
                brandExists = true;
            if (columnName == "sponsor_id")
                sponsorIdIsNullable = reader.GetString(1);
        }

        return (brandExists, sponsorIdIsNullable);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Test harness — WebApplicationFactory + ephemeral Postgres subclasses (the Story392_AdBriefsApi.cs/
// Story407_SponsorListAndDetail.cs "`file`-scoped types cannot cross files" precedent — this file
// supplies its own). ──

file sealed class Story411WebFactory(Story411Database db) : WebApplicationFactory<Program>
{
    public const string Password = "test-password-t435-brief-sponsor";

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
/// — see that type's own remarks. Supplies only the <c>"genwave-t435"</c> compose project-name prefix
/// this file's own arc needs.</summary>
file sealed class Story411Database : EphemeralStationDatabase
{
    Story411Database(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story411Database> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t435");
        var db = new Story411Database(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

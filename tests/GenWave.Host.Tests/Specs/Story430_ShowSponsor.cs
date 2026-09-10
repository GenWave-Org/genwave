// STORY-430 — A show can name a sponsor (SPEC F175.1–F175.3 · PLAN T449)
//
// BDD specification — xUnit through the deployed entry point (WebApplicationFactory<Program> against
// a real ephemeral Postgres — the Story410_SponsorDeleteGuard.cs arc idiom): every AC1–AC4/AC6 fact
// drives GET/POST/PATCH/DELETE /api/shows, /api/sponsors, /api/ad-packs over HTTP with an authed admin
// session, never IShowStore/ISponsorStore/ShowsController directly. ONE arc, ONE ephemeral Postgres
// (the Story410Arc precedent — "arrange once, many read-only Scenarios"): none of AC1–AC4/AC6's own
// steps mutate a row another step still needs, so splitting into several databases would buy nothing.
//
// AC5 (ScenarioNoOnAirChange) is the one Scenario with no arc at all: it calls
// GenWave.Orchestration.ShowIdentRequest.For and GenWave.Tts.PatterTemplateRenderer.Expand directly —
// the SAME two pure, no-I/O calls the Orchestrator's own StationId drain arm makes at render time — so
// it needs neither HTTP nor a database.
//
// A pack-owned sponsor (the AC6 ad-pack-uninstall-guard half) is seeded by raw SQL: no endpoint under
// this controller can set Sponsor.PackSlug directly (SponsorsController.Create refuses a caller-supplied
// packSlug — SPEC F171.1, sponsor_pack_slug_forbidden), and installing a real catalog pack is the
// AdPackUninstallGuardArc precedent's own heavier machinery (a routed FakeHttpMessageHandler over a
// catalog index) this file has no other reason to stand up. The SHOW that then references it goes
// through the real POST /api/shows route like every other show fixture in this file — ShowsController
// is under test here, unlike Story410's/Story416's own show fixtures, which predate this task's own
// sponsorId wiring and so seed shows by raw SQL too.

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
using GenWave.Core.Domain;
using GenWave.Host.Tests.Support;
using GenWave.Orchestration;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAShowCanNameASponsor
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    [Collection(Story430Collection.Name)]
    public sealed class ScenarioTheShowCarriesSponsorIdNullable(Story430Arc arc)
    {
        [Fact]
        public void StationShowHasANullableSponsorId()
        {
            Assert.Equal("bigint", arc.ShowSponsorIdDataType);
            Assert.Equal("YES", arc.ShowSponsorIdIsNullable);
        }

        [Fact]
        public void TheFkIsOnDeleteRestrict()
            // Postgres pg_constraint.confdeltype: 'r' = ON DELETE RESTRICT (db/46 step 14).
            => Assert.Equal("r", arc.ShowSponsorIdFkDeleteRule);
    }

    [Collection(Story430Collection.Name)]
    public sealed class ScenarioTheShowDtoExposesTheSponsorObject(Story430Arc arc)
    {
        [Fact]
        public void GetShowCarriesSponsorIdAndName()
        {
            Assert.Equal(HttpStatusCode.OK, arc.LinkedShowGetStatus);
            Assert.Equal(arc.SponsorId, arc.LinkedShowSponsorId);
            Assert.Equal(Story430Arc.SponsorName, arc.LinkedShowSponsorName);
        }

        [Fact]
        public void ListShowsCarriesSponsorIdAndName()
        {
            Assert.Equal(arc.SponsorId, arc.LinkedShowInListSponsorId);
            Assert.Equal(Story430Arc.SponsorName, arc.LinkedShowInListSponsorName);
        }

        [Fact]
        public void AnUnlinkedShowCarriesSponsorNull()
        {
            Assert.Equal(HttpStatusCode.OK, arc.UnlinkedShowGetStatus);
            Assert.True(arc.UnlinkedShowSponsorIsNull);
        }
    }

    [Collection(Story430Collection.Name)]
    public sealed class ScenarioPatchAcceptsSponsorIdNullClears(Story430Arc arc)
    {
        [Fact]
        public void PatchWithNullIs200()
            => Assert.Equal(HttpStatusCode.OK, arc.PatchClearStatus);

        [Fact]
        public void SponsorIsNullOnTheRow()
            => Assert.True(arc.PatchClearSponsorIsNull);
    }

    [Collection(Story430Collection.Name)]
    public sealed class ScenarioAPausedSponsorIsAccepted(Story430Arc arc)
    {
        [Fact]
        public void LinkingAPausedSponsorIs200WithPausedTrue()
        {
            Assert.Equal(HttpStatusCode.OK, arc.PausedSponsorPatchStatus);
            Assert.True(arc.PausedSponsorPatchSponsorPaused);
        }
    }

    public sealed class ScenarioNoOnAirChange
    {
        [Fact]
        public void AShowWithASponsorRendersTheSameIdentTextAsOneWithout()
        {
            var baseRequest = new SegmentRequest(
                Kind: SegmentKind.StationId,
                Voice: "af_heart",
                StationName: "GWAV 108.8",
                Track: null,
                LocalNow: DateTimeOffset.UtcNow,
                StationId: "genwave-1");

            // ShowSummary carries no sponsor member (Abstractions), so the ident request a sponsored
            // show produces is the same object an unsponsored one produces; this pins that For and the
            // renderer read only Name. Built as two SEPARATE, identically-constructed values (never one
            // reused twice) so the fact reads as "there is nothing on this type for a sponsor to move,"
            // not "the same object rendered against itself."
            var sponsoredSummary = new ShowSummary(1, "Cascade Morning Drive", Tagline: null, Flavor: null)
            {
                Slug = "cascade-morning-drive",
            };
            var unsponsoredSummary = new ShowSummary(1, "Cascade Morning Drive", Tagline: null, Flavor: null)
            {
                Slug = "cascade-morning-drive",
            };

            var renderer = new PatterTemplateRenderer();
            var sponsoredText = renderer.Expand(ShowIdentRequest.For(baseRequest, sponsoredSummary));
            var unsponsoredText = renderer.Expand(ShowIdentRequest.For(baseRequest, unsponsoredSummary));

            Assert.Equal(unsponsoredText, sponsoredText);
            Assert.Equal("You're listening to Cascade Morning Drive on GWAV 108.8.", sponsoredText);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    [Collection(Story430Collection.Name)]
    public sealed class ScenarioGuardsIncludeShows(Story430Arc arc)
    {
        [Fact]
        public void AnUnknownSponsorIdIs404SponsorNotFound()
        {
            Assert.Equal(HttpStatusCode.NotFound, arc.UnknownSponsorPatchStatus);
            Assert.Equal("sponsor_not_found", arc.UnknownSponsorPatchType);
            Assert.Equal("sponsorId", arc.UnknownSponsorPatchField);
        }

        [Fact]
        public void DeletingAShowsSponsorIs409SponsorInUseListingTheShow()
        {
            Assert.Equal(HttpStatusCode.Conflict, arc.SponsorDeleteGuardStatus);
            Assert.Equal("sponsor_in_use", arc.SponsorDeleteGuardType);
            Assert.Contains(Story430Arc.GuardShowName, arc.SponsorDeleteGuardBody, StringComparison.Ordinal);
        }

        [Fact]
        public void UninstallingThatSponsorsPackIs409AdPackInUseListingTheShow()
        {
            Assert.Equal(HttpStatusCode.Conflict, arc.PackUninstallGuardStatus);
            Assert.Equal("ad_pack_in_use", arc.PackUninstallGuardType);
            Assert.Contains(Story430Arc.PackGuardShowName, arc.PackUninstallGuardBody, StringComparison.Ordinal);
        }

        [Fact]
        public void NothingWasRemovedByEitherRefusal()
        {
            Assert.Equal(HttpStatusCode.OK, arc.SponsorStillExistsStatus);
            Assert.Equal(HttpStatusCode.OK, arc.GuardShowStillExistsStatus);
            Assert.Equal(HttpStatusCode.OK, arc.PackGuardShowStillExistsStatus);

            // By SQL, not by 200 — a 200 on GET /api/shows/{slug} only proves the ROW exists, never
            // that the LINK either guard is protecting survived the refusal that named it.
            Assert.Equal(1, arc.PackSponsorRowCount);
            Assert.Equal(1, arc.PackBriefRowCount);
            Assert.Equal(arc.GuardSponsorId, arc.GuardShowSponsorIdAfterRefusals);
            Assert.Equal(arc.PackSponsorId, arc.PackGuardShowSponsorIdAfterRefusals);
        }
    }
}

// ── Collection definition — one ephemeral Postgres/factory shared by every HTTP-backed Scenario above
// (the Story410Arc "arrange once, many read-only Scenarios" idiom). ──

[CollectionDefinition(Name)]
public sealed class Story430Collection : ICollectionFixture<Story430Arc>
{
    public const string Name = "Story430ShowSponsor";
}

/// <summary>
/// Arranges every fact STORY-430's HTTP-backed Scenarios read, entirely over the REAL production HTTP
/// pipeline with a real admin session — no <c>ShowRepository</c>/<c>SponsorRepository</c> call, no
/// <c>ShowsController</c>/<c>SponsorsController</c>/<c>AdPackController</c> call, anywhere in this
/// class except the one raw-SQL pack-owned-sponsor seed (see the file-header remarks for why no
/// endpoint can arrange that row instead).
/// </summary>
public sealed class Story430Arc : IAsyncLifetime
{
    public const string SponsorName = "Meridian Bicycles";
    public const string GuardShowName = "Fernwood Drive Time";
    public const string PackGuardShowName = "Ridgeline Roasters Hour";
    const string PackSlug = "story430-guard-pack";
    const string PackSponsorBrand = "Ridgeline Coffee";

    // ── AC1 ──
    public string ShowSponsorIdDataType { get; private set; } = "";
    public string ShowSponsorIdIsNullable { get; private set; } = "";
    public string? ShowSponsorIdFkDeleteRule { get; private set; }

    // ── AC2/AC3 ──
    public long SponsorId { get; private set; }
    public HttpStatusCode LinkedShowGetStatus { get; private set; }
    public long? LinkedShowSponsorId { get; private set; }
    public string? LinkedShowSponsorName { get; private set; }
    public long? LinkedShowInListSponsorId { get; private set; }
    public string? LinkedShowInListSponsorName { get; private set; }
    public HttpStatusCode UnlinkedShowGetStatus { get; private set; }
    public bool UnlinkedShowSponsorIsNull { get; private set; }
    public HttpStatusCode PatchClearStatus { get; private set; }
    public bool PatchClearSponsorIsNull { get; private set; }

    // ── AC4 ──
    public HttpStatusCode UnknownSponsorPatchStatus { get; private set; }
    public string? UnknownSponsorPatchType { get; private set; }
    public string? UnknownSponsorPatchField { get; private set; }

    // ── paused-sponsor-is-accepted (PLAN T449 ruling) ──
    public HttpStatusCode PausedSponsorPatchStatus { get; private set; }
    public bool PausedSponsorPatchSponsorPaused { get; private set; }

    // ── AC6 ──
    public long GuardSponsorId { get; private set; }
    public long PackSponsorId { get; private set; }
    public HttpStatusCode SponsorDeleteGuardStatus { get; private set; }
    public string? SponsorDeleteGuardType { get; private set; }
    public string SponsorDeleteGuardBody { get; private set; } = "";
    public HttpStatusCode PackUninstallGuardStatus { get; private set; }
    public string? PackUninstallGuardType { get; private set; }
    public string PackUninstallGuardBody { get; private set; } = "";
    public HttpStatusCode SponsorStillExistsStatus { get; private set; }
    public HttpStatusCode GuardShowStillExistsStatus { get; private set; }
    public HttpStatusCode PackGuardShowStillExistsStatus { get; private set; }
    public int PackSponsorRowCount { get; private set; }
    public int PackBriefRowCount { get; private set; }
    public long? GuardShowSponsorIdAfterRefusals { get; private set; }
    public long? PackGuardShowSponsorIdAfterRefusals { get; private set; }

    public async Task InitializeAsync()
    {
        // A LOCAL, not a field — Story430Database is file-local (CS9051), the Story410Database
        // precedent.
        await using var database = await Story430Database.StartAsync();

        // ── AC1: station.show.sponsor_id is a nullable bigint FK, ON DELETE RESTRICT — read straight
        // off Postgres's own catalogs, before any HTTP call. ──
        (ShowSponsorIdDataType, ShowSponsorIdIsNullable) =
            await Story430WireFixtures.ReadShowSponsorIdColumnAsync(database.StationConnectionString);
        ShowSponsorIdFkDeleteRule =
            await Story430WireFixtures.ReadShowSponsorIdFkDeleteRuleAsync(database.StationConnectionString);

        await using var factory = new Story430WebFactory(database);
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new { password = Story430WebFactory.Password });
        if (login.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login unexpectedly returned {login.StatusCode}");

        // ── AC2: a linked show's GET carries {id, name, paused}; an unlinked show's carries null. ──
        var sponsorCreate = await client.PostAsJsonAsync("/api/sponsors", new { name = SponsorName });
        var sponsorBody = await JsonDocument.ParseAsync(await sponsorCreate.Content.ReadAsStreamAsync());
        SponsorId = sponsorBody.RootElement.GetProperty("id").GetInt64();

        var linkedCreate = await client.PostAsJsonAsync(
            "/api/shows", new { name = "Meridian Morning Mix", sponsorId = SponsorId });
        var linkedBody = await JsonDocument.ParseAsync(await linkedCreate.Content.ReadAsStreamAsync());
        var linkedSlug = linkedBody.RootElement.GetProperty("slug").GetString()
            ?? throw new InvalidOperationException("show create returned no slug");

        var linkedGet = await client.GetAsync($"/api/shows/{linkedSlug}");
        LinkedShowGetStatus = linkedGet.StatusCode;
        var linkedGetBody = await JsonDocument.ParseAsync(await linkedGet.Content.ReadAsStreamAsync());
        var linkedSponsor = linkedGetBody.RootElement.GetProperty("sponsor");
        LinkedShowSponsorId = linkedSponsor.GetProperty("id").GetInt64();
        LinkedShowSponsorName = linkedSponsor.GetProperty("name").GetString();

        // ── AC2, list arm: GET /api/shows carries the SAME sponsor object the single-show GET
        // above just did, off ShowsController.List's own dictionary-backed map — a separate
        // production code path from Get, never proven by the single-show read alone. Captured here,
        // between the linked show's create and the AC3 clear below, while it still carries SponsorId. ──
        var listShows = await client.GetAsync("/api/shows");
        var listShowsBody = await JsonDocument.ParseAsync(await listShows.Content.ReadAsStreamAsync());
        var linkedShowInList = listShowsBody.RootElement.EnumerateArray()
            .FirstOrDefault(row => row.GetProperty("slug").GetString() == linkedSlug);
        if (linkedShowInList.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException("GET /api/shows did not carry the linked show");
        var linkedShowInListSponsor = linkedShowInList.GetProperty("sponsor");
        LinkedShowInListSponsorId = linkedShowInListSponsor.GetProperty("id").GetInt64();
        LinkedShowInListSponsorName = linkedShowInListSponsor.GetProperty("name").GetString();

        var unlinkedCreate = await client.PostAsJsonAsync("/api/shows", new { name = "Solstice Overnight" });
        var unlinkedBody = await JsonDocument.ParseAsync(await unlinkedCreate.Content.ReadAsStreamAsync());
        var unlinkedSlug = unlinkedBody.RootElement.GetProperty("slug").GetString()
            ?? throw new InvalidOperationException("show create returned no slug");

        var unlinkedGet = await client.GetAsync($"/api/shows/{unlinkedSlug}");
        UnlinkedShowGetStatus = unlinkedGet.StatusCode;
        var unlinkedGetBody = await JsonDocument.ParseAsync(await unlinkedGet.Content.ReadAsStreamAsync());
        UnlinkedShowSponsorIsNull = unlinkedGetBody.RootElement.GetProperty("sponsor").ValueKind == JsonValueKind.Null;

        // ── AC3: PATCH {sponsorId: null} clears a linked show's sponsor. ──
        var patchClear = await client.PatchAsJsonAsync(
            $"/api/shows/{linkedSlug}", new { name = "Meridian Morning Mix", sponsorId = (long?)null });
        PatchClearStatus = patchClear.StatusCode;
        var patchClearBody = await JsonDocument.ParseAsync(await patchClear.Content.ReadAsStreamAsync());
        PatchClearSponsorIsNull = patchClearBody.RootElement.GetProperty("sponsor").ValueKind == JsonValueKind.Null;

        // ── AC4: a non-null sponsorId naming no sponsor is 404 sponsor_not_found. ──
        var unknownSponsorPatch = await client.PatchAsJsonAsync(
            $"/api/shows/{unlinkedSlug}", new { name = "Solstice Overnight", sponsorId = 999_999_999L });
        UnknownSponsorPatchStatus = unknownSponsorPatch.StatusCode;
        var unknownSponsorPatchBody = await JsonDocument.ParseAsync(await unknownSponsorPatch.Content.ReadAsStreamAsync());
        UnknownSponsorPatchType = unknownSponsorPatchBody.RootElement.TryGetProperty("type", out var typeProperty)
            ? typeProperty.GetString() : null;
        UnknownSponsorPatchField = unknownSponsorPatchBody.RootElement.TryGetProperty("field", out var fieldProperty)
            ? fieldProperty.GetString() : null;

        // ── paused-sponsor-is-accepted (PLAN T449 ruling): pausing withholds a sponsor's
        // spots from air, it does not lock a show from naming it — a paused sponsor still links
        // through PATCH /api/shows/{slug}. Its own sponsor and show, dedicated to this one check. ──
        var pausedSponsorCreate = await client.PostAsJsonAsync("/api/sponsors", new { name = "Paused Sponsor Test" });
        var pausedSponsorBody = await JsonDocument.ParseAsync(await pausedSponsorCreate.Content.ReadAsStreamAsync());
        var pausedSponsorId = pausedSponsorBody.RootElement.GetProperty("id").GetInt64();

        var pauseResponse = await client.PostAsync($"/api/sponsors/{pausedSponsorId}/pause", content: null);
        if (pauseResponse.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"arrange: pause unexpectedly returned {pauseResponse.StatusCode}");

        var pausedSponsorShowCreate = await client.PostAsJsonAsync("/api/shows", new { name = "Paused Sponsor Show" });
        var pausedSponsorShowBody = await JsonDocument.ParseAsync(await pausedSponsorShowCreate.Content.ReadAsStreamAsync());
        var pausedSponsorShowSlug = pausedSponsorShowBody.RootElement.GetProperty("slug").GetString()
            ?? throw new InvalidOperationException("show create returned no slug");

        var pausedSponsorPatch = await client.PatchAsJsonAsync(
            $"/api/shows/{pausedSponsorShowSlug}", new { name = "Paused Sponsor Show", sponsorId = pausedSponsorId });
        PausedSponsorPatchStatus = pausedSponsorPatch.StatusCode;
        var pausedSponsorPatchBody = await JsonDocument.ParseAsync(await pausedSponsorPatch.Content.ReadAsStreamAsync());
        PausedSponsorPatchSponsorPaused =
            pausedSponsorPatchBody.RootElement.GetProperty("sponsor").GetProperty("paused").GetBoolean();

        // ── AC6a: an owner-authored sponsor referenced by a show refuses DELETE /api/sponsors/{id},
        // naming the show. Its own sponsor and show, never SponsorId/linkedSlug above — deleting one
        // sponsor must never poison a Scenario reading a different sponsor's own row. ──
        var guardSponsorCreate = await client.PostAsJsonAsync("/api/sponsors", new { name = "Fernwood Hardware" });
        var guardSponsorBody = await JsonDocument.ParseAsync(await guardSponsorCreate.Content.ReadAsStreamAsync());
        var guardSponsorId = guardSponsorBody.RootElement.GetProperty("id").GetInt64();
        GuardSponsorId = guardSponsorId;

        var guardShowCreate = await client.PostAsJsonAsync(
            "/api/shows", new { name = GuardShowName, sponsorId = guardSponsorId });
        var guardShowBody = await JsonDocument.ParseAsync(await guardShowCreate.Content.ReadAsStreamAsync());
        var guardShowSlug = guardShowBody.RootElement.GetProperty("slug").GetString()
            ?? throw new InvalidOperationException("show create returned no slug");

        var sponsorDeleteGuard = await client.DeleteAsync($"/api/sponsors/{guardSponsorId}");
        SponsorDeleteGuardStatus = sponsorDeleteGuard.StatusCode;
        SponsorDeleteGuardBody = await sponsorDeleteGuard.Content.ReadAsStringAsync();
        var sponsorDeleteGuardJson = JsonDocument.Parse(SponsorDeleteGuardBody);
        SponsorDeleteGuardType = sponsorDeleteGuardJson.RootElement.TryGetProperty("type", out var sponsorGuardType)
            ? sponsorGuardType.GetString() : null;

        // ── AC6b: a pack-owned sponsor referenced by a show refuses DELETE /api/ad-packs/{slug},
        // naming the show. The sponsor row itself is the one seed this file cannot arrange through an
        // endpoint (see file-header remarks) — the SHOW naming it still goes through POST /api/shows. ──
        var packSponsorId = await Story430WireFixtures.InsertPackOwnedSponsorAsync(
            database.StationConnectionString, PackSponsorBrand, PackSlug);
        PackSponsorId = packSponsorId;

        // A pack with no brief at all is a degenerate arrangement the ad_pack_in_use guard's own
        // real-world trigger never sees, so a brief row is seeded here too (PLAN T449 ruling) —
        // against the SAME (packSlug, folded brand) upsert key as the sponsor row above, so it
        // resolves to that IDENTICAL sponsor rather than a second, disconnected one
        // (station.sponsor's sponsor_pack_slug_name_key).
        await AdBriefWireFixtures.InsertPackBriefAsync(database.StationConnectionString, PackSlug, PackSponsorBrand);

        var packGuardShowCreate = await client.PostAsJsonAsync(
            "/api/shows", new { name = PackGuardShowName, sponsorId = packSponsorId });
        var packGuardShowBody = await JsonDocument.ParseAsync(await packGuardShowCreate.Content.ReadAsStreamAsync());
        var packGuardShowSlug = packGuardShowBody.RootElement.GetProperty("slug").GetString()
            ?? throw new InvalidOperationException("show create returned no slug");

        var packUninstallGuard = await client.DeleteAsync($"/api/ad-packs/{PackSlug}");
        PackUninstallGuardStatus = packUninstallGuard.StatusCode;
        PackUninstallGuardBody = await packUninstallGuard.Content.ReadAsStringAsync();
        var packUninstallGuardJson = JsonDocument.Parse(PackUninstallGuardBody);
        PackUninstallGuardType = packUninstallGuardJson.RootElement.TryGetProperty("type", out var packGuardType)
            ? packGuardType.GetString() : null;

        // ── AC6c: neither refusal removed anything — both sponsors and both shows are still there. ──
        var sponsorStillExists = await client.GetAsync($"/api/sponsors/{guardSponsorId}");
        SponsorStillExistsStatus = sponsorStillExists.StatusCode;
        var guardShowStillExists = await client.GetAsync($"/api/shows/{guardShowSlug}");
        GuardShowStillExistsStatus = guardShowStillExists.StatusCode;
        var packGuardShowStillExists = await client.GetAsync($"/api/shows/{packGuardShowSlug}");
        PackGuardShowStillExistsStatus = packGuardShowStillExists.StatusCode;

        // ── AC6c, continued: by SQL, not by 200 — a 200 on GET /api/shows/{slug} only proves the
        // ROW exists, never that the LINK either guard is protecting survived the refusal that named
        // it (PLAN T449 ruling). ──
        PackSponsorRowCount = await Story430WireFixtures.CountSponsorRowsAsync(
            database.StationConnectionString, packSponsorId);
        PackBriefRowCount = await Story430WireFixtures.CountPackBriefRowsAsync(
            database.StationConnectionString, PackSlug);
        GuardShowSponsorIdAfterRefusals = await Story430WireFixtures.ReadShowSponsorIdAsync(
            database.StationConnectionString, guardShowSlug);
        PackGuardShowSponsorIdAfterRefusals = await Story430WireFixtures.ReadShowSponsorIdAsync(
            database.StationConnectionString, packGuardShowSlug);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Test harness — WebApplicationFactory + ephemeral Postgres subclasses (the Story410_
// SponsorDeleteGuard.cs "`file`-scoped types cannot cross files" precedent — this file supplies its
// own). ──

file sealed class Story430WebFactory(Story430Database db) : WebApplicationFactory<Program>
{
    public const string Password = "test-password-t449-show-sponsor";

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
/// — see that type's own remarks. Supplies only the <c>"genwave-t449"</c> compose project-name prefix
/// this file's own arc needs.</summary>
file sealed class Story430Database : EphemeralStationDatabase
{
    Story430Database(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story430Database> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t449");
        var db = new Story430Database(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>Arrange/read helpers this file's own arc uses that have no endpoint of their own: the AC1
/// schema read (no admin route exposes <c>information_schema</c>/<c>pg_constraint</c>), and the AC6
/// pack-owned-sponsor seed (<c>POST /api/sponsors</c> deliberately refuses a caller-supplied
/// <c>packSlug</c> — SPEC F171.1 — so this is the only way to arrange one short of a full catalog
/// install).</summary>
public static class Story430WireFixtures
{
    public static async Task<(string DataType, string IsNullable)> ReadShowSponsorIdColumnAsync(
        string stationConnectionString)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            select data_type, is_nullable
            from information_schema.columns
            where table_schema = 'station' and table_name = 'show' and column_name = 'sponsor_id'
            """;
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException("station.show.sponsor_id column not found");
        return (reader.GetString(0), reader.GetString(1));
    }

    public static async Task<string?> ReadShowSponsorIdFkDeleteRuleAsync(string stationConnectionString)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select confdeltype::text from pg_constraint where conname = 'show_sponsor_id_fkey'";
        return (string?)await cmd.ExecuteScalarAsync();
    }

    public static async Task<long> InsertPackOwnedSponsorAsync(
        string stationConnectionString, string sponsorName, string packSlug)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "insert into station.sponsor (name, pack_slug) values (@name, @packSlug) returning id";
        cmd.Parameters.AddWithValue("name", sponsorName);
        cmd.Parameters.AddWithValue("packSlug", packSlug);
        return (long)(await cmd.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("sponsor insert returned no id"));
    }

    /// <summary>AC6c, by SQL (PLAN T449 ruling): the sponsor row a refusal must never touch.</summary>
    public static async Task<int> CountSponsorRowsAsync(string stationConnectionString, long sponsorId)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select count(*) from station.sponsor where id = @id";
        cmd.Parameters.AddWithValue("id", sponsorId);
        return (int)(long)(await cmd.ExecuteScalarAsync() ?? 0L);
    }

    /// <summary>AC6c, by SQL (PLAN T449 ruling): the pack brief the ad_pack_in_use refusal
    /// left behind — the guard's own real-world trigger, never present in a degenerate seed with no
    /// brief at all.</summary>
    public static async Task<int> CountPackBriefRowsAsync(string stationConnectionString, string packSlug)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select count(*) from station.ad_brief where pack_slug = @packSlug";
        cmd.Parameters.AddWithValue("packSlug", packSlug);
        return (int)(long)(await cmd.ExecuteScalarAsync() ?? 0L);
    }

    /// <summary>AC6c, by SQL (PLAN T449 ruling): the LINK a refusal must leave standing — a
    /// 200 on GET /api/shows/{slug} only proves the row exists, never that this column survived.
    /// DBNull-safe: a store bug that clears the column reads back as <see langword="null"/>, not a
    /// cast crash.</summary>
    public static async Task<long?> ReadShowSponsorIdAsync(string stationConnectionString, string slug)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select sponsor_id from station.show where slug = @slug";
        cmd.Parameters.AddWithValue("slug", slug);
        var raw = await cmd.ExecuteScalarAsync();
        return raw is null or DBNull ? null : (long)raw;
    }
}

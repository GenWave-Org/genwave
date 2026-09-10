// STORY-416 — Uninstalling an ad pack is guarded (SPEC F172.4 · PLAN T437)
//
// Drives the REAL production DELETE route (AdPackController.Uninstall, T437) through
// WebApplicationFactory<Program> against REAL ephemeral Postgres — the Story401_PackUninstallGuards.cs
// idiom one uninstall over: raw SQL inserts a reference the same way that file's own VoicePack/
// JinglePack guard facts do, then a real DELETE proves the route's own status/body. THREE separate
// databases, one per uninstall outcome (this file's own call, per the T437 brief — "three uninstall
// outcomes need isolation"): a clean pack with no spot history (AC1) and a pack whose own spots get
// retired (AC3) would otherwise leave the SAME pack's sponsor rows in two mutually-exclusive states
// (deleted vs. surviving, AdPackUninstallResult.Deleted's own remarks) if they shared one fixture pack.

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using GenWave.Host.Api;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureUninstallingAnAdPackIsGuarded
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — a pack with no spot history uninstalls clean
    // ---------------------------------------------------------------------

    [Collection(AdPackUninstallCleanCollection.Name)]
    public sealed class ScenarioCleanUninstallRemovesPackBriefsAndPackSponsors(AdPackUninstallCleanArc arc)
    {
        [Fact]
        public void UninstallIs204()
            => Assert.Equal(HttpStatusCode.NoContent, arc.UninstallStatus);

        [Fact]
        public void NoPackBriefRemains()
            => Assert.Equal(0, arc.BriefRowCountAfterUninstall);

        [Fact]
        public void NoPackSponsorRemains()
            // AC1's own literal "sponsor count is 0" example — this pack's own sponsors carry NO spot
            // history, so nothing blocks their own deletion (AdPackUninstallResult.Deleted's own
            // remarks: a sponsor survives ONLY when a spot this same call just retired still names
            // it).
            => Assert.Equal(0, arc.SponsorRowCountAfterUninstall);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — a pack whose own spots get retired, not deleted
    // ---------------------------------------------------------------------

    [Collection(AdPackUninstallRetireCollection.Name)]
    public sealed class ScenarioPackSourcedSpotsAreRetired(AdPackUninstallRetireArc arc)
    {
        [Fact]
        public void TheThreePackSpotsAreRetired()
        {
            // A mutation that made the guard treat this pack's OWN source='pack' spots as blocking
            // references would flip this uninstall's own 200 to 409 (nothing written, nothing
            // retired) — so this fact checks both: the call itself succeeded, AND every one of the
            // three spots (originally ready/draft/approved) now reads retired. 200, not the bare 204
            // AC1 gets, because those same three retirements are exactly what keeps this pack's Brand
            // sponsor standing — see the facts below.
            Assert.Equal(HttpStatusCode.OK, arc.UninstallStatus);
            Assert.Equal(3, arc.RetiredSpotCount);
            Assert.Equal(3, arc.RetiredSpotCountWithRetiredAtSet);
        }

        // ── T437 review round 2 (beyond STORY-416 AC1–AC3): pack sponsors with spot history survive
        // the uninstall (db/46 RESTRICT + SPEC F171.5 "spot (any state)"), answered 200 naming them —
        // SPEC F172.4 rider owed

        [Fact]
        public void TheResponseCarriesTheSameRetiredSpotCount()
            => Assert.Equal(3, arc.ResponseRetiredSpots);

        [Fact]
        public void KeptSponsorsNamesExactlyTheSponsorThoseThreeSpotsReferenceByIdAndName()
        {
            Assert.Equal([arc.KeptSponsorId], arc.ResponseKeptSponsorIds);
            Assert.Equal([AdPackUninstallRetireFixtures.Brand], arc.ResponseKeptSponsorNames);
        }

        [Fact]
        public void TheKeptSponsorSRowSurvivesInStationSponsor()
            => Assert.True(arc.KeptSponsorRowSurvives);

        [Fact]
        public void ThePackSSecondSponsorWithNoSpotHistoryIsGoneNotKept()
            => Assert.True(arc.NoHistorySponsorRowIsGone);

        [Fact]
        public void TheResponseSlugIsThePackSOwnSlug()
            // AdPackUninstallResponse.Slug was unproven (a mutation swapping it for a
            // literal stayed green everywhere else in this file).
            => Assert.Equal(AdPackUninstallRetireFixtures.PackSlug, arc.ResponseSlug);

        [Fact]
        public void TheKeptSponsorSPausedFieldIsPresentAndFalse()
            // LOW finding — SponsorRefDto.Paused was written but never read by any Fact; this pack's
            // own seeded sponsor is never paused.
            => Assert.Equal([false], arc.ResponseKeptSponsorPaused);

        [Fact]
        public void ARepeatUninstallOfTheSameSlugAnswers200AgainWithZeroRetiredSpotsAndTheSameKeptSponsors()
        {
            // That is the contract, not a bug: the kept sponsor's own pack_slug column is
            // never cleared, so AdBriefRepository.UninstallPackAsync's own NotFound pre-check never
            // flips for a slug still carrying a kept sponsor. A mutation narrowing that pre-check to
            // "briefs only" (the mutation probe's own (c) case) would turn this 404 instead.
            Assert.Equal(HttpStatusCode.OK, arc.SecondUninstallStatus);
            Assert.Equal(0, arc.SecondResponseRetiredSpots);
            Assert.Equal(arc.ResponseKeptSponsorIds, arc.SecondResponseKeptSponsorIds);
        }
    }

    // ---------------------------------------------------------------------
    // An owner-authored BRIEF on a pack sponsor, not just a spot, keeps
    // that sponsor standing rather than turning a clean uninstall into a nameless 409
    // ---------------------------------------------------------------------

    [Collection(AdPackUninstallOwnerBriefCollection.Name)]
    public sealed class ScenarioAnOwnerBriefOnAPackSponsorKeepsItStanding(AdPackUninstallOwnerBriefArc arc)
    {
        [Fact]
        public void UninstallIs200NeverA409()
            // The brief DELETE is pack_slug-scoped and never touches this owner brief. Without the
            // ad_brief survivor clause the sponsor DELETE would throw 23503 and the catch would re-read
            // the SPOT/SHOW guard only — InUse([], []), a 409 with nothing to name; the clause is why
            // this answers 200 instead.
            => Assert.Equal(HttpStatusCode.OK, arc.UninstallStatus);

        [Fact]
        public void KeptSponsorsNamesTheBriefHeldSponsorByIdAndName()
        {
            Assert.Equal([arc.SponsorId], arc.ResponseKeptSponsorIds);
            Assert.Equal([AdPackUninstallOwnerBriefFixtures.Brand], arc.ResponseKeptSponsorNames);
        }

        [Fact]
        public void TheOwnerBriefRowSurvives()
            => Assert.True(arc.OwnerBriefRowSurvives);
    }

    // ---------------------------------------------------------------------
    // SAD PATH — an owner-authored reference (spot or show) refuses the whole uninstall
    // ---------------------------------------------------------------------

    [Collection(AdPackUninstallGuardCollection.Name)]
    public sealed class ScenarioRefuseNamesTheReferencingOwnerWork(AdPackUninstallGuardArc arc)
    {
        [Fact]
        public void UninstallIs409AdPackInUse()
        {
            Assert.Equal(HttpStatusCode.Conflict, arc.BothGuardDeleteStatus);
            Assert.Contains("\"ad_pack_in_use\"", arc.BothGuardDeleteBody, StringComparison.Ordinal);
        }

        [Fact]
        public void TheDetailNamesTheOwnerSpotsAndShows()
        {
            Assert.Contains(AdPackUninstallGuardFixtures.OwnerSpotTitle, arc.BothGuardDeleteBody, StringComparison.Ordinal);
            Assert.Contains(AdPackUninstallGuardFixtures.OwnerShowName, arc.BothGuardDeleteBody, StringComparison.Ordinal);
        }

        [Fact]
        public void NothingWasRemoved()
        {
            Assert.Equal(AdPackUninstallGuardFixtures.BothPackBrandCount, arc.BothGuardBriefRowCountAfterRefusal);
            Assert.Equal(AdPackUninstallGuardFixtures.BothPackBrandCount, arc.BothGuardSponsorRowCountAfterRefusal);
        }
    }

    /// <summary>Mutation-testing independence (the T437 brief's own instruction): "drop the show half
    /// of the guard" must still turn something red via the SPOT half alone, and vice versa. The
    /// combined fixture above (one spot AND one show together) cannot prove that on its own — either
    /// half missing would still 409 via the other, masking the mutation. These two facts each install
    /// their OWN pack with exactly ONE reference kind present, so each half of the guard is pinned
    /// independently.</summary>
    [Collection(AdPackUninstallGuardCollection.Name)]
    public sealed class ScenarioEachReferenceKindAloneIsSufficientToRefuse(AdPackUninstallGuardArc arc)
    {
        [Fact]
        public void AnOwnerSpotAloneWithNoShowStillRefuses()
            => Assert.Equal(HttpStatusCode.Conflict, arc.SpotOnlyGuardDeleteStatus);

        [Fact]
        public void AShowAloneWithNoOwnerSpotStillRefuses()
            => Assert.Equal(HttpStatusCode.Conflict, arc.ShowOnlyGuardDeleteStatus);
    }

    /// <summary>No session, no route (the STORY-401/T413/T414 "every pack-kind route refuses an
    /// unauthenticated caller" posture, re-pinned here for ad-packs' own DELETE) — ASP.NET Core's own
    /// auth middleware denies before AdPackController.Uninstall ever runs.</summary>
    [Collection(AdPackUninstallGuardCollection.Name)]
    public sealed class ScenarioAnonymousCallerIsRefused(AdPackUninstallGuardArc arc)
    {
        [Fact]
        public void UninstallIs401WithoutASession()
            => Assert.Equal(HttpStatusCode.Unauthorized, arc.UnauthenticatedDeleteStatus);
    }

    /// <summary>The 404 case had no Fact anywhere in
    /// this file; a mutation collapsing the NotFound arm to <c>NoContent()</c> would have stayed green
    /// everywhere else here, since every OTHER Scenario in this file targets a slug that genuinely
    /// exists.</summary>
    [Collection(AdPackUninstallGuardCollection.Name)]
    public sealed class ScenarioUninstallOfANeverInstalledSlugIsRefused(AdPackUninstallGuardArc arc)
    {
        [Fact]
        public void UninstallOfANeverInstalledSlugIs404()
            => Assert.Equal(HttpStatusCode.NotFound, arc.NeverInstalledDeleteStatus);

        [Fact]
        public void The404BodyNamesTheSlug()
            => Assert.Contains(
                AdPackUninstallGuardFixtures.NeverInstalledPackSlug, arc.NeverInstalledDeleteBody, StringComparison.Ordinal);
    }

    /// <summary><see cref="AdPackController.Uninstall"/>'s
    /// own route-slug format/length gate (the SAME <c>CatalogInstallShell</c> pair <see
    /// cref="AdPackController.Install"/> already carries) had no Fact anywhere in this file; a mutation
    /// deleting either <c>if</c> block in <see cref="AdPackController.Uninstall"/> would have stayed
    /// green everywhere else here, since every OTHER Scenario in this file targets a slug that is
    /// already well-formed and in-bounds. Distinguishes the two 400s by <c>Detail</c> text, not
    /// <c>ProblemDetails.Type</c> — <see cref="CatalogInstallShell.BadSlugProblem"/> and <see
    /// cref="CatalogInstallShell.SlugTooLongProblem"/> both carry the identical "Invalid slug." Title
    /// and set no Type at all (<c>Story392_AdBriefsApi.cs</c>'s own "ProblemDetails.Type is omitted by
    /// the converter" precedent applies here too); Detail is the only field either problem actually
    /// varies.</summary>
    [Collection(AdPackUninstallGuardCollection.Name)]
    public sealed class ScenarioMalformedSlugIsRefusedBeforeTheStoreIsEverAsked(AdPackUninstallGuardArc arc)
    {
        [Fact]
        public void ASlugFailingTheRouteFormatIs400()
        {
            Assert.Equal(HttpStatusCode.BadRequest, arc.BadFormatSlugDeleteStatus);
            Assert.Contains("is not a valid catalog entry slug", arc.BadFormatSlugDeleteBody, StringComparison.Ordinal);
        }

        [Fact]
        public void ASlugOverTheLengthCeilingIs400()
        {
            Assert.Equal(HttpStatusCode.BadRequest, arc.TooLongSlugDeleteStatus);
            Assert.Contains("must be at most", arc.TooLongSlugDeleteBody, StringComparison.Ordinal);
        }
    }
}

// ── Arc 1 — clean uninstall (no spot history) ───────────────────────────────────────────────────

[CollectionDefinition(Name)]
public sealed class AdPackUninstallCleanCollection : ICollectionFixture<AdPackUninstallCleanArc>
{
    public const string Name = "Story416AdPackUninstallClean";
}

/// <summary>Boots one real ephemeral Postgres, installs a fresh pack, immediately uninstalls it — no
/// spot ever referenced any of its sponsors, so both the briefs AND the sponsors are gone
/// (AC1).</summary>
public sealed class AdPackUninstallCleanArc : IAsyncLifetime
{
    public HttpStatusCode UninstallStatus { get; private set; }
    public int BriefRowCountAfterUninstall { get; private set; }
    public int SponsorRowCountAfterUninstall { get; private set; }

    public async Task InitializeAsync()
    {
        await using var database = await Story416CleanDatabase.StartAsync();
        await using var factory = new AdPackUninstallWebFactory(
            database, AdPackUninstallCleanFixtures.IndexUrl, AdPackUninstallCleanFixtures.BuildRoutedHandler());
        var client = await AdPackUninstallWebFactory.LoggedInClientAsync(factory);

        var install = await client.PostAsync($"/api/ad-packs/{AdPackUninstallCleanFixtures.PackSlug}/install", null);
        if (!install.IsSuccessStatusCode)
            throw new InvalidOperationException($"fixture install failed: {await install.Content.ReadAsStringAsync()}");

        var uninstall = await client.DeleteAsync($"/api/ad-packs/{AdPackUninstallCleanFixtures.PackSlug}");
        UninstallStatus = uninstall.StatusCode;

        BriefRowCountAfterUninstall = await CountAsync(
            database.StationConnectionString, "select count(*)::int from station.ad_brief where pack_slug = @packSlug");
        SponsorRowCountAfterUninstall = await CountAsync(
            database.StationConnectionString, "select count(*)::int from station.sponsor where pack_slug = @packSlug");
    }

    static async Task<int> CountAsync(string stationConnectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(sql, new { packSlug = AdPackUninstallCleanFixtures.PackSlug });
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Arc 2 — retire, don't delete, the pack's own spots ──────────────────────────────────────────

[CollectionDefinition(Name)]
public sealed class AdPackUninstallRetireCollection : ICollectionFixture<AdPackUninstallRetireArc>
{
    public const string Name = "Story416AdPackUninstallRetire";
}

/// <summary>Boots one real ephemeral Postgres, installs a pack carrying TWO sponsors, inserts three
/// <c>source='pack'</c> spots by raw SQL against ONE of them (one per state the T437 brief names:
/// ready/draft/approved — every NOT NULL <c>station.ad_spot</c> column supplied), then uninstalls —
/// those three spots are the pack's OWN work, never a reason to refuse (AC3), and end up retired
/// rather than deleted (the FK-RESTRICT survivor clause AdPackUninstallResult.Deleted's own remarks
/// explain). The SECOND sponsor carries no spot history at all (PLAN T437 review round 2) — proving,
/// inside the SAME pack, that a sponsor with history survives while one without does not.</summary>
public sealed class AdPackUninstallRetireArc : IAsyncLifetime
{
    public HttpStatusCode UninstallStatus { get; private set; }
    public int RetiredSpotCount { get; private set; }
    public int RetiredSpotCountWithRetiredAtSet { get; private set; }

    // ── T437 review round 2 (beyond STORY-416 AC1–AC3): pack sponsors with spot history survive the
    // uninstall (db/46 RESTRICT + SPEC F171.5 "spot (any state)"), answered 200 naming them — SPEC
    // F172.4 rider owed
    public long KeptSponsorId { get; private set; }
    public int ResponseRetiredSpots { get; private set; }
    public IReadOnlyList<long> ResponseKeptSponsorIds { get; private set; } = [];
    public IReadOnlyList<string> ResponseKeptSponsorNames { get; private set; } = [];
    public bool KeptSponsorRowSurvives { get; private set; }
    public bool NoHistorySponsorRowIsGone { get; private set; }

    // ── The response's own Slug field and a KeptSponsor's Paused field were unproven by any Fact,
    // and a repeat DELETE of an already-uninstalled slug was unproven to keep answering 200 forever
    // rather than 404ing on the second call — this arc drives a SECOND uninstall of the same slug
    // right after the first.
    public string ResponseSlug { get; private set; } = "";
    public IReadOnlyList<bool> ResponseKeptSponsorPaused { get; private set; } = [];
    public HttpStatusCode SecondUninstallStatus { get; private set; }
    public int SecondResponseRetiredSpots { get; private set; }
    public IReadOnlyList<long> SecondResponseKeptSponsorIds { get; private set; } = [];

    public async Task InitializeAsync()
    {
        await using var database = await Story416RetireDatabase.StartAsync();
        await using var factory = new AdPackUninstallWebFactory(
            database, AdPackUninstallRetireFixtures.IndexUrl, AdPackUninstallRetireFixtures.BuildRoutedHandler());
        var client = await AdPackUninstallWebFactory.LoggedInClientAsync(factory);

        var install = await client.PostAsync($"/api/ad-packs/{AdPackUninstallRetireFixtures.PackSlug}/install", null);
        if (!install.IsSuccessStatusCode)
            throw new InvalidOperationException($"fixture install failed: {await install.Content.ReadAsStringAsync()}");

        var sponsorId = await AdPackUninstallFixtureSupport.ReadSponsorIdAsync(
            database.StationConnectionString, AdPackUninstallRetireFixtures.PackSlug, AdPackUninstallRetireFixtures.Brand);
        KeptSponsorId = sponsorId;

        // ad_spot_ready_requires_media_id (db/06/db/43) — a 'ready' row needs SOME media_id; the
        // column carries no FK (it crosses the library/station schema-role boundary, db/22's own
        // header), so a fake id is a legal, honest fixture value here, never a real library row.
        await InsertPackSpotAsync(database.StationConnectionString, sponsorId, "ready", "Ready pack spot", mediaId: 999_999);
        await InsertPackSpotAsync(database.StationConnectionString, sponsorId, "draft", "Draft pack spot", mediaId: null);
        await InsertPackSpotAsync(database.StationConnectionString, sponsorId, "approved", "Approved pack spot", mediaId: null);

        var uninstall = await client.DeleteAsync($"/api/ad-packs/{AdPackUninstallRetireFixtures.PackSlug}");
        UninstallStatus = uninstall.StatusCode;

        // 200, not the bare 204: this pack's Brand sponsor still carries the three spots just
        // retired above (a "spot (any state)", SPEC F171.5), so AdPackController.Uninstall's own
        // KeptSponsors.Count > 0 arm answers with AdPackUninstallResponse, naming it. Gated on the
        // status, not attempted unconditionally: a 204 carries no body at all, so a mutation
        // collapsing this arm back to NoContent() must turn a SPECIFIC assertion below red (expected
        // OK/expected the response fields populated) rather than crashing THIS arrange step for every
        // Fact in the Scenario alike (mutation testing discipline — kill via assertion, not a fixture
        // crash) — Response* simply stay at their own empty/zero defaults when this branch is skipped.
        if (UninstallStatus == HttpStatusCode.OK)
        {
            var uninstallBody = await JsonDocument.ParseAsync(await uninstall.Content.ReadAsStreamAsync());
            ResponseSlug = uninstallBody.RootElement.GetProperty("slug").GetString() ?? "";
            ResponseRetiredSpots = uninstallBody.RootElement.GetProperty("retiredSpots").GetInt32();
            var keptSponsorsElement = uninstallBody.RootElement.GetProperty("keptSponsors");
            ResponseKeptSponsorIds = keptSponsorsElement.EnumerateArray().Select(e => e.GetProperty("id").GetInt64()).ToArray();
            ResponseKeptSponsorNames = keptSponsorsElement.EnumerateArray().Select(e => e.GetProperty("name").GetString() ?? "").ToArray();
            ResponseKeptSponsorPaused = keptSponsorsElement.EnumerateArray().Select(e => e.GetProperty("paused").GetBoolean()).ToArray();

            // This pack's sponsor survived the first uninstall, and a kept sponsor's own pack_slug
            // column is never cleared, so AdBriefRepository.UninstallPackAsync's own NotFound
            // pre-check still sees this slug as installed — not a "not found": it answers 200 again,
            // forever, with RetiredSpots back to 0 (nothing left to retire) and the SAME
            // KeptSponsors. A pack whose sponsors carry no kept survivor (the clean-uninstall case)
            // 404s instead on its second DELETE.
            var secondUninstall = await client.DeleteAsync($"/api/ad-packs/{AdPackUninstallRetireFixtures.PackSlug}");
            SecondUninstallStatus = secondUninstall.StatusCode;
            if (SecondUninstallStatus == HttpStatusCode.OK)
            {
                var secondBody = await JsonDocument.ParseAsync(await secondUninstall.Content.ReadAsStreamAsync());
                SecondResponseRetiredSpots = secondBody.RootElement.GetProperty("retiredSpots").GetInt32();
                SecondResponseKeptSponsorIds = secondBody.RootElement.GetProperty("keptSponsors")
                    .EnumerateArray().Select(e => e.GetProperty("id").GetInt64()).ToArray();
            }
        }

        await using var conn = new NpgsqlConnection(database.StationConnectionString);
        await conn.OpenAsync();
        RetiredSpotCount = await conn.ExecuteScalarAsync<int>(
            "select count(*)::int from station.ad_spot where pack_slug = @packSlug and state = 'retired'::station.ad_state",
            new { packSlug = AdPackUninstallRetireFixtures.PackSlug });
        RetiredSpotCountWithRetiredAtSet = await conn.ExecuteScalarAsync<int>(
            """
            select count(*)::int from station.ad_spot
            where pack_slug = @packSlug and state = 'retired'::station.ad_state and retired_at is not null
            """,
            new { packSlug = AdPackUninstallRetireFixtures.PackSlug });
        KeptSponsorRowSurvives = await conn.ExecuteScalarAsync<bool>(
            "select exists(select 1 from station.sponsor where id = @sponsorId)", new { sponsorId });
        NoHistorySponsorRowIsGone = !await conn.ExecuteScalarAsync<bool>(
            "select exists(select 1 from station.sponsor where pack_slug = @packSlug and name = @brand)",
            new { packSlug = AdPackUninstallRetireFixtures.PackSlug, brand = AdPackUninstallRetireFixtures.NoHistoryBrand });
    }

    static async Task InsertPackSpotAsync(string stationConnectionString, long sponsorId, string state, string title, long? mediaId)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            """
            insert into station.ad_spot (sponsor_id, sponsor_name, title, source, pack_slug, state, spot_seconds, media_id)
            values (@SponsorId, @Brand, @Title, 'pack'::station.ad_source, @PackSlug, @State::station.ad_state, 30, @MediaId)
            """,
            new
            {
                SponsorId = sponsorId,
                Brand = AdPackUninstallRetireFixtures.Brand,
                Title = title,
                PackSlug = AdPackUninstallRetireFixtures.PackSlug,
                State = state,
                MediaId = mediaId,
            });
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Arc 3 — refusal: owner spot and/or show still names a pack sponsor ──────────────────────────

[CollectionDefinition(Name)]
public sealed class AdPackUninstallGuardCollection : ICollectionFixture<AdPackUninstallGuardArc>
{
    public const string Name = "Story416AdPackUninstallGuard";
}

/// <summary>Boots one real ephemeral Postgres, installs THREE independent packs (their own disjoint
/// slugs, so none of the three DELETE attempts below can ever cross-contaminate another's row
/// counts): one carrying BOTH an owner spot and a show reference (AC2's own "names the owner spots AND
/// shows" claim), and two more carrying exactly ONE reference kind each — the mutation-testing
/// independence this file's own <c>ScenarioEachReferenceKindAloneIsSufficientToRefuse</c> needs (see
/// that Scenario's own remarks). The anonymous-caller 401 targets <c>BothPackSlug</c> — any installed
/// slug proves it identically, since ASP.NET Core's own auth middleware denies before
/// <c>AdPackController.Uninstall</c> ever runs, so which pack sits behind the route is immaterial. A
/// FOURTH slug, <c>NeverInstalledPackSlug</c>, is deliberately never installed at all — it proves
/// the 404 case instead, which had no Fact anywhere in this file before this coverage was
/// added.</summary>
public sealed class AdPackUninstallGuardArc : IAsyncLifetime
{
    public HttpStatusCode BothGuardDeleteStatus { get; private set; }
    public string BothGuardDeleteBody { get; private set; } = "";
    public int BothGuardBriefRowCountAfterRefusal { get; private set; }
    public int BothGuardSponsorRowCountAfterRefusal { get; private set; }

    public HttpStatusCode SpotOnlyGuardDeleteStatus { get; private set; }
    public HttpStatusCode ShowOnlyGuardDeleteStatus { get; private set; }

    public HttpStatusCode UnauthenticatedDeleteStatus { get; private set; }

    public HttpStatusCode NeverInstalledDeleteStatus { get; private set; }
    public string NeverInstalledDeleteBody { get; private set; } = "";

    // ── The route slug format/length gate had no Fact ──
    public HttpStatusCode BadFormatSlugDeleteStatus { get; private set; }
    public string BadFormatSlugDeleteBody { get; private set; } = "";
    public HttpStatusCode TooLongSlugDeleteStatus { get; private set; }
    public string TooLongSlugDeleteBody { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await using var database = await Story416GuardDatabase.StartAsync();
        await using var factory = new AdPackUninstallWebFactory(
            database, AdPackUninstallGuardFixtures.IndexUrl, AdPackUninstallGuardFixtures.BuildRoutedHandler());
        var client = await AdPackUninstallWebFactory.LoggedInClientAsync(factory);

        foreach (var slug in AdPackUninstallGuardFixtures.InstallSlugs)
        {
            var install = await client.PostAsync($"/api/ad-packs/{slug}/install", null);
            if (!install.IsSuccessStatusCode)
                throw new InvalidOperationException($"fixture install of '{slug}' failed: {await install.Content.ReadAsStringAsync()}");
        }

        // ── Both reference kinds present together (AC2's own "names spots AND shows" claim) ──
        var bothSponsorId = await AdPackUninstallFixtureSupport.ReadSponsorIdAsync(
            database.StationConnectionString, AdPackUninstallGuardFixtures.BothPackSlug, AdPackUninstallGuardFixtures.OwnerSpotBrand);
        var bothShowSponsorId = await AdPackUninstallFixtureSupport.ReadSponsorIdAsync(
            database.StationConnectionString, AdPackUninstallGuardFixtures.BothPackSlug, AdPackUninstallGuardFixtures.OwnerShowBrand);
        await InsertOwnerSpotAsync(database.StationConnectionString, bothSponsorId, AdPackUninstallGuardFixtures.OwnerSpotTitle);
        await InsertShowAsync(
            database.StationConnectionString, bothShowSponsorId, AdPackUninstallGuardFixtures.OwnerShowName, "guard-both-show");

        var bothResponse = await client.DeleteAsync($"/api/ad-packs/{AdPackUninstallGuardFixtures.BothPackSlug}");
        BothGuardDeleteStatus = bothResponse.StatusCode;
        BothGuardDeleteBody = await bothResponse.Content.ReadAsStringAsync();
        BothGuardBriefRowCountAfterRefusal = await CountAsync(
            database.StationConnectionString, "station.ad_brief", AdPackUninstallGuardFixtures.BothPackSlug);
        BothGuardSponsorRowCountAfterRefusal = await CountAsync(
            database.StationConnectionString, "station.sponsor", AdPackUninstallGuardFixtures.BothPackSlug);

        // ── Spot reference alone, no show — proves the show half is not what is refusing ──
        var spotOnlySponsorId = await AdPackUninstallFixtureSupport.ReadSponsorIdAsync(
            database.StationConnectionString, AdPackUninstallGuardFixtures.SpotOnlyPackSlug, AdPackUninstallGuardFixtures.SpotOnlyBrand);
        await InsertOwnerSpotAsync(database.StationConnectionString, spotOnlySponsorId, "Spot-only guard spot");

        var spotOnlyResponse = await client.DeleteAsync($"/api/ad-packs/{AdPackUninstallGuardFixtures.SpotOnlyPackSlug}");
        SpotOnlyGuardDeleteStatus = spotOnlyResponse.StatusCode;

        // ── Show reference alone, no spot — proves the spot half is not what is refusing ──
        var showOnlySponsorId = await AdPackUninstallFixtureSupport.ReadSponsorIdAsync(
            database.StationConnectionString, AdPackUninstallGuardFixtures.ShowOnlyPackSlug, AdPackUninstallGuardFixtures.ShowOnlyBrand);
        await InsertShowAsync(database.StationConnectionString, showOnlySponsorId, "Show-only guard show", "guard-show-only-show");

        var showOnlyResponse = await client.DeleteAsync($"/api/ad-packs/{AdPackUninstallGuardFixtures.ShowOnlyPackSlug}");
        ShowOnlyGuardDeleteStatus = showOnlyResponse.StatusCode;

        // ── No session at all ──
        var anonymousClient = factory.CreateClient();
        var anonymousResponse = await anonymousClient.DeleteAsync($"/api/ad-packs/{AdPackUninstallGuardFixtures.BothPackSlug}");
        UnauthenticatedDeleteStatus = anonymousResponse.StatusCode;

        // ── A slug never installed at all ──
        var neverInstalledResponse = await client.DeleteAsync($"/api/ad-packs/{AdPackUninstallGuardFixtures.NeverInstalledPackSlug}");
        NeverInstalledDeleteStatus = neverInstalledResponse.StatusCode;
        NeverInstalledDeleteBody = await neverInstalledResponse.Content.ReadAsStringAsync();

        // ── A slug that fails CatalogInstallShell.SlugFormat — never reaches the
        //    store at all, so no install is needed for this one to prove the 400. ──
        var badFormatResponse = await client.DeleteAsync($"/api/ad-packs/{AdPackUninstallGuardFixtures.BadFormatSlug}");
        BadFormatSlugDeleteStatus = badFormatResponse.StatusCode;
        BadFormatSlugDeleteBody = await badFormatResponse.Content.ReadAsStringAsync();

        // ── A slug over CatalogInstallShell.MaxSlugLength — same story. ──
        var tooLongResponse = await client.DeleteAsync($"/api/ad-packs/{AdPackUninstallGuardFixtures.TooLongSlug}");
        TooLongSlugDeleteStatus = tooLongResponse.StatusCode;
        TooLongSlugDeleteBody = await tooLongResponse.Content.ReadAsStringAsync();
    }

    static async Task InsertOwnerSpotAsync(string stationConnectionString, long sponsorId, string title)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            """
            insert into station.ad_spot (sponsor_id, sponsor_name, title, source, state, spot_seconds)
            select id, name, @Title, 'owner'::station.ad_source, 'draft'::station.ad_state, 30
            from station.sponsor where id = @SponsorId
            """,
            new { SponsorId = sponsorId, Title = title });
    }

    static async Task InsertShowAsync(string stationConnectionString, long sponsorId, string name, string slug)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "insert into station.show (name, slug, sponsor_id) values (@Name, @Slug, @SponsorId)",
            new { Name = name, Slug = slug, SponsorId = sponsorId });
    }

    static async Task<int> CountAsync(string stationConnectionString, string table, string packSlug)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<int>($"select count(*)::int from {table} where pack_slug = @packSlug", new { packSlug });
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Test harnesses (project-prefix genwave-t437b, the T437 brief's own naming instruction) ────────

/// <summary>This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/>
/// harness for <see cref="AdPackUninstallCleanArc"/> — see that type's own remarks.</summary>
file sealed class Story416CleanDatabase : EphemeralStationDatabase
{
    Story416CleanDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story416CleanDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t437b-cln");
        var db = new Story416CleanDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/>
/// harness for <see cref="AdPackUninstallRetireArc"/> — see that type's own remarks.</summary>
file sealed class Story416RetireDatabase : EphemeralStationDatabase
{
    Story416RetireDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story416RetireDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t437b-ret");
        var db = new Story416RetireDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/>
/// harness for <see cref="AdPackUninstallGuardArc"/> — see that type's own remarks.</summary>
file sealed class Story416GuardDatabase : EphemeralStationDatabase
{
    Story416GuardDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story416GuardDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t437b-grd");
        var db = new Story416GuardDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> shared by every arc in this file — boots the real
/// Program.cs graph against a REAL ephemeral Postgres (<paramref name="database"/>) with every hosted
/// service removed (no background reach into <c>station.ad_brief</c>/<c>station.sponsor</c> racing
/// this file's own installs/deletes — the <c>AdPackInstallWebFactory</c> idiom, Story393_AdPackKind.cs)
/// and <c>Community:CatalogIndexUrl</c> pointed at whichever fake catalog origin the caller supplies.
/// Re-declared here rather than shared: that type is <see langword="file"/>-scoped in its own spec
/// file, per this project's own per-file harness convention.
/// </summary>
file sealed class AdPackUninstallWebFactory(EphemeralStationDatabase database, string indexUrl, FakeHttpMessageHandler handler)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story416-adpack-uninstall";

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
        builder.UseSetting("Community:CatalogIndexUrl", indexUrl);

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

// ── Fixtures — one shared routed-handler idiom, three independent fixture document sets ───────────

file static class AdPackUninstallFixtureSupport
{
    public static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public static string MetaJson => """
        {"author":"Test Fixture","description":"An ad-pack for the uninstall-endpoint specs.","audience":"everyone","added":"2026-09-09"}
        """;

    public static string IndexJson(IReadOnlyDictionary<string, string> slugToManifest)
    {
        var entries = string.Join(", ", slugToManifest.Select(kv => $$"""
            { "slug": "{{kv.Key}}", "kind": "ad-pack", "audience": "everyone",
              "manifest": { "path": "entries/ad-packs/{{kv.Key}}/{{kv.Key}}.ad-pack.json", "sha256": "{{Sha256Hex(kv.Value)}}" },
              "meta": { "path": "entries/ad-packs/{{kv.Key}}/{{kv.Key}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" } }
            """));
        return $$"""{ "generatedAt": "2026-09-09", "entries": [ {{entries}} ] }""";
    }

    /// <summary>Serves every fixture document at its own resolved URL, 404 for anything else — the
    /// <c>AdPackFixtures.BuildRoutedHandler</c> idiom, one file over.</summary>
    public static FakeHttpMessageHandler BuildRoutedHandler(string indexUrl, string directoryUrl, IReadOnlyDictionary<string, string> slugToManifest)
    {
        var routes = new Dictionary<string, string> { [indexUrl] = IndexJson(slugToManifest) };
        foreach (var (slug, manifestJson) in slugToManifest)
        {
            routes[directoryUrl + "entries/ad-packs/" + slug + "/" + slug + ".ad-pack.json"] = manifestJson;
            routes[directoryUrl + "entries/ad-packs/" + slug + "/" + slug + ".meta.json"] = MetaJson;
        }

        return new((request, _) =>
        {
            var absoluteUri = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(
                routes.TryGetValue(absoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
    }

    /// <summary>Reads the id of one pack-owned sponsor row by its <paramref name="packSlug"/>/
    /// <paramref name="brand"/> pair — every arc in this file needs this same lookup after installing a
    /// pack, to seed a reference against a KNOWN sponsor id (this was duplicated verbatim in
    /// <c>AdPackUninstallRetireArc</c> and <c>AdPackUninstallGuardArc</c>, and
    /// inlined a third time in <c>AdPackUninstallOwnerBriefArc</c>; one copy, three call sites).</summary>
    public static async Task<long> ReadSponsorIdAsync(string stationConnectionString, string packSlug, string brand)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        return await conn.QuerySingleAsync<long>(
            "select id from station.sponsor where pack_slug = @packSlug and name = @brand", new { packSlug, brand });
    }
}

/// <summary>One brand, one brief — <see cref="AdPackUninstallCleanArc"/>'s own fixture (AC1: a pack
/// with no spot history uninstalls clean).</summary>
file static class AdPackUninstallCleanFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/ad-pack-uninstall-clean-index.json";
    const string DirectoryUrl = "https://catalog.test/repo/";
    public const string PackSlug = "clean-uninstall-pack";
    const string Brand = "Al's Diner";

    static readonly string ManifestJson = $$"""
        { "packName": "Clean Uninstall Pack", "briefs": [ { "brand": "{{Brand}}", "premise": "Lunch special" } ] }
        """;

    public static FakeHttpMessageHandler BuildRoutedHandler() =>
        AdPackUninstallFixtureSupport.BuildRoutedHandler(IndexUrl, DirectoryUrl, new Dictionary<string, string> { [PackSlug] = ManifestJson });
}

/// <summary>TWO brands, one brief each — <see cref="AdPackUninstallRetireArc"/>'s own fixture (AC3: the
/// pack's own spots get retired, not treated as blocking references). <see cref="Brand"/> is the one
/// the arc gives spot history; <see cref="NoHistoryBrand"/> never gets a spot at all — PLAN T437
/// review round 2's own "prove survive AND delete inside one pack" pairing.</summary>
file static class AdPackUninstallRetireFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/ad-pack-uninstall-retire-index.json";
    const string DirectoryUrl = "https://catalog.test/repo/";
    public const string PackSlug = "retire-spots-pack";
    public const string Brand = "Bramble & Fitch Hardware";
    public const string NoHistoryBrand = "Thistle & Rose Florist";

    static readonly string ManifestJson = $$"""
        { "packName": "Retire Spots Pack", "briefs": [
          { "brand": "{{Brand}}", "premise": "Loyalty program" },
          { "brand": "{{NoHistoryBrand}}", "premise": "Spring bouquets" } ] }
        """;

    public static FakeHttpMessageHandler BuildRoutedHandler() =>
        AdPackUninstallFixtureSupport.BuildRoutedHandler(IndexUrl, DirectoryUrl, new Dictionary<string, string> { [PackSlug] = ManifestJson });
}

/// <summary>THREE independent packs — see <see cref="AdPackUninstallGuardArc"/>'s own class remarks
/// for why each carries its own disjoint slug and sponsor brand.</summary>
file static class AdPackUninstallGuardFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/ad-pack-uninstall-guard-index.json";
    const string DirectoryUrl = "https://catalog.test/repo/";

    public const int BothPackBrandCount = 2;

    public const string BothPackSlug = "guard-both-pack";
    public const string OwnerSpotBrand = "Al's Diner";
    public const string OwnerShowBrand = "Ceylon Tea Co";
    public const string OwnerSpotTitle = "Owner's own Al's Diner spot";
    public const string OwnerShowName = "Ceylon Tea Hour";

    public const string SpotOnlyPackSlug = "guard-spot-only-pack";
    public const string SpotOnlyBrand = "Fenwick Auto Repair";

    public const string ShowOnlyPackSlug = "guard-show-only-pack";
    public const string ShowOnlyBrand = "Harbor Light Coffee";

    /// <summary>Deliberately absent from
    /// <see cref="InstallSlugs"/>: this slug never gets a fixture manifest at all, since the whole
    /// point is proving the 404 case for a pack that was never installed in the first place.</summary>
    public const string NeverInstalledPackSlug = "guard-never-installed-pack";

    /// <summary>Fails <c>CatalogInstallShell.SlugFormat</c> (an
    /// uppercase letter; the regex only ever admits lowercase, digits, and single hyphens) while
    /// staying well under <see cref="CatalogInstallShell.MaxSlugLength"/>, so it exercises the format
    /// gate alone.</summary>
    public const string BadFormatSlug = "Not-A-Valid-SLUG";

    /// <summary>One character past
    /// <see cref="CatalogInstallShell.MaxSlugLength"/>, built from otherwise format-valid characters so
    /// this exercises the length gate alone (the length check runs first in
    /// <see cref="AdPackController.Uninstall"/>, so any over-length slug 400s here regardless).</summary>
    public static readonly string TooLongSlug = new string('a', CatalogInstallShell.MaxSlugLength + 1);

    /// <summary>The three slugs <see cref="AdPackUninstallGuardArc.InitializeAsync"/> actually installs
    /// — deliberately excludes <see cref="NeverInstalledPackSlug"/> (this remark's own former
    /// "AllSlugs subset" wording was wrong — that list was dead code, deleted): that slug carries no
    /// fixture manifest at all, so the install loop below must never
    /// attempt it.</summary>
    public static readonly IReadOnlyList<string> InstallSlugs = [BothPackSlug, SpotOnlyPackSlug, ShowOnlyPackSlug];

    static string OneBriefManifest(string brand) => $$"""
        { "packName": "{{brand}}'s Own Pack", "briefs": [ { "brand": "{{brand}}", "premise": "A local spot" } ] }
        """;

    public static FakeHttpMessageHandler BuildRoutedHandler() =>
        AdPackUninstallFixtureSupport.BuildRoutedHandler(
            IndexUrl, DirectoryUrl,
            new Dictionary<string, string>
            {
                // BothPackSlug installs TWO sponsors — one the owner spot names, one the show names —
                // so TheDetailNamesTheOwnerSpotsAndShows proves both halves land in the SAME 409, not
                // two coincidentally-equal brand strings.
                [BothPackSlug] = $$"""
                    { "packName": "Guard Both Pack", "briefs": [
                      { "brand": "{{OwnerSpotBrand}}", "premise": "A local spot" },
                      { "brand": "{{OwnerShowBrand}}", "premise": "Another local spot" } ] }
                    """,
                [SpotOnlyPackSlug] = OneBriefManifest(SpotOnlyBrand),
                [ShowOnlyPackSlug] = OneBriefManifest(ShowOnlyBrand),
            });
}

// ── Arc 4 — an OWNER brief on a pack sponsor keeps it standing too ─────────

[CollectionDefinition(Name)]
public sealed class AdPackUninstallOwnerBriefCollection : ICollectionFixture<AdPackUninstallOwnerBriefArc>
{
    public const string Name = "Story416AdPackUninstallOwnerBrief";
}

/// <summary>Boots one real ephemeral Postgres, installs a pack carrying one sponsor with NO spot
/// history at all, then creates an OWNER-authored <c>station.ad_brief</c> row on that SAME sponsor
/// through the REAL <c>POST /api/ad-briefs</c> route (that route's own precedent for why this is a
/// supported, not a hypothetical, state: it accepts any <c>sponsorId</c>, including a pack
/// sponsor's own) before uninstalling — proving a brief-held pack sponsor survives
/// the uninstall exactly the way a spot-held one already did in <see cref="AdPackUninstallRetireArc"/>,
/// rather than the sponsor DELETE throwing a nameless <c>23503</c> that this uninstall's own catch
/// block turned into an empty, unnamed 409.</summary>
public sealed class AdPackUninstallOwnerBriefArc : IAsyncLifetime
{
    public HttpStatusCode UninstallStatus { get; private set; }
    public long SponsorId { get; private set; }
    public IReadOnlyList<long> ResponseKeptSponsorIds { get; private set; } = [];
    public IReadOnlyList<string> ResponseKeptSponsorNames { get; private set; } = [];
    public bool OwnerBriefRowSurvives { get; private set; }

    public async Task InitializeAsync()
    {
        await using var database = await Story416OwnerBriefDatabase.StartAsync();
        await using var factory = new AdPackUninstallWebFactory(
            database, AdPackUninstallOwnerBriefFixtures.IndexUrl, AdPackUninstallOwnerBriefFixtures.BuildRoutedHandler());
        var client = await AdPackUninstallWebFactory.LoggedInClientAsync(factory);

        var install = await client.PostAsync($"/api/ad-packs/{AdPackUninstallOwnerBriefFixtures.PackSlug}/install", null);
        if (!install.IsSuccessStatusCode)
            throw new InvalidOperationException($"fixture install failed: {await install.Content.ReadAsStringAsync()}");

        SponsorId = await AdPackUninstallFixtureSupport.ReadSponsorIdAsync(
            database.StationConnectionString, AdPackUninstallOwnerBriefFixtures.PackSlug, AdPackUninstallOwnerBriefFixtures.Brand);

        // Through the REAL route, not raw SQL — POST /api/ad-briefs is exactly how an operator lands
        // an owner brief on a pack sponsor in production.
        var briefCreate = await client.PostAsJsonAsync(
            "/api/ad-briefs", new { sponsorId = SponsorId, premise = "Signed-copy Saturdays" });
        if (briefCreate.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"fixture owner brief create failed: {await briefCreate.Content.ReadAsStringAsync()}");

        var uninstall = await client.DeleteAsync($"/api/ad-packs/{AdPackUninstallOwnerBriefFixtures.PackSlug}");
        UninstallStatus = uninstall.StatusCode;

        // Gated on the status, matching AdPackUninstallRetireArc's own reasoning one arc over: a 404/409
        // carries no keptSponsors body to parse — Response* simply stay at their own empty defaults
        // when this branch is skipped, so a mutation that breaks the ad_brief survivor clause and
        // collapses the outcome back to a nameless 409 turns a SPECIFIC assertion red rather than
        // crashing this arrange step for every Fact in the Scenario.
        if (UninstallStatus == HttpStatusCode.OK)
        {
            var uninstallBody = await JsonDocument.ParseAsync(await uninstall.Content.ReadAsStreamAsync());
            var keptSponsorsElement = uninstallBody.RootElement.GetProperty("keptSponsors");
            ResponseKeptSponsorIds = keptSponsorsElement.EnumerateArray().Select(e => e.GetProperty("id").GetInt64()).ToArray();
            ResponseKeptSponsorNames = keptSponsorsElement.EnumerateArray().Select(e => e.GetProperty("name").GetString() ?? "").ToArray();
        }

        await using var readConn = new NpgsqlConnection(database.StationConnectionString);
        await readConn.OpenAsync();
        OwnerBriefRowSurvives = await readConn.ExecuteScalarAsync<bool>(
            "select exists(select 1 from station.ad_brief where sponsor_id = @sponsorId and pack_slug is null)",
            new { sponsorId = SponsorId });
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/>
/// harness for <see cref="AdPackUninstallOwnerBriefArc"/> — see that type's own remarks.</summary>
file sealed class Story416OwnerBriefDatabase : EphemeralStationDatabase
{
    Story416OwnerBriefDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story416OwnerBriefDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t437b-obr");
        var db = new Story416OwnerBriefDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>One brand, one PACK brief, no spot history — <see cref="AdPackUninstallOwnerBriefArc"/>'s
/// own fixture (proving an OWNER brief, not spot history, is what keeps this
/// sponsor standing).</summary>
file static class AdPackUninstallOwnerBriefFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/ad-pack-uninstall-owner-brief-index.json";
    const string DirectoryUrl = "https://catalog.test/repo/";
    public const string PackSlug = "owner-brief-pack";
    public const string Brand = "Marlowe & Finch Books";

    static readonly string ManifestJson = $$"""
        { "packName": "Owner Brief Pack", "briefs": [ { "brand": "{{Brand}}", "premise": "New arrivals" } ] }
        """;

    public static FakeHttpMessageHandler BuildRoutedHandler() =>
        AdPackUninstallFixtureSupport.BuildRoutedHandler(IndexUrl, DirectoryUrl, new Dictionary<string, string> { [PackSlug] = ManifestJson });
}

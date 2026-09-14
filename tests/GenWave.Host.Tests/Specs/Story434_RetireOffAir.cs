// STORY-434 — Retiring a spot through the API takes it off the air (gh-#722 · PLAN T460)
//
// Runner: xUnit through the deployed entry point (WebApplicationFactory<Program> against a real
// ephemeral Postgres — the Story423_WriteJob.cs arc idiom). Every fact drives POST /api/ads/{id}/retire
// over HTTP with an authed admin session; the library truth is read straight off library.media and
// through the REAL IMediaCatalog.GetRandomReadyAdSpotAsync (the pick Liquidsoap's break actually
// runs), never AdsController/AdSpotRepository directly. RED at plan time: the retire action only flips
// station.ad_spot.state today — library.media.eligible stays true and the pick keeps returning the
// retired spot's media until the worker's RetireStaleAsync sweep eventually catches it.

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
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureRetiringASpotTakesItOffTheAir
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    [Collection(Story434Collection.Name)]
    public sealed class ScenarioRetireFlipsTheMediaIneligible(Story434Arc arc)
    {
        [Fact]
        public void TheMediaWasAirableBeforeRetire()
            => Assert.True(arc.PickBeforeRetireFoundTheSpot, "arrange: the seeded ready spot was never pickable in the first place");

        [Fact]
        public void RetireWas200()
            => Assert.Equal(HttpStatusCode.OK, arc.RetireStatus);

        [Fact]
        public void TheMediaRowIsIneligible()
            => Assert.False(arc.MediaEligibleAfterRetire);
    }

    [Collection(Story434Collection.Name)]
    public sealed class ScenarioThePickNeverReturnsTheRetiredSpot(Story434Arc arc)
    {
        [Fact]
        public void TheRandomReadyAdPickIsNull()
            => Assert.Null(arc.PickAfterRetire);
    }

    [Collection(Story434Collection.Name)]
    public sealed class ScenarioTheSpotRowIsRetired(Story434Arc arc)
    {
        [Fact]
        public void StateIsRetired()
            => Assert.Equal("retired", arc.RetiredState);
    }

    [Collection(Story434Collection.Name)]
    public sealed class ScenarioARetiredApprovedSpotHasNoMediaToFlip(Story434Arc arc)
    {
        [Fact]
        public void RetireWas200()
            => Assert.Equal(HttpStatusCode.OK, arc.ApprovedRetireStatus);

        [Fact]
        public void TheRowIsRetired()
            => Assert.Equal("retired", arc.ApprovedRetiredState);
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    [Collection(Story434Collection.Name)]
    public sealed class ScenarioAStaleIfMatchLeavesEligibilityAlone(Story434Arc arc)
    {
        [Fact]
        public void RetireWasRefused()
            => Assert.NotEqual(HttpStatusCode.OK, arc.StaleRetireStatus);

        [Fact]
        public void TheMediaRowIsStillEligible()
            => Assert.True(arc.StaleMediaStillEligible);
    }
}

[CollectionDefinition(Name)]
public sealed class Story434Collection : ICollectionFixture<Story434Arc>
{
    public const string Name = "Story434RetireOffAir";
}

public sealed class Story434Arc : IAsyncLifetime
{
    public bool PickBeforeRetireFoundTheSpot { get; private set; }
    public HttpStatusCode RetireStatus { get; private set; }
    public bool MediaEligibleAfterRetire { get; private set; } = true;
    public MediaReference? PickAfterRetire { get; private set; }
    public string? RetiredState { get; private set; }

    public HttpStatusCode ApprovedRetireStatus { get; private set; }
    public string? ApprovedRetiredState { get; private set; }

    public HttpStatusCode StaleRetireStatus { get; private set; }
    public bool StaleMediaStillEligible { get; private set; }

    /// <summary>db/01 seeds <c>default</c> as id=1, so the <c>ads</c> library lands at id=2 — matching
    /// <see cref="Story434WebFactory"/>'s <c>Station:Scope:LibraryIds</c> (the Story425 precedent).</summary>
    const long AdsLibraryId = 2;

    public async Task InitializeAsync()
    {
        await using var database = await Story434Database.StartAsync();
        var seededLibraryId = await SeedAdsLibraryAsync(database.LibraryConnectionString);
        if (seededLibraryId != AdsLibraryId)
            throw new InvalidOperationException($"arrange: the ads library landed at id {seededLibraryId}, not {AdsLibraryId}");

        await using var factory = new Story434WebFactory(database);
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Story434WebFactory.Password });
        if (login.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login unexpectedly returned {login.StatusCode}");

        var catalog = factory.Services.GetRequiredService<IMediaCatalog>();
        var scope = new LibraryScope([AdsLibraryId]);

        // ── AC1–AC3: one airable ad row, one ready spot pointing at it, retired for real. ──
        var mediaId = await InsertAirableAdMediaAsync(database.LibraryConnectionString, "/authored/ads/retire-1.wav", "Retire Test Brand");
        var (spotId, version) = await AdsWireFixtures.InsertReadySpotAsync(database.StationConnectionString, "Retire Test Brand", mediaId);

        var before = await catalog.GetRandomReadyAdSpotAsync(scope, [], CancellationToken.None);
        PickBeforeRetireFoundTheSpot = before is not null && before.MediaId == mediaId.ToString();

        var retire = new HttpRequestMessage(HttpMethod.Post, $"/api/ads/{spotId}/retire");
        retire.Headers.TryAddWithoutValidation("If-Match", $"W/\"{version}\"");
        var retireResponse = await client.SendAsync(retire);
        RetireStatus = retireResponse.StatusCode;

        MediaEligibleAfterRetire = (await AdSpotJobTestHelpers.ReadLibraryMediaAsync(database.LibraryConnectionString, mediaId)).Eligible;
        PickAfterRetire = await catalog.GetRandomReadyAdSpotAsync(scope, [], CancellationToken.None);
        RetiredState = (await AdSpotJobTestHelpers.GetSpotAsync(client, spotId)).GetProperty("state").GetString();

        // ── AC4: an approved spot (no media yet) retires cleanly — nothing to flip. ──
        var sponsorId = await AdSpotJobTestHelpers.CreateSponsorAsync(client, "Harbor Lane Bakery");
        var (approvedId, approveEtag) = await AdSpotJobTestHelpers.CreateDraftSpotWithScriptAsync(
            client, sponsorId, "Approved then retired",
            "ANNOUNCER: Harbor Lane Bakery has a deal so good it's almost illegal.\nANNOUNCER: Call 555-0184 - that's 555-0184 - Harbor Lane.");
        var approve = new HttpRequestMessage(HttpMethod.Post, $"/api/ads/{approvedId}/approve");
        approve.Headers.TryAddWithoutValidation("If-Match", approveEtag);
        var approveResponse = await client.SendAsync(approve);
        if (approveResponse.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"arrange: approve unexpectedly returned {approveResponse.StatusCode}");

        var (_, freshEtag) = await AdSpotJobTestHelpers.GetSpotWithETagAsync(client, approvedId);
        var retireApproved = new HttpRequestMessage(HttpMethod.Post, $"/api/ads/{approvedId}/retire");
        retireApproved.Headers.TryAddWithoutValidation("If-Match", freshEtag);
        var retireApprovedResponse = await client.SendAsync(retireApproved);
        ApprovedRetireStatus = retireApprovedResponse.StatusCode;
        ApprovedRetiredState = (await AdSpotJobTestHelpers.GetSpotAsync(client, approvedId)).GetProperty("state").GetString();

        // ── AC5 (sad): a stale If-Match is refused and the media stays airable. ──
        var staleMediaId = await InsertAirableAdMediaAsync(database.LibraryConnectionString, "/authored/ads/retire-2.wav", "Stale Retire Brand");
        var (staleSpotId, _) = await AdsWireFixtures.InsertReadySpotAsync(database.StationConnectionString, "Stale Retire Brand", staleMediaId);
        var staleRetire = new HttpRequestMessage(HttpMethod.Post, $"/api/ads/{staleSpotId}/retire");
        staleRetire.Headers.TryAddWithoutValidation("If-Match", "W/\"1\"");
        StaleRetireStatus = (await client.SendAsync(staleRetire)).StatusCode;
        StaleMediaStillEligible = (await AdSpotJobTestHelpers.ReadLibraryMediaAsync(database.LibraryConnectionString, staleMediaId)).Eligible;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    static async Task<long> SeedAdsLibraryAsync(string libraryConnectionString)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "insert into library.library (name) values ('ads') returning id";
        return (long)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("insert returned no id"));
    }

    /// <summary>A <c>library.media</c> row that satisfies every predicate of
    /// <c>MediaRepository.GetRandomReadyAdSpotAsync</c> (ready, measurable, eligible, <c>imaging_kind =
    /// 'ad'</c>, in the ads library) — the shape <c>InsertAuthoredAsync</c> lands for a rendered spot.</summary>
    static async Task<long> InsertAirableAdMediaAsync(string libraryConnectionString, string path, string title)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            insert into library.media (
              path, format, size_bytes, mtime, state, library_id, duration_ms, title, artist,
              integrated_lufs, true_peak_dbtp, measurable, imaging_kind, eligible)
            values (@path, 'wav', 1024, now(), 'ready', @libraryId, 7000, @title, 'GWAV 108.8',
              -16.0, -1.0, true, 'ad', true)
            returning id
            """;
        cmd.Parameters.AddWithValue("path", path);
        cmd.Parameters.AddWithValue("title", title);
        cmd.Parameters.AddWithValue("libraryId", AdsLibraryId);
        return (long)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("insert returned no id"));
    }
}

file sealed class Story434WebFactory(EphemeralStationDatabase db) : WebApplicationFactory<Program>
{
    public const string Password = "test-password-t460-retire-off-air";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", db.LibraryConnectionString);
        builder.UseSetting("ConnectionStrings:Station", db.StationConnectionString);
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "2");
        builder.UseSetting("Ads:LibraryName", "ads");

        // No hosted service — in particular no AdSpotWorker, whose RetireStaleAsync sweep is the ONLY
        // thing that flips eligibility today; these facts must see the retire action do it itself.
        builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
    }
}

file sealed class Story434Database : EphemeralStationDatabase
{
    Story434Database(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story434Database> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t460c");
        var db = new Story434Database(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

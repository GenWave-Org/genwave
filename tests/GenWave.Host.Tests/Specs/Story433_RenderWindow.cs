// STORY-433 — Approve tells me when the station will render it (API half: AC1–AC3 · gh-#745 · PLAN T457)
// The UI half (AC4–AC7, the toast copy) lives in admin-ui/__specs__/approve-copy.spec.tsx.
//
// Runner: xUnit through the deployed entry point (WebApplicationFactory<Program> against a real
// ephemeral Postgres — the Story423_WriteJob.cs arc idiom). Every fact reads GET /api/ads/{id} over
// HTTP with an authed admin session; the spot is approved through POST /api/ads/{id}/approve for
// real. No hosted service runs, so an approved spot STAYS approved for the read. Two factories share
// the one database: Ads:WorkerIntervalMinutes=10 (the default) and =3, proving the window follows the
// configured interval rather than a hard-coded ten. RED at plan time: AdSpotDto has no
// renderWithinMinutes property yet, so every JsonValueKind read below is Undefined.

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

public static class FeatureApproveTellsMeWhenTheStationWillRenderIt
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    [Collection(Story433Collection.Name)]
    public sealed class ScenarioAnApprovedSpotCarriesTheRenderWindow(Story433Arc arc)
    {
        [Fact]
        public void ApproveWas200()
            => Assert.Equal(HttpStatusCode.OK, arc.DefaultApproveStatus);

        [Fact]
        public void TheSpotIsStillApprovedWhenRead()
            => Assert.Equal("approved", arc.DefaultApprovedState);

        [Fact]
        public void RenderWithinMinutesIsTen()
            => Assert.Equal(10, arc.DefaultRenderWithinMinutes);
    }

    [Collection(Story433Collection.Name)]
    public sealed class ScenarioTheWindowFollowsTheConfiguredInterval(Story433Arc arc)
    {
        [Fact]
        public void RenderWithinMinutesIsThree()
            => Assert.Equal(3, arc.ShortIntervalRenderWithinMinutes);
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated) — the window is null for every other state
    // ---------------------------------------------------------------------

    [Collection(Story433Collection.Name)]
    public sealed class ScenarioTheWindowIsNullOutsideApproved(Story433Arc arc)
    {
        [Fact]
        public void AReadySpotHasANullWindow()
            => Assert.Equal(JsonValueKind.Null, arc.ReadyRenderWithinMinutesKind);

        [Fact]
        public void ADraftSpotHasANullWindow()
            => Assert.Equal(JsonValueKind.Null, arc.DraftRenderWithinMinutesKind);
    }
}

[CollectionDefinition(Name)]
public sealed class Story433Collection : ICollectionFixture<Story433Arc>
{
    public const string Name = "Story433RenderWindow";
}

public sealed class Story433Arc : IAsyncLifetime
{
    const string Story433Password = "test-password-t457-render-window";

    public HttpStatusCode DefaultApproveStatus { get; private set; }
    public string? DefaultApprovedState { get; private set; }
    public int? DefaultRenderWithinMinutes { get; private set; }
    public int? ShortIntervalRenderWithinMinutes { get; private set; }
    public JsonValueKind ReadyRenderWithinMinutesKind { get; private set; }
    public JsonValueKind DraftRenderWithinMinutesKind { get; private set; }

    const string ApprovableScript =
        "ANNOUNCER: Marlow's Garden Supply has a deal so good it's almost illegal.\n" +
        "ANNOUNCER: Call 555-0190 - that's 555-0190 - Marlow's.";

    public async Task InitializeAsync()
    {
        await using var database = await Story433Database.StartAsync();

        // ── Default interval (10) ──
        await using (var factory = new Story433WebFactory(database, workerIntervalMinutes: 10))
        {
            var client = await LoginAsync(factory);
            var sponsorId = await AdSpotJobTestHelpers.CreateSponsorAsync(client, "Marlow's Garden Supply");

            var (approvedId, etag) = await AdSpotJobTestHelpers.CreateDraftSpotWithScriptAsync(
                client, sponsorId, "Window spot", ApprovableScript);
            var approve = new HttpRequestMessage(HttpMethod.Post, $"/api/ads/{approvedId}/approve");
            approve.Headers.TryAddWithoutValidation("If-Match", etag);
            var approveResponse = await client.SendAsync(approve);
            DefaultApproveStatus = approveResponse.StatusCode;

            var approvedBody = await AdSpotJobTestHelpers.GetSpotAsync(client, approvedId);
            DefaultApprovedState = approvedBody.GetProperty("state").GetString();
            DefaultRenderWithinMinutes = ReadWindow(approvedBody);

            // AC3 — a ready spot (seeded via SQL, the Story392 retire precedent) and a plain draft.
            var (readyId, _) = await AdsWireFixtures.InsertReadySpotAsync(
                database.StationConnectionString, brand: "Window Ready Brand", mediaId: 999_998);
            ReadyRenderWithinMinutesKind = WindowKind(await AdSpotJobTestHelpers.GetSpotAsync(client, readyId));

            var draftId = await AdSpotJobTestHelpers.CreateDraftSpotAsync(client, sponsorId, "Draft spot", "A brief.");
            DraftRenderWithinMinutesKind = WindowKind(await AdSpotJobTestHelpers.GetSpotAsync(client, draftId));
        }

        // ── Short interval (3) — same database, a second host reading a different Ads:WorkerIntervalMinutes. ──
        await using (var factory = new Story433WebFactory(database, workerIntervalMinutes: 3))
        {
            var client = await LoginAsync(factory);
            var sponsorId = await AdSpotJobTestHelpers.CreateSponsorAsync(client, "Quick Fox Couriers");
            var (approvedId, etag) = await AdSpotJobTestHelpers.CreateDraftSpotWithScriptAsync(
                client, sponsorId, "Short window spot",
                "ANNOUNCER: Quick Fox Couriers has a deal so good it's almost illegal.\nANNOUNCER: Call 555-0191 - that's 555-0191 - Quick Fox.");
            var approve = new HttpRequestMessage(HttpMethod.Post, $"/api/ads/{approvedId}/approve");
            approve.Headers.TryAddWithoutValidation("If-Match", etag);
            var approveResponse = await client.SendAsync(approve);
            if (approveResponse.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"arrange: approve unexpectedly returned {approveResponse.StatusCode}");

            ShortIntervalRenderWithinMinutes = ReadWindow(await AdSpotJobTestHelpers.GetSpotAsync(client, approvedId));
        }
    }

    static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Story433Password });
        if (login.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login unexpectedly returned {login.StatusCode}");
        return client;
    }

    static JsonValueKind WindowKind(JsonElement body) =>
        body.TryGetProperty("renderWithinMinutes", out var window) ? window.ValueKind : JsonValueKind.Undefined;

    static int? ReadWindow(JsonElement body) =>
        body.TryGetProperty("renderWithinMinutes", out var window) && window.ValueKind == JsonValueKind.Number
            ? window.GetInt32()
            : null;

    public Task DisposeAsync() => Task.CompletedTask;
}

file sealed class Story433WebFactory(EphemeralStationDatabase db, int workerIntervalMinutes)
    : WebApplicationFactory<Program>
{
    public const string Password = "test-password-t457-render-window";

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
        builder.UseSetting("Ads:WorkerIntervalMinutes", workerIntervalMinutes.ToString());

        // No hosted service at all — the render worker must NOT run, or the approved spot would not
        // stay approved long enough to read its window.
        builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
    }
}

file sealed class Story433Database : EphemeralStationDatabase
{
    Story433Database(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story433Database> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t457c");
        var db = new Story433Database(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

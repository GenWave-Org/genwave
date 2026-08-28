// STORY-366 — My sensor works on the appliance with the admin plane off (SPEC F145.6 · PLAN T351)
//
// BDD specification — xUnit. WIRED T351. Entry-point discipline: every fact drives the REAL
// production binary (WebApplicationFactory<Program>, the Story345 factory idiom over an ephemeral
// Postgres — see SensorGateStationDatabase/SensorGateWebFactory at the bottom of this file, mirroring
// PaWireProofWebFactory's own ConnectionStrings/UseSetting shape) — two factories on the SAME station
// db: one with Admin:Enabled=true to mint the token through POST /api/announcements/token
// (reveal-once), then one with Admin:Enabled=false (the compose.demo.yaml posture) driven with ONLY
// that Bearer. F145.6: the token-authed now-playing read answers with the admin plane off; submit and
// the token endpoints stay admin-surface (404).
//
// Two Arcs, two ephemeral databases (the Story345 "each Scenario group arranges its own Postgres
// exactly once" idiom) — shared across MULTIPLE Scenario classes here via xUnit's
// ICollectionFixture<T>/[Collection] (not IClassFixture<T>, which would instantiate a FRESH fixture
// per test class and multiply the Postgres startup): HappyPathArc covers AC1/AC2/AC3/AC4 (one mint,
// exercised both admin-on and admin-off against the same db) PLUS F145.6's "public or private
// station" half (the T351 review round-2 finding: Admin:Enabled=false AND Station:SpectatorMode=true
// still answers 200 on the token read — a third factory instance on the SAME db/token, added to this
// Arc rather than a new one, since it needs nothing SadPathArc's own db doesn't already share);
// SadPathArc covers AC6/AC7 (no token, then a minted-then-revoked one, against the same db, in that
// order — so AC6 genuinely observes "no hash row" before anything is ever minted).

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Host.Api;
using GenWave.Host.Playout;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

[CollectionDefinition(Name)]
public sealed class Story366HappyPathCollection : ICollectionFixture<HappyPathArc>
{
    public const string Name = "Story366HappyPath";
}

[CollectionDefinition(Name)]
public sealed class Story366SadPathCollection : ICollectionFixture<SadPathArc>
{
    public const string Name = "Story366SadPath";
}

// ---------------------------------------------------------------------
// HAPPY PATH — the read answers, everything else stays dark
// ---------------------------------------------------------------------

[Collection(Story366HappyPathCollection.Name)]
public sealed class ScenarioTheReadAnswersWithTheAdminPlaneOff(HappyPathArc arc)
{
    // Given Admin:Enabled=false and a minted token, When GET /api/announcements/now-playing.
    [Fact]
    public void TheResponseIsTwoHundred() => Assert.Equal(HttpStatusCode.OK, arc.ReadStatusAdminOff);

    [Fact]
    public void TheBodyIsTheNowPlayingSnapshot() =>
        Assert.Equal(new AnnouncementNowPlayingDto("A Song", "An Artist", "Flip"), arc.ReadBodyAdminOff);
}

[Collection(Story366HappyPathCollection.Name)]
public sealed class ScenarioSubmitStaysAdminSurface(HappyPathArc arc)
{
    // Same station and token, When POST /api/announcements.
    [Fact]
    public void TheResponseIsFourOhFour() => Assert.Equal(HttpStatusCode.NotFound, arc.SubmitStatusAdminOff);
}

[Collection(Story366HappyPathCollection.Name)]
public sealed class ScenarioTheTokenEndpointsStayAdminSurface(HappyPathArc arc)
{
    [Fact]
    public void PostTokenIsFourOhFour() => Assert.Equal(HttpStatusCode.NotFound, arc.PostTokenStatusAdminOff);

    [Fact]
    public void DeleteTokenIsFourOhFour() => Assert.Equal(HttpStatusCode.NotFound, arc.DeleteTokenStatusAdminOff);
}

[Collection(Story366HappyPathCollection.Name)]
public sealed class ScenarioTheReadStillWorksWithTheAdminPlaneOn(HappyPathArc arc)
{
    // Given Admin:Enabled=true, When the read is called with the token, and again with a cookie.
    [Fact]
    public void TheTokenReadIsTwoHundred() => Assert.Equal(HttpStatusCode.OK, arc.TokenReadStatusAdminOn);

    [Fact]
    public void TheCookieReadIsTwoHundred() => Assert.Equal(HttpStatusCode.OK, arc.CookieReadStatusAdminOn);
}

[Collection(Story366HappyPathCollection.Name)]
public sealed class ScenarioTheReadAnswersWithAdminOffAndSpectatorModeOn(HappyPathArc arc)
{
    // F145.6's "public or private station" half: given Admin:Enabled=false AND
    // Station:SpectatorMode=true (the public-appliance posture) and a minted token, When
    // GET /api/announcements/now-playing — the token door's own fail-closed contract is the
    // privacy floor here, never a SpectatorMode check the read would otherwise have to duplicate.
    [Fact]
    public void TheResponseIsTwoHundred() => Assert.Equal(HttpStatusCode.OK, arc.SpectatorModeReadStatusAdminOff);
}

// ---------------------------------------------------------------------
// SAD PATH — no token, no read
// ---------------------------------------------------------------------

[Collection(Story366SadPathCollection.Name)]
public sealed class ScenarioNoTokenRowNoRead(SadPathArc arc)
{
    // Given Admin:Enabled=false and no token ever minted, When any Bearer value is sent.
    [Fact]
    public void TheResponseIsFourOhOne() => Assert.Equal(HttpStatusCode.Unauthorized, arc.NoTokenReadStatus);
}

[Collection(Story366SadPathCollection.Name)]
public sealed class ScenarioARevokedTokenIsRefusedOnTheReadToo(SadPathArc arc)
{
    // Given a token minted then revoked (DELETE /api/announcements/token with admin on).
    [Fact]
    public void TheResponseIsFourOhOne() => Assert.Equal(HttpStatusCode.Unauthorized, arc.RevokedReadStatus);
}

// ── Arc fixtures — each arranges its own ephemeral Postgres + production host exactly ONCE
// (IAsyncLifetime.InitializeAsync, shared across every Scenario class in its collection via
// ICollectionFixture<T>) and tears both factories/the database down before any Fact runs — only the
// captured VALUES below survive for the Facts to read. ─────────────────────────────────────────────

public sealed class HappyPathArc : IAsyncLifetime
{
    public HttpStatusCode ReadStatusAdminOff { get; private set; }
    public AnnouncementNowPlayingDto? ReadBodyAdminOff { get; private set; }
    public HttpStatusCode SubmitStatusAdminOff { get; private set; }
    public HttpStatusCode PostTokenStatusAdminOff { get; private set; }
    public HttpStatusCode DeleteTokenStatusAdminOff { get; private set; }
    public HttpStatusCode TokenReadStatusAdminOn { get; private set; }
    public HttpStatusCode CookieReadStatusAdminOn { get; private set; }
    public HttpStatusCode SpectatorModeReadStatusAdminOff { get; private set; }

    public async Task InitializeAsync()
    {
        await using var db = await SensorGateStationDatabase.StartAsync();

        // ── Admin plane ON: log in, mint the token (reveal-once), and prove the read still works
        // for BOTH credentials while the plane is on (AC4) — done here, on this factory, while the
        // session and the freshly revealed plaintext are both still in hand.
        await using var adminOnFactory = new SensorGateWebFactory(db, adminEnabled: true);
        var loggedInClient = adminOnFactory.CreateClient();
        await SensorGateSupport.LoginAsync(loggedInClient, SensorGateWebFactory.Password);

        var generate = await loggedInClient.PostAsync("/api/announcements/token", content: null);
        var generatedDto = await generate.Content.ReadFromJsonAsync<AnnounceTokenGeneratedDto>()
            ?? throw new InvalidOperationException("token generate unexpectedly returned no body");
        var plaintext = generatedDto.Token;

        var tokenReadOnAdminOn = adminOnFactory.CreateClient();
        tokenReadOnAdminOn.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", plaintext);
        TokenReadStatusAdminOn = (await tokenReadOnAdminOn.GetAsync("/api/announcements/now-playing")).StatusCode;

        CookieReadStatusAdminOn = (await loggedInClient.GetAsync("/api/announcements/now-playing")).StatusCode;

        // ── Admin plane OFF, same station db, ONLY the Bearer token from above (SPEC F145.6).
        await using var adminOffFactory = new SensorGateWebFactory(db, adminEnabled: false);

        // A real on-air snapshot for AC1's body assertion — NowPlayingService is purely in-memory, so
        // it must be seeded on THIS factory's own container (the admin-on factory above has its own,
        // separate instance).
        adminOffFactory.Services.GetRequiredService<NowPlayingService>().Update(
            SingleStation.IdString,
            new NowPlayingSnapshot(
                MediaId: "42", Title: "A Song", Artist: "An Artist", GainDb: 0, StartedAt: DateTimeOffset.UtcNow,
                DurationMs: 180_000, IsDrain: false, DjName: "Flip"));

        var bearerOnlyClient = adminOffFactory.CreateClient();
        bearerOnlyClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", plaintext);

        var readResponse = await bearerOnlyClient.GetAsync("/api/announcements/now-playing");
        ReadStatusAdminOff = readResponse.StatusCode;
        ReadBodyAdminOff = await readResponse.Content.ReadFromJsonAsync<AnnouncementNowPlayingDto>();

        var submitResponse = await bearerOnlyClient.PostAsJsonAsync(
            "/api/announcements", new { message = "From the smart speaker" });
        SubmitStatusAdminOff = submitResponse.StatusCode;

        PostTokenStatusAdminOff = (await bearerOnlyClient.PostAsync("/api/announcements/token", content: null)).StatusCode;
        DeleteTokenStatusAdminOff = (await bearerOnlyClient.DeleteAsync("/api/announcements/token")).StatusCode;

        // ── Admin plane OFF AND Station:SpectatorMode ON (the PUBLIC-appliance posture, F145.6's
        // "public or private station" half) — same station db, same Bearer token; a separate
        // container instance again needs its own NowPlayingService seed.
        await using var adminOffSpectatorFactory = new SensorGateWebFactory(db, adminEnabled: false, spectatorMode: true);
        adminOffSpectatorFactory.Services.GetRequiredService<NowPlayingService>().Update(
            SingleStation.IdString,
            new NowPlayingSnapshot(
                MediaId: "42", Title: "A Song", Artist: "An Artist", GainDb: 0, StartedAt: DateTimeOffset.UtcNow,
                DurationMs: 180_000, IsDrain: false, DjName: "Flip"));

        var spectatorBearerClient = adminOffSpectatorFactory.CreateClient();
        spectatorBearerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", plaintext);
        SpectatorModeReadStatusAdminOff = (await spectatorBearerClient.GetAsync("/api/announcements/now-playing")).StatusCode;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public sealed class SadPathArc : IAsyncLifetime
{
    public HttpStatusCode NoTokenReadStatus { get; private set; }
    public HttpStatusCode RevokedReadStatus { get; private set; }

    public async Task InitializeAsync()
    {
        await using var db = await SensorGateStationDatabase.StartAsync();
        await using var adminOffFactory = new SensorGateWebFactory(db, adminEnabled: false);

        // AC6 — no token has EVER been minted on this station yet (a fresh db): any Bearer value is
        // refused, the SPEC F145.4 "no hash row" fail-closed state.
        var neverMintedClient = adminOffFactory.CreateClient();
        neverMintedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "any-value-whatsoever");
        NoTokenReadStatus = (await neverMintedClient.GetAsync("/api/announcements/now-playing")).StatusCode;

        // AC7 — mint then revoke (admin plane on, session-only door), then the SAME plaintext is
        // refused on the admin-off read.
        await using var adminOnFactory = new SensorGateWebFactory(db, adminEnabled: true);
        var loggedInClient = adminOnFactory.CreateClient();
        await SensorGateSupport.LoginAsync(loggedInClient, SensorGateWebFactory.Password);

        var generate = await loggedInClient.PostAsync("/api/announcements/token", content: null);
        var generatedDto = await generate.Content.ReadFromJsonAsync<AnnounceTokenGeneratedDto>()
            ?? throw new InvalidOperationException("token generate unexpectedly returned no body");
        var plaintext = generatedDto.Token;
        await loggedInClient.DeleteAsync("/api/announcements/token");

        var revokedClient = adminOffFactory.CreateClient();
        revokedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", plaintext);
        RevokedReadStatus = (await revokedClient.GetAsync("/api/announcements/now-playing")).StatusCode;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Test harness ───────────────────────────────────────────────────────────────────────────────────

/// <summary>Shared plumbing both Arc fixtures above call.</summary>
file static class SensorGateSupport
{
    public static async Task LoginAsync(HttpClient client, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { password });
        if (response.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login unexpectedly returned {response.StatusCode}");
    }
}

/// <summary>
/// This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/> harness
/// (Support/EphemeralStationDatabase.cs — T351 review hoist, replacing what used to be a verbatim
/// copy of Story345_PaWireProof.cs's own <c>TestStationDatabase</c>; that shared type's own remarks
/// carry the full "which compose file, why a unique project name + OS-assigned port" rationale).
/// Supplies only what genuinely varies for THIS file: the <c>"genwave-sensorgate"</c> compose
/// project-name prefix — this file needs no extra query method the base doesn't already provide.
/// </summary>
file sealed class SensorGateStationDatabase : EphemeralStationDatabase
{
    SensorGateStationDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<SensorGateStationDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-sensorgate");
        var db = new SensorGateStationDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>
/// Boots the real production composition root (Program.cs) against a real ephemeral Postgres
/// (<see cref="SensorGateStationDatabase"/>) — mirrors PaWireProofWebFactory's (Story345_PaWireProof.cs)
/// own "only IHostedService removed, everything else genuine" posture, narrowed to just the
/// ConnectionStrings/Admin/Station keys this file's own Facts ever touch (no Tts/Llm endpoint is ever
/// reached here — submit 404s at the surface gate before any render, and the token/read paths never
/// synthesize anything — so appsettings.Development.json's own shipped Tts/Llm defaults are left
/// untouched). <paramref name="adminEnabled"/> is the main axis this file's own ACs turn on;
/// <paramref name="spectatorMode"/> (default off) is the second axis
/// <see cref="ScenarioTheReadAnswersWithAdminOffAndSpectatorModeOn"/> turns on, proving F145.6's
/// "public or private station" half — the token read never gates on <c>Station:SpectatorMode</c> the
/// way <c>AnnouncementsController.Post</c>'s own SpectatorMode check does.
/// </summary>
file sealed class SensorGateWebFactory(SensorGateStationDatabase db, bool adminEnabled, bool spectatorMode = false)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story366-sensor-gate";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", db.LibraryConnectionString);
        builder.UseSetting("ConnectionStrings:Station", db.StationConnectionString);
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Admin:Enabled", adminEnabled ? "true" : "false");
        builder.UseSetting("Station:SpectatorMode", spectatorMode ? "true" : "false");

        // The exact four Station:* keys compose.yaml itself overrides in production (grep compose.yaml
        // for Station__Id/Station__Name/Station__Voice/Station__Scope__LibraryIds__0) — mirrors
        // PaWireProofWebFactory's own precedent.
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");

        builder.ConfigureTestServices(services =>
        {
            // No Liquidsoap/real-background-loop reach during this test — mirrors every other
            // WebApplicationFactory-based spec in this suite.
            services.RemoveAll<IHostedService>();
        });
    }
}

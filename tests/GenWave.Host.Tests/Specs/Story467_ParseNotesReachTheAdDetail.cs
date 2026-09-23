// STORY-467 — Parse notes reach the ad detail (gh-#742 · SPEC F200.3 · PLAN T551)
//
// BDD specification — xUnit through the deployed entry point (WebApplicationFactory<Program> against
// a real ephemeral Postgres — the Story392_AdsApi.cs/Story435_FailedJobKind.cs arc idiom): POST
// /api/ads with a script carrying a NARRATOR line (an unknown speaker tag, SPEC F200.1), then GET
// /api/ads/{id} and read the wire's own parseNotes field — never AdsController/AdScriptParseNotes
// directly.

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureParsenotesreachtheaddetail
{
    [Collection(Story467Collection.Name)]
    public sealed class ScenarioGetAdById(Story467Arc arc)
    {
        // Given: WebApplicationFactory, an ad whose script carried a NARRATOR line, GET /api/ads/{id}

        /// <summary>AC5 — parseNotes contains the note</summary>
        [Fact]
        public void ServesTheNote() =>
            Assert.Contains("unknown-tag:NARRATOR", arc.ParseNotes);
    }
}

[CollectionDefinition(Name)]
public sealed class Story467Collection : ICollectionFixture<Story467Arc>
{
    public const string Name = "Story467ParseNotesReachTheAdDetail";
}

public sealed class Story467Arc : IAsyncLifetime
{
    const string NarratorLineScript =
        "ANNOUNCER: Cravin's Diner has a deal so good it's almost illegal.\n" +
        "VOICE1: Almost. Stop by tonight.\n" +
        "NARRATOR: In a world of ordinary diners...";

    public IReadOnlyList<string> ParseNotes { get; private set; } = [];

    public async Task InitializeAsync()
    {
        await using var database = await Story467Database.StartAsync();
        await using var factory = new Story467WebFactory(database);
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new { password = Story467WebFactory.Password });
        if (login.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login unexpectedly returned {login.StatusCode}");

        var sponsorId = await AdSpotJobTestHelpers.CreateSponsorAsync(client, "Narrator Line Sponsor");
        var (spotId, _) = await AdSpotJobTestHelpers.CreateDraftSpotWithScriptAsync(
            client, sponsorId, "Narrator line spot", NarratorLineScript);

        var body = await AdSpotJobTestHelpers.GetSpotAsync(client, spotId);
        ParseNotes = body.GetProperty("parseNotes").EnumerateArray().Select(note => note.GetString() ?? "").ToList();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

file sealed class Story467WebFactory(EphemeralStationDatabase db) : WebApplicationFactory<Program>
{
    public const string Password = "test-password-t551-parse-notes";

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

file sealed class Story467Database : EphemeralStationDatabase
{
    Story467Database(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story467Database> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t551");
        var db = new Story467Database(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

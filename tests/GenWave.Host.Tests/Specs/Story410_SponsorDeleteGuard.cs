// STORY-410 — Deleting a sponsor is guarded (SPEC F171.5 · PLAN T434)
//
// BDD specification — xUnit through the deployed entry point (WebApplicationFactory<Program> against
// a real ephemeral Postgres — the Story392_AdBriefsApi.cs arc idiom): every fact drives
// DELETE/POST /api/sponsors/* over HTTP with an authed admin session, never ISponsorStore/
// SponsorsController directly. Two arcs (the unreferenced-delete happy path needs its OWN sponsor —
// deleting it would otherwise poison the referenced-sponsor arc's own fixtures if they shared one).
// The referenced sponsor's 2 briefs / 5 spots (across states) / 1 show are seeded directly via raw SQL
// (the AdBriefWireFixtures precedent): no brief/spot/show-authoring endpoint under test here, so they
// can only ever be arranged independently of the API under test.

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

public static class FeatureDeletingASponsorIsGuarded
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — nothing references the sponsor
    // ---------------------------------------------------------------------

    [Collection(Story410Collection.Name)]
    public sealed class ScenarioDeleteSucceedsWhenUnreferenced(Story410Arc arc)
    {
        [Fact]
        public void DeleteIs204()
            => Assert.Equal(HttpStatusCode.NoContent, arc.UnreferencedDeleteStatus);

        [Fact]
        public void TheRowIsGone()
            => Assert.Equal(HttpStatusCode.NotFound, arc.UnreferencedGetAfterDeleteStatus);
    }

    // ---------------------------------------------------------------------
    // Pause covers the "keep it around" case
    // ---------------------------------------------------------------------

    [Collection(Story410Collection.Name)]
    public sealed class ScenarioPauseCoversTheKeepItAroundCase(Story410Arc arc)
    {
        [Fact]
        public void PauseOnAReferencedSponsorIs200()
            => Assert.Equal(HttpStatusCode.OK, arc.PauseOnReferencedStatus);
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated) — briefs/spots/shows still reference the sponsor
    // ---------------------------------------------------------------------

    [Collection(Story410Collection.Name)]
    public sealed class ScenarioRefuseNamesTheCounts(Story410Arc arc)
    {
        [Fact]
        public void DeleteOnAReferencedSponsorIs409SponsorInUse()
        {
            Assert.Equal(HttpStatusCode.Conflict, arc.ReferencedDeleteStatus);
            Assert.Equal("sponsor_in_use", arc.ReferencedDeleteType);
        }

        [Fact]
        public void TheDetailNamesBriefsSpotsAndShowsCounts()
        {
            Assert.Equal(2, arc.ReferencedDeleteBriefs);
            Assert.Equal(5, arc.ReferencedDeleteSpots);
            Assert.Equal(1, arc.ReferencedDeleteShows);
        }

        [Fact]
        public void TheDetailListsTheFirstTenReferencingTitles()
        {
            Assert.True(arc.ReferencedDeleteTitles.Count is > 0 and <= 10);
            Assert.Contains(Story410Arc.ShowName, arc.ReferencedDeleteTitles);
        }

        [Fact]
        public void NothingWasRemoved()
            => Assert.Equal(HttpStatusCode.OK, arc.ReferencedGetAfterFailedDeleteStatus);
    }
}

// ── Collection definition — one ephemeral Postgres/factory shared by every Scenario above (the
// Story392_AdBriefsApi.cs "arrange once, many read-only Scenarios" idiom). ──

[CollectionDefinition(Name)]
public sealed class Story410Collection : ICollectionFixture<Story410Arc>
{
    public const string Name = "Story410SponsorDeleteGuard";
}

/// <summary>
/// Arranges every fact STORY-410's Scenarios read, entirely over the REAL production HTTP pipeline
/// with a real admin session — no <c>SponsorRepository</c> call, no <c>SponsorsController</c> call,
/// anywhere in this class except the raw-SQL brief/spot/show seed (no authoring endpoint for any of
/// the three exists under THIS controller to arrange them through instead).
/// </summary>
public sealed class Story410Arc : IAsyncLifetime
{
    public const string ShowName = "Cascade Morning Drive";

    public HttpStatusCode UnreferencedDeleteStatus { get; private set; }
    public HttpStatusCode UnreferencedGetAfterDeleteStatus { get; private set; }

    public HttpStatusCode PauseOnReferencedStatus { get; private set; }

    public HttpStatusCode ReferencedDeleteStatus { get; private set; }
    public string? ReferencedDeleteType { get; private set; }
    public int ReferencedDeleteBriefs { get; private set; }
    public int ReferencedDeleteSpots { get; private set; }
    public int ReferencedDeleteShows { get; private set; }
    public IReadOnlyList<string> ReferencedDeleteTitles { get; private set; } = [];

    public HttpStatusCode ReferencedGetAfterFailedDeleteStatus { get; private set; }

    public async Task InitializeAsync()
    {
        // A LOCAL, not a field — Story410Database is file-local (CS9051), the Story392AdBriefsDatabase
        // precedent.
        await using var database = await Story410Database.StartAsync();

        var referencedSponsorId = await Story410WireFixtures.InsertReferencedSponsorAsync(
            database.StationConnectionString, "Cascade Roofing", ShowName);

        await using var factory = new Story410WebFactory(database);
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new { password = Story410WebFactory.Password });
        if (login.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login unexpectedly returned {login.StatusCode}");

        // ── AC1: an unreferenced sponsor's own delete succeeds and the row is gone. ──
        var unreferencedCreateResponse = await client.PostAsJsonAsync("/api/sponsors", new { name = "Lone Pine Diner" });
        var unreferencedCreated = await JsonDocument.ParseAsync(await unreferencedCreateResponse.Content.ReadAsStreamAsync());
        var unreferencedId = unreferencedCreated.RootElement.GetProperty("id").GetInt64();

        var unreferencedDeleteResponse = await client.DeleteAsync($"/api/sponsors/{unreferencedId}");
        UnreferencedDeleteStatus = unreferencedDeleteResponse.StatusCode;

        var unreferencedGetAfterDeleteResponse = await client.GetAsync($"/api/sponsors/{unreferencedId}");
        UnreferencedGetAfterDeleteStatus = unreferencedGetAfterDeleteResponse.StatusCode;

        // ── Pause is the "keep it around" alternative to a refused delete — proven on the SAME
        // referenced sponsor the AC2 refusal below targets. ──
        var pauseOnReferencedResponse = await client.PostAsync($"/api/sponsors/{referencedSponsorId}/pause", content: null);
        PauseOnReferencedStatus = pauseOnReferencedResponse.StatusCode;
        // Resumed immediately after: pausing must not itself change whether the sponsor is
        // referenced, and the refusal below is what's actually under test.
        await client.PostAsync($"/api/sponsors/{referencedSponsorId}/resume", content: null);

        // ── AC2: a referenced sponsor's delete is refused, names the counts and first ten titles, and
        // removes nothing. ──
        var referencedDeleteResponse = await client.DeleteAsync($"/api/sponsors/{referencedSponsorId}");
        ReferencedDeleteStatus = referencedDeleteResponse.StatusCode;
        var referencedDeleteBody = await JsonDocument.ParseAsync(await referencedDeleteResponse.Content.ReadAsStreamAsync());
        ReferencedDeleteType = referencedDeleteBody.RootElement.TryGetProperty("type", out var typeProperty)
            ? typeProperty.GetString() : null;
        ReferencedDeleteBriefs = referencedDeleteBody.RootElement.GetProperty("briefs").GetInt32();
        ReferencedDeleteSpots = referencedDeleteBody.RootElement.GetProperty("spots").GetInt32();
        ReferencedDeleteShows = referencedDeleteBody.RootElement.GetProperty("shows").GetInt32();
        ReferencedDeleteTitles = referencedDeleteBody.RootElement.GetProperty("titles")
            .EnumerateArray().Select(t => t.GetString() ?? "").ToList();

        var referencedGetAfterFailedDeleteResponse = await client.GetAsync($"/api/sponsors/{referencedSponsorId}");
        ReferencedGetAfterFailedDeleteStatus = referencedGetAfterFailedDeleteResponse.StatusCode;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Test harness — WebApplicationFactory + ephemeral Postgres subclasses (the Story392_AdBriefsApi.cs
// "`file`-scoped types cannot cross files" precedent — this file supplies its own). ──

file sealed class Story410WebFactory(Story410Database db) : WebApplicationFactory<Program>
{
    public const string Password = "test-password-t434d-sponsors-delete-guard";

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
/// — see that type's own remarks. Supplies only the <c>"genwave-t434d"</c> compose project-name prefix
/// this file's own arc needs.</summary>
file sealed class Story410Database : EphemeralStationDatabase
{
    Story410Database(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story410Database> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t434d");
        var db = new Story410Database(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>Arrange helpers this file's own arc uses — raw SQL against the ephemeral database's own
/// connection string, never through <c>SponsorRepository</c> (the <c>AdBriefWireFixtures</c>
/// precedent): no brief/spot/show-authoring endpoint exists under <c>SponsorsController</c>, so 2
/// briefs, 5 spots spread across distinct <c>station.ad_state</c> values, and 1 show are seeded
/// independently of the API under test.</summary>
public static class Story410WireFixtures
{
    public static async Task<long> InsertReferencedSponsorAsync(
        string stationConnectionString, string sponsorName, string showName)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();

        await using var sponsorCmd = conn.CreateCommand();
        sponsorCmd.CommandText = "insert into station.sponsor (name) values (@name) returning id";
        sponsorCmd.Parameters.AddWithValue("name", sponsorName);
        var sponsorId = (long)(await sponsorCmd.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("sponsor insert returned no id"));

        // ── 2 briefs (distinct premises — ad_brief_sponsor_id_premise_key is UNIQUE per sponsor). ──
        await using var briefsCmd = conn.CreateCommand();
        briefsCmd.CommandText =
            """
            insert into station.ad_brief (sponsor_id, premise, enabled)
            values (@sponsorId, 'Fall roof inspection special', true),
                   (@sponsorId, 'Storm damage same-week repair', true)
            """;
        briefsCmd.Parameters.AddWithValue("sponsorId", sponsorId);
        await briefsCmd.ExecuteNonQueryAsync();

        // ── 5 spots, one per distinct station.ad_state (source='owner' — no LLM/pack angle needed for
        // this fixture). 'ready' carries a media_id — db/43's own ad_spot_ready_requires_media_id CHECK
        // (no FK on media_id: it crosses the db/22 schema-role boundary, so any bigint satisfies it).
        await using var spotsCmd = conn.CreateCommand();
        spotsCmd.CommandText =
            """
            insert into station.ad_spot (sponsor_name, sponsor_id, title, source, state, media_id)
            values (@sponsorName, @sponsorId, 'Cascade Roofing — draft cut', 'owner', 'draft', null),
                   (@sponsorName, @sponsorId, 'Cascade Roofing — approved cut', 'owner', 'approved', null),
                   (@sponsorName, @sponsorId, 'Cascade Roofing — rendering cut', 'owner', 'rendering', null),
                   (@sponsorName, @sponsorId, 'Cascade Roofing — ready cut', 'owner', 'ready', 1),
                   (@sponsorName, @sponsorId, 'Cascade Roofing — retired cut', 'owner', 'retired', null)
            """;
        spotsCmd.Parameters.AddWithValue("sponsorName", sponsorName);
        spotsCmd.Parameters.AddWithValue("sponsorId", sponsorId);
        await spotsCmd.ExecuteNonQueryAsync();

        // ── 1 show. ──
        await using var showCmd = conn.CreateCommand();
        showCmd.CommandText =
            "insert into station.show (name, slug, sponsor_id) values (@name, @slug, @sponsorId)";
        showCmd.Parameters.AddWithValue("name", showName);
        showCmd.Parameters.AddWithValue("slug", "cascade-morning-drive");
        showCmd.Parameters.AddWithValue("sponsorId", sponsorId);
        await showCmd.ExecuteNonQueryAsync();

        return sponsorId;
    }
}

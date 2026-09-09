// STORY-409 — Pause and resume a sponsor (SPEC F171.4 · PLAN T434)
//
// BDD specification — xUnit through the deployed entry point (WebApplicationFactory<Program> against
// a real ephemeral Postgres — the Story392_AdBriefsApi.cs arc idiom): every fact drives
// POST /api/sponsors/{id}/pause and .../resume over HTTP with an authed admin session, never
// ISponsorStore/SponsorsController directly. One arc (Story409Arc) arranges everything every Scenario
// below reads.

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

public static class FeaturePauseAndResumeASponsor
{
    [Collection(Story409Collection.Name)]
    public sealed class ScenarioPauseIsIdempotent(Story409Arc arc)
    {
        [Fact]
        public void TheFirstPauseIs200WithPausedTrue()
        {
            Assert.Equal(HttpStatusCode.OK, arc.FirstPauseStatus);
            Assert.True(arc.FirstPausePaused);
        }

        [Fact]
        public void TheSecondPauseIs200Too()
        {
            Assert.Equal(HttpStatusCode.OK, arc.SecondPauseStatus);
            Assert.True(arc.SecondPausePaused);
        }
    }

    [Collection(Story409Collection.Name)]
    public sealed class ScenarioResumeIsIdempotent(Story409Arc arc)
    {
        [Fact]
        public void TheFirstResumeIs200WithPausedFalse()
        {
            Assert.Equal(HttpStatusCode.OK, arc.FirstResumeStatus);
            Assert.False(arc.FirstResumePaused);
        }

        [Fact]
        public void TheSecondResumeIs200Too()
        {
            Assert.Equal(HttpStatusCode.OK, arc.SecondResumeStatus);
            Assert.False(arc.SecondResumePaused);
        }
    }

    [Collection(Story409Collection.Name)]
    public sealed class ScenarioPausedAtIsStampedAndCleared(Story409Arc arc)
    {
        [Fact]
        public void PauseStampsPausedAt()
        {
            Assert.NotNull(arc.FirstPausePausedAt);
            // A repeat pause leaves the ORIGINAL stamp untouched (SponsorRepository.SetPausedAsync's
            // own coalesce(paused_at, now()) — never restamped).
            Assert.Equal(arc.FirstPausePausedAt, arc.SecondPausePausedAt);
        }

        [Fact]
        public void ResumeClearsPausedAt()
            => Assert.Null(arc.FirstResumePausedAt);
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    // T434 round-2 review finding F2: pause/resume on an unknown id were unpinned by all 36 facts.
    [Collection(Story409Collection.Name)]
    public sealed class ScenarioAnUnknownIdIsRefused(Story409Arc arc)
    {
        [Fact]
        public void PausingAnUnknownIdIs404()
            => Assert.Equal(HttpStatusCode.NotFound, arc.PauseUnknownIdStatus);

        [Fact]
        public void ResumingAnUnknownIdIs404()
            => Assert.Equal(HttpStatusCode.NotFound, arc.ResumeUnknownIdStatus);
    }
}

// ── Collection definition — one ephemeral Postgres/factory shared by every Scenario above (the
// Story392_AdBriefsApi.cs "arrange once, many read-only Scenarios" idiom). ──

[CollectionDefinition(Name)]
public sealed class Story409Collection : ICollectionFixture<Story409Arc>
{
    public const string Name = "Story409SponsorPauseResume";
}

/// <summary>
/// Arranges every fact STORY-409's Scenarios read, entirely over the REAL production HTTP pipeline
/// with a real admin session — no <c>SponsorRepository</c> call, no <c>SponsorsController</c> call,
/// anywhere in this class.
/// </summary>
public sealed class Story409Arc : IAsyncLifetime
{
    public HttpStatusCode FirstPauseStatus { get; private set; }
    public bool FirstPausePaused { get; private set; }
    public DateTime? FirstPausePausedAt { get; private set; }

    public HttpStatusCode SecondPauseStatus { get; private set; }
    public bool SecondPausePaused { get; private set; }
    public DateTime? SecondPausePausedAt { get; private set; }

    public HttpStatusCode FirstResumeStatus { get; private set; }
    public bool FirstResumePaused { get; private set; }
    public DateTime? FirstResumePausedAt { get; private set; }

    public HttpStatusCode SecondResumeStatus { get; private set; }
    public bool SecondResumePaused { get; private set; }

    // T434 round-2 review finding F2.
    public HttpStatusCode PauseUnknownIdStatus { get; private set; }
    public HttpStatusCode ResumeUnknownIdStatus { get; private set; }

    public async Task InitializeAsync()
    {
        // A LOCAL, not a field — Story409Database is file-local (CS9051), the Story392AdBriefsDatabase
        // precedent.
        await using var database = await Story409Database.StartAsync();
        await using var factory = new Story409WebFactory(database);
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new { password = Story409WebFactory.Password });
        if (login.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login unexpectedly returned {login.StatusCode}");

        var createResponse = await client.PostAsJsonAsync("/api/sponsors", new { name = "Cascade Hardware" });
        var created = await JsonDocument.ParseAsync(await createResponse.Content.ReadAsStreamAsync());
        var id = created.RootElement.GetProperty("id").GetInt64();

        // ── AC1/AC3: pause twice — both 200, both paused:true, the second leaves pausedAt untouched. ──
        var firstPauseResponse = await client.PostAsync($"/api/sponsors/{id}/pause", content: null);
        FirstPauseStatus = firstPauseResponse.StatusCode;
        var firstPauseBody = await JsonDocument.ParseAsync(await firstPauseResponse.Content.ReadAsStreamAsync());
        FirstPausePaused = firstPauseBody.RootElement.GetProperty("paused").GetBoolean();
        FirstPausePausedAt = ReadNullableDateTime(firstPauseBody.RootElement, "pausedAt");

        // A real wall-clock gap so a WRONGLY-restamped pausedAt would visibly differ from the first.
        await Task.Delay(1100);

        var secondPauseResponse = await client.PostAsync($"/api/sponsors/{id}/pause", content: null);
        SecondPauseStatus = secondPauseResponse.StatusCode;
        var secondPauseBody = await JsonDocument.ParseAsync(await secondPauseResponse.Content.ReadAsStreamAsync());
        SecondPausePaused = secondPauseBody.RootElement.GetProperty("paused").GetBoolean();
        SecondPausePausedAt = ReadNullableDateTime(secondPauseBody.RootElement, "pausedAt");

        // ── AC2/AC3: resume twice — both 200, both paused:false, pausedAt cleared. ──
        var firstResumeResponse = await client.PostAsync($"/api/sponsors/{id}/resume", content: null);
        FirstResumeStatus = firstResumeResponse.StatusCode;
        var firstResumeBody = await JsonDocument.ParseAsync(await firstResumeResponse.Content.ReadAsStreamAsync());
        FirstResumePaused = firstResumeBody.RootElement.GetProperty("paused").GetBoolean();
        FirstResumePausedAt = ReadNullableDateTime(firstResumeBody.RootElement, "pausedAt");

        var secondResumeResponse = await client.PostAsync($"/api/sponsors/{id}/resume", content: null);
        SecondResumeStatus = secondResumeResponse.StatusCode;
        var secondResumeBody = await JsonDocument.ParseAsync(await secondResumeResponse.Content.ReadAsStreamAsync());
        SecondResumePaused = secondResumeBody.RootElement.GetProperty("paused").GetBoolean();

        // ── T434 round-2 review finding F2: pause/resume on an unknown id is 404. ──
        var pauseUnknownIdResponse = await client.PostAsync("/api/sponsors/999999/pause", content: null);
        PauseUnknownIdStatus = pauseUnknownIdResponse.StatusCode;

        var resumeUnknownIdResponse = await client.PostAsync("/api/sponsors/999999/resume", content: null);
        ResumeUnknownIdStatus = resumeUnknownIdResponse.StatusCode;
    }

    static DateTime? ReadNullableDateTime(JsonElement root, string propertyName)
    {
        var property = root.GetProperty(propertyName);
        return property.ValueKind == JsonValueKind.Null ? null : property.GetDateTime();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Test harness — WebApplicationFactory + ephemeral Postgres subclasses (the Story392_AdBriefsApi.cs
// "`file`-scoped types cannot cross files" precedent — this file supplies its own). ──

file sealed class Story409WebFactory(Story409Database db) : WebApplicationFactory<Program>
{
    public const string Password = "test-password-t434c-sponsors-pause-resume";

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
/// — see that type's own remarks. Supplies only the <c>"genwave-t434c"</c> compose project-name prefix
/// this file's own arc needs.</summary>
file sealed class Story409Database : EphemeralStationDatabase
{
    Story409Database(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story409Database> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t434c");
        var db = new Story409Database(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

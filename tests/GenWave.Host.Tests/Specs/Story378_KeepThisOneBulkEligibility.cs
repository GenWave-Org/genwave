// STORY-376 AC6 (SPEC F153.10 · PLAN T378) — "Keep this one": POST /api/media/eligibility narrowed
// by filter.mediaIds flips exactly the named rows and leaves every sibling untouched.
//
// BDD specification — xUnit. Entry-point discipline: the real production binary
// (WebApplicationFactory<Program>) against a real ephemeral Postgres — the
// Story374_TheGardenerTendsAQueue.cs arcs' own idiom (unique compose project name, OS-assigned port,
// every hosted service removed so no background loop can touch the seeded rows). A real admin
// session drives POST /api/media/eligibility over the wire, and the row-level assertions read the
// eligible column back with a raw, independent SQL query — never through the endpoint's own
// "affected" count alone, so this proves the actual rows changed, not just that the endpoint claims
// they did.
//
// T378 review MED-4 adds a fourth row (D) in a SECOND library OUTSIDE the station's own scope
// (Station:Scope:LibraryIds only ever names library 1 here), named in the SAME mediaIds list
// alongside B/C — proving the id filter never bypasses the mandatory scope predicate, even for an
// id an operator explicitly asked for.
//
// T378 review MED-3 adds a second call against the SAME arrangement: 501 media ids (one past the
// controller's own 500 cap) → 400, with the ProblemDetails body naming only the COUNT and the CAP,
// never any of the caller's own id values.

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

public static class FeatureKeepThisOneBulkEligibility
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    [Collection(KeepThisOneCollection.Name)]
    public sealed class ScenarioKeepThisOneFlipsOnlyTheNamedSiblings(KeepThisOneArc arc)
    {
        // Given three sibling rows (A, B, C), When POST /api/media/eligibility is called with
        // { eligible: false, filter: { mediaIds: [B, C] } } — the "Keep this one" action on A.
        [Fact]
        public void TheResponseIsOk()
        {
            Assert.Equal(HttpStatusCode.OK, arc.StatusCode);
        }

        [Fact]
        public void TheResponseReportsExactlyTwoAffected()
        {
            Assert.Equal(2, arc.Affected);
        }

        [Fact]
        public void TheFirstNamedSiblingBecomesIneligible()
        {
            Assert.False(arc.SiblingBEligibleAfter);
        }

        [Fact]
        public void TheSecondNamedSiblingBecomesIneligible()
        {
            Assert.False(arc.SiblingCEligibleAfter);
        }

        [Fact]
        public void TheKeptRowIsUntouched()
        {
            Assert.True(arc.KeptRowAEligibleAfter);
        }

        // T378 review MED-4 — row D lives in a SECOND library outside the station's own scope
        // (Station:Scope:LibraryIds only ever names library 1) and was named in the SAME
        // mediaIds list as B/C — the mandatory scope predicate must still exclude it.
        [Fact]
        public void TheOutOfScopeSiblingIsUntouchedEvenThoughNamed()
        {
            Assert.True(arc.OutOfScopeRowDEligibleAfter);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    [Collection(KeepThisOneCollection.Name)]
    public sealed class ScenarioTooManyMediaIds(KeepThisOneArc arc)
    {
        // Given 501 media ids (one past the controller's own 500 cap), When
        // POST /api/media/eligibility is called.
        [Fact]
        public void TheResponseIsBadRequest()
        {
            Assert.Equal(HttpStatusCode.BadRequest, arc.TooManyMediaIdsStatusCode);
        }

        [Fact]
        public void TheBodyNeverEchoesAnyOfTheCallersOwnIds()
        {
            // T378 review MED-3 — the ProblemDetails Detail names only the COUNT (501) and the CAP
            // (500), structural numbers, never one of the caller's own (deliberately huge, far from
            // any real row id) media ids.
            Assert.DoesNotContain(KeepThisOneArc.FirstOversizedMediaId.ToString(), arc.TooManyMediaIdsBody, StringComparison.Ordinal);
        }
    }
}

// ── Collection definition — one ephemeral Postgres/factory for this file's single Scenario (the
// Story374 "arrange once, many read-only Scenarios" idiom). ──

[CollectionDefinition(Name)]
public sealed class KeepThisOneCollection : ICollectionFixture<KeepThisOneArc>
{
    public const string Name = "Story378KeepThisOne";
}

/// <summary>
/// Seeds three sibling rows (A kept, B/C the near-duplicate group's other members), calls the real
/// <c>POST /api/media/eligibility</c> over HTTP with <c>filter.mediaIds = [B, C]</c>, then reads every
/// row's <c>eligible</c> column back independently — proving the bulk write is scoped to exactly the
/// named ids, not the whole in-scope table (the F3 default behaviour an empty/absent
/// <c>mediaIds</c> would otherwise produce).
/// </summary>
public sealed class KeepThisOneArc : IAsyncLifetime
{
    /// <summary>T378 review MED-3 — the first id in the oversized (501-long) mediaIds list, chosen
    /// far outside any real row id range so a substring match against the response body is an
    /// unambiguous "did the server echo one of the caller's own ids" check.</summary>
    public const long FirstOversizedMediaId = 9_000_001;

    public HttpStatusCode StatusCode { get; private set; }
    public int Affected { get; private set; }
    public bool KeptRowAEligibleAfter { get; private set; }
    public bool SiblingBEligibleAfter { get; private set; }
    public bool SiblingCEligibleAfter { get; private set; }
    public bool OutOfScopeRowDEligibleAfter { get; private set; }
    public HttpStatusCode TooManyMediaIdsStatusCode { get; private set; }
    public string TooManyMediaIdsBody { get; private set; } = "";

    public async Task InitializeAsync()
    {
        // A LOCAL, not a field — Story378KeepThisOneDatabase is file-local (CS9051), the same reason
        // Story374's own arcs give for the identical shape.
        await using var database = await Story378KeepThisOneDatabase.StartAsync();

        var rowA = await InsertMediaRowAsync(database.LibraryConnectionString, "/test/t378-a.flac");
        var rowB = await InsertMediaRowAsync(database.LibraryConnectionString, "/test/t378-b.flac");
        var rowC = await InsertMediaRowAsync(database.LibraryConnectionString, "/test/t378-c.flac");

        // T378 review MED-4 — row D lives in a SECOND library the station's own scope never names
        // (Station:Scope:LibraryIds:0 = "1" only, on Story378WebFactory below).
        var otherLibraryId = await InsertLibraryAsync(database.LibraryConnectionString, "t378-out-of-scope");
        var rowD = await InsertMediaRowAsync(database.LibraryConnectionString, "/test/t378-d-out-of-scope.flac", otherLibraryId);

        await using var factory = new Story378WebFactory(database);
        var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new { password = Story378WebFactory.Password });
        if (login.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login unexpectedly returned {login.StatusCode}");

        // MED-4: D is named alongside B/C even though it sits outside the station's scope.
        var response = await client.PostAsJsonAsync("/api/media/eligibility", new
        {
            eligible = false,
            filter = new { mediaIds = new[] { rowB, rowC, rowD } },
        });
        StatusCode = response.StatusCode;

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Affected = body.GetProperty("affected").GetInt32();

        KeptRowAEligibleAfter = await ReadEligibleAsync(database.LibraryConnectionString, rowA);
        SiblingBEligibleAfter = await ReadEligibleAsync(database.LibraryConnectionString, rowB);
        SiblingCEligibleAfter = await ReadEligibleAsync(database.LibraryConnectionString, rowC);
        OutOfScopeRowDEligibleAfter = await ReadEligibleAsync(database.LibraryConnectionString, rowD);

        // MED-3 — a second, independent call against the SAME session/scope: 501 media ids (one
        // past the controller's own 500 cap) never reaches the repository at all.
        var oversizedIds = Enumerable.Range(0, 501).Select(i => FirstOversizedMediaId + i).ToArray();
        var tooMany = await client.PostAsJsonAsync("/api/media/eligibility", new
        {
            eligible = false,
            filter = new { mediaIds = oversizedIds },
        });
        TooManyMediaIdsStatusCode = tooMany.StatusCode;
        TooManyMediaIdsBody = await tooMany.Content.ReadAsStringAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    static async Task<long> InsertLibraryAsync(string libraryConnectionString, string name)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "insert into library.library (name) values (@name) returning id";
        cmd.Parameters.AddWithValue("name", name);
        return (long)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("insert returned no id"));
    }

    static async Task<long> InsertMediaRowAsync(string libraryConnectionString, string path, long? libraryId = null)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = libraryId is null
            ? """
              insert into library.media (path, format, size_bytes, mtime, state)
              values (@path, 'flac', 1024, now(), 'ready')
              returning id
              """
            : """
              insert into library.media (path, format, size_bytes, mtime, state, library_id)
              values (@path, 'flac', 1024, now(), 'ready', @libraryId)
              returning id
              """;
        cmd.Parameters.AddWithValue("path", path);
        if (libraryId is not null)
            cmd.Parameters.AddWithValue("libraryId", libraryId.Value);
        return (long)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("insert returned no id"));
    }

    static async Task<bool> ReadEligibleAsync(string libraryConnectionString, long mediaId)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select eligible from library.media where id = @mediaId";
        cmd.Parameters.AddWithValue("mediaId", mediaId);
        return (bool)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("row not found"));
    }
}

// ── Test harness — WebApplicationFactory + ephemeral Postgres subclasses (Story374's own idiom;
// `file`-scoped types cannot cross files, so this file supplies its own, exactly as Story374's own
// remarks on EphemeralStationDatabase explain). ──

/// <summary>
/// Boots the real production composition root against a real ephemeral Postgres with every hosted
/// service removed (no gardener/rotation/liquidsoap background loop reach) — this Scenario only needs
/// the real <c>MediaController</c> endpoint over a real admin session.
/// </summary>
file sealed class Story378WebFactory(Story378KeepThisOneDatabase db) : WebApplicationFactory<Program>
{
    public const string Password = "test-password-t378-keep-this-one";

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

/// <summary>
/// This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/> harness — see
/// that type's own remarks for the full "which compose file, why a unique project name + OS-assigned
/// port" rationale. Supplies only the <c>"genwave-t378"</c> compose project-name prefix this file's
/// own arc needs.
/// </summary>
file sealed class Story378KeepThisOneDatabase : EphemeralStationDatabase
{
    Story378KeepThisOneDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story378KeepThisOneDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t378");
        var db = new Story378KeepThisOneDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

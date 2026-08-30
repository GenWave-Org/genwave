// STORY-368 — I can see how healthy my rotation is (SPEC F149.5 · PLAN T371)
//
// BDD specification — xUnit. WIRED T371. Entry-point discipline: every fact drives the REAL
// production binary (WebApplicationFactory<Program>, the Story345/Story366/Story367 factory idiom
// over an ephemeral station+library Postgres — tests/GenWave.Host.Tests/Support/
// EphemeralStationDatabase) seeded with the 10-row rotation fixture the ACs share (6 never aired,
// 3 aired once, 1 aired 6 times, one last aired 91 days ago), read back through GET /api/status,
// GET /api/media, and GET /api/media/{id}. AC2's dashboard tile itself is a Jest todo in admin-ui
// (__specs__/rotation-health-tile.spec.tsx) — the fact here only pins that GET /api/status carries
// the `rotation` property the tile reads from.
//
// db/41-gardener-migration.sh is run once against the ephemeral database (RunFileInContainer, the
// Story367_TheStationRemembersEveryAiring.cs idiom) so library.media_rotation exists and
// Gardener:RotationSince is stamped; every ledger row is then seeded directly via raw SQL (never
// through IMediaRotationSink) — this suite is about the READ surfaces, not the write path Story367
// already covers.

using System.Globalization;
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

[CollectionDefinition(Name)]
public sealed class Story368RotationHealthCollection : ICollectionFixture<RotationHealthArc>
{
    public const string Name = "Story368RotationHealth";
}

public static class FeatureRotationHealthIsVisible
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the numbers surface on status, the catalog, and the detail page
    // ---------------------------------------------------------------------

    [Collection(Story368RotationHealthCollection.Name)]
    public sealed class ScenarioStatusCarriesTheRotationCounts(RotationHealthArc arc)
    {
        // Given a station with 10 playable rows (6 never aired, 3 aired once, 1 aired 6 times,
        // one last aired 91 days ago), When GET /api/status is called.
        [Fact]
        public void NeverAiredIsSix() => Assert.Equal(6, arc.Rotation.GetProperty("neverAired").GetInt64());

        [Fact]
        public void AiredOnceIsThree() => Assert.Equal(3, arc.Rotation.GetProperty("airedOnce").GetInt64());

        [Fact]
        public void NotAiredDays90IsOne() => Assert.Equal(1, arc.Rotation.GetProperty("notAiredDays90").GetInt64());

        // T371 review LOW-4 — pins rotation.playable itself. 11, not the AC's own illustrative 10:
        // this arc's fixture ALSO seeds an eleventh playable row (play_count 3, well inside 90 days)
        // for ScenarioTheDetailPageShowsRotationFacts (AC5) — a row that satisfies none of
        // neverAired/airedOnce/notAiredDays90 but IS playable-and-in-scope, so it inflates playable
        // by exactly one over the AC1 Given's own count. The twelfth (unavailable) row for AC6 is
        // correctly excluded — playable never counts it.
        [Fact]
        public void PlayableIsEleven() => Assert.Equal(11, arc.Rotation.GetProperty("playable").GetInt64());

        [Fact]
        public void RotationSinceIsTheLedgerEpoch() =>
            Assert.Equal(arc.ExpectedRotationSince, arc.Rotation.GetProperty("rotationSince").GetDateTimeOffset());
    }

    [Collection(Story368RotationHealthCollection.Name)]
    public sealed class ScenarioTheDashboardShowsARotationHealthTile(RotationHealthArc arc)
    {
        // Given the status above, When GET /api/status is called — the tile itself renders from
        // this property in admin-ui (Jest todo there, not here).
        [Fact]
        public void TheStatusResponseCarriesARotationProperty() =>
            Assert.True(arc.StatusRoot.TryGetProperty("rotation", out _));
    }

    [Collection(Story368RotationHealthCollection.Name)]
    public sealed class ScenarioNeverAiredFilter(RotationHealthArc arc)
    {
        // Given the catalog above, When GET /api/media?never-aired=true is called.
        [Fact]
        public void ExactlyTheSixNeverAiredRowsAreReturned() =>
            Assert.Equal(arc.NeverAiredMediaIds.OrderBy(id => id), arc.NeverAiredFilterResultIds.OrderBy(id => id));
    }

    [Collection(Story368RotationHealthCollection.Name)]
    public sealed class ScenarioAiredBeforeFilter(RotationHealthArc arc)
    {
        // Given the catalog above, When GET /api/media?aired-before=<today − 90d> is called.
        [Fact]
        public void ExactlyTheRowLastAiredNinetyOneDaysAgoIsReturned() =>
            Assert.Equal([arc.StaleMediaId], arc.AiredBeforeFilterResultIds);
    }

    [Collection(Story368RotationHealthCollection.Name)]
    public sealed class ScenarioTheDetailPageShowsRotationFacts(RotationHealthArc arc)
    {
        // Given a media row with play_count 3, first T1, last T2, When GET /api/media/{id} is called.
        [Fact]
        public void PlaysIsThree() => Assert.Equal(3, arc.DetailBody.GetProperty("plays").GetInt32());

        [Fact]
        public void FirstAiredAtIsTOne() =>
            Assert.Equal(arc.DetailFirstAiredAt, arc.DetailBody.GetProperty("firstAiredAt").GetDateTimeOffset());

        [Fact]
        public void LastAiredAtIsTTwo() =>
            Assert.Equal(arc.DetailLastAiredAt, arc.DetailBody.GetProperty("lastAiredAt").GetDateTimeOffset());
    }

    // ---------------------------------------------------------------------
    // SAD PATH — unplayable rows never surface in the filters
    // ---------------------------------------------------------------------

    [Collection(Story368RotationHealthCollection.Name)]
    public sealed class ScenarioTheFiltersAreInertForUnplayableRows(RotationHealthArc arc)
    {
        // Given an unavailable row that never aired, When GET /api/media?never-aired=true is called.
        [Fact]
        public void ItIsNotReturned() => Assert.DoesNotContain(arc.UnavailableNeverAiredMediaId, arc.NeverAiredFilterResultIds);
    }

    [Collection(Story368RotationHealthCollection.Name)]
    public sealed class ScenarioAiredBeforeRejectsAnUnparseableDate(RotationHealthArc arc)
    {
        // Post-review (pre-commit smoke) — a bad aired-before value 400s naming the field and the
        // expected shape only, never echoing the caller's own value back into the body (the
        // F87.3/F150 400 posture this epic holds everywhere else). Given aired-before=yesterday,
        // When GET /api/media?aired-before=yesterday is called.
        [Fact]
        public void TheResponseIsFourHundred() => Assert.Equal(HttpStatusCode.BadRequest, arc.BadAiredBeforeStatus);

        [Fact]
        public void TheDetailNamesTheFieldAndShapeOnly() =>
            Assert.Equal(
                "aired-before must be a date in yyyy-MM-dd format.",
                arc.BadAiredBeforeBody.GetProperty("detail").GetString());

        [Fact]
        public void TheDetailNeverEchoesTheCallersValue() =>
            Assert.DoesNotContain("yesterday", arc.BadAiredBeforeBody.GetProperty("detail").GetString());
    }
}

// ── Arc fixture — arranges its own ephemeral Postgres + production host exactly ONCE
// (IAsyncLifetime.InitializeAsync, shared across every Scenario class above via
// ICollectionFixture<T>) and captures every response the Facts read. ─────────────────────────────

public sealed class RotationHealthArc : IAsyncLifetime
{
    public JsonElement StatusRoot { get; private set; }
    public JsonElement Rotation { get; private set; }
    public DateTimeOffset ExpectedRotationSince { get; private set; }

    public IReadOnlyList<long> NeverAiredMediaIds { get; private set; } = [];
    public IReadOnlyList<long> NeverAiredFilterResultIds { get; private set; } = [];
    public IReadOnlyList<long> AiredBeforeFilterResultIds { get; private set; } = [];
    public long StaleMediaId { get; private set; }
    public long UnavailableNeverAiredMediaId { get; private set; }

    public JsonElement DetailBody { get; private set; }
    public DateTimeOffset DetailFirstAiredAt { get; private set; }
    public DateTimeOffset DetailLastAiredAt { get; private set; }

    public HttpStatusCode BadAiredBeforeStatus { get; private set; }
    public JsonElement BadAiredBeforeBody { get; private set; }

    // Typed as the (non-file-local) base class — C# forbids a file-local type (Story368StationDatabase)
    // from appearing in a member signature of this non-file-local Arc type, field types included.
    EphemeralStationDatabase? database;

    public async Task InitializeAsync()
    {
        var db = await Story368StationDatabase.StartAsync();
        database = db;

        // library.media_rotation + Gardener:RotationSince (SPEC F149.3) — the schema/epoch half of
        // db/41, never the booth-log seed itself (this arc writes ledger rows directly).
        db.RunFileInContainer(Path.Combine(RepoRoot(), "db", "41-gardener-migration.sh"));
        ExpectedRotationSince = await ReadRotationSinceAsync(db.StationConnectionString);

        var now = DateTimeOffset.UtcNow;

        // AC1's own 10-row fixture: 6 never aired, 3 aired once (recent), 1 aired 6 times whose
        // LAST airing is 91 days ago (stale AND the one "aired 6 times" row — the only shape AC1's
        // own Given can mean once 6+3+1 already totals 10).
        var neverAiredIds = new List<long>();
        for (var i = 0; i < 6; i++)
            neverAiredIds.Add(await InsertPlayableMediaRowAsync(db.LibraryConnectionString, $"/rotation/never-{i}.flac"));
        NeverAiredMediaIds = neverAiredIds;

        for (var i = 0; i < 3; i++)
        {
            var id = await InsertPlayableMediaRowAsync(db.LibraryConnectionString, $"/rotation/once-{i}.flac");
            var airedAt = now.AddDays(-2);
            await InsertLedgerRowAsync(db.LibraryConnectionString, id, playCount: 1, firstAiredAt: airedAt, lastAiredAt: airedAt);
        }

        StaleMediaId = await InsertPlayableMediaRowAsync(db.LibraryConnectionString, "/rotation/stale.flac");
        await InsertLedgerRowAsync(
            db.LibraryConnectionString, StaleMediaId, playCount: 6,
            firstAiredAt: now.AddDays(-200), lastAiredAt: now.AddDays(-91));

        // AC5's own detail row — plays 3, distinct first/last, well inside the 90-day window so it
        // never leaks into notAiredDays90/never-aired/aired-before.
        var detailMediaId = await InsertPlayableMediaRowAsync(db.LibraryConnectionString, "/rotation/detail.flac");
        DetailFirstAiredAt = Truncate(now.AddDays(-30));
        DetailLastAiredAt = Truncate(now.AddDays(-10));
        await InsertLedgerRowAsync(db.LibraryConnectionString, detailMediaId, playCount: 3, DetailFirstAiredAt, DetailLastAiredAt);

        // AC6 — an unavailable row that never aired: playable-only, so it must never surface under
        // never-aired=true even though it has no ledger row either.
        UnavailableNeverAiredMediaId = await InsertPlayableMediaRowAsync(db.LibraryConnectionString, "/rotation/unavailable.flac");
        await SetStateAsync(db.LibraryConnectionString, UnavailableNeverAiredMediaId, "unavailable");

        await using var factory = new Story368WebFactory(db);
        var client = factory.CreateClient();
        await LoginAsync(client, Story368WebFactory.Password);

        var statusResponse = await client.GetAsync("/api/status");
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        var statusDoc = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
        StatusRoot = statusDoc.RootElement.Clone();
        Rotation = StatusRoot.GetProperty("rotation").Clone();

        var neverAiredResponse = await client.GetAsync("/api/media?never-aired=true");
        Assert.Equal(HttpStatusCode.OK, neverAiredResponse.StatusCode);
        NeverAiredFilterResultIds = await ReadMediaIdsAsync(neverAiredResponse);

        var airedBeforeDate = DateOnly.FromDateTime(now.AddDays(-90).UtcDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var airedBeforeResponse = await client.GetAsync($"/api/media?aired-before={airedBeforeDate}");
        Assert.Equal(HttpStatusCode.OK, airedBeforeResponse.StatusCode);
        AiredBeforeFilterResultIds = await ReadMediaIdsAsync(airedBeforeResponse);

        var detailResponse = await client.GetAsync($"/api/media/{detailMediaId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        DetailBody = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync()).RootElement.Clone();

        // Post-review (pre-commit smoke) — an unparseable aired-before value: pins the 400's own
        // Detail text (field + expected shape only, never the caller's value echoed back).
        var badAiredBeforeResponse = await client.GetAsync("/api/media?aired-before=yesterday");
        BadAiredBeforeStatus = badAiredBeforeResponse.StatusCode;
        BadAiredBeforeBody = JsonDocument.Parse(await badAiredBeforeResponse.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    public async Task DisposeAsync()
    {
        if (database is not null)
            await database.DisposeAsync();
    }

    static async Task<IReadOnlyList<long>> ReadMediaIdsAsync(HttpResponseMessage response)
    {
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.EnumerateArray()
            .Select(row => long.Parse(row.GetProperty("mediaId").GetString()!, CultureInfo.InvariantCulture))
            .ToList();
    }

    static async Task LoginAsync(HttpClient client, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { password });
        if (response.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login unexpectedly returned {response.StatusCode}");
    }

    static DateTimeOffset Truncate(DateTimeOffset value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, TimeSpan.Zero);

    static async Task<long> InsertPlayableMediaRowAsync(string libraryConnectionString, string path)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            insert into library.media (path, format, size_bytes, mtime, state, measurable)
            values (@path, 'flac', 1024, now(), 'ready', true)
            returning id
            """;
        cmd.Parameters.AddWithValue("path", path);
        return (long)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("insert returned no id"));
    }

    static async Task SetStateAsync(string libraryConnectionString, long mediaId, string state)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "update library.media set state = @state where id = @mediaId";
        cmd.Parameters.AddWithValue("state", state);
        cmd.Parameters.AddWithValue("mediaId", mediaId);
        await cmd.ExecuteNonQueryAsync();
    }

    static async Task InsertLedgerRowAsync(
        string libraryConnectionString, long mediaId, int playCount,
        DateTimeOffset firstAiredAt, DateTimeOffset lastAiredAt)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            insert into library.media_rotation (media_id, play_count, first_aired_at, last_aired_at)
            values (@mediaId, @playCount, @firstAiredAt, @lastAiredAt)
            """;
        cmd.Parameters.AddWithValue("mediaId", mediaId);
        cmd.Parameters.AddWithValue("playCount", playCount);
        cmd.Parameters.AddWithValue("firstAiredAt", firstAiredAt);
        cmd.Parameters.AddWithValue("lastAiredAt", lastAiredAt);
        await cmd.ExecuteNonQueryAsync();
    }

    static async Task<DateTimeOffset> ReadRotationSinceAsync(string stationConnectionString)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select value from station.settings where key = 'Gardener:RotationSince'";
        var raw = (string?)await cmd.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("expected Gardener:RotationSince to be stamped after the migration ran");
        return JsonSerializer.Deserialize<DateTimeOffset>(raw);
    }

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;

        if (dir is null) throw new InvalidOperationException("repo root (GenWave.sln) not found");
        return dir.FullName;
    }
}

// ── Test harness — WebApplicationFactory subclasses ───────────────────────────────────────────────

file sealed class Story368StationDatabase : EphemeralStationDatabase
{
    Story368StationDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story368StationDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-rotationhealth");
        var db = new Story368StationDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>
/// Boots the real production composition root (Program.cs) against a real ephemeral Postgres
/// (<see cref="Story368StationDatabase"/>) — mirrors SensorGateWebFactory's (Story366) own shape.
/// <c>Station:SafeScope:LibraryIds:0</c> is overridden to a harmless, non-colliding placeholder id
/// (the Story355WebFactory/T355 review precedent, HIGH-1/HIGH-2) — appsettings.Development.json's
/// own shipped default is <c>[1]</c>, the SAME library id every media row this file seeds lands in
/// (the implicit default), so leaving it unset would silently exclude every seeded row from every
/// rotation-health count and filter this suite asserts on.
/// </summary>
file sealed class Story368WebFactory(Story368StationDatabase db) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story368-rotation-health";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", db.LibraryConnectionString);
        builder.UseSetting("ConnectionStrings:Station", db.StationConnectionString);
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Admin:Enabled", "true");

        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");
        builder.UseSetting("Station:SafeScope:LibraryIds:0", "999999");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
        });
    }
}

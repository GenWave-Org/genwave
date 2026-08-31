// STORY-370 — I can thumb from the booth (SPEC F150.1, F150.8 · PLAN T367)
//
// BDD specification — xUnit. WIRED T367. Entry-point discipline: every fact drives the REAL
// production binary (WebApplicationFactory<Program>, the Story345/Story366 factory idiom over an
// ephemeral station+library Postgres — tests/GenWave.Host.Tests/Support/EphemeralStationDatabase)
// seeded with a real booth-log track-started row, driven through POST
// /api/booth-log/{id}/station-thumb with a real Curation-authorized session (or none, for AC6). AC4
// (the two distinct on-screen controls) is a Jest todo in admin-ui, not this suite.
//
// T367 review LOW-1: AC2/AC3's "byte-identical to before" facts seed ONE real row into each of
// station.persona_taste, station.persona_taste_thumb, and library.media_rating BEFORE the thumb —
// nothing else in this arc ever touches those tables, so a count-only check would have passed
// vacuously even if the station-thumb write path silently mutated an EXISTING row (only insert/delete
// would move a bare count). Comparing a full row-content SNAPSHOT (every column, string_agg'd) before
// and after is what actually proves "byte-identical", not merely "same row count".
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureThumbFromTheBooth
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the station's own thumb lands, and nothing else it touches moves
    // ---------------------------------------------------------------------

    [Collection(StationThumbCollection.Name)]
    public sealed class ScenarioAnOperatorStationThumbIsRecorded(StationThumbArc arc)
    {
        // Given a booth-log track-started row 7 for media 42, When
        // POST /api/booth-log/7/station-thumb {direction: "up"} is called with a Curation session.
        [Fact]
        public void MediaThumbHoldsTheOperatorRowForMedia42AtRow7sStartedAt()
        {
            Assert.True(arc.ThumbRowExists, "expected a library.media_thumb row");
            Assert.Equal("up", arc.ThumbDirection);
            Assert.Equal("operator", arc.ThumbSource);
        }

        [Fact]
        public void MediaRotation42ThumbsUpIsOne() => Assert.Equal(1L, arc.ThumbsUpAfter);
    }

    [Collection(StationThumbCollection.Name)]
    public sealed class ScenarioThePersonaTasteThumbIsUntouched(StationThumbArc arc)
    {
        // Given the same thumb, When station.persona_taste and station.persona_taste_thumb are read.
        // Each table carries a genuinely seeded row (LOW-1) — this is a real before/after content
        // comparison, never vacuously "both zero".
        [Fact]
        public void PersonaTasteIsByteIdenticalToBefore()
        {
            Assert.False(string.IsNullOrEmpty(arc.PersonaTasteSnapshotBefore), "expected a seeded station.persona_taste row");
            Assert.Equal(arc.PersonaTasteSnapshotBefore, arc.PersonaTasteSnapshotAfter);
        }

        [Fact]
        public void PersonaTasteThumbIsByteIdenticalToBefore()
        {
            Assert.False(string.IsNullOrEmpty(arc.PersonaTasteThumbSnapshotBefore), "expected a seeded station.persona_taste_thumb row");
            Assert.Equal(arc.PersonaTasteThumbSnapshotBefore, arc.PersonaTasteThumbSnapshotAfter);
        }
    }

    [Collection(StationThumbCollection.Name)]
    public sealed class ScenarioTheCurationLedgerIsUntouched(StationThumbArc arc)
    {
        // Given the same thumb, When library.media_rating is read. A genuinely seeded row (LOW-1).
        [Fact]
        public void ItIsByteIdenticalToBefore()
        {
            Assert.False(string.IsNullOrEmpty(arc.MediaRatingSnapshotBefore), "expected a seeded library.media_rating row");
            Assert.Equal(arc.MediaRatingSnapshotBefore, arc.MediaRatingSnapshotAfter);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — non-music rows and unauthenticated callers get nothing
    // ---------------------------------------------------------------------

    [Collection(StationThumbCollection.Name)]
    public sealed class ScenarioNonMusicRowsAreNotThumbable(StationThumbArc arc)
    {
        // Given a booth-log patter-aired row, When station-thumb is posted for it.
        [Fact]
        public void TheResponseIsFourHundredNamingTheKind()
        {
            Assert.Equal(HttpStatusCode.BadRequest, arc.NonMusicStatus);
            Assert.Contains("patter-aired", arc.NonMusicBody, StringComparison.Ordinal);
        }
    }

    [Collection(StationThumbCollection.Name)]
    public sealed class ScenarioTheSurfaceIsAdminOnly(StationThumbArc arc)
    {
        // Given no session, When station-thumb is posted.
        [Fact]
        public void TheResponseIsFourOhOne() => Assert.Equal(HttpStatusCode.Unauthorized, arc.NoSessionStatus);
    }
}

// ── Collection definition — one ephemeral Postgres/factory shared across every Scenario above (the
// Story367/Story369 "one Arc, one InitializeAsync, Facts just assert" idiom). ────────────────────────

[CollectionDefinition(Name)]
public sealed class StationThumbCollection : ICollectionFixture<StationThumbArc>
{
    public const string Name = "Story370StationThumb";
}

/// <summary>
/// Arranges ONE ephemeral station+library Postgres and drives every T367 fact above through the REAL
/// production binary (STORY-370 AC1, AC2, AC3, AC5, AC6), capturing every value a Scenario class reads
/// (IAsyncLifetime.InitializeAsync).
/// </summary>
public sealed class StationThumbArc : IAsyncLifetime
{
    public bool ThumbRowExists { get; private set; }
    public string ThumbDirection { get; private set; } = "";
    public string ThumbSource { get; private set; } = "";
    public long ThumbsUpAfter { get; private set; }

    public string PersonaTasteSnapshotBefore { get; private set; } = "";
    public string PersonaTasteSnapshotAfter { get; private set; } = "";
    public string PersonaTasteThumbSnapshotBefore { get; private set; } = "";
    public string PersonaTasteThumbSnapshotAfter { get; private set; } = "";

    public string MediaRatingSnapshotBefore { get; private set; } = "";
    public string MediaRatingSnapshotAfter { get; private set; } = "";

    public HttpStatusCode NonMusicStatus { get; private set; }
    public string NonMusicBody { get; private set; } = "";

    public HttpStatusCode NoSessionStatus { get; private set; }

    public async Task InitializeAsync()
    {
        await using var db = await StationThumbStationDatabase.StartAsync();

        // "media 42" / "row 7" (AC1's own symbolic naming) are whatever ids Postgres actually
        // assigns — the AC's own point is the RELATIONSHIP (this row's media, this row's
        // occurred_at), not a literal id 7/42.
        var mediaId = await GardenerSeedFixtures.InsertMediaRowAsync(db.LibraryConnectionString, "/test/story370-station-thumb.flac");
        var occurredAt = DateTimeOffset.Parse("2026-08-10T09:00:00Z");
        var trackRowId = await StationThumbFixtures.InsertTrackStartedRowAsync(db.StationConnectionString, mediaId, occurredAt);
        var patterRowId = await StationThumbFixtures.InsertPatterAiredRowAsync(db.StationConnectionString);

        // ── LOW-1: seed ONE real row into each of the three tables the AC2/AC3 facts prove untouched
        // — nothing else in this arc ever writes to any of them, so any UPDATE (never mind INSERT/
        // DELETE) the station-thumb write path might wrongly cause is visible in the before/after
        // snapshot comparison below.
        var personaId = await StationThumbFixtures.InsertPersonaAsync(db.StationConnectionString, "Story370 Probe DJ");
        await StationThumbFixtures.InsertPersonaTasteRowAsync(db.StationConnectionString, personaId);
        await StationThumbFixtures.InsertPersonaTasteThumbRowAsync(db.StationConnectionString, personaId, trackRowId);
        await StationThumbFixtures.InsertMediaRatingRowAsync(db.LibraryConnectionString, mediaId);

        await using var factory = new StationThumbWebFactory(db);

        // ── AC2 / AC3: read BEFORE the write too — a genuine before/after CONTENT comparison, never
        // merely "both zero" (LOW-1).
        PersonaTasteSnapshotBefore = await StationThumbFixtures.ReadPersonaTasteSnapshotAsync(db.StationConnectionString);
        PersonaTasteThumbSnapshotBefore = await StationThumbFixtures.ReadPersonaTasteThumbSnapshotAsync(db.StationConnectionString);
        MediaRatingSnapshotBefore = await StationThumbFixtures.ReadMediaRatingSnapshotAsync(db.LibraryConnectionString);

        var client = await LoggedInClientAsync(factory);

        // ── AC1: the happy-path thumb.
        var response = await client.PostAsJsonAsync($"/api/booth-log/{trackRowId}/station-thumb", new { direction = "up" });
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"station-thumb POST failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        var thumbRow = await StationThumbFixtures.ReadThumbRowAsync(db.LibraryConnectionString, mediaId, occurredAt, "operator");
        ThumbRowExists = thumbRow is not null;
        ThumbDirection = thumbRow?.Direction ?? "";
        ThumbSource = thumbRow?.Source ?? "";
        ThumbsUpAfter = await StationThumbFixtures.ReadThumbsUpAsync(db.LibraryConnectionString, mediaId);

        // ── AC2 / AC3: read AFTER.
        PersonaTasteSnapshotAfter = await StationThumbFixtures.ReadPersonaTasteSnapshotAsync(db.StationConnectionString);
        PersonaTasteThumbSnapshotAfter = await StationThumbFixtures.ReadPersonaTasteThumbSnapshotAsync(db.StationConnectionString);
        MediaRatingSnapshotAfter = await StationThumbFixtures.ReadMediaRatingSnapshotAsync(db.LibraryConnectionString);

        // ── AC5: a patter-aired (non-music) row — 400 naming the kind.
        var nonMusicResponse = await client.PostAsJsonAsync($"/api/booth-log/{patterRowId}/station-thumb", new { direction = "up" });
        NonMusicStatus = nonMusicResponse.StatusCode;
        NonMusicBody = await nonMusicResponse.Content.ReadAsStringAsync();

        // ── AC6: no session at all — the SAME real track row, an anonymous client.
        var anonymousClient = factory.CreateClient();
        var noSessionResponse = await anonymousClient.PostAsJsonAsync(
            $"/api/booth-log/{trackRowId}/station-thumb", new { direction = "up" });
        NoSessionStatus = noSessionResponse.StatusCode;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = StationThumbWebFactory.Password });
        if (login.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login failed: {login.StatusCode}");
        return client;
    }
}

file sealed class StationThumbStationDatabase : EphemeralStationDatabase
{
    StationThumbStationDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<StationThumbStationDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-stationthumb");
        var db = new StationThumbStationDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>
/// Boots the real production composition root against a real ephemeral Postgres (the
/// Story367/Story369 "four Station:* keys, everything else off appsettings.json's own default"
/// factory idiom) — the station-thumb route carries no live switch of its own (unlike the spectator
/// thumb route's <c>Station:Thumbs:Enabled</c>, SPEC F150.2): <c>AdminSurface</c>/<c>Curation</c> is
/// always live, so nothing beyond a session is needed to reach it.
/// </summary>
file sealed class StationThumbWebFactory(StationThumbStationDatabase db) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story370-station-thumb";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", db.LibraryConnectionString);
        builder.UseSetting("ConnectionStrings:Station", db.StationConnectionString);
        builder.UseSetting("Admin:Password", Password);

        // The exact four Station:* keys compose.yaml itself overrides in production (Story366/367's
        // own precedent) — every other Station:* leaf rides appsettings.json's own shipped default.
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");
        // gh-#99: every media row this file seeds lands in the DEFAULT library (id 1) — an
        // unreachable safe-scope id keeps it out of the gh-#99 exclusion path this fact never means
        // to exercise (Story367's own precedent).
        builder.UseSetting("Station:SafeScope:LibraryIds:0", "999999");

        builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
    }
}

/// <summary>Raw SQL reads/writes this file's own facts need — an independent read of what actually
/// landed in Postgres, mirroring Story369_ListenersThumbTheTrackPlaying.cs's own
/// <c>ThumbTestFixtures</c> (that type is itself <see langword="file"/>-private to its own file, so
/// this is a genuine second copy by necessity, not a drift risk to pretend away — the same
/// "no shared test-support project between spec files" precedent <see cref="EphemeralStationDatabase"/>'s
/// own remarks document).</summary>
file static class StationThumbFixtures
{
    public readonly record struct ThumbRow(string Direction, string Source);

    public static async Task<long> InsertTrackStartedRowAsync(string stationConnectionString, long mediaId, DateTimeOffset occurredAt)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            insert into station.booth_log (occurred_at, kind, summary, media_id)
            values (@occurredAt, 'track-started', 'seed fixture', @mediaId)
            returning id
            """;
        cmd.Parameters.AddWithValue("occurredAt", occurredAt);
        cmd.Parameters.AddWithValue("mediaId", mediaId);
        return (long)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("insert returned no id"));
    }

    public static async Task<long> InsertPatterAiredRowAsync(string stationConnectionString)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            insert into station.booth_log (occurred_at, kind, summary)
            values (now(), 'patter-aired', 'seed fixture')
            returning id
            """;
        return (long)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("insert returned no id"));
    }

    public static async Task<long> InsertPersonaAsync(string stationConnectionString, string name)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "insert into station.persona (name) values (@name) returning id::bigint";
        cmd.Parameters.AddWithValue("name", name);
        return (long)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("insert returned no id"));
    }

    /// <summary>LOW-1's own seed row — an authored taste rule, unrelated to this file's own thumb
    /// entirely (predicate/context are inert jsonb, never read by anything the station-thumb write
    /// path touches).</summary>
    public static async Task InsertPersonaTasteRowAsync(string stationConnectionString, long personaId)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            insert into station.persona_taste (persona_id, predicate, context, weight, source)
            values (@personaId, '{"artist":"Story370 Probe Artist"}'::jsonb, '{}'::jsonb, 0.5, 'authored')
            """;
        cmd.Parameters.AddWithValue("personaId", personaId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>LOW-1's own seed row — an F84.5 ledger entry against the ALREADY-seeded track-started
    /// row (any real booth_log id satisfies the FK; this row is otherwise unrelated to the F150.8
    /// thumb this file posts against that same row id).</summary>
    public static async Task InsertPersonaTasteThumbRowAsync(string stationConnectionString, long personaId, long boothLogId)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "insert into station.persona_taste_thumb (persona_id, booth_log_id, direction) values (@personaId, @boothLogId, 'up')";
        cmd.Parameters.AddWithValue("personaId", personaId);
        cmd.Parameters.AddWithValue("boothLogId", boothLogId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>LOW-1's own seed row — an F33 rating row for the SAME media id the station-thumb
    /// targets, at the column defaults (score 50, never_play false).</summary>
    public static async Task InsertMediaRatingRowAsync(string libraryConnectionString, long mediaId)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "insert into library.media_rating (media_id) values (@mediaId)";
        cmd.Parameters.AddWithValue("mediaId", mediaId);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<ThumbRow?> ReadThumbRowAsync(
        string libraryConnectionString, long mediaId, DateTimeOffset airingStartedAt, string listenerKey)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            select direction::text, source::text from library.media_thumb
            where media_id = @mediaId and airing_started_at = @startedAt and listener_key = @listenerKey
            """;
        cmd.Parameters.AddWithValue("mediaId", mediaId);
        cmd.Parameters.AddWithValue("startedAt", airingStartedAt);
        cmd.Parameters.AddWithValue("listenerKey", listenerKey);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new ThumbRow(reader.GetString(0), reader.GetString(1));
    }

    public static async Task<long> ReadThumbsUpAsync(string libraryConnectionString, long mediaId)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select thumbs_up from library.media_rotation where media_id = @mediaId";
        cmd.Parameters.AddWithValue("mediaId", mediaId);
        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? 0L : Convert.ToInt64(result);
    }

    /// <summary>Whole-table content snapshot (T367 review LOW-1) — every column of every row,
    /// concatenated in a STABLE (<c>order by id</c>) order via <c>string_agg</c>, so "byte-identical
    /// to before" means the actual comparison it claims to, not merely "same row count": an UPDATE
    /// that leaves the row count unchanged still changes this string. Coalesces to <c>""</c> for an
    /// empty table (never <see langword="null"/>) so callers can compare uniformly.</summary>
    public static async Task<string> ReadPersonaTasteSnapshotAsync(string stationConnectionString)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            select coalesce(string_agg(
                persona_id || '|' || predicate::text || '|' || context::text || '|' || weight || '|' || source || '|' || updated_at::text,
                ';' order by id), '')
            from station.persona_taste
            """;
        return (string?)await cmd.ExecuteScalarAsync() ?? "";
    }

    public static async Task<string> ReadPersonaTasteThumbSnapshotAsync(string stationConnectionString)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            select coalesce(string_agg(persona_id || '|' || booth_log_id || '|' || direction, ';' order by id), '')
            from station.persona_taste_thumb
            """;
        return (string?)await cmd.ExecuteScalarAsync() ?? "";
    }

    public static async Task<string> ReadMediaRatingSnapshotAsync(string libraryConnectionString)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            select coalesce(string_agg(
                media_id || '|' || score || '|' || never_play || '|' || updated_at::text,
                ';' order by media_id), '')
            from library.media_rating
            """;
        return (string?)await cmd.ExecuteScalarAsync() ?? "";
    }
}

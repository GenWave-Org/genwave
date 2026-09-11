// STORY-427 — Music picker is the installed music (SPEC F174.7 · PLAN T446)
//
// BDD specification — xUnit through the deployed entry point (WebApplicationFactory<Program>
// against a real ephemeral Postgres — the Story382/Story374 arc idiom): these facts drive
// GET /api/media?imagingKind=&jingleRole= over HTTP with an authed admin session, never the
// repository directly. One arc (MusicPickerBrowseArc) arranges everything every Scenario below
// reads — the SAME "arrange once, many read-only Scenarios" idiom Story382's own
// KindScopedPagingArc already establishes.
//
// Under spec: the browse route filters by imagingKind and jingleRole together (STORY-427 AC1) —
// each returned row carries id, title, and pack (the installing pack's display name) — and an
// unrecognized filter value refuses the whole request with 400, naming the field, never echoing
// the caller's value (STORY-427 AC2). AC3/AC4 (the wizard's own music dropdown) are T448's
// territory, not this task's.

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
using Npgsql;

namespace GenWave.Host.Tests.Specs;

public static class FeatureMusicPickerIsTheInstalledMusic
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    /// <summary>STORY-427 AC1 — imagingKind=jingle&amp;jingleRole=bed returns exactly the 8 seeded
    /// beds, each row shaped for a music-picker list.</summary>
    [Collection(MusicPickerBrowseCollection.Name)]
    public sealed class ScenarioTheBrowseRouteFiltersByImagingKindAndJingleRole(MusicPickerBrowseArc arc)
    {
        [Fact]
        public void TheFilteredBrowseIs200()
            => Assert.Equal(HttpStatusCode.OK, arc.FilteredBedsStatus);

        [Fact]
        public void ExactlyTheEightBedsAreReturned()
            // A real SET comparison against the seeded bed ids (both sorted, AC1's own "exactly
            // those 8 tracks" wording) — not a bare count, so a filter that returns the right
            // COUNT via the wrong ROWS (e.g. an off-by-one jingle_role match, or the sting/
            // station_id rows leaking in) still fails this fact by name.
            => Assert.Equal(arc.SeededBedIds, arc.FilteredBedIds);

        [Fact]
        public void EachRowCarriesIdTitleAndPack()
        {
            Assert.Equal(MusicPickerBrowseArc.BedCount, arc.FilteredBedRows.Count);
            Assert.All(arc.FilteredBedRows, row => Assert.False(string.IsNullOrEmpty(row.Id)));
            Assert.Equal(
                arc.SeededBedTitles,
                arc.FilteredBedRows.Select(row => row.Title ?? "").ToHashSet());
            Assert.All(arc.FilteredBedRows, row => Assert.Equal(MusicPickerBrowseArc.PackName, row.Pack));
        }
    }

    /// <summary>imagingKind alone (no jingleRole) returns the whole pack — the 8 beds plus the 1
    /// sting plus the 1 station_id row — never the plain scanned-music row or the liner row.</summary>
    // gh-#718 — the packs' library normally sits OUTSIDE the station rotation scope, so the
    // picker's unnamed browse came back empty while the station's own render picked a bed anyway.
    // The picker now names the pack's library (learned from GET /api/jingle-packs' libraryId,
    // Story399); this Scenario is the Host fact that the named browse reaches those beds.
    [Collection(MusicPickerBrowseCollection.Name)]
    public sealed class ScenarioANamedLibraryBrowseReachesBedsOutsideTheStationScope(MusicPickerBrowseArc arc)
    {
        [Fact]
        public void TheNamedLibraryBrowseIs200()
            => Assert.Equal(HttpStatusCode.OK, arc.NamedLibraryBedsStatus);

        [Fact]
        public void ItReturnsExactlyTheBedInThatLibrary()
            => Assert.Equal([arc.OutOfScopeBedId], arc.NamedLibraryBedIds);

        [Fact]
        public void ItIsFlaggedOutOfScopeRatherThanHidden()
            // F23.6 — scope is a curation boundary, not a trust boundary: rows come back, the
            // header says so. The unnamed browse keeps hiding this bed (ExactlyTheEightBedsAreReturned).
            => Assert.Equal("true", arc.NamedLibraryOutOfScopeHeader);
    }

    [Collection(MusicPickerBrowseCollection.Name)]
    public sealed class ScenarioImagingKindAloneReturnsTheWholeInstalledPack(MusicPickerBrowseArc arc)
    {
        [Fact]
        public void AllTenPackRowsAreReturned()
            => Assert.Equal(10, arc.WholePackCount);
    }

    /// <summary>PLAN T446 ruling (F2) — an absent imagingKind means no imaging filter at all, never
    /// ImagingKindTokens.TryParse's own null-to-liner default narrowing the browse down to the one
    /// liner row this arc also seeded.</summary>
    [Collection(MusicPickerBrowseCollection.Name)]
    public sealed class ScenarioAbsentImagingKindAppliesNoFilter(MusicPickerBrowseArc arc)
    {
        [Fact]
        public void AnUnfilteredBrowseStillListsEveryKindOfRow()
            => Assert.Subset(arc.UnfilteredBrowseIds, arc.AllSeededIds);
    }

    /// <summary>PLAN T446 ruling — the "N unavailable tracks hidden" count on an imaging-filtered
    /// browse counts against that SAME filtered row set, never the whole scope's unavailable rows.
    /// This arc seeds one unavailable bed (would match imagingKind=jingle&amp;jingleRole=bed) and
    /// one unavailable plain music row (would not); the filtered browse's header must name only the
    /// bed.</summary>
    [Collection(MusicPickerBrowseCollection.Name)]
    public sealed class ScenarioTheHiddenCountOnAFilteredBrowseMatchesItsOwnFilter(MusicPickerBrowseArc arc)
    {
        [Fact]
        public void TheHiddenCountOnAFilteredBrowseCountsOnlyTheFilteredRows()
            => Assert.Equal("1", arc.FilteredHiddenCountHeader);
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    /// <summary>STORY-427 AC2 — an unrecognized filter value refuses the whole request.</summary>
    [Collection(MusicPickerBrowseCollection.Name)]
    public sealed class ScenarioRejectingInvalidFilterValues(MusicPickerBrowseArc arc)
    {
        [Fact]
        public void AnUnknownImagingKindIs400()
            => Assert.Equal(HttpStatusCode.BadRequest, arc.UnknownImagingKindStatus);

        [Fact]
        public void AnUnknownJingleRoleIs400()
            => Assert.Equal(HttpStatusCode.BadRequest, arc.UnknownJingleRoleStatus);

        [Fact]
        public void JingleRoleWithoutImagingKindIs400()
            => Assert.Equal(HttpStatusCode.BadRequest, arc.JingleRoleAloneStatus);

        /// <summary>PLAN T446 ruling — the fourth cell of the 400 matrix: jingleRole named alongside
        /// an imagingKind that parses but isn't jingle still refuses the request, naming the same
        /// "applies only with imagingKind=jingle" reason a wholly absent imagingKind gets.</summary>
        [Fact]
        public void JingleRoleWithANonJingleImagingKindIs400()
        {
            Assert.Equal(HttpStatusCode.BadRequest, arc.JingleRoleWithNonJingleImagingKindStatus);
            Assert.Equal(
                "jingleRole applies only with imagingKind=jingle.",
                arc.JingleRoleWithNonJingleImagingKindBody.GetProperty("detail").GetString());
        }

        /// <summary>PLAN T446 ruling (F1) — the 400's Detail names the field and the accepted set
        /// only, never the caller's own unrecognized value (the Story368 aired-before precedent).</summary>
        [Fact]
        public void TheImagingKindDetailNamesTheFieldAndTheAcceptedSet() =>
            Assert.Equal(
                "imagingKind must be one of: liner, station_id, jingle, promo, ad.",
                arc.UnknownImagingKindBody.GetProperty("detail").GetString());

        [Fact]
        public void TheImagingKindDetailNeverEchoesTheCallersValue() =>
            Assert.DoesNotContain("nonsense", arc.UnknownImagingKindBody.GetProperty("detail").GetString());

        [Fact]
        public void TheJingleRoleDetailNamesTheFieldAndTheAcceptedSet() =>
            Assert.Equal(
                "jingleRole must be one of: bed, sting, station_id.",
                arc.UnknownJingleRoleBody.GetProperty("detail").GetString());

        [Fact]
        public void TheJingleRoleDetailNeverEchoesTheCallersValue() =>
            Assert.DoesNotContain("nonsense", arc.UnknownJingleRoleBody.GetProperty("detail").GetString());
    }
}

// ── Collection definition — one ephemeral Postgres/factory shared by every Scenario above (the
// Story382/Story374 "arrange once, many read-only Scenarios" idiom, via ICollectionFixture<T>). ──

[CollectionDefinition(Name)]
public sealed class MusicPickerBrowseCollection : ICollectionFixture<MusicPickerBrowseArc>
{
    public const string Name = "Story427MusicPickerBrowse";
}

/// <summary>
/// Seeds one jingle pack (slug <c>story427-pack</c>, display name "Story 427 Pack"): 8 bed assets,
/// 1 sting, 1 station_id — the same 10-row "a pack IS its files" shape a real jingle-pack install
/// (<c>JinglePackRepository.AssetUpsertSql</c>) would itself write — directly via raw SQL, never
/// through the install pipeline (which needs real audio bytes and ffmpeg; the browse filter under
/// test here only cares what already landed in <c>library.media</c>, mirroring Story382's own
/// GardenerRotFixtures posture of an "independent read of what actually landed"). Also seeds one
/// plain scanned-music row (<c>imaging_kind</c> null) and one liner row, so the filtered browse
/// must actually EXCLUDE something to pass rather than happening to return everything present. Two
/// further rows (PLAN T446 ruling) sit in <c>state = 'unavailable'</c> — one more bed under the same
/// pack, one more plain music row — never visible on any page this arc drives, but present so the
/// "N unavailable tracks hidden" header has something real to count.
/// </summary>
public sealed class MusicPickerBrowseArc : IAsyncLifetime
{
    public const int BedCount = 8;
    public const string PackName = "Story 427 Pack";
    const string PackSlug = "story427-pack";

    public HttpStatusCode FilteredBedsStatus { get; private set; }
    public IReadOnlyList<long> SeededBedIds { get; private set; } = [];
    public IReadOnlyList<long> FilteredBedIds { get; private set; } = [];
    public IReadOnlySet<string> SeededBedTitles { get; private set; } = new HashSet<string>();
    public IReadOnlyList<(string Id, string? Title, string? Pack)> FilteredBedRows { get; private set; } = [];

    public int WholePackCount { get; private set; }

    /// <summary>PLAN T446 ruling (F2) — every VISIBLE id this arc seeds across every imaging kind
    /// plus the plain scanned-music row (12 total) — never the two <c>state = 'unavailable'</c> rows
    /// the browse hides on every page it draws, filtered or not — so an unfiltered browse can be
    /// checked against the whole visible seeded set rather than only the pack.</summary>
    public HashSet<long> AllSeededIds { get; private set; } = [];
    public HashSet<long> UnfilteredBrowseIds { get; private set; } = [];

    /// <summary>PLAN T446 ruling — the X-Unavailable-Hidden header off the AC1 filtered browse
    /// (imagingKind=jingle&amp;jingleRole=bed), read as a raw string the way Gh113's own header
    /// facts do.</summary>
    public string? FilteredHiddenCountHeader { get; private set; }

    /// <summary>gh-#718 — the one bed seeded into a SECOND library (the packs' own, outside
    /// <c>Station:Scope:LibraryIds</c> = [1]) and the named-library browse that reaches it.</summary>
    public long OutOfScopeLibraryId { get; private set; }
    public long OutOfScopeBedId { get; private set; }
    public HttpStatusCode NamedLibraryBedsStatus { get; private set; }
    public string? NamedLibraryOutOfScopeHeader { get; private set; }
    public IReadOnlyList<long> NamedLibraryBedIds { get; private set; } = [];

    public HttpStatusCode UnknownImagingKindStatus { get; private set; }
    public HttpStatusCode UnknownJingleRoleStatus { get; private set; }
    public HttpStatusCode JingleRoleAloneStatus { get; private set; }
    public HttpStatusCode JingleRoleWithNonJingleImagingKindStatus { get; private set; }

    /// <summary>PLAN T446 ruling (F1) — the two 400 bodies, captured so a Fact can pin the exact
    /// Detail text and prove it never echoes the caller's own unrecognized value (the Story368
    /// RotationHealthArc.BadAiredBeforeBody precedent, same endpoint, sibling filter).</summary>
    public JsonElement UnknownImagingKindBody { get; private set; }
    public JsonElement UnknownJingleRoleBody { get; private set; }
    public JsonElement JingleRoleWithNonJingleImagingKindBody { get; private set; }

    public async Task InitializeAsync()
    {
        // A LOCAL, not a field — Story427MusicPickerDatabase is file-local (CS9051), the same
        // reason Story382's own arc gives for the identical shape.
        await using var database = await Story427MusicPickerDatabase.StartAsync();

        var bedIds = new List<long>();
        var bedTitles = new HashSet<string>();
        for (var i = 1; i <= BedCount; i++)
        {
            var title = $"Story 427 Bed {i:D2}";
            bedTitles.Add(title);
            bedIds.Add(await InsertJinglePackRowAsync(
                database.LibraryConnectionString, $"/test/t446-bed-{i:D2}.flac", title, "bed"));
        }
        SeededBedIds = bedIds.OrderBy(id => id).ToList();
        SeededBedTitles = bedTitles;

        var stingId = await InsertJinglePackRowAsync(
            database.LibraryConnectionString, "/test/t446-sting-01.flac", "Story 427 Sting", "sting");
        var stationIdRowId = await InsertJinglePackRowAsync(
            database.LibraryConnectionString, "/test/t446-stationid-01.flac", "Story 427 Station ID", "station_id");

        var plainMusicId = await InsertPlainMusicRowAsync(
            database.LibraryConnectionString, "/test/t446-music-01.flac", "Story 427 Plain Music");
        var linerId = await InsertLinerRowAsync(
            database.LibraryConnectionString, "/test/t446-liner-01.flac", "Story 427 Liner");

        // PLAN T446 ruling — two rows the browse hides on every page, never counted in
        // AllSeededIds: one more bed under this same pack (matches the AC1 filter), one more plain
        // music row (does not). The filtered "N hidden" header must name only the bed.
        await InsertJinglePackRowAsync(
            database.LibraryConnectionString, "/test/t446-bed-hidden.flac", "Story 427 Hidden Bed", "bed",
            state: "unavailable");
        await InsertPlainMusicRowAsync(
            database.LibraryConnectionString, "/test/t446-music-hidden.flac", "Story 427 Hidden Music",
            state: "unavailable");

        AllSeededIds = [.. bedIds, stingId, stationIdRowId, plainMusicId, linerId];

        // gh-#718 — a second library standing in for the packs' own `ads` library, deliberately
        // OUTSIDE this factory's Station:Scope:LibraryIds = [1], with one ready bed under the same
        // pack. Never part of AllSeededIds: every unnamed browse above is station-scoped and must
        // keep hiding it (ExactlyTheEightBedsAreReturned already pins that by set equality).
        OutOfScopeLibraryId = await InsertLibraryAsync(database.LibraryConnectionString, "ads");
        OutOfScopeBedId = await InsertJinglePackRowAsync(
            database.LibraryConnectionString, "/test/t446-bed-outside-scope.flac", "Story 427 Out-Of-Scope Bed", "bed",
            libraryId: OutOfScopeLibraryId);

        await using var factory = new Story427WebFactory(database);
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new { password = Story427WebFactory.Password });
        if (login.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login unexpectedly returned {login.StatusCode}");

        // gh-#718 — the picker's exact query plus F23.2's named-library override.
        var namedLibrary = await client.GetAsync(
            $"/api/media?imagingKind=jingle&jingleRole=bed&limit=200&library-id={OutOfScopeLibraryId}");
        NamedLibraryBedsStatus = namedLibrary.StatusCode;
        NamedLibraryOutOfScopeHeader = namedLibrary.Headers.TryGetValues("X-Out-Of-Scope", out var outOfScopeValues)
            ? string.Join(",", outOfScopeValues)
            : null;
        NamedLibraryBedIds = JsonDocument.Parse(await namedLibrary.Content.ReadAsStringAsync())
            .RootElement.EnumerateArray()
            .Select(row => long.Parse(row.GetProperty("mediaId").GetString() ?? "0"))
            .OrderBy(id => id)
            .ToList();

        // STORY-427 AC1 — the happy path.
        var filtered = await client.GetAsync("/api/media?imagingKind=jingle&jingleRole=bed");
        FilteredBedsStatus = filtered.StatusCode;
        // PLAN T446 ruling — the hidden count on THIS filtered browse must count only the one
        // hidden bed seeded above, never the hidden plain music row that doesn't match the filter.
        FilteredHiddenCountHeader = filtered.Headers.TryGetValues("X-Unavailable-Hidden", out var hiddenValues)
            ? hiddenValues.Single()
            : null;
        var filteredRows = JsonDocument.Parse(await filtered.Content.ReadAsStringAsync())
            .RootElement.EnumerateArray().ToList();
        FilteredBedIds = filteredRows
            .Select(row => long.Parse(row.GetProperty("mediaId").GetString() ?? "0"))
            .OrderBy(id => id)
            .ToList();
        FilteredBedRows = filteredRows
            .Select(row => (
                Id: row.GetProperty("mediaId").GetString() ?? "",
                Title: row.TryGetProperty("title", out var titleProperty) ? titleProperty.GetString() : null,
                Pack: row.TryGetProperty("pack", out var packProperty) ? packProperty.GetString() : null))
            .ToList();

        // imagingKind=jingle alone — the whole installed pack, 10 rows.
        var wholePack = await client.GetAsync("/api/media?imagingKind=jingle");
        WholePackCount = JsonDocument.Parse(await wholePack.Content.ReadAsStringAsync())
            .RootElement.GetArrayLength();

        // PLAN T446 ruling (F2) — no imagingKind named at all: the page must still list every kind
        // of row this arc seeded, proving "absent means no filter" rather than
        // ImagingKindTokens.TryParse's own null-to-liner default silently narrowing the browse.
        var unfiltered = await client.GetAsync("/api/media");
        UnfilteredBrowseIds = JsonDocument.Parse(await unfiltered.Content.ReadAsStringAsync())
            .RootElement.EnumerateArray()
            .Select(row => long.Parse(row.GetProperty("mediaId").GetString() ?? "0"))
            .ToHashSet();

        // STORY-427 AC2 — sad path. Bodies are captured, not just status, so a Fact can pin the
        // exact Detail text and prove it never echoes the caller's own unrecognized value (PLAN
        // T446 ruling F1).
        var unknownKind = await client.GetAsync("/api/media?imagingKind=nonsense");
        UnknownImagingKindStatus = unknownKind.StatusCode;
        UnknownImagingKindBody = JsonDocument.Parse(await unknownKind.Content.ReadAsStringAsync()).RootElement.Clone();

        var unknownRole = await client.GetAsync("/api/media?imagingKind=jingle&jingleRole=nonsense");
        UnknownJingleRoleStatus = unknownRole.StatusCode;
        UnknownJingleRoleBody = JsonDocument.Parse(await unknownRole.Content.ReadAsStringAsync()).RootElement.Clone();

        var roleAlone = await client.GetAsync("/api/media?jingleRole=bed");
        JingleRoleAloneStatus = roleAlone.StatusCode;

        // PLAN T446 ruling — the fourth cell of the 400 matrix: jingleRole named alongside an
        // imagingKind that parses but isn't jingle.
        var roleWithNonJingleKind = await client.GetAsync("/api/media?imagingKind=liner&jingleRole=bed");
        JingleRoleWithNonJingleImagingKindStatus = roleWithNonJingleKind.StatusCode;
        JingleRoleWithNonJingleImagingKindBody =
            JsonDocument.Parse(await roleWithNonJingleKind.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    static async Task<long> InsertJinglePackRowAsync(
        string libraryConnectionString, string path, string title, string jingleRole, string state = "ready",
        long libraryId = 1)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            insert into library.media
                (path, format, size_bytes, mtime, state, duration_ms, title, artist,
                 eligible, imaging_kind, jingle_role, pack_slug, library_id)
            values
                (@path, 'wav', 1024, now(), @state, 3000, @title, @artist,
                 true, 'jingle', @jingleRole, @packSlug, @libraryId)
            returning id
            """;
        cmd.Parameters.AddWithValue("libraryId", libraryId);
        cmd.Parameters.AddWithValue("path", path);
        cmd.Parameters.AddWithValue("title", title);
        cmd.Parameters.AddWithValue("artist", PackName);
        cmd.Parameters.AddWithValue("jingleRole", jingleRole);
        cmd.Parameters.AddWithValue("packSlug", PackSlug);
        cmd.Parameters.AddWithValue("state", state);
        return (long)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("insert returned no id"));
    }

    static async Task<long> InsertLibraryAsync(string libraryConnectionString, string name)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "insert into library.library (name) values (@name) returning id";
        cmd.Parameters.AddWithValue("name", name);
        return (long)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("insert returned no id"));
    }

    static async Task<long> InsertPlainMusicRowAsync(
        string libraryConnectionString, string path, string title, string state = "ready")
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            insert into library.media (path, format, size_bytes, mtime, state, duration_ms, title, artist, eligible)
            values (@path, 'flac', 1024, now(), @state, 200000, @title, 'Story 427 Artist', true)
            returning id
            """;
        cmd.Parameters.AddWithValue("path", path);
        cmd.Parameters.AddWithValue("title", title);
        cmd.Parameters.AddWithValue("state", state);
        return (long)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("insert returned no id"));
    }

    static async Task<long> InsertLinerRowAsync(string libraryConnectionString, string path, string title)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            insert into library.media (path, format, size_bytes, mtime, state, duration_ms, title, artist, eligible, imaging_kind)
            values (@path, 'wav', 1024, now(), 'ready', 5000, @title, 'Station', true, 'liner')
            returning id
            """;
        cmd.Parameters.AddWithValue("path", path);
        cmd.Parameters.AddWithValue("title", title);
        return (long)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("insert returned no id"));
    }
}

// ── Test harness — WebApplicationFactory + ephemeral Postgres subclasses (Story382's own idiom;
// `file`-scoped types cannot cross files, so this file supplies its own, exactly as
// EphemeralStationDatabase's own remarks explain). ──

/// <summary>
/// Boots the real production composition root against a real ephemeral Postgres with every hosted
/// service removed (no gardener/rotation/liquidsoap background loop reach) — this arc only needs
/// the real <c>MediaController</c> browse endpoint over a real admin session.
/// </summary>
file sealed class Story427WebFactory(Story427MusicPickerDatabase db) : WebApplicationFactory<Program>
{
    public const string Password = "test-password-t446-music-picker-browse";

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
/// port" rationale. Supplies only the <c>"genwave-t446"</c> compose project-name prefix this file's
/// own arc needs.
/// </summary>
file sealed class Story427MusicPickerDatabase : EphemeralStationDatabase
{
    Story427MusicPickerDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story427MusicPickerDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t446");
        var db = new Story427MusicPickerDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

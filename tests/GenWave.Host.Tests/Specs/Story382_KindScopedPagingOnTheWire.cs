// STORY-382 — I page through a big kind at my own pace (SPEC F153.9 rider 2026-08-31 · PLAN T386 · gh-#657)
//
// BDD specification — xUnit through the deployed entry point (WebApplicationFactory<Program>
// against a real ephemeral Postgres — the Story374/Story378 arc idiom): these facts drive
// GET /api/gardener/findings over HTTP with an authed admin session, never the repository
// directly. One arc (KindScopedPagingArc) arranges everything every Scenario below reads —
// the SAME "arrange once, many read-only Scenarios" idiom GardenerFindingsCollection already
// establishes in Story374_TheGardenerTendsAQueue.cs.
//
// Under spec: a kind=-scoped response gains `total` (groups for near_duplicate, rows otherwise);
// the near-duplicate path routes through T385's group-paged read; a call WITHOUT kind= stays
// byte-compatible with the T377 shape (flat page, grouped response, NO total property). The
// 400/clamp posture is T377's and is not re-pinned here. STORY-383 AC1–AC3's wire half lives
// here; their store half is MediaLibrary.Tests Story383_DuplicateClustersNeverSplit.cs.

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

public static class FeatureKindScopedPagingOnTheWire
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    /// <summary>STORY-382 AC6 — a flat kind, scoped: 60 open dead_file findings seeded.</summary>
    [Collection(KindScopedPagingCollection.Name)]
    public sealed class ScenarioKindScopedResponseCarriesTotal(KindScopedPagingArc arc)
    {
        [Fact]
        public void TotalIsTheExactOpenRowCountForTheKind()
        {
            // GET /api/gardener/findings?kind=dead_file&state=open&limit=25 → body.total == 60.
            Assert.Equal(60, arc.KindScopedTotal);
        }

        [Fact]
        public void ThePageCarriesLimitRowsOfThatKindOnly()
        {
            // 25 findings, every group.kind == dead_file.
            Assert.Equal((25, "dead_file"), (arc.KindScopedFindingsCount, arc.KindScopedGroupKind));
        }

        [Fact]
        public void TheResponseCarriesATotalProperty()
        {
            // Presence pin (LOW-3) — without this, a missing "total" only surfaces as a fixture
            // arrangement exception (GetProperty throwing in InitializeAsync) shared across every
            // fact in this file, never as a named failure of its own.
            Assert.True(arc.KindScopedHasTotalProperty);
        }
    }

    /// <summary>STORY-383 AC1–AC3 on the wire — 30 seeded duplicate groups of 2–4 members.</summary>
    [Collection(KindScopedPagingCollection.Name)]
    public sealed class ScenarioNearDuplicatesPageByGroupOnTheWire(KindScopedPagingArc arc)
    {
        [Fact]
        public void LimitSelectsWholeGroupsNeverPartialOnes()
        {
            // ?kind=near_duplicate&limit=25 → 25 duplicateGroups, each with ALL its members — the
            // Story383 EveryReturnedGroupIsWhole shape (LOW-2): expected vs. actual member-count
            // SEQUENCES, in group_key order, compared in one shot, so a failure names exactly which
            // page-one group broke rather than collapsing every group into one precomputed bool.
            Assert.Equal(arc.NearDupPage1ExpectedMemberSizes, arc.NearDupPage1ActualMemberSizes);
        }

        [Fact]
        public void OffsetContinuesAtTheNextGroup()
        {
            // ?offset=25 → the remaining 5 groups, disjoint from page one's groupKeys — named counts
            // (page count, intersection count) via a genuine set comparison (LOW-2, the Story383
            // SharesNoGroupKeyWithPageOne shape), not a precomputed bool.
            Assert.Equal((5, 0), (arc.NearDupPage2Keys.Count, arc.NearDupPage2Keys.Intersect(arc.NearDupPage1Keys).Count()));
        }

        [Fact]
        public void TotalCountsGroupsNotRows()
        {
            // body.total == 30 (groups) while /api/status.gardener.open.nearDuplicate == 90 (rows) —
            // named values (LOW-1), so a status regression (e.g. to 45) can no longer stay green
            // behind a bare inequality (30 != 45 would still have been "true").
            Assert.Equal((30, 90), (arc.NearDupTotal, arc.StatusOpenNearDuplicateRowCount));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH / regression pins
    // ---------------------------------------------------------------------

    /// <summary>STORY-382 AC8 — the T377 contract for un-scoped callers stands verbatim.</summary>
    [Collection(KindScopedPagingCollection.Name)]
    public sealed class ScenarioTheUnscopedCallKeepsTheT377Shape(KindScopedPagingArc arc)
    {
        [Fact]
        public void CarriesNoTotalProperty()
        {
            // GET /api/gardener/findings?state=open → the JSON body has no "total" member at all.
            Assert.False(arc.UnscopedHasTotalProperty);
        }

        [Fact]
        public void TheResponseHasExactlyOneTopLevelProperty()
        {
            // LOW-4 — the stronger sibling of CarriesNoTotalProperty: the root object's own property
            // SET is exactly ["groups"], pinning byte-compatibility with T377 against "total" AND any
            // future stray top-level member, not just the one named property.
            Assert.Equal(["groups"], arc.UnscopedPropertyNames);
        }

        [Fact]
        public void PagesFlatAcrossKinds()
        {
            // Seed enough dead_file rows to fill the page: the near_duplicate group is absent —
            // the gh-#654 behavior, correct for THIS un-scoped shape and pinned as such.
            Assert.DoesNotContain("near_duplicate", arc.UnscopedFloodKinds);
        }
    }
}

// ── Collection definition — one ephemeral Postgres/factory shared by every Scenario above (the
// Story374 "arrange once, many read-only Scenarios" idiom, via ICollectionFixture<T>). ──

[CollectionDefinition(Name)]
public sealed class KindScopedPagingCollection : ICollectionFixture<KindScopedPagingArc>
{
    public const string Name = "Story382KindScopedPaging";
}

/// <summary>
/// Seeds 60 open <c>dead_file</c> findings and 30 open <c>near_duplicate</c> groups (2–4 members
/// each, 90 rows total) directly via raw SQL (<see cref="GardenerRotFixtures"/> — the SAME
/// "independent read of what actually landed" posture Story374's own fixtures already establish;
/// never through a reconcile pass), then drives every query this file's Scenarios need over the
/// REAL production HTTP pipeline with a real admin session — the SAME
/// <see cref="EphemeralStationDatabase"/>-subclass idiom Story378_KeepThisOneBulkEligibility.cs
/// already uses. The 60 dead_file rows do double duty: they prove an exact kind-scoped total
/// (STORY-382 AC6) AND, reused with a smaller <c>limit</c>, flood an un-scoped page past every
/// near_duplicate row (STORY-382 AC8's own gh-#654 regression pin) — one arrangement, no second
/// seed needed. Group keys are zero-padded (<c>grp-01</c>..<c>grp-30</c>) so lexicographic
/// <c>group_key asc</c> ordering matches numeric order, making page one/page two's own group
/// split deterministic.
/// </summary>
public sealed class KindScopedPagingArc : IAsyncLifetime
{
    const int DeadFileCount = 60;
    const int NearDuplicateGroupCount = 30;

    public bool KindScopedHasTotalProperty { get; private set; }
    public int KindScopedTotal { get; private set; }
    public int KindScopedFindingsCount { get; private set; }
    public string KindScopedGroupKind { get; private set; } = "";

    /// <summary>LOW-2 — the seeded (expected) and observed (actual) per-group member counts for
    /// page one, BOTH in <c>group_key</c> ascending order, so <see cref="Assert.Equal{T}(T,T)"/> over
    /// the two sequences names exactly which group (by position) broke, the Story383
    /// EveryReturnedGroupIsWhole shape — never a single precomputed bool collapsing every group's own
    /// comparison into one opaque pass/fail.</summary>
    public IReadOnlyList<int> NearDupPage1ExpectedMemberSizes { get; private set; } = [];
    public IReadOnlyList<int> NearDupPage1ActualMemberSizes { get; private set; } = [];

    /// <summary>LOW-2 — the raw group_key sets for page one/page two, so the disjointness fact does
    /// its own set comparison (<c>Intersect</c>) rather than reading a precomputed bool.</summary>
    public IReadOnlySet<string> NearDupPage1Keys { get; private set; } = new HashSet<string>();
    public IReadOnlySet<string> NearDupPage2Keys { get; private set; } = new HashSet<string>();

    public int NearDupTotal { get; private set; }
    public int StatusOpenNearDuplicateRowCount { get; private set; }

    public bool UnscopedHasTotalProperty { get; private set; }
    public IReadOnlyList<string> UnscopedPropertyNames { get; private set; } = [];
    public IReadOnlyList<string> UnscopedFloodKinds { get; private set; } = [];

    public async Task InitializeAsync()
    {
        // A LOCAL, not a field — Story382KindScopedPagingDatabase is file-local (CS9051), the same
        // reason Story374's/Story378's own arcs give for the identical shape.
        await using var database = await Story382KindScopedPagingDatabase.StartAsync();

        for (var i = 1; i <= DeadFileCount; i++)
        {
            var mediaId = await GardenerRotFixtures.InsertPlayableMediaRowAsync(
                database.LibraryConnectionString, $"/test/t386-dead-{i:D2}.flac", 200_000, $"Dead Song {i}", "Artist Dead");
            await GardenerRotFixtures.InsertFindingAsync(
                database.LibraryConnectionString, mediaId, "dead_file", "open", null, "{}");
        }

        var seededMemberCounts = new Dictionary<string, int>();
        for (var g = 1; g <= NearDuplicateGroupCount; g++)
        {
            var groupKey = $"grp-{g:D2}";
            var memberCount = 2 + (g - 1) % 3;
            seededMemberCounts[groupKey] = memberCount;

            for (var m = 1; m <= memberCount; m++)
            {
                var mediaId = await GardenerRotFixtures.InsertPlayableMediaRowAsync(
                    database.LibraryConnectionString, $"/test/t386-dup-{g:D2}-{m}.flac", 200_000 + m * 1_000,
                    $"Dup Song {g}", "Artist Dup");
                await GardenerRotFixtures.InsertFindingAsync(
                    database.LibraryConnectionString, mediaId, "near_duplicate", "open", groupKey, "{}");
            }
        }

        await using var factory = new Story382WebFactory(database);
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new { password = Story382WebFactory.Password });
        if (login.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login unexpectedly returned {login.StatusCode}");

        // STORY-382 AC6 — a flat kind, scoped. TryGetProperty first (LOW-3) so a missing "total"
        // fails ONLY TheResponseCarriesATotalProperty, never crashes the whole arrangement for every
        // other fact in this file.
        var kindScoped = await client.GetAsync("/api/gardener/findings?kind=dead_file&state=open&limit=25");
        var kindScopedRoot = JsonDocument.Parse(await kindScoped.Content.ReadAsStringAsync()).RootElement;
        KindScopedHasTotalProperty = kindScopedRoot.TryGetProperty("total", out var kindScopedTotalProperty);
        KindScopedTotal = KindScopedHasTotalProperty ? kindScopedTotalProperty.GetInt32() : -1;
        var kindScopedGroup = kindScopedRoot.GetProperty("groups").EnumerateArray().Single();
        KindScopedGroupKind = kindScopedGroup.GetProperty("kind").GetString() ?? "";
        KindScopedFindingsCount = kindScopedGroup.GetProperty("findings").GetArrayLength();

        // STORY-383 AC1–AC3 on the wire — near-duplicate group paging, page one, both sides of the
        // LOW-2 sequence comparison built in group_key ascending order (matching the store's own
        // paging order, Garden.RotFindingRepository.ListNearDuplicateGroupPageAsync).
        var nearDupPage1 = await client.GetAsync("/api/gardener/findings?kind=near_duplicate&limit=25");
        var nearDupPage1Root = JsonDocument.Parse(await nearDupPage1.Content.ReadAsStringAsync()).RootElement;
        NearDupTotal = nearDupPage1Root.GetProperty("total").GetInt32();
        var page1Groups = nearDupPage1Root.GetProperty("groups").EnumerateArray().Single()
            .GetProperty("duplicateGroups").EnumerateArray()
            .OrderBy(duplicateGroup => duplicateGroup.GetProperty("groupKey").GetString(), StringComparer.Ordinal)
            .ToList();
        // A FIXED Take(25) (the query's own limit), never page1Groups.Count — the whole point of a
        // named expected sequence is to catch a wrong PAGE SIZE too, not just wrong per-group sizes;
        // sizing "expected" off the actual response would silently agree with a truncated page.
        NearDupPage1ExpectedMemberSizes = seededMemberCounts
            .OrderBy(seeded => seeded.Key, StringComparer.Ordinal)
            .Take(25)
            .Select(seeded => seeded.Value)
            .ToArray();
        NearDupPage1ActualMemberSizes = page1Groups
            .Select(duplicateGroup => duplicateGroup.GetProperty("members").GetArrayLength())
            .ToArray();
        NearDupPage1Keys = page1Groups.Select(duplicateGroup => duplicateGroup.GetProperty("groupKey").GetString() ?? "").ToHashSet();

        // Page two — offset continues at the next group.
        var nearDupPage2 = await client.GetAsync("/api/gardener/findings?kind=near_duplicate&limit=25&offset=25");
        var nearDupPage2Root = JsonDocument.Parse(await nearDupPage2.Content.ReadAsStringAsync()).RootElement;
        NearDupPage2Keys = nearDupPage2Root.GetProperty("groups").EnumerateArray().Single()
            .GetProperty("duplicateGroups").EnumerateArray()
            .Select(duplicateGroup => duplicateGroup.GetProperty("groupKey").GetString() ?? "")
            .ToHashSet();

        var status = await client.GetAsync("/api/status");
        var statusRoot = JsonDocument.Parse(await status.Content.ReadAsStringAsync()).RootElement;
        StatusOpenNearDuplicateRowCount = statusRoot.GetProperty("gardener").GetProperty("open").GetProperty("nearDuplicate").GetInt32();

        // STORY-382 AC8 — the un-scoped call stays byte-compatible with T377's pinned shape.
        var unscoped = await client.GetAsync("/api/gardener/findings?state=open");
        var unscopedRoot = JsonDocument.Parse(await unscoped.Content.ReadAsStringAsync()).RootElement;
        UnscopedHasTotalProperty = unscopedRoot.TryGetProperty("total", out _);
        UnscopedPropertyNames = unscopedRoot.EnumerateObject().Select(property => property.Name).ToList();

        // A limit well under the 60 seeded dead_file rows (which sort first, kind before
        // near_duplicate in the library.rot_kind enum's own declaration order) — the page fills on
        // dead_file alone, so no near_duplicate group ever reaches it (gh-#654's own regression).
        var unscopedFlood = await client.GetAsync($"/api/gardener/findings?state=open&limit={DeadFileCount - 10}");
        var unscopedFloodRoot = JsonDocument.Parse(await unscopedFlood.Content.ReadAsStringAsync()).RootElement;
        UnscopedFloodKinds = unscopedFloodRoot.GetProperty("groups").EnumerateArray()
            .Select(group => group.GetProperty("kind").GetString() ?? "")
            .ToList();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Test harness — WebApplicationFactory + ephemeral Postgres subclasses (Story374's/Story378's
// own idiom; `file`-scoped types cannot cross files, so this file supplies its own, exactly as
// Story378's own remarks on EphemeralStationDatabase explain). ──

/// <summary>
/// Boots the real production composition root against a real ephemeral Postgres with every hosted
/// service removed (no gardener/rotation/liquidsoap background loop reach) — this arc only needs
/// the real <c>GardenerController</c> endpoints and <c>StatusController</c> over a real admin
/// session.
/// </summary>
file sealed class Story382WebFactory(Story382KindScopedPagingDatabase db) : WebApplicationFactory<Program>
{
    public const string Password = "test-password-t386-kind-scoped-paging";

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
/// port" rationale. Supplies only the <c>"genwave-t386"</c> compose project-name prefix this file's
/// own arc needs.
/// </summary>
file sealed class Story382KindScopedPagingDatabase : EphemeralStationDatabase
{
    Story382KindScopedPagingDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story382KindScopedPagingDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t386");
        var db = new Story382KindScopedPagingDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

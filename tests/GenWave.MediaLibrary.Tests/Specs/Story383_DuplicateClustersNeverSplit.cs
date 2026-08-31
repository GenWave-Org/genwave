// STORY-383 — Duplicate clusters never split (SPEC F153.9 rider 2026-08-31 · PLAN T385 · gh-#657)
//
// BDD specification — xUnit, REAL Postgres via DatabaseFixture (the Story376 posture: these facts
// drive the store's SQL against a live database, never a mock — the T362 loop law says every new
// SQL read gets a Postgres-backed fact). WIRED at T385 — every near_duplicate finding here is seeded
// DIRECTLY into library.rot_finding (never through ReconcileNearDuplicatesAsync): the file's own
// original guidance, since these facts target the READ's own group-paging shape, not the reconcile
// pass's own duplicate-detection SQL.
//
// Under spec: the kind-scoped joined read on IRotFindingStore/RotFindingRepository returns
// (rows, total) where, for kind=near_duplicate, limit/offset count DISTINCT group_keys (ordered
// asc — stable across pages) and the row set carries every member of each selected group; for
// every other kind limit/offset/total count rows exactly as T377 built them. The kind-LESS read
// keeps its current shape verbatim (regression pin). STORY-382 AC6 (total is exact) is pinned
// here at the store; its wire half lives in Host.Tests Story382_KindScopedPagingOnTheWire.cs.

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Garden;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureDuplicateClustersNeverSplit
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static RotFindingRepository Repo(DatabaseFixture db) => new(db.DataSource);

    /// <summary>A minimal media row — the FK target every <c>rot_finding</c> row needs.
    /// <c>ListWithMediaAsync</c>'s own join surfaces its columns, but no fact in this file asserts on
    /// them, so no tag/duration/state is worth seeding here.</summary>
    static async Task<long> InsertMediaRowAsync(DatabaseFixture db, string path)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            "insert into library.media (path, format, size_bytes, mtime) values (@path, 'flac', 1024, now()) returning id",
            new { path });
    }

    /// <summary>One OPEN <c>near_duplicate</c> finding, seeded directly (this file's own header
    /// explains why) — isolates the group-paged READ under test from
    /// <see cref="RotFindingRepository.ReconcileNearDuplicatesAsync"/>'s own SQL entirely.</summary>
    static async Task<long> InsertNearDuplicateFindingAsync(DatabaseFixture db, long mediaId, string groupKey)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            """
            insert into library.rot_finding (media_id, kind, state, group_key, evidence)
            values (@mediaId, 'near_duplicate'::library.rot_kind, 'open', @groupKey, '{}')
            returning id
            """,
            new { mediaId, groupKey });
    }

    /// <summary>One RESOLVED <c>near_duplicate</c> finding with its <paramref name="groupKey"/> left
    /// intact — mirrors what <c>ReconcileNearDuplicatesAsync</c>'s own resolve half actually leaves
    /// behind (it flips <c>state</c>/<c>resolved_at</c>/<c>updated_at</c> only, never <c>group_key</c>):
    /// a member that stopped being a duplicate on its own, still sitting inside its old group's row.
    /// </summary>
    static async Task<long> InsertResolvedNearDuplicateFindingAsync(DatabaseFixture db, long mediaId, string groupKey)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            """
            insert into library.rot_finding (media_id, kind, state, group_key, evidence, resolved_at)
            values (@mediaId, 'near_duplicate'::library.rot_kind, 'resolved', @groupKey, '{}', now())
            returning id
            """,
            new { mediaId, groupKey });
    }

    /// <summary>One OPEN <c>dead_file</c> finding with an explicit <paramref name="openedAt"/> — the
    /// flat kind-scoped fact (<see cref="ScenarioFlatKindsCountRows"/>) needs a DETERMINISTIC
    /// <c>opened_at desc</c> order regardless of wall-clock jitter between inserts.</summary>
    static async Task<long> InsertDeadFileFindingAsync(DatabaseFixture db, long mediaId, DateTimeOffset openedAt)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            """
            insert into library.rot_finding (media_id, kind, state, evidence, opened_at)
            values (@mediaId, 'dead_file'::library.rot_kind, 'open', '{}', @openedAt)
            returning id
            """,
            new { mediaId, openedAt });
    }

    static async Task<IReadOnlyList<long>> ReadFindingIdsForGroupAsync(DatabaseFixture db, string groupKey)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var ids = await conn.QueryAsync<long>(
            "select id from library.rot_finding where kind = 'near_duplicate'::library.rot_kind and group_key = @groupKey",
            new { groupKey });
        return ids.ToList();
    }

    /// <summary>One OPEN <c>near_duplicate</c> finding with an explicit <paramref name="openedAt"/> —
    /// the MED-5 ordering fact needs a DETERMINISTIC <c>opened_at desc</c> sequence within one group,
    /// regardless of wall-clock jitter between inserts.</summary>
    static async Task<long> InsertNearDuplicateFindingWithOpenedAtAsync(
        DatabaseFixture db, long mediaId, string groupKey, DateTimeOffset openedAt)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            """
            insert into library.rot_finding (media_id, kind, state, group_key, evidence, opened_at)
            values (@mediaId, 'near_duplicate'::library.rot_kind, 'open', @groupKey, '{}', @openedAt)
            returning id
            """,
            new { mediaId, groupKey, openedAt });
    }

    /// <summary>ONE near_duplicate group with exactly <paramref name="memberCount"/> OPEN member rows,
    /// returned in insertion order — the HIGH-1 facts need a fixed size (3 members) rather than
    /// <see cref="SeedGroupsAsync"/>'s own 2/3/4-cycling sizes, so they can dismiss a known subset.
    /// </summary>
    static async Task<IReadOnlyList<long>> SeedOneGroupAsync(DatabaseFixture db, string groupKey, int memberCount)
    {
        var ids = new List<long>();
        for (var m = 0; m < memberCount; m++)
        {
            var mediaId = await InsertMediaRowAsync(db, $"/t385/{groupKey}-{m}.flac");
            ids.Add(await InsertNearDuplicateFindingAsync(db, mediaId, groupKey));
        }

        return ids;
    }

    /// <summary><paramref name="groupCount"/> near_duplicate groups ("grp-00".."grp-NN", zero-padded
    /// so <c>group_key</c>'s own text-ascending order matches numeric order), sizes cycling 2/3/4
    /// members (STORY-383 AC1's own "2–4 members each") — every member row OPEN, none dismissed.
    /// </summary>
    static async Task<IReadOnlyList<(string GroupKey, int MemberCount)>> SeedGroupsAsync(DatabaseFixture db, int groupCount)
    {
        var groups = new List<(string GroupKey, int MemberCount)>();
        for (var g = 0; g < groupCount; g++)
        {
            var groupKey = $"grp-{g:D2}";
            var memberCount = 2 + g % 3;
            for (var m = 0; m < memberCount; m++)
            {
                var mediaId = await InsertMediaRowAsync(db, $"/t385/{groupKey}-{m}.flac");
                await InsertNearDuplicateFindingAsync(db, mediaId, groupKey);
            }

            groups.Add((groupKey, memberCount));
        }

        return groups;
    }

    /// <summary><paramref name="rowCount"/> OPEN <c>dead_file</c> findings, one second apart, so
    /// <c>opened_at desc</c> ordering is deterministic — index 0 is the OLDEST (last in the
    /// desc-ordered read), the highest index the NEWEST (first).</summary>
    static async Task<IReadOnlyList<long>> SeedDeadFileRowsAsync(DatabaseFixture db, int rowCount)
    {
        var baseTime = DateTimeOffset.UtcNow.AddHours(-1);
        var ids = new List<long>();
        for (var i = 0; i < rowCount; i++)
        {
            var mediaId = await InsertMediaRowAsync(db, $"/t385/dead-{i:D3}.flac");
            ids.Add(await InsertDeadFileFindingAsync(db, mediaId, baseTime.AddSeconds(i)));
        }

        return ids;
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    /// <summary>STORY-383 AC1 — 30 open near_duplicate groups of 2–4 members each; the store is
    /// asked for the first page of 25 groups.</summary>
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioLimitCountsGroups(DatabaseFixture db)
    {
        [Fact]
        public async Task ReturnsExactlyTwentyFiveDistinctGroupKeys()
        {
            await db.ResetAsync();
            await SeedGroupsAsync(db, 30);

            var page = await Repo(db).ListWithMediaAsync(
                RotKind.NearDuplicate, RotState.Open, limit: 25, offset: 0, ct: CancellationToken.None);

            Assert.Equal(25, page.Items.Select(r => r.Finding.GroupKey).Distinct().Count());
        }

        [Fact]
        public async Task EveryReturnedGroupIsWhole()
        {
            await db.ResetAsync();
            var groups = await SeedGroupsAsync(db, 30);

            var page = await Repo(db).ListWithMediaAsync(
                RotKind.NearDuplicate, RotState.Open, limit: 25, offset: 0, ct: CancellationToken.None);

            // ONE assertion over a homogeneous set (the T374 review MED-1 idiom): every returned
            // group's own member count, in group_key order, compared against the table's own seeded
            // member count for that same group, in one shot.
            var actualSizes = page.Items
                .GroupBy(r => r.Finding.GroupKey)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => g.Count())
                .ToArray();
            var expectedSizes = groups.Take(25).Select(g => g.MemberCount).ToArray();

            Assert.Equal(expectedSizes, actualSizes);
        }
    }

    /// <summary>STORY-383 AC2 — the same 30 groups, second page (offset 25 groups).</summary>
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioOffsetCountsGroups(DatabaseFixture db)
    {
        [Fact]
        public async Task ReturnsTheRemainingFiveWholeGroups()
        {
            await db.ResetAsync();
            await SeedGroupsAsync(db, 30);

            var page = await Repo(db).ListWithMediaAsync(
                RotKind.NearDuplicate, RotState.Open, limit: 25, offset: 25, ct: CancellationToken.None);

            Assert.Equal(5, page.Items.Select(r => r.Finding.GroupKey).Distinct().Count());
        }

        [Fact]
        public async Task SharesNoGroupKeyWithPageOne()
        {
            await db.ResetAsync();
            await SeedGroupsAsync(db, 30);
            var repo = Repo(db);

            var pageOne = await repo.ListWithMediaAsync(
                RotKind.NearDuplicate, RotState.Open, limit: 25, offset: 0, ct: CancellationToken.None);
            var pageTwo = await repo.ListWithMediaAsync(
                RotKind.NearDuplicate, RotState.Open, limit: 25, offset: 25, ct: CancellationToken.None);

            var pageOneKeys = pageOne.Items.Select(r => r.Finding.GroupKey).ToHashSet();
            var pageTwoKeys = pageTwo.Items.Select(r => r.Finding.GroupKey).ToHashSet();

            Assert.Empty(pageOneKeys.Intersect(pageTwoKeys));
        }
    }

    /// <summary>STORY-383 AC3 + STORY-382 AC6 — the total that rides beside the rows.</summary>
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTotalCountsGroups(DatabaseFixture db)
    {
        [Fact]
        public async Task TotalIsThirtyOnEveryPage()
        {
            await db.ResetAsync();
            await SeedGroupsAsync(db, 30);
            var repo = Repo(db);

            var pageOne = await repo.ListWithMediaAsync(
                RotKind.NearDuplicate, RotState.Open, limit: 25, offset: 0, ct: CancellationToken.None);
            var pageTwo = await repo.ListWithMediaAsync(
                RotKind.NearDuplicate, RotState.Open, limit: 25, offset: 25, ct: CancellationToken.None);

            Assert.Equal((30, 30), (pageOne.Total, pageTwo.Total));
        }

        [Fact]
        public async Task TotalCountsOnlyOpenGroups()
        {
            await db.ResetAsync();
            var groups = await SeedGroupsAsync(db, 30);
            var repo = Repo(db);
            var dismissedGroupKey = groups[0].GroupKey;
            var findingIds = await ReadFindingIdsForGroupAsync(db, dismissedGroupKey);
            foreach (var findingId in findingIds)
                await repo.DismissAsync(findingId, CancellationToken.None);

            var page = await repo.ListWithMediaAsync(
                RotKind.NearDuplicate, RotState.Open, limit: 25, offset: 0, ct: CancellationToken.None);

            Assert.Equal(29, page.Total);
        }
    }

    /// <summary>T385 review HIGH-1 (RULED, proven live) — <c>state</c> scopes which GROUPS qualify,
    /// never which MEMBER rows render: a group with at least one matching member renders EVERY member,
    /// regardless of that member's own state.</summary>
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMixedStateGroupRendersWhole(DatabaseFixture db)
    {
        [Fact]
        public async Task AllThreeMembersRenderWhenOneIsDismissed()
        {
            await db.ResetAsync();
            var repo = Repo(db);
            var memberIds = await SeedOneGroupAsync(db, "grp-mixed", memberCount: 3);
            await repo.DismissAsync(memberIds[0], CancellationToken.None);

            var page = await repo.ListWithMediaAsync(
                RotKind.NearDuplicate, RotState.Open, limit: 25, offset: 0, ct: CancellationToken.None);

            var renderedMemberCount = page.Items.Count(r => r.Finding.GroupKey == "grp-mixed");

            Assert.Equal(3, renderedMemberCount);
        }
    }

    /// <summary>Round-2 review HIGH-2 (RULED) — a RESOLVED member's own <c>group_key</c> survives the
    /// resolve untouched (the resolve half never clears it), but it must never render inside its old
    /// group: dismissed = the operator closed the finding while the media is still a duplicate →
    /// render; resolved = the system closed it because the media is no longer a duplicate → don't
    /// render.</summary>
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioResolvedMembersStayHidden(DatabaseFixture db)
    {
        [Fact]
        public async Task OnlyTheStillDuplicateMembersRender()
        {
            await db.ResetAsync();
            var repo = Repo(db);
            var openIdOne = await InsertNearDuplicateFindingAsync(
                db, await InsertMediaRowAsync(db, "/t385/resolved-open-a.flac"), "grp-resolved");
            var openIdTwo = await InsertNearDuplicateFindingAsync(
                db, await InsertMediaRowAsync(db, "/t385/resolved-open-b.flac"), "grp-resolved");
            await InsertResolvedNearDuplicateFindingAsync(
                db, await InsertMediaRowAsync(db, "/t385/resolved-stale-c.flac"), "grp-resolved");

            var page = await repo.ListWithMediaAsync(
                RotKind.NearDuplicate, RotState.Open, limit: 25, offset: 0, ct: CancellationToken.None);

            var renderedIds = page.Items
                .Where(r => r.Finding.GroupKey == "grp-resolved")
                .Select(r => r.Finding.Id)
                .OrderBy(id => id)
                .ToArray();

            Assert.Equal(new[] { openIdOne, openIdTwo }.OrderBy(id => id), renderedIds);
        }
    }

    /// <summary>Round-3 review MED-6 (RULED, fix verified live) — a FULLY-resolved group (every member
    /// resolved, <c>group_key</c> still intact) must never become a phantom page slot: with
    /// <paramref name="state"/> omitted (the endpoint's documented "any" default), group qualification
    /// now excludes resolved rows exactly like member rendering already does, so
    /// <see cref="RotFindingPage.Total"/> only ever counts groups that actually render at least one
    /// row.</summary>
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioFullyResolvedGroupsNeverPhantom(DatabaseFixture db)
    {
        [Fact]
        public async Task TotalMatchesRenderedGroupsWhenStateIsOmitted()
        {
            await db.ResetAsync();
            var repo = Repo(db);
            await InsertResolvedNearDuplicateFindingAsync(
                db, await InsertMediaRowAsync(db, "/t385/med6-resolved-a.flac"), "grp-med6-resolved");
            await InsertResolvedNearDuplicateFindingAsync(
                db, await InsertMediaRowAsync(db, "/t385/med6-resolved-b.flac"), "grp-med6-resolved");
            await SeedOneGroupAsync(db, "grp-med6-open", memberCount: 2);

            var page = await repo.ListWithMediaAsync(
                RotKind.NearDuplicate, state: null, limit: 25, offset: 0, ct: CancellationToken.None);

            var renderedGroupCount = page.Items.Select(r => r.Finding.GroupKey).Distinct().Count();

            Assert.Equal(renderedGroupCount, page.Total);
        }
    }

    /// <summary>T385 review HIGH-1's own flip side — a group where NO member matches the state filter
    /// never qualifies at all, so it neither consumes a page slot nor counts into
    /// <see cref="RotFindingPage.Total"/>.</summary>
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioFullyDismissedGroupDoesNotQualify(DatabaseFixture db)
    {
        [Fact]
        public async Task TotalCountsOnlyTheStillOpenGroup()
        {
            await db.ResetAsync();
            var repo = Repo(db);
            var dismissedGroupIds = await SeedOneGroupAsync(db, "grp-all-dismissed", memberCount: 3);
            foreach (var findingId in dismissedGroupIds)
                await repo.DismissAsync(findingId, CancellationToken.None);
            await SeedOneGroupAsync(db, "grp-open", memberCount: 2);

            var page = await repo.ListWithMediaAsync(
                RotKind.NearDuplicate, RotState.Open, limit: 25, offset: 0, ct: CancellationToken.None);

            Assert.Equal(1, page.Total);
        }
    }

    /// <summary>STORY-383 review MED-5 — the member sequence within one group matches
    /// <c>group_key asc, opened_at desc, id</c> exactly; deterministic <c>opened_at</c> values, ascending
    /// insertion order, so a correct <c>opened_at desc</c> read reverses the insertion order while an
    /// (incorrect) <c>id</c>-only read would not.</summary>
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMemberOrderWithinAGroup(DatabaseFixture db)
    {
        [Fact]
        public async Task MatchesOpenedAtDescThenId()
        {
            await db.ResetAsync();
            var repo = Repo(db);
            var baseTime = DateTimeOffset.UtcNow.AddHours(-1);

            var idOldest = await InsertNearDuplicateFindingWithOpenedAtAsync(
                db, await InsertMediaRowAsync(db, "/t385/order-a.flac"), "grp-order", baseTime);
            var idMiddle = await InsertNearDuplicateFindingWithOpenedAtAsync(
                db, await InsertMediaRowAsync(db, "/t385/order-b.flac"), "grp-order", baseTime.AddSeconds(10));
            var idNewest = await InsertNearDuplicateFindingWithOpenedAtAsync(
                db, await InsertMediaRowAsync(db, "/t385/order-c.flac"), "grp-order", baseTime.AddSeconds(20));

            var page = await repo.ListWithMediaAsync(
                RotKind.NearDuplicate, RotState.Open, limit: 25, offset: 0, ct: CancellationToken.None);

            var actualIds = page.Items.Select(r => r.Finding.Id).ToArray();

            Assert.Equal([idNewest, idMiddle, idOldest], actualIds);
        }

        /// <summary>Round-2 nit — the <c>f.id</c> tiebreak was unpinned: two members sharing the SAME
        /// <c>opened_at</c> must still land in a deterministic, id-ascending order.</summary>
        [Fact]
        public async Task TiesOnOpenedAtBreakByIdAscending()
        {
            await db.ResetAsync();
            var repo = Repo(db);
            var sameOpenedAt = DateTimeOffset.UtcNow.AddHours(-1);

            var idFirst = await InsertNearDuplicateFindingWithOpenedAtAsync(
                db, await InsertMediaRowAsync(db, "/t385/tie-a.flac"), "grp-tie", sameOpenedAt);
            var idSecond = await InsertNearDuplicateFindingWithOpenedAtAsync(
                db, await InsertMediaRowAsync(db, "/t385/tie-b.flac"), "grp-tie", sameOpenedAt);

            var page = await repo.ListWithMediaAsync(
                RotKind.NearDuplicate, RotState.Open, limit: 25, offset: 0, ct: CancellationToken.None);

            var actualIds = page.Items.Select(r => r.Finding.Id).ToArray();

            Assert.Equal([idFirst, idSecond], actualIds);
        }
    }

    /// <summary>STORY-383 AC5 + STORY-382 AC6 — a flat kind (dead_file) under the same read.</summary>
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioFlatKindsCountRows(DatabaseFixture db)
    {
        [Fact]
        public async Task LimitAndOffsetCountRowsExactlyAsBefore()
        {
            await db.ResetAsync();
            var findingIds = await SeedDeadFileRowsAsync(db, 60);

            var page = await Repo(db).ListWithMediaAsync(
                RotKind.DeadFile, RotState.Open, limit: 25, offset: 25, ct: CancellationToken.None);

            // Desc-ordered read: highest index (newest) first. offset 25/limit 25 of 60 rows lands on
            // original indices 34 down to 10 (25 rows), rows 26-50 in opened_at desc order.
            var expected = findingIds.Skip(10).Take(25).Reverse().ToArray();
            var actual = page.Items.Select(r => r.Finding.Id).ToArray();

            Assert.Equal(expected, actual);
        }

        [Fact]
        public async Task TotalIsTheExactMatchingRowCount()
        {
            await db.ResetAsync();
            await SeedDeadFileRowsAsync(db, 60);

            var page = await Repo(db).ListWithMediaAsync(
                RotKind.DeadFile, RotState.Open, limit: 25, offset: 25, ct: CancellationToken.None);

            Assert.Equal(60, page.Total);
        }
    }

    /// <summary>T385 review MED-3 — an <c>offset</c> past the last matching row/group returns an EMPTY
    /// page, but <see cref="RotFindingPage.Total"/> must still reflect the true seeded total: the
    /// rationale <see cref="Garden.RotFindingRepository.ListWithMediaAsync"/>'s own remarks give for a
    /// separate count read (over a <c>count(*) over()</c> window, which would carry no total at all on
    /// a zero-row result) pinned as a live fact, one branch each.</summary>
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioOffsetPastTheEndStillPinsTotal(DatabaseFixture db)
    {
        [Fact]
        public async Task NearDuplicateGroupsTotalSurvivesAnEmptyPage()
        {
            await db.ResetAsync();
            await SeedGroupsAsync(db, 30);

            var page = await Repo(db).ListWithMediaAsync(
                RotKind.NearDuplicate, RotState.Open, limit: 25, offset: 100, ct: CancellationToken.None);

            Assert.Equal(30, page.Total);
        }

        [Fact]
        public async Task FlatKindTotalSurvivesAnEmptyPage()
        {
            await db.ResetAsync();
            await SeedDeadFileRowsAsync(db, 10);

            var page = await Repo(db).ListWithMediaAsync(
                RotKind.DeadFile, RotState.Open, limit: 25, offset: 100, ct: CancellationToken.None);

            Assert.Equal(10, page.Total);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH / regression pins
    // ---------------------------------------------------------------------

    /// <summary>STORY-382 AC8's store half — the kind-LESS read is byte-compatible with T377.</summary>
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheKindlessReadIsUnchanged(DatabaseFixture db)
    {
        [Fact]
        public async Task PagesFlatAcrossKindsInTheT377Order()
        {
            // No kind filter → kind, group_key nulls last, opened_at desc, id — a near_duplicate
            // group MAY split at the page boundary here; that is the pinned old contract. 24
            // dead_file rows (kind sorts before near_duplicate in library.rot_kind's own declared
            // order) plus one 4-member near_duplicate group: limit 25 exhausts on the group's FIRST
            // member only.
            await db.ResetAsync();
            for (var i = 0; i < 24; i++)
            {
                var mediaId = await InsertMediaRowAsync(db, $"/t385/split-dead-{i:D2}.flac");
                await InsertDeadFileFindingAsync(db, mediaId, DateTimeOffset.UtcNow);
            }

            for (var i = 0; i < 4; i++)
            {
                var mediaId = await InsertMediaRowAsync(db, $"/t385/split-dup-{i}.flac");
                await InsertNearDuplicateFindingAsync(db, mediaId, "grp-split");
            }

            var page = await Repo(db).ListWithMediaAsync(
                null, RotState.Open, limit: 25, offset: 0, ct: CancellationToken.None);

            var nearDuplicateRowsOnPage = page.Items.Count(r => r.Finding.Kind == RotKind.NearDuplicate);

            Assert.Equal(1, nearDuplicateRowsOnPage);
        }
    }
}

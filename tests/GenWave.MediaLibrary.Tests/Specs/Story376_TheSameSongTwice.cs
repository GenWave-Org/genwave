// STORY-376 — The same song twice (SPEC F153.5 · PLAN T354, T374)
//
// BDD specification — xUnit, REAL Postgres via DatabaseFixture. AC1–AC2 (this file's own
// ScenarioTheFold/ScenarioTheVariantTail) are SQL-only facts over db/41-gardener-migration.sh's
// library.fold_key/title_key/title_variant — db/01-library.sh's own fresh-init mirror of those
// functions (see that script's own Gardener remarks) means DatabaseFixture's fresh-init database
// already carries them with no migration script to run first, the same "call the function directly"
// posture Story143_TrackEnergyGeneratedColumn.cs takes for its own STORED generated column. AC3–AC5,
// AC7 (the near_duplicate PASS's own findings/evidence behavior) were PENDING until T374 — WIRED now,
// driving RotFindingRepository.ReconcileNearDuplicatesAsync directly (no NearDuplicateGardenerPass in
// most facts; the AC7 scenario is the one exception, since it is about the PASS's own dependency
// shape, not the repository's SQL). The Scenarios below tagged "T354 review pin" still drive
// library.find_near_duplicates directly — the FUNCTION's own contract, independent of the pass/
// repository that consumes it. AC6 (Keep this one) is a Host/Jest concern — the bulk-eligibility click
// lives in the Admin UI test suite, not here; no fact for it in this file. The T374 ORCHESTRATOR
// ruling — per-partition anchoring stays, 200000/203000/203500 ms at 2000 ms opens no finding — is
// pinned as a regression fact by ScenarioTheKnownMissIsPinned below.

using System.Text.Json;
using Dapper;
using GenWave.MediaLibrary.Garden;
using GenWave.MediaLibrary.Options;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureTheSameSongTwice
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>One scalar query, one parameter — fold_key/title_key/title_variant each want the
    /// identical "run this SQL, get a nullable string back" shape (T354 review LOW-4: three
    /// near-identical helpers differing only in the SQL literal collapsed into this one).</summary>
    static async Task<string?> ScalarAsync(DatabaseFixture db, string sql, string arg)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<string?>(sql, new { arg });
    }

    /// <summary>Inserts a ready + measurable + eligible (default) row carrying the given
    /// artist/title/duration — the minimal shape find_near_duplicates' own playable predicate needs,
    /// written directly (not via MediaRepository.WriteEnrichmentAsync) since these facts drive the
    /// SQL function alone, not the enrichment pipeline. <paramref name="imagingKind"/> defaults to
    /// <see langword="null"/> (an ordinary music row) — every pre-T406 call site keeps that default;
    /// passing e.g. <c>"ad"</c> is PLAN T406's own MED-2(a) addition, exercising the SAME
    /// <c>imaging_kind is null</c> fence <see cref="MediaRepository.PlayablePredicate"/> carries in
    /// C#, now mirrored inside <c>library.find_near_duplicates</c> itself (db/44).</summary>
    static async Task<long> InsertReadyRowAsync(
        DatabaseFixture db, string path, string artist, string title, int durationMs, string? imagingKind = null)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            """
            insert into library.media (path, format, size_bytes, mtime, state, measurable, duration_ms, artist, title, imaging_kind)
            values (@path, 'flac', 1024, now(), 'ready', true, @durationMs, @artist, @title, @imagingKind)
            returning id
            """,
            new { path, durationMs, artist, title, imagingKind });
    }

    /// <summary>Flags a row never_play — the one predicate leg find_near_duplicates must honor
    /// identically to MediaRepository.PlayablePredicate (T354 review MED-1).</summary>
    static async Task SetNeverPlayAsync(DatabaseFixture db, long mediaId)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "insert into library.media_rating (media_id, never_play) values (@mediaId, true)",
            new { mediaId });
    }

    static async Task<IReadOnlyList<(long MediaId, string GroupKey, string? TitleVariant)>> FindNearDuplicatesAsync(DatabaseFixture db, int toleranceMs)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var rows = await conn.QueryAsync<(long MediaId, string GroupKey, string? TitleVariant)>(
            "select media_id, group_key, title_variant from library.find_near_duplicates(@toleranceMs)",
            new { toleranceMs });
        return rows.ToList();
    }

    // ---------------------------------------------------------------------
    // T374 helpers — the near_duplicate PASS's own repository (RotFindingRepository) and its
    // library.rot_finding rows (the Story375_RotFindingFlapGuard.cs "Repo(db) => new(db.DataSource)"
    // idiom, one seam over).
    // ---------------------------------------------------------------------

    static RotFindingRepository Repo(DatabaseFixture db) => new(db.DataSource);

    /// <summary>The Keep-this-one shape (STORY-376 AC6, T378's own future write): flips a row
    /// ineligible directly, bypassing the admin bulk-eligibility endpoint that AC6 itself owns —
    /// this file only needs the DOWNSTREAM effect on the group's own findings (AC6's own "resolve
    /// on the next pass" half), not the endpoint.</summary>
    static async Task SetEligibleFalseAsync(DatabaseFixture db, long mediaId)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "update library.media set eligible = false where id = @mediaId", new { mediaId });
    }

    /// <summary>Retags a row's title in place — <c>artist_key</c>/<c>title_key</c>/
    /// <c>title_variant</c> are STORED generated columns (db/41), so this alone moves the row into
    /// a different <c>find_near_duplicates</c> partition without touching any generated column
    /// directly (T374 review HIGH-1's own group_key-refresh regression fact).</summary>
    static async Task RetagTitleAsync(DatabaseFixture db, long mediaId, string title)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "update library.media set title = @title where id = @mediaId", new { mediaId, title });
    }

    /// <summary>One <c>near_duplicate</c> finding for <paramref name="mediaId"/>, or
    /// <see langword="null"/> when none is open/resolved/dismissed yet — the
    /// <c>ReadRotationAsync</c>/Story371 idiom (a nullable named ValueTuple, positional column
    /// order). <c>evidence::text</c> keeps the jsonb column opaque to Dapper, exactly like
    /// <see cref="RotFindingRepository.ListAsync"/>'s own read does in production.</summary>
    static async Task<(long Id, string State, string? GroupKey, string Evidence, DateTimeOffset OpenedAt)?> ReadFindingAsync(DatabaseFixture db, long mediaId)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<(long, string, string?, string, DateTimeOffset)?>(
            """
            select id, state::text, group_key, evidence::text, opened_at
            from library.rot_finding
            where media_id = @mediaId and kind = 'near_duplicate'
            """,
            new { mediaId });
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the fold, the variant tail, and the duplicate group
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheFold(DatabaseFixture db)
    {
        // Given titles "Héllo, World!" and "hello world", When fold_key runs.
        [Fact]
        public async Task BothYieldHelloWorld()
        {
            var folded = (
                await ScalarAsync(db, "select library.fold_key(@arg)", "Héllo, World!"),
                await ScalarAsync(db, "select library.fold_key(@arg)", "hello world"));

            Assert.Equal(("hello world", "hello world"), folded);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheVariantTail(DatabaseFixture db)
    {
        // Given "Song (feat. X)", "Song [Live]", "Song (2011 Remaster)", "Song", When
        // title_key and title_variant are computed.
        [Fact]
        public async Task AllFourShareTitleKeySong()
        {
            var keys = (
                await ScalarAsync(db, "select library.title_key(@arg)", "Song (feat. X)"),
                await ScalarAsync(db, "select library.title_key(@arg)", "Song [Live]"),
                await ScalarAsync(db, "select library.title_key(@arg)", "Song (2011 Remaster)"),
                await ScalarAsync(db, "select library.title_key(@arg)", "Song"));

            Assert.Equal(("song", "song", "song", "song"), keys);
        }

        [Fact]
        public async Task TheVariantsAreFeatXLiveRemasterAndNull()
        {
            var variants = (
                await ScalarAsync(db, "select library.title_variant(@arg)", "Song (feat. X)"),
                await ScalarAsync(db, "select library.title_variant(@arg)", "Song [Live]"),
                await ScalarAsync(db, "select library.title_variant(@arg)", "Song (2011 Remaster)"),
                await ScalarAsync(db, "select library.title_variant(@arg)", "Song"));

            Assert.Equal(("feat x", "live", "2011 remaster", (string?)null), variants);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioADuplicateGroup(DatabaseFixture db)
    {
        // Given two ready rows, same artist_key/title_key, same variant, durations 200,000 and
        // 201,500 ms, When the near_duplicate pass runs. Both facts below share this one arrangement
        // (the Story371_ThumbsAggregateIsBounded.cs "SeedAgedRowAndSweepAsync" idiom: written once,
        // re-run per fact since xUnit gives each [Fact] its own fresh class instance).
        async Task<((string A, string B) States, (string? A, string? B) GroupKeys)> ArrangeAsync()
        {
            await db.ResetAsync();
            var rowA = await InsertReadyRowAsync(db, "/gardener/t374-dup-a.flac", "Artist", "Song", 200_000);
            var rowB = await InsertReadyRowAsync(db, "/gardener/t374-dup-b.flac", "Artist", "Song", 201_500);

            await Repo(db).ReconcileNearDuplicatesAsync(2_000, CancellationToken.None);

            var findingA = await ReadFindingAsync(db, rowA);
            var findingB = await ReadFindingAsync(db, rowB);
            return ((findingA!.Value.State, findingB!.Value.State), (findingA.Value.GroupKey, findingB.Value.GroupKey));
        }

        [Fact]
        public async Task BothRowsHaveAnOpenFinding()
        {
            var (states, _) = await ArrangeAsync();
            Assert.Equal(("open", "open"), states);
        }

        [Fact]
        public async Task TheyShareOneGroupKey()
        {
            var (_, groupKeys) = await ArrangeAsync();
            Assert.Equal(groupKeys.A, groupKeys.B);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioEvidenceSiblingsForAPair(DatabaseFixture db)
    {
        // Given the same duplicate pair as ScenarioADuplicateGroup, When the pass runs, Then row
        // A's own evidence.siblings names exactly row B — the group's OTHER member, never itself
        // (T374's own evidence shape, STORY-376 AC3's own "for Keep-this-one" reuse).
        [Fact]
        public async Task SiblingsOnRowAIsExactlyRowB()
        {
            await db.ResetAsync();
            var rowA = await InsertReadyRowAsync(db, "/gardener/t374-sib-a.flac", "Artist", "Song", 200_000);
            var rowB = await InsertReadyRowAsync(db, "/gardener/t374-sib-b.flac", "Artist", "Song", 200_500);

            await Repo(db).ReconcileNearDuplicatesAsync(2_000, CancellationToken.None);

            var findingA = await ReadFindingAsync(db, rowA);
            using var evidence = JsonDocument.Parse(findingA!.Value.Evidence);
            var siblingIds = evidence.RootElement.GetProperty("siblings").EnumerateArray()
                .Select(e => e.GetProperty("media_id").GetInt64()).ToArray();

            Assert.Equal([rowB], siblingIds);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioVersionsAreNotFlagged(DatabaseFixture db)
    {
        // Given "Song" and "Song [Live]" by the same artist — no duplicate PAIR on either side, so
        // no group ever forms — When the pass runs.
        [Fact]
        public async Task NoFindingIsOpened()
        {
            await db.ResetAsync();
            var studio = await InsertReadyRowAsync(db, "/gardener/t374-versions-studio.flac", "Artist", "Song", 200_000);
            var live = await InsertReadyRowAsync(db, "/gardener/t374-versions-live.flac", "Artist", "Song [Live]", 200_000);

            await Repo(db).ReconcileNearDuplicatesAsync(2_000, CancellationToken.None);

            var (studioFinding, liveFinding) = (await ReadFindingAsync(db, studio), await ReadFindingAsync(db, live));
            Assert.Equal((false, false), (studioFinding.HasValue, liveFinding.HasValue));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAStudioPairsVersionsListTheLiveRow(DatabaseFixture db)
    {
        // Given a studio PAIR (a duplicate group of two) plus one live row of the same song, When
        // the pass runs, Then the pair's own evidence.versions names exactly the live row — never a
        // sibling, since the sibling is already covered by evidence.siblings (STORY-376 AC4).
        [Fact]
        public async Task TheStudioPairsEvidenceVersionsIsExactlyTheLiveRow()
        {
            await db.ResetAsync();
            var studioA = await InsertReadyRowAsync(db, "/gardener/t374-ac4-studio-a.flac", "Artist", "Song", 200_000);
            await InsertReadyRowAsync(db, "/gardener/t374-ac4-studio-b.flac", "Artist", "Song", 200_500);
            var live = await InsertReadyRowAsync(db, "/gardener/t374-ac4-live.flac", "Artist", "Song [Live]", 200_000);

            await Repo(db).ReconcileNearDuplicatesAsync(2_000, CancellationToken.None);

            var findingA = await ReadFindingAsync(db, studioA);
            using var evidence = JsonDocument.Parse(findingA!.Value.Evidence);
            var versionIds = evidence.RootElement.GetProperty("versions").EnumerateArray()
                .Select(e => e.GetProperty("media_id").GetInt64()).ToArray();

            Assert.Equal([live], versionIds);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioDurationTolerance(DatabaseFixture db)
    {
        // Given same keys, durations 200,000 and 203,000 ms, When the pass runs.
        [Fact]
        public async Task NoFindingIsOpened()
        {
            await db.ResetAsync();
            var rowA = await InsertReadyRowAsync(db, "/gardener/t374-ac5-a.flac", "Artist", "Song", 200_000);
            var rowB = await InsertReadyRowAsync(db, "/gardener/t374-ac5-b.flac", "Artist", "Song", 203_000);

            await Repo(db).ReconcileNearDuplicatesAsync(2_000, CancellationToken.None);

            var (findingA, findingB) = (await ReadFindingAsync(db, rowA), await ReadFindingAsync(db, rowB));
            Assert.Equal((false, false), (findingA.HasValue, findingB.HasValue));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheKnownMissIsPinned(DatabaseFixture db)
    {
        // Per-partition anchor, SPEC F153.5's own T374 rider (ORCHESTRATOR ruling, 2026-08-30):
        // 200000/203000/203500 ms at a 2000 ms tolerance opens NO finding even though the last two
        // rows are only 500 ms apart — the anchor is the PARTITION's shortest duration (200000)
        // only, never a second clustering level, so 203000 (Δ3000) and 203500 (Δ3500) both fail to
        // qualify against that one anchor. A known miss, pinned as a regression fact on purpose —
        // NOT a bug to fix here.
        [Fact]
        public async Task NoFindingIsOpened()
        {
            await db.ResetAsync();
            var rowA = await InsertReadyRowAsync(db, "/gardener/t374-knownmiss-a.flac", "Artist", "Song", 200_000);
            var rowB = await InsertReadyRowAsync(db, "/gardener/t374-knownmiss-b.flac", "Artist", "Song", 203_000);
            var rowC = await InsertReadyRowAsync(db, "/gardener/t374-knownmiss-c.flac", "Artist", "Song", 203_500);

            await Repo(db).ReconcileNearDuplicatesAsync(2_000, CancellationToken.None);

            var (findingA, findingB, findingC) = (
                await ReadFindingAsync(db, rowA), await ReadFindingAsync(db, rowB), await ReadFindingAsync(db, rowC));
            Assert.Equal((false, false, false), (findingA.HasValue, findingB.HasValue, findingC.HasValue));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioKeepThisOneResolvesTheWholeGroup(DatabaseFixture db)
    {
        // Given a duplicate group of two, already open, When row B is set ineligible (the
        // Keep-this-one shape T378 will drive through the admin bulk-eligibility endpoint) and a
        // second reconcile runs, Then BOTH findings resolve — a group of one is not a group, so row
        // A's own finding resolves too, not just row B's.
        [Fact]
        public async Task BothFindingsResolve()
        {
            await db.ResetAsync();
            var rowA = await InsertReadyRowAsync(db, "/gardener/t374-keep-a.flac", "Artist", "Song", 200_000);
            var rowB = await InsertReadyRowAsync(db, "/gardener/t374-keep-b.flac", "Artist", "Song", 200_500);
            var repo = Repo(db);
            await repo.ReconcileNearDuplicatesAsync(2_000, CancellationToken.None);

            await SetEligibleFalseAsync(db, rowB);
            await repo.ReconcileNearDuplicatesAsync(2_000, CancellationToken.None);

            var findingA = await ReadFindingAsync(db, rowA);
            var findingB = await ReadFindingAsync(db, rowB);
            Assert.Equal(("resolved", "resolved"), (findingA!.Value.State, findingB!.Value.State));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioADismissedFindingStaysDismissed(DatabaseFixture db)
    {
        // Given an open duplicate-group finding dismissed at the store level, When the SAME group
        // still qualifies and a second reconcile runs, Then the finding stays dismissed
        // (dismissed-forever, SPEC F153.2).
        [Fact]
        public async Task TheFindingStaysDismissed()
        {
            await db.ResetAsync();
            var rowA = await InsertReadyRowAsync(db, "/gardener/t374-dismiss-a.flac", "Artist", "Song", 200_000);
            await InsertReadyRowAsync(db, "/gardener/t374-dismiss-b.flac", "Artist", "Song", 200_500);
            var repo = Repo(db);
            await repo.ReconcileNearDuplicatesAsync(2_000, CancellationToken.None);
            var findingId = (await ReadFindingAsync(db, rowA))!.Value.Id;
            await repo.DismissAsync(findingId, CancellationToken.None);

            await repo.ReconcileNearDuplicatesAsync(2_000, CancellationToken.None);

            Assert.Equal("dismissed", (await ReadFindingAsync(db, rowA))!.Value.State);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAnOpenFindingsOpenedAtIsStable(DatabaseFixture db)
    {
        // Given an open duplicate-group finding, When the SAME group reconciles again, Then
        // opened_at is unchanged — only a genuine resolved -> open transition stamps a fresh one
        // (F153.2's "as built" amendment, shared with the dead_file pass).
        [Fact]
        public async Task OpenedAtIsUnchanged()
        {
            await db.ResetAsync();
            var rowA = await InsertReadyRowAsync(db, "/gardener/t374-stable-a.flac", "Artist", "Song", 200_000);
            await InsertReadyRowAsync(db, "/gardener/t374-stable-b.flac", "Artist", "Song", 200_500);
            var repo = Repo(db);
            await repo.ReconcileNearDuplicatesAsync(2_000, CancellationToken.None);
            var openedAtFirst = (await ReadFindingAsync(db, rowA))!.Value.OpenedAt;

            await repo.ReconcileNearDuplicatesAsync(2_000, CancellationToken.None);

            Assert.Equal(openedAtFirst, (await ReadFindingAsync(db, rowA))!.Value.OpenedAt);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioGroupKeyRefreshesOnRetag(DatabaseFixture db)
    {
        // Given a duplicate pair (row 1 + row 2, both "Song", 200,000/200,500 ms) plus a lone
        // "Song [Live]" row (row 3, 200,000 ms), reconciled once, When row 1 is retagged to
        // "Song [Live]" — moving it into row 3's group while it never drops out of
        // find_near_duplicates entirely — and a second reconcile runs (T374 review HIGH-1: the
        // conflict path's group_key column must refresh, not just evidence). Both facts share this
        // one arrangement.
        async Task<(long Row1, long Row2, long Row3)> ArrangeAsync()
        {
            await db.ResetAsync();
            var row1 = await InsertReadyRowAsync(db, "/gardener/t374-retag-1.flac", "Artist", "Song", 200_000);
            var row2 = await InsertReadyRowAsync(db, "/gardener/t374-retag-2.flac", "Artist", "Song", 200_500);
            var row3 = await InsertReadyRowAsync(db, "/gardener/t374-retag-3.flac", "Artist", "Song [Live]", 200_000);
            var repo = Repo(db);
            await repo.ReconcileNearDuplicatesAsync(2_000, CancellationToken.None);

            await RetagTitleAsync(db, row1, "Song [Live]");
            await repo.ReconcileNearDuplicatesAsync(2_000, CancellationToken.None);

            return (row1, row2, row3);
        }

        [Fact]
        public async Task Row1sGroupKeyColumnEqualsRow3s()
        {
            var (row1, _, row3) = await ArrangeAsync();
            var (finding1, finding3) = (await ReadFindingAsync(db, row1), await ReadFindingAsync(db, row3));
            Assert.Equal(finding3!.Value.GroupKey, finding1!.Value.GroupKey);
        }

        [Fact]
        public async Task Row2Resolves()
        {
            var (_, row2, _) = await ArrangeAsync();
            Assert.Equal("resolved", (await ReadFindingAsync(db, row2))!.Value.State);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioVersionsAreOrderedNearestToTheAnchorFirst(DatabaseFixture db)
    {
        // Given a duplicate group of two ("Song"/"Song", 200,000/200,500 ms — row A is the group's
        // own anchor row at 200,000 ms) plus two "Song [Live]" version rows at 100,000 and 201,000
        // ms, When the pass runs, Then row A's own evidence.versions lists the 201,000 row FIRST —
        // nearest to the ANCHOR's own duration, not the numerically smallest duration (T374 review
        // MED-2: absolute order would have surfaced the 100,000 row first).
        [Fact]
        public async Task VersionsZeroIsTheNearestRowToTheAnchor()
        {
            await db.ResetAsync();
            var rowA = await InsertReadyRowAsync(db, "/gardener/t374-nearest-a.flac", "Artist", "Song", 200_000);
            await InsertReadyRowAsync(db, "/gardener/t374-nearest-b.flac", "Artist", "Song", 200_500);
            await InsertReadyRowAsync(db, "/gardener/t374-nearest-far.flac", "Artist", "Song [Live]", 100_000);
            var rowNear = await InsertReadyRowAsync(db, "/gardener/t374-nearest-near.flac", "Artist", "Song [Live]", 201_000);

            await Repo(db).ReconcileNearDuplicatesAsync(2_000, CancellationToken.None);

            var findingA = await ReadFindingAsync(db, rowA);
            using var evidence = JsonDocument.Parse(findingA!.Value.Evidence);
            var firstVersionId = evidence.RootElement.GetProperty("versions")[0].GetProperty("media_id").GetInt64();

            Assert.Equal(rowNear, firstVersionId);
        }
    }

    // ---------------------------------------------------------------------
    // T354 REVIEW PINS — find_near_duplicates' own contract, ahead of the T374 pass that consumes it
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioNoUsableKeyIsNeverGrouped(DatabaseFixture db)
    {
        // T354 review pin (HIGH): fold_key('Мир') and fold_key('日本語') both fold to NOTHING at
        // all — that must mean title_key stays NULL, not the same bogus '' key, or every such row
        // would land in one false duplicate group (STORY-376 AC1's own "the fold", sad-path). Same
        // artist on both rows on purpose, so the exclusion is provably about title_key, not artist.
        [Fact]
        public async Task ACyrillicTitleAndACjkTitleAreNotGroupedTogether()
        {
            await db.ResetAsync();
            var cyrillicId = await InsertReadyRowAsync(db, "/gardener/t354-cyrillic.flac", "Artist", "Мир", 200_000);
            var cjkId = await InsertReadyRowAsync(db, "/gardener/t354-cjk.flac", "Artist", "日本語", 200_500);

            var groups = await FindNearDuplicatesAsync(db, 2_000);

            Assert.DoesNotContain(groups, g => g.MediaId == cyrillicId || g.MediaId == cjkId);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioNeverPlayIsExcludedFromGrouping(DatabaseFixture db)
    {
        // T354 review pin (MED-1): find_near_duplicates must honor the SAME playable predicate as
        // MediaRepository.PlayablePredicate (src/GenWave.MediaLibrary/Catalog/MediaRepository.cs) —
        // a never_play row is never playable, so it can never seed or join a duplicate group; its
        // would-be partner, now alone, is not returned either.
        [Fact]
        public async Task ANeverPlayRowIsNotGrouped()
        {
            await db.ResetAsync();
            var openId = await InsertReadyRowAsync(db, "/gardener/t354-never-play-open.flac", "Artist", "Title", 200_000);
            var flaggedId = await InsertReadyRowAsync(db, "/gardener/t354-never-play-flagged.flac", "Artist", "Title", 200_500);
            await SetNeverPlayAsync(db, flaggedId);

            var groups = await FindNearDuplicatesAsync(db, 2_000);

            Assert.DoesNotContain(groups, g => g.MediaId == openId || g.MediaId == flaggedId);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioImagingIsExcludedFromGrouping(DatabaseFixture db)
    {
        // PLAN T406 review MED-2(a): find_near_duplicates carried a STALE, pre-T395 copy of
        // MediaRepository.PlayablePredicate — missing the F158.4 "and imaging_kind is null" rotation
        // fence T395 added to the real predicate — until db/44's `create or replace` closed the drift
        // (this file's own db/41 header remarks). The SAME shape as ScenarioNeverPlayIsExcludedFromGrouping
        // immediately above, one predicate leg over: an authored imaging row (a liner, a station id, an
        // ad spot) is never playable, so a pair of them — near-identical duration, same artist/title —
        // must never seed or join a duplicate group. Mutant-verified: deleting "and m.imaging_kind is
        // null" from library.find_near_duplicates reds this fact (both rows come back grouped).
        [Fact]
        public async Task AnAdImagingPairIsNotGrouped()
        {
            await db.ResetAsync();
            var adA = await InsertReadyRowAsync(db, "/gardener/t406-imaging-ad-a.flac", "Artist", "Title", 15_000, imagingKind: "ad");
            var adB = await InsertReadyRowAsync(db, "/gardener/t406-imaging-ad-b.flac", "Artist", "Title", 15_200, imagingKind: "ad");

            var groups = await FindNearDuplicatesAsync(db, 2_000);

            Assert.DoesNotContain(groups, g => g.MediaId == adA || g.MediaId == adB);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioVariantsFormSeparateGroups(DatabaseFixture db)
    {
        // T354 review pin (LOW-1, "F153.5 amended at T354 review", RULED): group_key must fold in
        // title_variant, or a studio pair and a live pair of the exact same song collide onto ONE
        // text group_key despite find_near_duplicates already keeping the two GROUPS structurally
        // apart (the variant partition never let a studio row join a live one to begin with).
        [Fact]
        public async Task TheStudioPairSharesOneGroupKey()
        {
            await db.ResetAsync();
            var studioA = await InsertReadyRowAsync(db, "/gardener/t354-variant-studio-a.flac", "Artist", "Song", 200_000);
            var studioB = await InsertReadyRowAsync(db, "/gardener/t354-variant-studio-b.flac", "Artist", "Song", 200_500);
            await InsertReadyRowAsync(db, "/gardener/t354-variant-live-a.flac", "Artist", "Song [Live]", 200_000);
            await InsertReadyRowAsync(db, "/gardener/t354-variant-live-b.flac", "Artist", "Song [Live]", 200_500);

            var groups = await FindNearDuplicatesAsync(db, 2_000);

            var studioGroupKeys = new[] { studioA, studioB }
                .Select(id => groups.Single(g => g.MediaId == id).GroupKey)
                .Distinct();
            Assert.Single(studioGroupKeys);
        }

        [Fact]
        public async Task TheLivePairsGroupKeyDiffersFromTheStudioPairs()
        {
            await db.ResetAsync();
            var studioA = await InsertReadyRowAsync(db, "/gardener/t354-variant-studio-a.flac", "Artist", "Song", 200_000);
            await InsertReadyRowAsync(db, "/gardener/t354-variant-studio-b.flac", "Artist", "Song", 200_500);
            var liveA = await InsertReadyRowAsync(db, "/gardener/t354-variant-live-a.flac", "Artist", "Song [Live]", 200_000);
            await InsertReadyRowAsync(db, "/gardener/t354-variant-live-b.flac", "Artist", "Song [Live]", 200_500);

            var groups = await FindNearDuplicatesAsync(db, 2_000);

            var studioKey = groups.Single(g => g.MediaId == studioA).GroupKey;
            var liveKey = groups.Single(g => g.MediaId == liveA).GroupKey;
            Assert.NotEqual(studioKey, liveKey);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioToleranceAnchorsToTheShortestDuration(DatabaseFixture db)
    {
        // T354 review pin (LOW-2, RULED): tolerance never chains transitively — every candidate is
        // measured against the GROUP's shortest duration, not its nearest neighbor (STORY-376 AC5's
        // own 3s distance: 200,000 -> 203,000 is 3s apart and must stay ungrouped even though
        // 200,000 -> 201,500 -> 203,000 would chain under a nearest-neighbor rule).
        [Fact]
        public async Task TheFirstTwoAreGroupedAndTheThirdIsNot()
        {
            await db.ResetAsync();
            var shortest = await InsertReadyRowAsync(db, "/gardener/t354-tolerance-200000.flac", "Artist", "Song", 200_000);
            var middle = await InsertReadyRowAsync(db, "/gardener/t354-tolerance-201500.flac", "Artist", "Song", 201_500);
            await InsertReadyRowAsync(db, "/gardener/t354-tolerance-203000.flac", "Artist", "Song", 203_000);

            var groups = await FindNearDuplicatesAsync(db, 2_000);

            Assert.Equal(
                new[] { shortest, middle }.OrderBy(id => id),
                groups.Select(g => g.MediaId).OrderBy(id => id));
        }
    }

    // ---------------------------------------------------------------------
    // MIGRATION CONVERGENCE (T354 review MED-2) — the Story357_AnnouncementStore house shape: a
    // fresh-init snapshot assert (db/01's own mirror of db/41) plus a drop-and-rerun-twice assert
    // (db/41's own "safe to run multiple times" promise). db/41 DOES ship a db/01/db/06 mirror
    // (unlike db/36/Story317, which has none) — the Story357 shape, not the Story317 one, applies.
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMigrationConvergence(DatabaseFixture db)
    {
        [Fact]
        public void TheFreshInitSnapshotIncludesTheFourGardenerTables()
        {
            // DatabaseFixture.InitialSchema is captured once, immediately after Postgres finishes
            // running ONLY db/01 + db/06 and before any spec class — this one included — ever runs
            // db/41. A dropped db/01 mirror of any one of these four tables turns this fact red.
            var present = (
                db.InitialSchema.ContainsKey(("library", "media_rotation", "media_id")),
                db.InitialSchema.ContainsKey(("library", "media_thumb", "id")),
                db.InitialSchema.ContainsKey(("library", "rot_finding", "id")),
                db.InitialSchema.ContainsKey(("library", "file_action", "id")));

            Assert.Equal((true, true, true, true), present);
        }

        [Fact]
        public void TheFreshInitSnapshotIncludesTheThreeGeneratedColumns()
        {
            var present = (
                db.InitialSchema.ContainsKey(("library", "media", "artist_key")),
                db.InitialSchema.ContainsKey(("library", "media", "title_key")),
                db.InitialSchema.ContainsKey(("library", "media", "title_variant")));

            Assert.Equal((true, true, true), present);
        }

        [Fact]
        public async Task TheFreshInitSnapshotIncludesThePartialDuplicateKeysIndex()
        {
            // InitialSchema only snapshots information_schema.columns (its own doc comment) — an
            // index has no column identity to key on, so this checks pg_indexes directly instead.
            // Safe at any point in this collection's run: DatabaseCollection runs its test classes
            // SEQUENTIALLY (xUnit's own same-collection default), and the only fact in this file that
            // ever drops a Gardener object (TheMigrationScriptConvergesAfterADropAndTwoReruns below)
            // always leaves it recreated before it returns.
            await using var conn = await db.DataSource.OpenConnectionAsync();
            var exists = await conn.ExecuteScalarAsync<bool>(
                """
                select exists(
                    select 1 from pg_indexes
                    where schemaname = 'library' and tablename = 'media' and indexname = 'media_dup_keys')
                """);

            Assert.True(exists, "library.media is missing its media_dup_keys partial index.");
        }

        [Fact]
        public async Task TheFreshInitSnapshotIncludesTheThumbListenerIndex()
        {
            // T366 review MED-1: the F150.5 per-listener daily cap read filters
            // (listener_key, created_at) — extends this convergence pin's own index list (this
            // scenario's class remarks) the same way TheFreshInitSnapshotIncludesThePartialDuplicateKeysIndex
            // above already covers media_dup_keys.
            await using var conn = await db.DataSource.OpenConnectionAsync();
            var exists = await conn.ExecuteScalarAsync<bool>(
                """
                select exists(
                    select 1 from pg_indexes
                    where schemaname = 'library' and tablename = 'media_thumb'
                      and indexname = 'media_thumb_listener_created_idx')
                """);

            Assert.True(exists, "library.media_thumb is missing its media_thumb_listener_created_idx index.");
        }

        [Fact]
        public async Task TheFreshInitSnapshotIncludesTheFiveGardenerEnumTypes()
        {
            await using var conn = await db.DataSource.OpenConnectionAsync();
            var count = await conn.ExecuteScalarAsync<int>(
                """
                select count(*)::int from pg_type
                where typnamespace = 'library'::regnamespace
                  and typname in ('thumb_direction', 'thumb_source', 'rot_kind', 'rot_state', 'file_verb')
                """);

            Assert.Equal(5, count);
        }

        [Fact]
        public async Task TheFreshInitSnapshotIncludesTheFiveGardenerFunctions()
        {
            // T365 added library.recompute_nudge (SPEC F150.9) to db/41 + this db/01 mirror,
            // widening this pin from four functions to five.
            await using var conn = await db.DataSource.OpenConnectionAsync();
            var count = await conn.ExecuteScalarAsync<int>(
                """
                select count(*)::int from pg_proc
                where pronamespace = 'library'::regnamespace
                  and proname in ('fold_key', 'title_variant', 'title_key', 'find_near_duplicates', 'recompute_nudge')
                """);

            Assert.Equal(5, count);
        }

        [Fact]
        public async Task TheMigrationScriptConvergesAfterADropAndTwoReruns()
        {
            // Simulate a pre-T354 database: drop the four Gardener tables db/41 (re-)creates plus
            // the three generated columns library.media grew (a bare DROP TABLE cannot reach those —
            // they live on library.media itself, not a table of their own; dropping them also drops
            // media_dup_keys, since a partial index over a dropped column cannot survive). Then run
            // the migration TWICE — its own "safe to run multiple times" promise (db/41's own
            // header), proven the Story357/Story304 way: drop, rerun, prove it is back.
            await using (var conn = await db.DataSource.OpenConnectionAsync())
            {
                await conn.ExecuteAsync(
                    "drop table if exists library.media_rotation, library.media_thumb, library.rot_finding, library.file_action");
                await conn.ExecuteAsync(
                    "alter table library.media " +
                    "drop column if exists artist_key, drop column if exists title_key, drop column if exists title_variant");
            }

            var scriptPath = Path.Combine(db.RepoRoot, "db", "41-gardener-migration.sh");
            db.RunFileInContainer(scriptPath);
            db.RunFileInContainer(scriptPath);

            await using var verifyConn = await db.DataSource.OpenConnectionAsync();
            var restored = await verifyConn.ExecuteScalarAsync<bool>(
                """
                select exists(
                    select 1 from information_schema.columns
                    where table_schema = 'library' and table_name = 'media' and column_name = 'artist_key')
                """);

            Assert.True(restored, "library.media.artist_key was not restored after dropping and re-running db/41 twice.");

            // PLAN T406 review MED-2(b) — the convergence landmine: db/41's own find_near_duplicates
            // body is the STALE, pre-fence copy (db/44's own header remarks) — its `create or replace`
            // above just reverted this SHARED collection fixture's function back to un-fenced for
            // every fact that runs after this one, exactly what a bare re-run of db/41 alone (this
            // fact's own scenario) does on a real box too. migrate.sh's own glob always runs db/44
            // AFTER db/41 in filename order on any real deploy or upgrade — reproduced here rather
            // than merely asserted, so the fixture this fact hands back to the collection is never
            // left un-fenced (STORY-387's ScenarioImagingIsExcludedFromGrouping fact above already
            // covers the fence's own behavior; this is the OTHER half — a shared-fixture regression a
            // per-fact behavioral pin alone can never catch, since it depends on RUN ORDER across
            // facts, not any one fact's own inputs).
            db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "44-near-duplicates-imaging-fence-migration.sh"));

            var fenced = await verifyConn.ExecuteScalarAsync<string>(
                "select pg_get_functiondef('library.find_near_duplicates(int)'::regprocedure)");

            Assert.Contains("and m.imaging_kind is null", fenced, StringComparison.Ordinal);
        }

        [Fact]
        public void TheDb01MirrorAndDb44AreByteIdenticalBeyondTheCreateClause()
        {
            // PLAN T406 review Fold 2 — the same hand-sync class of drift that let db/41's own
            // find_near_duplicates fall a full cycle behind the real PlayablePredicate (the T395/
            // T406 saga this file's own remarks above tell): the fence term alone is covered from
            // both sides by ScenarioImagingIsExcludedFromGrouping (behavior, against whichever
            // script actually ran) and TheMigrationScriptConvergesAfterADropAndTwoReruns (text,
            // against db/44 specifically) — neither would notice a DIFFERENT divergence, e.g. the
            // tolerance window, the group_key expression, or the anchor/qualifying/grouped CTE
            // shape drifting between db/01's fresh-init mirror and db/44's upgrade-path copy while
            // both still happened to keep the fence term. This fact pins the two texts equal
            // wholesale — everything from the shared `returns table (...)` signature through the
            // closing `$$;`, deliberately EXCLUDING only the one line that must legitimately differ
            // (`create function` in db/01 vs `create or replace function` in db/44, db/44's own
            // header explains why `create or replace` is required there).
            var db01Text = File.ReadAllText(Path.Combine(db.RepoRoot, "db", "01-library.sh"));
            var db44Text = File.ReadAllText(Path.Combine(db.RepoRoot, "db", "44-near-duplicates-imaging-fence-migration.sh"));

            var db01Definition = ExtractFindNearDuplicatesDefinition(db01Text);
            var db44Definition = ExtractFindNearDuplicatesDefinition(db44Text);

            Assert.Equal(db01Definition, db44Definition);
        }

        /// <summary>Extracts <c>library.find_near_duplicates</c>' full definition from the shared
        /// <c>returns table (...)</c> signature through the closing <c>$$;</c> — everything EXCEPT
        /// the leading <c>create function</c>/<c>create or replace function</c> line, the one
        /// legitimate difference between db/01's fresh-init mirror and db/44's upgrade-path copy.
        /// </summary>
        static string ExtractFindNearDuplicatesDefinition(string scriptText)
        {
            const string anchor = "returns table (media_id bigint, group_key text, title_variant text)";
            var start = scriptText.IndexOf(anchor, StringComparison.Ordinal);
            if (start < 0)
                throw new InvalidOperationException("find_near_duplicates' return-shape anchor was not found.");

            var end = scriptText.IndexOf("$$;", start, StringComparison.Ordinal);
            if (end < 0)
                throw new InvalidOperationException("find_near_duplicates' closing '$$;' was not found.");

            return scriptText[start..(end + 3)];
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — no filesystem, no excuse
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioThePassReadsNoFiles(DatabaseFixture db)
    {
        // Given a library on an unreachable mount — NearDuplicateGardenerPass never asks for
        // Library:MediaRoot at all (unlike DeadFileGardenerPass), so there is nothing to point at a
        // missing directory here; the constructor's own dependency shape is what makes the mount
        // irrelevant, pinned structurally by the second fact below — When the near_duplicate PASS
        // itself runs (NearDuplicateGardenerPass, not the repository directly), Then it completes
        // and opens findings for both AC3-shaped rows from catalog data alone. ONE assertion over a
        // homogeneous set (T374 review MED-1): the COUNT of open near_duplicate findings across the
        // two rows, not a tuple pairing two independently-named claims.
        [Fact]
        public async Task ThePassOpensFindingsFromCatalogDataAlone()
        {
            await db.ResetAsync();
            var rowA = await InsertReadyRowAsync(db, "/gardener/t374-ac7-a.flac", "Artist", "Song", 200_000);
            var rowB = await InsertReadyRowAsync(db, "/gardener/t374-ac7-b.flac", "Artist", "Song", 201_500);
            var pass = new NearDuplicateGardenerPass(
                Repo(db), new FakeOptionsMonitor<GardenerOptions>(new GardenerOptions { DuplicateToleranceMs = 2_000 }));

            await pass.RunAsync(CancellationToken.None);

            await using var conn = await db.DataSource.OpenConnectionAsync();
            var openFindingCount = await conn.ExecuteScalarAsync<int>(
                """
                select count(*)::int from library.rot_finding
                where kind = 'near_duplicate' and state = 'open' and media_id = any(@mediaIds)
                """,
                new { mediaIds = new[] { rowA, rowB } });

            Assert.Equal(2, openFindingCount);
        }

        // Given the pass's own type, When its constructor's dependencies are inspected, Then none
        // of them come from System.IO or Microsoft.Extensions.FileProviders — "reads no files"
        // pinned structurally, not just by this scenario's own happy-path behavior above.
        [Fact]
        public void TheConstructorAcceptsNoFilesystemDependency()
        {
            var parameterNamespaces = typeof(NearDuplicateGardenerPass)
                .GetConstructors()
                .Single()
                .GetParameters()
                .Select(p => p.ParameterType.Namespace ?? string.Empty)
                .ToArray();

            Assert.DoesNotContain(parameterNamespaces, ns =>
                ns.StartsWith("System.IO", StringComparison.Ordinal) ||
                ns.StartsWith("Microsoft.Extensions.FileProviders", StringComparison.Ordinal));
        }
    }
}

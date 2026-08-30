// STORY-376 — The same song twice (SPEC F153.5 · PLAN T354, T374)
//
// BDD specification — xUnit, REAL Postgres via DatabaseFixture. AC1–AC2 (this file's own
// ScenarioTheFold/ScenarioTheVariantTail) are SQL-only facts over db/41-gardener-migration.sh's
// library.fold_key/title_key/title_variant — db/01-library.sh's own fresh-init mirror of those
// functions (see that script's own Gardener remarks) means DatabaseFixture's fresh-init database
// already carries them with no migration script to run first, the same "call the function directly"
// posture Story143_TrackEnergyGeneratedColumn.cs takes for its own STORED generated column. AC3–AC5,
// AC7 (the near_duplicate PASS's own findings/evidence behavior) remain PENDING until T374 — the
// Scenarios below tagged "T354 review pin" instead drive library.find_near_duplicates directly (the
// same SQL-only posture as AC1/AC2), proving the FUNCTION's own contract ahead of the pass that will
// consume it. AC6 (Keep this one) is a Host/Jest concern — the bulk-eligibility click lives in the
// Admin UI test suite, not here; no fact for it in this file.

using Dapper;

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
    /// SQL function alone, not the enrichment pipeline.</summary>
    static async Task<long> InsertReadyRowAsync(DatabaseFixture db, string path, string artist, string title, int durationMs)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            """
            insert into library.media (path, format, size_bytes, mtime, state, measurable, duration_ms, artist, title)
            values (@path, 'flac', 1024, now(), 'ready', true, @durationMs, @artist, @title)
            returning id
            """,
            new { path, durationMs, artist, title });
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

    public sealed class ScenarioADuplicateGroup
    {
        // Given two ready rows, same artist_key/title_key, same variant, durations 200,000 and
        // 201,500 ms, When the near_duplicate pass runs.
        [Fact(Skip = "pending T374 (STORY-376 AC3)")]
        public void BothRowsHaveAnOpenFindingSharingOneGroupKey() => Assert.Fail("pending T374");
    }

    public sealed class ScenarioVersionsAreNotFlagged
    {
        // Given "Song" and "Song [Live]" by the same artist, When the pass runs.
        [Fact(Skip = "pending T374 (STORY-376 AC4)")]
        public void NoFindingIsOpened() => Assert.Fail("pending T374");

        [Fact(Skip = "pending T374 (STORY-376 AC4)")]
        public void TheLiveRowIsListedInTheOthersEvidenceVersions() => Assert.Fail("pending T374");
    }

    public sealed class ScenarioDurationTolerance
    {
        // Given same keys, durations 200,000 and 203,000 ms, When the pass runs.
        [Fact(Skip = "pending T374 (STORY-376 AC5)")]
        public void NoFindingIsOpened() => Assert.Fail("pending T374");
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
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — no filesystem, no excuse
    // ---------------------------------------------------------------------

    public sealed class ScenarioThePassReadsNoFiles
    {
        // Given a library on an unreachable mount, When the pass runs.
        [Fact(Skip = "pending T374 (STORY-376 AC7)")]
        public void ThePassCompletesSqlOnly() => Assert.Fail("pending T374");

        [Fact(Skip = "pending T374 (STORY-376 AC7)")]
        public void FindingsOpenFromCatalogDataAlone() => Assert.Fail("pending T374");
    }
}

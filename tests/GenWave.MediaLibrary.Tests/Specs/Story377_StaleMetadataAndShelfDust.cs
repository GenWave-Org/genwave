// STORY-377 — Stale metadata and shelf-dust (SPEC F153.6–F153.7 · PLAN T375)
//
// BDD specification — xUnit, REAL Postgres via DatabaseFixture, the Story376/RotFindingRepository
// shape one seam over: every fact seeds library.media (and, for shelf_dust, library.media_rotation/
// library.rot_finding) on BASE columns only, drives RotFindingRepository.ReconcileStaleMetadataAsync/
// ReconcileShelfDustAsync directly (title_key/title_variant are generated columns this pass never
// reads), and reads library.rot_finding back. The ONE exception is the ShelfDustDays knob boundary
// (ScenarioTheKnobGovernsTheBoundary below), which drives the real ShelfDustGardenerPass over a
// FakeOptionsMonitor<GardenerOptions> — the pass→repository arc, proven at least once, the same
// posture Story376's own ScenarioThePassReadsNoFiles takes for NearDuplicateGardenerPass.

using System.Text.Json;
using Dapper;
using GenWave.MediaLibrary.Garden;
using GenWave.MediaLibrary.Options;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureStaleMetadataAndShelfDust
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static RotFindingRepository Repo(DatabaseFixture db) => new(db.DataSource);

    /// <summary>A <c>stale_metadata</c> candidate row — every column the pass reads, defaulted to
    /// a fully "clean" row (non-blank artist/title, no enrichment misses recorded, measurable true)
    /// so each Scenario only needs to override the ONE field it is testing. <c>moods</c> itself is
    /// never bound here: every fact in this file leaves it at its column default (NULL), since the
    /// pass's own moods condition is `moods is null and mood_tag_missed_at is not null` — the
    /// SECOND half alone is what any fact needs to drive.</summary>
    static async Task<long> InsertStaleMetadataCandidateAsync(
        DatabaseFixture db,
        string path,
        string? artist = "Artist",
        string? title = "Title",
        int? year = 2000,
        bool yearLookupMissed = false,
        bool moodTagMissed = false,
        bool? measurable = true,
        bool tagsEdited = false)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            """
            insert into library.media (
                path, format, size_bytes, mtime, state, eligible, measurable,
                artist, title, year, year_lookup_missed_at, mood_tag_missed_at, tags_edited_at
            )
            values (
                @path, 'flac', 1024, now(), 'ready', true, @measurable,
                @artist, @title, @year,
                case when @yearLookupMissed then now() else null end,
                case when @moodTagMissed then now() else null end,
                case when @tagsEdited then now() else null end
            )
            returning id
            """,
            new { path, measurable, artist, title, year, yearLookupMissed, moodTagMissed, tagsEdited });
    }

    static async Task SetNeverPlayAsync(DatabaseFixture db, long mediaId)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "insert into library.media_rating (media_id, never_play) values (@mediaId, true)",
            new { mediaId });
    }

    static async Task SetArtistAsync(DatabaseFixture db, long mediaId, string artist)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync("update library.media set artist = @artist where id = @mediaId", new { mediaId, artist });
    }

    static async Task SetMoodTagMissedAsync(DatabaseFixture db, long mediaId)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "update library.media set mood_tag_missed_at = now() where id = @mediaId", new { mediaId });
    }

    /// <summary>One finding for <paramref name="mediaId"/>/<paramref name="kind"/>, or
    /// <see langword="null"/> when none exists yet — the Story376 <c>ReadFindingAsync</c> idiom,
    /// widened to take the kind (this file drives two). <c>evidence::text</c> keeps the jsonb
    /// column opaque to Dapper, exactly like production's own <c>ListAsync</c>.</summary>
    static async Task<(long Id, string State, string Evidence, DateTimeOffset OpenedAt)?> ReadFindingAsync(
        DatabaseFixture db, long mediaId, string kind)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<(long, string, string, DateTimeOffset)?>(
            """
            select id, state::text, evidence::text, opened_at
            from library.rot_finding
            where media_id = @mediaId and kind = @kind::library.rot_kind
            """,
            new { mediaId, kind });
    }

    static string[] FieldsOf(string evidenceJson)
    {
        using var evidence = JsonDocument.Parse(evidenceJson);
        return evidence.RootElement.GetProperty("fields").EnumerateArray()
            .Select(e => e.GetString() ?? string.Empty).ToArray();
    }

    // Shelf-dust helpers.

    static async Task<long> InsertPlayableRowAsync(DatabaseFixture db, string path, TimeSpan discoveredAgo)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            """
            insert into library.media (path, format, size_bytes, mtime, state, measurable, eligible, discovered_at)
            values (@path, 'flac', 1024, now(), 'ready', true, true, now() - @discoveredAgo)
            returning id
            """,
            new { path, discoveredAgo });
    }

    static async Task InsertRotationRowAsync(DatabaseFixture db, long mediaId, int playCount)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "insert into library.media_rotation (media_id, play_count) values (@mediaId, @playCount)",
            new { mediaId, playCount });
    }

    static async Task SetPlayCountAsync(DatabaseFixture db, long mediaId, int playCount)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "update library.media_rotation set play_count = @playCount where media_id = @mediaId",
            new { mediaId, playCount });
    }

    /// <summary>Seeds an <c>unreachable</c> finding directly (T376 is not built yet — STORY-377's
    /// own AC6 fixture note) rather than through a pass. <paramref name="state"/> defaults to
    /// <c>open</c>; T375 review MED-3 widens this to also seed a <c>dismissed</c> row, pinning that
    /// <c>ShelfDustPredicate</c>'s own <c>u.state = 'open'</c> clause — not merely "any unreachable
    /// row exists" — is what gates shelf_dust.</summary>
    static async Task InsertOpenUnreachableFindingAsync(DatabaseFixture db, long mediaId, string state = "open")
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            """
            insert into library.rot_finding (media_id, kind, state, evidence, opened_at, dismissed_at, updated_at)
            values (
                @mediaId, 'unreachable', @state::library.rot_state, '{}'::jsonb, now(),
                case when @state = 'dismissed' then now() else null end, now()
            )
            """,
            new { mediaId, state });
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — stale_metadata surfaces fixable rows
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioBlankArtist(DatabaseFixture db)
    {
        // Given a ready row with artist null, When the stale_metadata pass runs.
        [Fact]
        public async Task AnOpenFindingNamesArtistInEvidenceFields()
        {
            await db.ResetAsync();
            var mediaId = await InsertStaleMetadataCandidateAsync(db, "/gardener/t375-blank-artist.flac", artist: null);

            await Repo(db).ReconcileStaleMetadataAsync(CancellationToken.None);

            var finding = await ReadFindingAsync(db, mediaId, "stale_metadata");
            Assert.Contains("artist", FieldsOf(finding!.Value.Evidence));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioWhitespaceArtist(DatabaseFixture db)
    {
        // Given a ready row whose artist is whitespace-only, When the pass runs (AC1's own
        // "also a whitespace-only artist" rider).
        [Fact]
        public async Task AnOpenFindingNamesArtistInEvidenceFields()
        {
            await db.ResetAsync();
            var mediaId = await InsertStaleMetadataCandidateAsync(db, "/gardener/t375-whitespace-artist.flac", artist: "   ");

            await Repo(db).ReconcileStaleMetadataAsync(CancellationToken.None);

            var finding = await ReadFindingAsync(db, mediaId, "stale_metadata");
            Assert.Contains("artist", FieldsOf(finding!.Value.Evidence));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheTrackNnFamily(DatabaseFixture db)
    {
        // Given titles "Track 07", "track 7", "Track07", When the pass runs. One fact over the
        // homogeneous set (the Story376 ScenarioThePassReadsNoFiles idiom): the COUNT of findings
        // naming title across the three rows, not three independently-named assertions.
        [Fact]
        public async Task AllThreeHaveAFindingNamingTitle()
        {
            await db.ResetAsync();
            var idA = await InsertStaleMetadataCandidateAsync(db, "/gardener/t375-track-a.flac", title: "Track 07");
            var idB = await InsertStaleMetadataCandidateAsync(db, "/gardener/t375-track-b.flac", title: "track 7");
            var idC = await InsertStaleMetadataCandidateAsync(db, "/gardener/t375-track-c.flac", title: "Track07");

            await Repo(db).ReconcileStaleMetadataAsync(CancellationToken.None);

            await using var conn = await db.DataSource.OpenConnectionAsync();
            var countNamingTitle = await conn.ExecuteScalarAsync<int>(
                """
                select count(*)::int from library.rot_finding
                where kind = 'stale_metadata' and state = 'open' and media_id = any(@ids)
                  and evidence->'fields' ? 'title'
                """,
                new { ids = new[] { idA, idB, idC } });

            Assert.Equal(3, countNamingTitle);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTitlesThatOnlyLookLikeTrackNumbers(DatabaseFixture db)
    {
        // Given titles "Track 7 of Hearts" (trailing text after the number) and "Backtrack 1"
        // (leading text before "track"), When the pass runs — the anchored regex must reject
        // both: neither is the whole-string "Track NN" family (T375 review MED-3).
        [Fact]
        public async Task NeitherHasAFindingNamingTitle()
        {
            await db.ResetAsync();
            var idA = await InsertStaleMetadataCandidateAsync(db, "/gardener/t375-track-negative-a.flac", title: "Track 7 of Hearts");
            var idB = await InsertStaleMetadataCandidateAsync(db, "/gardener/t375-track-negative-b.flac", title: "Backtrack 1");

            await Repo(db).ReconcileStaleMetadataAsync(CancellationToken.None);

            await using var conn = await db.DataSource.OpenConnectionAsync();
            var countNamingTitle = await conn.ExecuteScalarAsync<int>(
                """
                select count(*)::int from library.rot_finding
                where kind = 'stale_metadata' and state = 'open' and media_id = any(@ids)
                  and evidence->'fields' ? 'title'
                """,
                new { ids = new[] { idA, idB } });

            Assert.Equal(0, countNamingTitle);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioEnrichmentMisses(DatabaseFixture db)
    {
        // Given a row with year null and year_lookup_missed_at set, moods null and
        // mood_tag_missed_at set, When the pass runs.
        [Fact]
        public async Task OneFindingHasFieldsYearAndMoodsExactly()
        {
            await db.ResetAsync();
            var mediaId = await InsertStaleMetadataCandidateAsync(
                db, "/gardener/t375-enrichment-misses.flac", year: null, yearLookupMissed: true, moodTagMissed: true);

            await Repo(db).ReconcileStaleMetadataAsync(CancellationToken.None);

            var finding = await ReadFindingAsync(db, mediaId, "stale_metadata");
            Assert.Equal(new[] { "year", "moods" }, FieldsOf(finding!.Value.Evidence));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioOperatorEditsAreExempt(DatabaseFixture db)
    {
        // Given a row with tags_edited_at set and artist deliberately blank, When the pass runs.
        [Fact]
        public async Task NoFindingNamesArtist()
        {
            await db.ResetAsync();
            var mediaId = await InsertStaleMetadataCandidateAsync(
                db, "/gardener/t375-operator-edited-artist.flac", artist: null, tagsEdited: true);

            await Repo(db).ReconcileStaleMetadataAsync(CancellationToken.None);

            var finding = await ReadFindingAsync(db, mediaId, "stale_metadata");
            Assert.False(finding.HasValue);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioOperatorEditDoesNotExemptMoods(DatabaseFixture db)
    {
        // Given a row with tags_edited_at set and moods missing (artist/title/year all clean),
        // When the pass runs — the boundary AC4 draws: tags_edited_at exempts only the three
        // operator-patchable fields, never moods.
        [Fact]
        public async Task TheFindingNamesMoodsOnly()
        {
            await db.ResetAsync();
            var mediaId = await InsertStaleMetadataCandidateAsync(
                db, "/gardener/t375-operator-edited-moods.flac", moodTagMissed: true, tagsEdited: true);

            await Repo(db).ReconcileStaleMetadataAsync(CancellationToken.None);

            var finding = await ReadFindingAsync(db, mediaId, "stale_metadata");
            Assert.Equal(new[] { "moods" }, FieldsOf(finding!.Value.Evidence));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioOperatorEditExemptsTheTrackTitle(DatabaseFixture db)
    {
        // Given a row with tags_edited_at set and title "Track 07" (the ONLY otherwise-stale
        // field), When the pass runs — title is one of the three operator-patchable fields the
        // exemption covers (T375 review MED-3), so no finding opens at all.
        [Fact]
        public async Task NoFindingNamesTitle()
        {
            await db.ResetAsync();
            var mediaId = await InsertStaleMetadataCandidateAsync(
                db, "/gardener/t375-operator-edited-title.flac", title: "Track 07", tagsEdited: true);

            await Repo(db).ReconcileStaleMetadataAsync(CancellationToken.None);

            Assert.False((await ReadFindingAsync(db, mediaId, "stale_metadata")).HasValue);
        }
    }

    // ---------------------------------------------------------------------
    // EXCLUSIONS — stale_metadata narrows, it does not over-flag
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioYearMissingWithoutARecordedLookupMiss(DatabaseFixture db)
    {
        // Given year is null but year_lookup_missed_at is null (never attempted), When the pass
        // runs — a row still waiting on its first lookup attempt is not yet "stale".
        [Fact]
        public async Task NoFindingIsOpened()
        {
            await db.ResetAsync();
            var mediaId = await InsertStaleMetadataCandidateAsync(db, "/gardener/t375-year-not-attempted.flac", year: null);

            await Repo(db).ReconcileStaleMetadataAsync(CancellationToken.None);

            Assert.False((await ReadFindingAsync(db, mediaId, "stale_metadata")).HasValue);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMeasurableIsUnknown(DatabaseFixture db)
    {
        // Given measurable is NULL (not yet analysed), When the pass runs — NULL is not stale,
        // only an explicit false is.
        [Fact]
        public async Task NoFindingIsOpened()
        {
            await db.ResetAsync();
            var mediaId = await InsertStaleMetadataCandidateAsync(db, "/gardener/t375-measurable-unknown.flac", measurable: null);

            await Repo(db).ReconcileStaleMetadataAsync(CancellationToken.None);

            Assert.False((await ReadFindingAsync(db, mediaId, "stale_metadata")).HasValue);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMeasurableIsFalse(DatabaseFixture db)
    {
        // Given measurable is explicitly false, When the pass runs.
        [Fact]
        public async Task TheFindingNamesMeasurable()
        {
            await db.ResetAsync();
            var mediaId = await InsertStaleMetadataCandidateAsync(db, "/gardener/t375-measurable-false.flac", measurable: false);

            await Repo(db).ReconcileStaleMetadataAsync(CancellationToken.None);

            var finding = await ReadFindingAsync(db, mediaId, "stale_metadata");
            Assert.Contains("measurable", FieldsOf(finding!.Value.Evidence));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioNeverPlayRowWithABlankArtist(DatabaseFixture db)
    {
        // Given a never_play row with a blank artist, When the pass runs — never_play rows are
        // out of scope entirely, the same way the near_duplicate/rotation-health predicates
        // already exclude them.
        [Fact]
        public async Task NoFindingIsOpened()
        {
            await db.ResetAsync();
            var mediaId = await InsertStaleMetadataCandidateAsync(db, "/gardener/t375-never-play-blank-artist.flac", artist: null);
            await SetNeverPlayAsync(db, mediaId);

            await Repo(db).ReconcileStaleMetadataAsync(CancellationToken.None);

            Assert.False((await ReadFindingAsync(db, mediaId, "stale_metadata")).HasValue);
        }
    }

    // ---------------------------------------------------------------------
    // LIFECYCLE — stale_metadata resolves, dismisses, and refreshes like every other pass
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioFixingTheArtistResolvesTheFinding(DatabaseFixture db)
    {
        // Given an open finding for a blank artist, When the artist is fixed via SQL and a second
        // reconcile runs.
        [Fact]
        public async Task TheFindingResolves()
        {
            await db.ResetAsync();
            var mediaId = await InsertStaleMetadataCandidateAsync(db, "/gardener/t375-fix-artist.flac", artist: null);
            var repo = Repo(db);
            await repo.ReconcileStaleMetadataAsync(CancellationToken.None);

            await SetArtistAsync(db, mediaId, "Now Known");
            await repo.ReconcileStaleMetadataAsync(CancellationToken.None);

            var finding = await ReadFindingAsync(db, mediaId, "stale_metadata");
            Assert.Equal("resolved", finding!.Value.State);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioADismissedFindingStaysDismissed(DatabaseFixture db)
    {
        // Given an open finding dismissed at the store level, When the row is STILL stale and a
        // second reconcile runs, Then it stays dismissed (dismissed-forever, SPEC F153.2).
        [Fact]
        public async Task TheFindingStaysDismissed()
        {
            await db.ResetAsync();
            var mediaId = await InsertStaleMetadataCandidateAsync(db, "/gardener/t375-dismissed.flac", artist: null);
            var repo = Repo(db);
            await repo.ReconcileStaleMetadataAsync(CancellationToken.None);
            var findingId = (await ReadFindingAsync(db, mediaId, "stale_metadata"))!.Value.Id;
            await repo.DismissAsync(findingId, CancellationToken.None);

            await repo.ReconcileStaleMetadataAsync(CancellationToken.None);

            Assert.Equal("dismissed", (await ReadFindingAsync(db, mediaId, "stale_metadata"))!.Value.State);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioOpenedAtIsStableWhileFieldsGrow(DatabaseFixture db)
    {
        // Given an open finding naming only artist, When a SECOND field (moods) goes stale
        // between ticks and a second reconcile runs. Both facts share this one arrangement (the
        // Story376 ScenarioGroupKeyRefreshesOnRetag idiom).
        async Task<(long MediaId, DateTimeOffset FirstOpenedAt)> ArrangeAsync()
        {
            await db.ResetAsync();
            var mediaId = await InsertStaleMetadataCandidateAsync(db, "/gardener/t375-fields-grow.flac", artist: null);
            var repo = Repo(db);
            await repo.ReconcileStaleMetadataAsync(CancellationToken.None);
            var firstOpenedAt = (await ReadFindingAsync(db, mediaId, "stale_metadata"))!.Value.OpenedAt;

            await SetMoodTagMissedAsync(db, mediaId);
            await repo.ReconcileStaleMetadataAsync(CancellationToken.None);

            return (mediaId, firstOpenedAt);
        }

        [Fact]
        public async Task OpenedAtIsUnchanged()
        {
            var (mediaId, firstOpenedAt) = await ArrangeAsync();
            Assert.Equal(firstOpenedAt, (await ReadFindingAsync(db, mediaId, "stale_metadata"))!.Value.OpenedAt);
        }

        [Fact]
        public async Task FieldsNowNamesArtistAndMoods()
        {
            var (mediaId, _) = await ArrangeAsync();
            var finding = await ReadFindingAsync(db, mediaId, "stale_metadata");
            Assert.Equal(new[] { "artist", "moods" }, FieldsOf(finding!.Value.Evidence));
        }
    }

    // ---------------------------------------------------------------------
    // PASS WIRING — stale_metadata drives through the real repository (T375 review MED-3: every
    // other stale_metadata fact above drives RotFindingRepository directly, mirroring shelf_dust's
    // own ScenarioJustOverTheShelfDustDaysBoundaryThroughThePass)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioThePassRunsOverTheRealRepository(DatabaseFixture db)
    {
        // Given a blank-artist row (AC1's own shape), When the real StaleMetadataGardenerPass runs
        // (not the repository directly) — proving the pass -> repository arc itself.
        [Fact]
        public async Task AFindingOpensNamingArtist()
        {
            await db.ResetAsync();
            var mediaId = await InsertStaleMetadataCandidateAsync(db, "/gardener/t375-pass-wiring.flac", artist: null);
            var pass = new StaleMetadataGardenerPass(Repo(db));

            await pass.RunAsync(CancellationToken.None);

            var finding = await ReadFindingAsync(db, mediaId, "stale_metadata");
            Assert.Contains("artist", FieldsOf(finding!.Value.Evidence));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — shelf_dust surfaces forgotten rows
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioShelfDustWithNoLedgerRow(DatabaseFixture db)
    {
        // Given a playable row discovered 91 days ago with no ledger row, When the shelf_dust
        // pass runs.
        [Fact]
        public async Task AnOpenShelfDustFindingExists()
        {
            await db.ResetAsync();
            var mediaId = await InsertPlayableRowAsync(db, "/gardener/t375-dust-no-ledger.flac", TimeSpan.FromDays(91));

            await Repo(db).ReconcileShelfDustAsync(TimeSpan.FromDays(90), CancellationToken.None);

            Assert.Equal("open", (await ReadFindingAsync(db, mediaId, "shelf_dust"))!.Value.State);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioShelfDustWithAZeroPlayLedgerRow(DatabaseFixture db)
    {
        // Given the same row, but WITH a ledger row at play_count = 0, When the pass runs — a
        // ledger row that has simply never recorded a play is exactly as dusty as no row at all.
        [Fact]
        public async Task AnOpenShelfDustFindingExists()
        {
            await db.ResetAsync();
            var mediaId = await InsertPlayableRowAsync(db, "/gardener/t375-dust-zero-plays.flac", TimeSpan.FromDays(91));
            await InsertRotationRowAsync(db, mediaId, playCount: 0);

            await Repo(db).ReconcileShelfDustAsync(TimeSpan.FromDays(90), CancellationToken.None);

            Assert.Equal("open", (await ReadFindingAsync(db, mediaId, "shelf_dust"))!.Value.State);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioShelfDustEvidenceNamesTheExactAge(DatabaseFixture db)
    {
        // Given a row discovered exactly 91 days ago, When the pass runs, Then
        // evidence.days_on_shelf reads 91 exactly — the reconcile runs milliseconds after the
        // seed, well inside the same UTC day boundary.
        [Fact]
        public async Task DaysOnShelfIsNinetyOne()
        {
            await db.ResetAsync();
            var mediaId = await InsertPlayableRowAsync(db, "/gardener/t375-dust-evidence.flac", TimeSpan.FromDays(91));

            await Repo(db).ReconcileShelfDustAsync(TimeSpan.FromDays(90), CancellationToken.None);

            var finding = await ReadFindingAsync(db, mediaId, "shelf_dust");
            using var evidence = JsonDocument.Parse(finding!.Value.Evidence);
            Assert.Equal(91, evidence.RootElement.GetProperty("days_on_shelf").GetInt32());
        }
    }

    // ---------------------------------------------------------------------
    // AC6 — shelf_dust defers to unreachable
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioShelfDustExcludesAnAlreadyUnreachableRow(DatabaseFixture db)
    {
        // Given a dusty row that already carries an open unreachable finding, When the shelf_dust
        // pass runs, Then no shelf_dust finding opens for it.
        [Fact]
        public async Task NoShelfDustFindingIsOpened()
        {
            await db.ResetAsync();
            var mediaId = await InsertPlayableRowAsync(db, "/gardener/t375-dust-unreachable.flac", TimeSpan.FromDays(91));
            await InsertOpenUnreachableFindingAsync(db, mediaId);

            await Repo(db).ReconcileShelfDustAsync(TimeSpan.FromDays(90), CancellationToken.None);

            Assert.False((await ReadFindingAsync(db, mediaId, "shelf_dust")).HasValue);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioADismissedUnreachableFindingDoesNotSuppressDust(DatabaseFixture db)
    {
        // Given a dusty row that carries a DISMISSED (not open) unreachable finding, When the
        // shelf_dust pass runs, Then a shelf_dust finding still opens — pins ShelfDustPredicate's
        // own "u.state = 'open'" clause (T375 review MED-3): only a currently-open unreachable
        // finding defers to F153.8, never a dismissed one.
        [Fact]
        public async Task AnOpenShelfDustFindingExists()
        {
            await db.ResetAsync();
            var mediaId = await InsertPlayableRowAsync(db, "/gardener/t375-dust-dismissed-unreachable.flac", TimeSpan.FromDays(91));
            await InsertOpenUnreachableFindingAsync(db, mediaId, state: "dismissed");

            await Repo(db).ReconcileShelfDustAsync(TimeSpan.FromDays(90), CancellationToken.None);

            Assert.Equal("open", (await ReadFindingAsync(db, mediaId, "shelf_dust"))!.Value.State);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioShelfDustResolvesOnceUnreachableOpens(DatabaseFixture db)
    {
        // Given an already-open shelf_dust finding, When an unreachable finding opens for the
        // SAME row and a second reconcile runs, Then the shelf_dust finding resolves.
        [Fact]
        public async Task TheShelfDustFindingResolves()
        {
            await db.ResetAsync();
            var mediaId = await InsertPlayableRowAsync(db, "/gardener/t375-dust-then-unreachable.flac", TimeSpan.FromDays(91));
            var repo = Repo(db);
            await repo.ReconcileShelfDustAsync(TimeSpan.FromDays(90), CancellationToken.None);

            await InsertOpenUnreachableFindingAsync(db, mediaId);
            await repo.ReconcileShelfDustAsync(TimeSpan.FromDays(90), CancellationToken.None);

            Assert.Equal("resolved", (await ReadFindingAsync(db, mediaId, "shelf_dust"))!.Value.State);
        }
    }

    // ---------------------------------------------------------------------
    // LIFECYCLE — shelf_dust resolves once a row finally airs
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioADustyRowThatThenAirsResolves(DatabaseFixture db)
    {
        // Given an open shelf_dust finding backed by a play_count = 0 ledger row, When that row
        // finally airs (play_count -> 1) and a second reconcile runs.
        [Fact]
        public async Task TheFindingResolves()
        {
            await db.ResetAsync();
            var mediaId = await InsertPlayableRowAsync(db, "/gardener/t375-dust-airs.flac", TimeSpan.FromDays(91));
            await InsertRotationRowAsync(db, mediaId, playCount: 0);
            var repo = Repo(db);
            await repo.ReconcileShelfDustAsync(TimeSpan.FromDays(90), CancellationToken.None);

            await SetPlayCountAsync(db, mediaId, playCount: 1);
            await repo.ReconcileShelfDustAsync(TimeSpan.FromDays(90), CancellationToken.None);

            Assert.Equal("resolved", (await ReadFindingAsync(db, mediaId, "shelf_dust"))!.Value.State);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — a fresh row earns no finding, and the ShelfDustDays knob is honoured exactly
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAFreshRowIsNotDust(DatabaseFixture db)
    {
        // Given a playable row discovered yesterday, When the pass runs.
        [Fact]
        public async Task NoFindingIsOpened()
        {
            await db.ResetAsync();
            var mediaId = await InsertPlayableRowAsync(db, "/gardener/t375-fresh.flac", TimeSpan.FromDays(1));

            await Repo(db).ReconcileShelfDustAsync(TimeSpan.FromDays(90), CancellationToken.None);

            Assert.False((await ReadFindingAsync(db, mediaId, "shelf_dust")).HasValue);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioJustUnderTheShelfDustDaysBoundary(DatabaseFixture db)
    {
        // Given ShelfDustDays = 90 driven live through the real ShelfDustGardenerPass (T375
        // review LOW-2: mirrors ScenarioJustOverTheShelfDustDaysBoundaryThroughThePass's own
        // FakeOptionsMonitor<GardenerOptions> rig rather than a hard-coded TimeSpan) and a row
        // discovered 89 days ago, When the pass runs — one day short of the threshold is still
        // fresh.
        [Fact]
        public async Task NoFindingIsOpened()
        {
            await db.ResetAsync();
            var mediaId = await InsertPlayableRowAsync(db, "/gardener/t375-boundary-under.flac", TimeSpan.FromDays(89));
            var pass = new ShelfDustGardenerPass(
                Repo(db), new FakeOptionsMonitor<GardenerOptions>(new GardenerOptions { ShelfDustDays = 90 }));

            await pass.RunAsync(CancellationToken.None);

            Assert.False((await ReadFindingAsync(db, mediaId, "shelf_dust")).HasValue);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioJustOverTheShelfDustDaysBoundaryThroughThePass(DatabaseFixture db)
    {
        // Given ShelfDustDays = 90 driven live through the real ShelfDustGardenerPass (a
        // FakeOptionsMonitor<GardenerOptions>, the Story376 ScenarioThePassReadsNoFiles idiom) and
        // a row discovered 90 days and one minute ago, When the pass runs — one minute past the
        // threshold opens a finding, proving the pass->repository arc itself, not just the SQL.
        [Fact]
        public async Task AFindingIsOpened()
        {
            await db.ResetAsync();
            var mediaId = await InsertPlayableRowAsync(
                db, "/gardener/t375-boundary-over.flac", TimeSpan.FromDays(90) + TimeSpan.FromMinutes(1));
            var pass = new ShelfDustGardenerPass(
                Repo(db), new FakeOptionsMonitor<GardenerOptions>(new GardenerOptions { ShelfDustDays = 90 }));

            await pass.RunAsync(CancellationToken.None);

            Assert.True((await ReadFindingAsync(db, mediaId, "shelf_dust")).HasValue);
        }
    }
}

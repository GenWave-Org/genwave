// STORY-357 — An accepted announcement never vanishes (SPEC F143 · PLAN T337)
//
// BDD specification — xUnit, REAL Postgres via DatabaseFixture (mirrors Story224_RequestStore.cs's
// own fixture family: direct AnnouncementRepository construction over StationDataSource, an
// independent raw-SQL read for verifying writes rather than reading back through the repository
// under test). T337 lands db/40 (folded into db/06 for this fixture, per its own remarks) +
// AnnouncementRepository — the durable store beneath the House Voice epic's endpoint (T339), vend
// (T341), and lifecycle guardians (T343), none of which exist yet; this file owns only the store.
//
// ScenarioMigrationConvergence mirrors Story305_ShowRepository.cs's own fresh-init (assert against
// DatabaseFixture.InitialSchema, proving db/06's own mirror of db/40) and Story304_AiredKindStamp.cs's
// own in-place shape (drop the table, re-run the migration script, prove convergence) — station.
// announcement is a leaf table with no FK dependents (DatabaseFixture.ResetAnnouncementAsync's own
// remarks), so a bare DROP TABLE is the FK-safe equivalent of Story304's own multi-object drop.

using Dapper;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureAnnouncementStoreLifecycle
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// An independent raw-SQL read (bypasses <see cref="AnnouncementRepository"/> itself) so a fact
    /// verifies what the repository under test actually persisted, not what its own read method
    /// merely reports back — the same posture <c>Story224_RequestStore.ReadRowAsync</c> takes.
    /// Reuses <see cref="AnnouncementRow"/> (rather than a duplicate local record) since its shape is
    /// already exactly this table's columns; <see cref="AnnouncementStateTypeHandler"/> (registered by
    /// <see cref="DatabaseFixture.InitializeAsync"/>) maps the raw <c>state</c> text back to
    /// <see cref="AnnouncementState"/> here exactly as it does inside the repository itself.
    /// </summary>
    static async Task<AnnouncementRow> ReadRowAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<AnnouncementRow>(
            """
            select id, message, verbatim, requested_voice, source, state, decline_reason,
                   collapse_count, created_at, expires_at, claimed_at, aired_at, state_changed_at
            from station.announcement where id = @id
            """,
            new { id });
    }

    static async Task<int> CountAllRowsAsync(DatabaseFixture db)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<int>("select count(*)::int from station.announcement");
    }

    /// <summary>Runs db/40-announcements-migration.sh against the test database via the fixture.
    /// Mirrors Story304/Story305's own RunMigrationScript helper. Safe to call unconditionally — the
    /// script is idempotent (CREATE TABLE/INDEX IF NOT EXISTS).</summary>
    static void RunMigrationScript(DatabaseFixture db) =>
        db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "40-announcements-migration.sh"));

    /// <summary>Mirrors Story304's own TableExistsAsync helper.</summary>
    static async Task<bool> TableExistsAsync(DatabaseFixture db, string table)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        var count = await conn.ExecuteScalarAsync<long>(
            "select count(*) from information_schema.tables where table_schema = 'station' and table_name = @table",
            new { table });
        return count > 0;
    }

    // ---------------------------------------------------------------------
    // AC1 — accepted means durable; AC5 — a restart loses nothing
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAcceptedRowsAreDurable(DatabaseFixture db)
    {
        [Fact]
        public async Task AnInsertedAnnouncementIsPending()
        {
            // Given no prior announcements...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);

            // When one is accepted...
            var id = await repo.InsertAsync(
                "On air in five minutes", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: TimeSpan.FromMinutes(20), CancellationToken.None);

            // Then it lands pending.
            var row = await ReadRowAsync(db, id);
            Assert.Equal(AnnouncementState.Pending, row.State);
        }

        [Fact]
        public async Task AnInsertedAnnouncementsExpiryIsComputedFromCreatedAtPlusTtl()
        {
            // Given no prior announcements...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);

            // When one is accepted with an explicit 20-minute TTL...
            var id = await repo.InsertAsync(
                "On air in five minutes", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: TimeSpan.FromMinutes(20), CancellationToken.None);

            // Then expires_at is computed from its own created_at + TTL.
            var row = await ReadRowAsync(db, id);
            Assert.Equal(row.CreatedAt.AddMinutes(20), row.ExpiresAt, TimeSpan.FromSeconds(2));
        }

        [Fact]
        public async Task TheDefaultTtlIsFifteenMinutes()
        {
            // Given no prior announcements...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);

            // When one is accepted with no TTL override...
            var id = await repo.InsertAsync(
                "Dinner's ready", verbatim: false, requestedVoice: null, source: AnnouncementSource.Session,
                ttl: null, CancellationToken.None);

            // Then its expiry is 900 seconds (15 minutes) past its own created_at.
            var row = await ReadRowAsync(db, id);
            Assert.Equal(row.CreatedAt.AddSeconds(900), row.ExpiresAt, TimeSpan.FromSeconds(2));
        }

        [Fact]
        public async Task AFreshRepositoryInstanceReadsTheSamePendingRows()
        {
            // Given a pending announcement inserted through one repository instance...
            await db.ResetAnnouncementAsync();
            var writer = Harness.AnnouncementRepo(db);
            var id = await writer.InsertAsync(
                "Storm's coming, bring the washing in", verbatim: true, requestedVoice: null,
                source: AnnouncementSource.Token, ttl: null, CancellationToken.None);

            // When a FRESH instance reads it — the api-restart shape: no in-memory state survives
            // an instance boundary except the row itself...
            var restarted = Harness.AnnouncementRepo(db);
            var history = await restarted.HistoryAsync(10, CancellationToken.None);

            // Then the same row is still there, still pending and deliverable.
            Assert.Contains(history, row => row.Id == id && row.State == AnnouncementState.Pending);
        }
    }

    // ---------------------------------------------------------------------
    // AC2 — expiry is visible, never silent
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioExpiryIsVisibleNeverSilent(DatabaseFixture db)
    {
        [Fact]
        public async Task APendingRowPastItsTtlTransitionsToExpired()
        {
            // Given a pending announcement whose TTL has already passed...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);
            var id = await repo.InsertAsync(
                "Bins go out tonight", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: TimeSpan.FromSeconds(-1), CancellationToken.None);

            // When the lifecycle sweep evaluates it...
            await repo.ExpireStaleAsync(DateTimeOffset.UtcNow, CancellationToken.None);

            // Then its state is expired.
            var row = await ReadRowAsync(db, id);
            Assert.Equal(AnnouncementState.Expired, row.State);
        }

        [Fact]
        public async Task ExpireStaleAsyncAlsoExpiresStaleClaimedRows()
        {
            // Given an announcement CLAIMED for delivery, with a short TTL...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);
            var id = await repo.InsertAsync(
                "Bins go out tonight", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: TimeSpan.FromMinutes(1), CancellationToken.None);
            var claimed = await repo.ClaimOldestAsync(1, DateTimeOffset.UtcNow, CancellationToken.None);
            Assert.Single(claimed);

            // When the lifecycle sweep evaluates it well past its TTL — claimed, not pending...
            await repo.ExpireStaleAsync(DateTimeOffset.UtcNow.AddMinutes(5), CancellationToken.None);

            // Then its state is expired too — the sweep does not skip claimed rows.
            var row = await ReadRowAsync(db, id);
            Assert.Equal(AnnouncementState.Expired, row.State);
        }

        [Fact]
        public async Task TheExpiryStampsStateChangedAt()
        {
            // Given a pending announcement, and its insert-time state_changed_at...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);
            var id = await repo.InsertAsync(
                "Bins go out tonight", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: TimeSpan.FromSeconds(-1), CancellationToken.None);
            var beforeExpiry = await ReadRowAsync(db, id);
            await Task.Delay(TimeSpan.FromMilliseconds(50));

            // When it expires...
            await repo.ExpireStaleAsync(DateTimeOffset.UtcNow, CancellationToken.None);

            // Then state_changed_at moved forward — the expiry is stamped, not silent.
            var afterExpiry = await ReadRowAsync(db, id);
            Assert.True(afterExpiry.StateChangedAt > beforeExpiry.StateChangedAt);
        }

        [Fact]
        public async Task AnExpiredRowStillAppearsInTheHistoryRead()
        {
            // Given an announcement that has expired...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);
            var id = await repo.InsertAsync(
                "Bins go out tonight", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: TimeSpan.FromSeconds(-1), CancellationToken.None);
            await repo.ExpireStaleAsync(DateTimeOffset.UtcNow, CancellationToken.None);

            // When the history surface is read...
            var history = await repo.HistoryAsync(10, CancellationToken.None);

            // Then the expired row is still in it — visible, not vanished.
            Assert.Contains(history, row => row.Id == id && row.State == AnnouncementState.Expired);
        }
    }

    // ---------------------------------------------------------------------
    // AC4 — identical text collapses
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioIdenticalTextCollapses(DatabaseFixture db)
    {
        [Fact]
        public async Task ACaseFoldedDuplicateCreatesNoNewRow()
        {
            // Given a pending announcement...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);
            var firstId = await repo.InsertAsync(
                "Dinner's ready", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: null, CancellationToken.None);

            // When a case-folded-identical submission arrives...
            var secondId = await repo.InsertAsync(
                "DINNER'S READY", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: null, CancellationToken.None);

            // Then it collapses into the same row — no new id.
            Assert.Equal(firstId, secondId);
        }

        [Fact]
        public async Task TheExistingRowsCollapseCountIncrements()
        {
            // Given a pending announcement...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);
            var id = await repo.InsertAsync(
                "Dinner's ready", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: null, CancellationToken.None);

            // When a case-folded-identical submission arrives...
            await repo.InsertAsync(
                "dinner's ready", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: null, CancellationToken.None);

            // Then the existing row's collapse_count increments from its default of 1.
            var row = await ReadRowAsync(db, id);
            Assert.Equal(2, row.CollapseCount);
        }

        [Fact]
        public async Task TheExistingRowsTtlIsUntouchedByTheCollapse()
        {
            // Given a pending announcement with a 5-minute TTL...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);
            var id = await repo.InsertAsync(
                "Dinner's ready", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: TimeSpan.FromMinutes(5), CancellationToken.None);
            var originalExpiresAt = (await ReadRowAsync(db, id)).ExpiresAt;

            // When a case-folded-identical submission arrives carrying a DIFFERENT TTL...
            await repo.InsertAsync(
                "dinner's ready", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: TimeSpan.FromHours(1), CancellationToken.None);

            // Then the original row's expiry is untouched — the collapse never re-computes it.
            var row = await ReadRowAsync(db, id);
            Assert.Equal(originalExpiresAt, row.ExpiresAt);
        }

        [Fact]
        public async Task APastTtlPendingRowDoesNotAbsorbANewSubmission()
        {
            // Given a pending announcement whose TTL has already passed — undeliverable, but not yet
            // swept by ExpireStaleAsync (the collapse target must equal the DELIVERABLE set, not merely
            // state = 'pending' alone)...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);
            var staleId = await repo.InsertAsync(
                "Storm's coming, bring the washing in", verbatim: true, requestedVoice: null,
                source: AnnouncementSource.Token, ttl: TimeSpan.FromSeconds(-1), CancellationToken.None);

            // When a case-folded-identical submission arrives carrying a FRESH TTL...
            var freshId = await repo.InsertAsync(
                "STORM'S COMING, BRING THE WASHING IN", verbatim: true, requestedVoice: null,
                source: AnnouncementSource.Token, ttl: TimeSpan.FromMinutes(20), CancellationToken.None);

            // Then the new submission is itself claimable (deliverable) — proof it landed its own
            // fresh row rather than folding into the stale one and inheriting its already-passed
            // expiry (a fold would leave nothing claimable at all, since the stale row's expiry is
            // untouched by a collapse — see TheExistingRowsTtlIsUntouchedByTheCollapse above).
            var claimed = await repo.ClaimOldestAsync(10, DateTimeOffset.UtcNow, CancellationToken.None);
            Assert.Contains(claimed, row => row.Id == freshId);
        }

        [Fact]
        public async Task CollapseOnlyFoldsIntoPendingRowsNotAiredOnes()
        {
            // Given an announcement that has already AIRED...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);
            var airedId = await repo.InsertAsync(
                "Dinner's ready", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: null, CancellationToken.None);
            var claimed = await repo.ClaimOldestAsync(1, DateTimeOffset.UtcNow, CancellationToken.None);
            await repo.MarkAiredAsync(Assert.Single(claimed).Id, CancellationToken.None);

            // When a case-folded-identical submission arrives...
            var newId = await repo.InsertAsync(
                "DINNER'S READY", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: null, CancellationToken.None);

            // Then it lands its own new row — an aired row is never a collapse target.
            Assert.NotEqual(airedId, newId);
        }
    }

    // ---------------------------------------------------------------------
    // AC2 (decline half) — every transition is total; no row is ever deleted
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTransitionsAreTotalAndNothingIsDeleted(DatabaseFixture db)
    {
        [Fact]
        public async Task ADeclineStampsItsReason()
        {
            // Given a pending announcement...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);
            var id = await repo.InsertAsync(
                "Doorbell test", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: null, CancellationToken.None);

            // When it is declined with a reason...
            await repo.MarkDeclinedAsync([id], "station went public", CancellationToken.None);

            // Then the reason is stamped on the row.
            var row = await ReadRowAsync(db, id);
            Assert.Equal("station went public", row.DeclineReason);
        }

        [Fact]
        public async Task ADeclineStampsStateChangedAt()
        {
            // Given a pending announcement, and its insert-time state_changed_at...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);
            var id = await repo.InsertAsync(
                "Doorbell test", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: null, CancellationToken.None);
            var beforeDecline = await ReadRowAsync(db, id);
            await Task.Delay(TimeSpan.FromMilliseconds(50));

            // When it is declined...
            await repo.MarkDeclinedAsync([id], "station went public", CancellationToken.None);

            // Then state_changed_at moved forward — the decline is stamped, not silent.
            var afterDecline = await ReadRowAsync(db, id);
            Assert.True(afterDecline.StateChangedAt > beforeDecline.StateChangedAt);
        }

        [Fact]
        public async Task NoLifecycleTransitionEverDeletesARow()
        {
            // Given four announcements driven through every transition this store offers...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);

            var declinedId = await repo.InsertAsync(
                "Declined one", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: null, CancellationToken.None);
            await repo.MarkDeclinedAsync([declinedId], "station went public", CancellationToken.None);

            await repo.InsertAsync(
                "Expired one", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: TimeSpan.FromSeconds(-1), CancellationToken.None);
            await repo.ExpireStaleAsync(DateTimeOffset.UtcNow, CancellationToken.None);

            await repo.InsertAsync(
                "Aired one", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: null, CancellationToken.None);
            var claimed = await repo.ClaimOldestAsync(1, DateTimeOffset.UtcNow, CancellationToken.None);
            await repo.MarkAiredAsync(Assert.Single(claimed).Id, CancellationToken.None);

            await repo.InsertAsync(
                "Still pending", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: null, CancellationToken.None);

            // When every row's own outcome is inspected together...
            var totalRows = await CountAllRowsAsync(db);

            // Then all four still exist — declined, expired, aired, and pending alike.
            Assert.Equal(4, totalRows);
        }
    }

    // ---------------------------------------------------------------------
    // db/40's own DDL: fresh init (db/06's mirror) and in-place migration converge
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMigrationConvergence(DatabaseFixture db)
    {
        [Fact]
        public void TheFreshInitSnapshotIncludesTheAnnouncementStateColumn()
        {
            // DatabaseFixture.InitialSchema is captured once, immediately after Postgres finishes
            // running ONLY db/01 + db/06 (db-compose.yaml's own docker-entrypoint-initdb.d mount) and
            // before any spec class — this one included — ever runs db/40. Unlike
            // station.schedule_special (F120.5, deliberately NO db/06 mirror), station.announcement
            // DOES ship one (db/40's own header) — the Story305 mirror-assert shape: a dropped mirror
            // turns this fact red, since nothing here ever runs db/40 itself.
            var found = db.InitialSchema.TryGetValue(("station", "announcement", "state"), out var column);

            Assert.True(found, "station.announcement.state missing from the fresh-init schema snapshot");
            Assert.Equal("text", column.DataType);
            Assert.Equal("NO", column.IsNullable);
        }

        [Fact]
        public async Task TheMigrationScriptIsIdempotentAndRestoresTheTableInPlace()
        {
            // Simulate a pre-T337 database by dropping the table db/40 adds — station.announcement is
            // a leaf table with no FK dependents (DatabaseFixture.ResetAnnouncementAsync's own
            // remarks), so a bare DROP TABLE is FK-safe with no cascade to worry about.
            await using (var conn = await db.StationDataSource.OpenConnectionAsync())
                await conn.ExecuteAsync("drop table if exists station.announcement");
            Assert.False(await TableExistsAsync(db, "announcement"));

            // Running it twice must be a safe no-op the second time (every migration file's own "safe
            // to run multiple times" promise).
            RunMigrationScript(db);
            RunMigrationScript(db);

            // The table is back, along with the vend/claim path's own partial index (SPEC F144.1).
            Assert.True(await TableExistsAsync(db, "announcement"));
            await using var verifyConn = await db.StationDataSource.OpenConnectionAsync();
            var hasDeliverableIndex = await verifyConn.ExecuteScalarAsync<bool>(
                """
                select exists(
                    select 1 from pg_indexes
                    where schemaname = 'station' and tablename = 'announcement'
                      and indexname = 'announcement_deliverable')
                """);
            Assert.True(hasDeliverableIndex, "station.announcement is missing its announcement_deliverable partial index.");
        }
    }
}

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

using System.Text.RegularExpressions;
using Dapper;
using GenWave.Core.Abstractions;
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

    /// <summary>Backdates a claimed row's own <c>claimed_at</c> directly — the ONLY way a test can
    /// put a row genuinely past the re-arm grace without an actual wall-clock wait (PLAN T343). No
    /// repository member writes <c>claimed_at</c> to an arbitrary instant; this is deliberately raw
    /// SQL, mirroring <see cref="ReadRowAsync"/>'s own "an independent path proves what the
    /// repository under test actually persisted" posture one step further.</summary>
    static async Task SetClaimedAtAsync(DatabaseFixture db, long id, DateTimeOffset claimedAt)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "update station.announcement set claimed_at = @ClaimedAt where id = @Id",
            new { Id = id, ClaimedAt = claimedAt });
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
    // SPEC F144.1 (PLAN T341, T341 review finding F3) — the vend-side claim: the max<=0 clamp,
    // proven against the REAL Postgres-backed repository (GenWave.Orchestration.Tests cannot
    // reference MediaLibrary, so this is the ONLY place that clamp is ever exercised for real), and
    // field-mapping fidelity from AnnouncementRow onto the narrower AnnouncementItem.
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioClaimDeliverablePinsTheVendSeam(DatabaseFixture db)
    {
        [Fact]
        public async Task AMaxOfZeroClaimsNothingAndTouchesNoRowState()
        {
            // Given a pending, deliverable announcement...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);
            var id = await repo.InsertAsync(
                "Bins go out tonight", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: null, CancellationToken.None);

            // When the vend seam is asked to claim at most zero...
            var claimed = await repo.ClaimDeliverableAsync(0, CancellationToken.None);

            // Then nothing comes back, and the row is left exactly as it was — still pending, unclaimed.
            Assert.Empty(claimed);
            var row = await ReadRowAsync(db, id);
            Assert.Equal(AnnouncementState.Pending, row.State);
            Assert.Null(row.ClaimedAt);
        }

        [Fact]
        public async Task ANegativeMaxClaimsNothingRatherThanFaulting()
        {
            // Given a pending, deliverable announcement...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);
            await repo.InsertAsync(
                "Bins go out tonight", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: null, CancellationToken.None);

            // When the vend seam is asked to claim at most negative-one — the clamp's own defensive
            // floor: a raw negative LIMIT is a Postgres error (22003, invalid_row_count_in_limit_clause),
            // never "none", so a caller mistake here must never surface as an unhandled exception.
            var claimed = await repo.ClaimDeliverableAsync(-1, CancellationToken.None);

            // Then it degrades to an empty claim — never a PostgresException.
            Assert.Empty(claimed);
        }

        [Fact]
        public async Task AClaimedRowMapsEveryFieldOntoItsOwnAnnouncementItemMember()
        {
            // Given a pending announcement with every field distinct from every other — transposition-
            // proof: Message and RequestedVoice share a type (string), so a copy-paste bug swapping
            // ToAnnouncementItem's argument order would compile clean and still needs to be caught here.
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);
            var id = await repo.InsertAsync(
                "The garage sale starts at nine", verbatim: false, requestedVoice: "nova",
                source: AnnouncementSource.Token, ttl: null, CancellationToken.None);

            // When it is claimed through the vend seam...
            var claimed = await repo.ClaimDeliverableAsync(10, CancellationToken.None);

            // Then every AnnouncementItem field carries its OWN column's value, never a neighbor's.
            var item = Assert.Single(claimed);
            Assert.Equal(id, item.Id);
            Assert.Equal("The garage sale starts at nine", item.Message);
            Assert.False(item.Verbatim);
            Assert.Equal("nova", item.RequestedVoice);
        }
    }

    // ---------------------------------------------------------------------
    // PLAN T343 — the lifecycle guardians' own repository primitives: MarkAiredAsync's collapse-
    // count return, FindClaimedPastGraceAsync's grace-filtered read, and DeclineAllLiveAsync's bulk
    // pending+claimed sweep (SPEC F143.3, F144.5, F145.2).
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMarkAiredReturnsTheCollapseCount(DatabaseFixture db)
    {
        [Fact]
        public async Task ARowNeverCollapsedIntoReturnsOne()
        {
            // Given a claimed announcement that was never a collapse target...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);
            await repo.InsertAsync(
                "Bins go out tonight", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: null, CancellationToken.None);
            var claimed = await repo.ClaimOldestAsync(1, DateTimeOffset.UtcNow, CancellationToken.None);

            // When it airs...
            var collapseCount = await repo.MarkAiredAsync(Assert.Single(claimed).Id, CancellationToken.None);

            // Then the booth log's own carry is the DDL's default of 1.
            Assert.Equal(1, collapseCount);
        }

        [Fact]
        public async Task ARowThatCollapsedThreeSubmissionsReturnsThree()
        {
            // Given a pending announcement that absorbed two duplicate submissions before it claimed...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);
            await repo.InsertAsync(
                "Dinner's ready", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: null, CancellationToken.None);
            await repo.InsertAsync(
                "DINNER'S READY", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: null, CancellationToken.None);
            await repo.InsertAsync(
                "dinner's ready", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: null, CancellationToken.None);
            var claimed = await repo.ClaimOldestAsync(1, DateTimeOffset.UtcNow, CancellationToken.None);

            // When it airs...
            var collapseCount = await repo.MarkAiredAsync(Assert.Single(claimed).Id, CancellationToken.None);

            // Then the collapse count carries through to the aired stamp.
            Assert.Equal(3, collapseCount);
        }

        [Fact]
        public async Task ARowNotCurrentlyClaimedReturnsNull()
        {
            // Given a still-pending announcement, never claimed...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);
            var id = await repo.InsertAsync(
                "Bins go out tonight", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: null, CancellationToken.None);

            // When something calls MarkAiredAsync on it anyway (a stale/duplicate TrackAired signal)...
            var collapseCount = await repo.MarkAiredAsync(id, CancellationToken.None);

            // Then it reports null — never throws, never silently succeeds against the wrong state.
            Assert.Null(collapseCount);

            var row = await ReadRowAsync(db, id);
            Assert.Equal(AnnouncementState.Pending, row.State);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioFindClaimedPastGraceReadsOnlyDueCandidates(DatabaseFixture db)
    {
        [Fact]
        public async Task AClaimedRowOlderThanTheThresholdIsReturned()
        {
            // Given a claimed announcement backdated well past a 6-minute grace...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);
            var id = await repo.InsertAsync(
                "Bins go out tonight", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: TimeSpan.FromHours(1), CancellationToken.None);
            var claimed = await repo.ClaimOldestAsync(1, DateTimeOffset.UtcNow, CancellationToken.None);
            Assert.Single(claimed);
            var now = DateTimeOffset.UtcNow;
            await SetClaimedAtAsync(db, id, now - TimeSpan.FromMinutes(10));

            // When the guardian sweep reads its own re-arm candidates...
            var candidates = await repo.FindClaimedPastGraceAsync(TimeSpan.FromMinutes(6), now, CancellationToken.None);

            // Then it is a candidate.
            Assert.Contains(id, candidates);
        }

        [Fact]
        public async Task AClaimedRowWithinTheThresholdIsNotReturned()
        {
            // Given a claimed announcement, claimed just now — well inside a 6-minute grace...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);
            await repo.InsertAsync(
                "Bins go out tonight", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: TimeSpan.FromHours(1), CancellationToken.None);
            await repo.ClaimOldestAsync(1, DateTimeOffset.UtcNow, CancellationToken.None);

            // When the guardian sweep reads its own re-arm candidates...
            var candidates = await repo.FindClaimedPastGraceAsync(
                TimeSpan.FromMinutes(6), DateTimeOffset.UtcNow, CancellationToken.None);

            // Then it is not a candidate yet.
            Assert.Empty(candidates);
        }

        [Fact]
        public async Task APendingRowNeverClaimedIsNeverReturnedRegardlessOfAge()
        {
            // Given an announcement that has never been claimed at all...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);
            await repo.InsertAsync(
                "Bins go out tonight", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: TimeSpan.FromHours(1), CancellationToken.None);

            // When the guardian sweep reads its own re-arm candidates, with a grace of zero (the
            // widest possible net)...
            var candidates = await repo.FindClaimedPastGraceAsync(
                TimeSpan.Zero, DateTimeOffset.UtcNow, CancellationToken.None);

            // Then a never-claimed row is never a re-arm candidate — only `claimed` rows are.
            Assert.Empty(candidates);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioDeclineAllLiveSweepsPendingAndClaimedTogether(DatabaseFixture db)
    {
        [Fact]
        public async Task APendingRowDeclinesWithTheGivenReason()
        {
            // Given a pending announcement live at the moment the station goes public...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);
            var id = await repo.InsertAsync(
                "Dinner's ready", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: null, CancellationToken.None);

            // When the flip sweeps...
            var declined = await repo.DeclineAllLiveAsync("station went public", CancellationToken.None);

            // Then it declines, reason stamped.
            Assert.Equal(1, declined);
            var row = await ReadRowAsync(db, id);
            Assert.Equal(AnnouncementState.Declined, row.State);
            Assert.Equal("station went public", row.DeclineReason);
        }

        [Fact]
        public async Task AClaimedRowDeclinesInTheSameSweepAsAPendingRow()
        {
            // Given one pending AND one claimed announcement, both live at the moment of the flip...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);
            var pendingId = await repo.InsertAsync(
                "Dinner's ready", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: null, CancellationToken.None);
            var toClaimId = await repo.InsertAsync(
                "Storm's coming, bring the washing in", verbatim: true, requestedVoice: null,
                source: AnnouncementSource.Token, ttl: null, CancellationToken.None);
            var claimed = await repo.ClaimOldestAsync(10, DateTimeOffset.UtcNow, CancellationToken.None);
            Assert.Contains(claimed, r => r.Id == toClaimId);

            // When the flip sweeps ONCE...
            var declined = await repo.DeclineAllLiveAsync("station went public", CancellationToken.None);

            // Then BOTH rows decline — the pending one and the claimed one, together.
            Assert.Equal(2, declined);
            Assert.Equal(AnnouncementState.Declined, (await ReadRowAsync(db, pendingId)).State);
            Assert.Equal(AnnouncementState.Declined, (await ReadRowAsync(db, toClaimId)).State);
        }

        [Fact]
        public async Task AnAlreadyAiredRowIsNeverTouchedByTheFlip()
        {
            // Given an announcement that has already aired before the flip...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);
            var airedId = await repo.InsertAsync(
                "Dinner's ready", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: null, CancellationToken.None);
            var claimed = await repo.ClaimOldestAsync(1, DateTimeOffset.UtcNow, CancellationToken.None);
            await repo.MarkAiredAsync(Assert.Single(claimed).Id, CancellationToken.None);

            // When the flip sweeps...
            var declined = await repo.DeclineAllLiveAsync("station went public", CancellationToken.None);

            // Then the already-aired row is untouched — declined counts zero, its own state unchanged.
            Assert.Equal(0, declined);
            Assert.Equal(AnnouncementState.Aired, (await ReadRowAsync(db, airedId)).State);
        }

        [Fact]
        public async Task NothingLiveMeansAHarmlessZeroCount()
        {
            // Given no announcements at all...
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);

            // When the flip sweeps anyway (a redundant re-write of SpectatorMode=true)...
            var declined = await repo.DeclineAllLiveAsync("station went public", CancellationToken.None);

            // Then it is a normal, silent zero — never an error.
            Assert.Equal(0, declined);
        }
    }

    // ---------------------------------------------------------------------
    // T344 review finding F1 — the wire state mapping: IAnnouncementStore.HistoryAsync's explicit
    // interface implementation (ToStateText's five-way switch, PLAN T344) was exercised by NOTHING
    // before this fact — every OTHER HistoryAsync-touching fact in this file drives the internal,
    // AnnouncementRow-returning overload instead, never the narrower Core-crossing seam the endpoint
    // actually calls. A mutation swapping any of ToStateText's five outputs for garbage must turn
    // this fact red (the second, cross-language net lives beside
    // FeatureAnnouncementStoreLifecycle, below, as FeatureAnnouncementStateWireParity).
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheExplicitHistoryAsyncMapsEveryStateToItsWireText(DatabaseFixture db)
    {
        [Fact]
        public async Task EachOfTheFiveLifecycleStatesReadsBackAsItsOwnLowercaseWireString()
        {
            // Given one announcement driven into EACH of the five lifecycle states. Claimed and aired
            // land FIRST, each claiming the only pending row at that moment — ClaimOldestAsync claims
            // the OLDEST deliverable rows across the whole table, so the still-pending row below must
            // land last or it would be claimed out from under this fact.
            await db.ResetAnnouncementAsync();
            var repo = Harness.AnnouncementRepo(db);

            var claimedId = await repo.InsertAsync(
                "Claimed one", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: TimeSpan.FromHours(1), CancellationToken.None);
            await repo.ClaimOldestAsync(1, DateTimeOffset.UtcNow, CancellationToken.None);

            await repo.InsertAsync(
                "Aired one", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: TimeSpan.FromHours(1), CancellationToken.None);
            var toAir = await repo.ClaimOldestAsync(1, DateTimeOffset.UtcNow, CancellationToken.None);
            var airedId = Assert.Single(toAir).Id;
            await repo.MarkAiredAsync(airedId, CancellationToken.None);

            var expiredId = await repo.InsertAsync(
                "Expired one", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: TimeSpan.FromSeconds(-1), CancellationToken.None);
            await repo.ExpireStaleAsync(DateTimeOffset.UtcNow, CancellationToken.None);

            var declinedId = await repo.InsertAsync(
                "Declined one", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: TimeSpan.FromHours(1), CancellationToken.None);
            await repo.MarkDeclinedAsync([declinedId], "station went public", CancellationToken.None);

            var pendingId = await repo.InsertAsync(
                "Pending one", verbatim: true, requestedVoice: null, source: AnnouncementSource.Token,
                ttl: TimeSpan.FromHours(1), CancellationToken.None);

            // When the endpoint-facing seam reads history — the EXPLICIT IAnnouncementStore.HistoryAsync
            // implementation, not the internal AnnouncementRow-returning overload every other fact in
            // this file drives...
            IAnnouncementStore store = repo;
            var history = await store.HistoryAsync(10, CancellationToken.None);

            // Then each row carries its OWN lowercase wire state string — the exact five ToStateText
            // produces, not a neighbor's.
            Assert.Equal("claimed", history.Single(e => e.Id == claimedId).State);
            Assert.Equal("aired", history.Single(e => e.Id == airedId).State);
            Assert.Equal("expired", history.Single(e => e.Id == expiredId).State);
            Assert.Equal("declined", history.Single(e => e.Id == declinedId).State);
            Assert.Equal("pending", history.Single(e => e.Id == pendingId).State);
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

// T344 review finding F1 (second net) — cross-language parity: AnnouncementRepository.ToStateText's
// five outputs must never drift from admin-ui/lib/announcements-api.ts's own AnnouncementState union
// literal (the wire type the admin page's state chips switch on) — the same
// FeaturePersonaSlugParity (Story192_PersonaSlugParity.cs)/Story337 icon-name-contract
// "repo-content-fact" idiom: string-parse the .ts source directly (no TS toolchain runs inside
// xUnit), assert the real C# mapping against the parsed set. ToStateText is `internal` (not
// `private`) for exactly this reason — see its own remarks in AnnouncementRepository.cs.
public static class FeatureAnnouncementStateWireParity
{
    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static string AnnouncementsApiTsPath =>
        Path.Combine(RepoRoot, "admin-ui", "lib", "announcements-api.ts");

    static readonly Regex UnionPattern = new(
        "export type AnnouncementState = ((?:\"[a-z]+\"(?: \\| )?)+);", RegexOptions.None);

    /// <summary>Extracts every quoted member of the <c>AnnouncementState</c> union literal.</summary>
    static IReadOnlyList<string> ParseTsAnnouncementStateUnion()
    {
        var text = File.ReadAllText(AnnouncementsApiTsPath);
        var match = UnionPattern.Match(text);
        Assert.True(match.Success, $"could not find the AnnouncementState union literal in {AnnouncementsApiTsPath}");

        var names = Regex.Matches(match.Groups[1].Value, "\"([a-z]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToList();
        Assert.True(names.Count > 0, $"parsed zero states out of {AnnouncementsApiTsPath}");
        return names;
    }

    public sealed class ScenarioToStateTextMatchesTheTsUnion
    {
        [Fact]
        public void ToStateTextsFiveOutputsMatchTheTsAnnouncementStateUnion()
        {
            // The C# switch and the TS union literal cannot drift (parity pin, the T68 golden-table
            // idiom) — every AnnouncementState member's own wire text, compared as a SET against
            // every state the TS union names.
            var tsStates = ParseTsAnnouncementStateUnion().OrderBy(s => s, StringComparer.Ordinal).ToList();
            var csStates = Enum.GetValues<AnnouncementState>()
                .Select(AnnouncementRepository.ToStateText)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

            Assert.Equal(tsStates, csStates);
        }
    }
}

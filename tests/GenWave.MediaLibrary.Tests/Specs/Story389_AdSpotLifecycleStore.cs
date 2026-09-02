// STORY-389 — A spot has a visible lifecycle (store half: AC1/AC6 · F159 · PLAN T398)
// The stock-keeping half (AC2–AC5) lives in GenWave.Ads.Tests/Specs/Story389_AdStockKeeping.cs.
//
// BDD specification — xUnit, REAL Postgres via DatabaseFixture (mirrors Story357_AnnouncementStore.cs's
// own fixture family: direct AdSpotRepository/AdBriefRepository construction over StationDataSource,
// an independent raw-SQL read for verifying writes rather than reading back through the repository
// under test where that matters). T398 lands AdSpotRepository/AdBriefRepository — the durable state
// machine beneath the writer (T400), the render task (T401), the worker (T402), and the API (T403),
// none of which exist yet; this file owns only the store. Every new SQL read here also gets its own
// live fact (the T362 loop law).

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureAdSpotLifecycleStore
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>A fully-populated <see cref="NewAdSpot"/> for a llm-sourced draft — every spec that
    /// doesn't care about a particular field overrides only the one it does.</summary>
    static NewAdSpot Draft(
        string brand = "Bramble & Fitch", string title = "Draft spot", AdSource source = AdSource.Llm,
        string? packSlug = null, int spotSeconds = 30) =>
        new(brand, title, Brief: "A cozy hardware shop", Script: null, source, packSlug, spotSeconds,
            VoicePlan: null, BedMediaId: null, InitialState: AdState.Draft, FailReason: null);

    /// <summary>An independent raw-SQL read (bypasses <see cref="AdSpotRepository"/> itself) so a
    /// fact verifies what the repository under test actually persisted — the same posture
    /// <c>Story357_AnnouncementStore.ReadRowAsync</c> takes.</summary>
    static async Task<(string State, string? FailReason, long? MediaId, DateTime StateChangedAt)> ReadRowAsync(
        DatabaseFixture db, long id)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<(string, string?, long?, DateTime)>(
            "select state::text, fail_reason, media_id, state_changed_at from station.ad_spot where id = @id",
            new { id });
    }

    static async Task<int> CountAllSpotRowsAsync(DatabaseFixture db)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<int>("select count(*)::int from station.ad_spot");
    }

    static async Task<int> CountAllBriefRowsAsync(DatabaseFixture db)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<int>("select count(*)::int from station.ad_brief");
    }

    /// <summary>Backdates a row's own <c>state_changed_at</c> directly — the ONLY way a test can put
    /// a <see cref="AdState.Ready"/> row genuinely past a refresh age without an actual wall-clock
    /// wait (mirrors <c>Story357_AnnouncementStore.SetClaimedAtAsync</c>'s own posture).</summary>
    static async Task SetStateChangedAtAsync(DatabaseFixture db, long id, DateTime stateChangedAt)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "update station.ad_spot set state_changed_at = @StateChangedAt where id = @Id",
            new { Id = id, StateChangedAt = stateChangedAt });
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — CreateAsync lands in the requested initial state
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioCreateAsyncLandsInTheRequestedInitialState(DatabaseFixture db)
    {
        [Fact]
        public async Task ACreatedDraftSpotLandsInDraft()
        {
            // Given no prior spots...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);

            // When one is created with InitialState = Draft (the default, un-auto-approved path)...
            var spot = await repo.CreateAsync(Draft(), CancellationToken.None);

            // Then it lands Draft.
            Assert.Equal(AdState.Draft, spot.State);
        }

        [Fact]
        public async Task ACreatedApprovedSpotLandsInApproved()
        {
            // Given no prior spots...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);

            // When one is created with InitialState = Approved (Station:Ads:AutoApprove's own path,
            // PLAN T400)...
            var spot = await repo.CreateAsync(
                Draft() with { InitialState = AdState.Approved }, CancellationToken.None);

            // Then it lands Approved directly — no separate approve round trip needed.
            Assert.Equal(AdState.Approved, spot.State);
        }

        [Fact]
        public async Task ACreatedFailedSpotCarriesItsFailReason()
        {
            // Given no prior spots...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);

            // When one is created already Failed — STORY-390 AC3's own outcome, a script that never
            // passed validation after its one re-ask...
            var spot = await repo.CreateAsync(
                Draft() with { InitialState = AdState.Failed, FailReason = "brand_collision" },
                CancellationToken.None);

            // Then it lands Failed, with the violated rule's own id.
            Assert.Equal(AdState.Failed, spot.State);
            Assert.Equal("brand_collision", spot.FailReason);
        }

        [Fact]
        public async Task ACreatedSpotStampsStateChangedAtOnCreation()
        {
            // Given no prior spots...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);

            // When one is created...
            var spot = await repo.CreateAsync(Draft(), CancellationToken.None);

            // Then its initial state_changed_at is stamped, not left null/default.
            Assert.True(spot.StateChangedAt > default(DateTime));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — CreateAsync's own guard clauses
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioCreateAsyncRejectsIllegalInitialStates(DatabaseFixture db)
    {
        [Fact]
        public async Task CreatingDirectlyIntoReadyIsRejected()
        {
            // Given no prior spots...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);

            // When a caller attempts to create a spot already Ready — reachable only via a
            // transition on this store, never at birth...
            var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                repo.CreateAsync(Draft() with { InitialState = AdState.Ready }, CancellationToken.None));

            // Then it is refused before ever reaching Postgres.
            Assert.Contains("Draft, Approved, or Failed", ex.Message);
        }

        [Fact]
        public async Task CreatingFailedWithNoFailReasonIsRejected()
        {
            // Given no prior spots...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);

            // When a caller attempts to create a Failed spot with no reason...
            // Then it is refused — "fail_reason iff Failed" enforced in C#, ahead of db/43's own CHECK.
            await Assert.ThrowsAsync<ArgumentException>(() =>
                repo.CreateAsync(Draft() with { InitialState = AdState.Failed, FailReason = null }, CancellationToken.None));
        }

        [Fact]
        public async Task CreatingADraftSpotCarryingAFailReasonIsRejected()
        {
            // Given no prior spots...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);

            // When a caller attempts to create a Draft spot that ALSO carries a fail reason...
            // Then it is refused — the other half of the "iff" guard.
            await Assert.ThrowsAsync<ArgumentException>(() =>
                repo.CreateAsync(Draft() with { FailReason = "should never be set" }, CancellationToken.None));
        }
    }

    // ---------------------------------------------------------------------
    // AC1 — every legal transition stamps state_changed_at
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioEveryLegalTransitionStampsStateChangedAt(DatabaseFixture db)
    {
        [Fact]
        public async Task DraftToApprovedStampsStateChangedAt()
        {
            // Given a draft spot...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(), CancellationToken.None);
            await Task.Delay(TimeSpan.FromMilliseconds(20));

            // When it is approved...
            var outcome = await repo.ApproveAsync(spot.Id, spot.Version, CancellationToken.None);

            // Then the transition applied and state_changed_at moved forward.
            Assert.Equal(AdSpotWriteResult.Updated, outcome.Result);
            Assert.Equal(AdState.Approved, outcome.Spot!.State);
            Assert.True(outcome.Spot.StateChangedAt > spot.StateChangedAt);
        }

        [Fact]
        public async Task ApprovedToRenderingStampsStateChangedAt()
        {
            // Given an approved spot...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft() with { InitialState = AdState.Approved }, CancellationToken.None);
            await Task.Delay(TimeSpan.FromMilliseconds(20));

            // When the worker claims it...
            var claimed = await repo.ClaimNextApprovedAsync(CancellationToken.None);

            // Then it moved to Rendering and state_changed_at moved forward.
            Assert.NotNull(claimed);
            Assert.Equal(spot.Id, claimed!.Id);
            Assert.Equal(AdState.Rendering, claimed.State);
            Assert.True(claimed.StateChangedAt > spot.StateChangedAt);
        }

        [Fact]
        public async Task RenderingToReadyStampsStateChangedAt()
        {
            // Given a spot claimed into Rendering...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft() with { InitialState = AdState.Approved }, CancellationToken.None);
            var claimed = (await repo.ClaimNextApprovedAsync(CancellationToken.None))!;
            await Task.Delay(TimeSpan.FromMilliseconds(20));

            // When the render completes...
            var ok = await repo.MarkReadyAsync(claimed.Id, mediaId: 4242, CancellationToken.None);

            // Then it moved to Ready and state_changed_at moved forward.
            Assert.True(ok);
            var row = await ReadRowAsync(db, claimed.Id);
            Assert.Equal("ready", row.State);
            Assert.True(row.StateChangedAt > claimed.StateChangedAt);
        }

        [Fact]
        public async Task RenderingToFailedStampsStateChangedAt()
        {
            // Given a spot claimed into Rendering...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            await repo.CreateAsync(Draft() with { InitialState = AdState.Approved }, CancellationToken.None);
            var claimed = (await repo.ClaimNextApprovedAsync(CancellationToken.None))!;
            await Task.Delay(TimeSpan.FromMilliseconds(20));

            // When the render fails...
            var ok = await repo.MarkFailedAsync(claimed.Id, "tts_timeout", CancellationToken.None);

            // Then it moved to Failed, reason stamped, state_changed_at moved forward.
            Assert.True(ok);
            var row = await ReadRowAsync(db, claimed.Id);
            Assert.Equal("failed", row.State);
            Assert.Equal("tts_timeout", row.FailReason);
            Assert.True(row.StateChangedAt > claimed.StateChangedAt);
        }

        [Fact]
        public async Task FailedToApprovedRetryStampsStateChangedAt()
        {
            // Given a failed spot...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(
                Draft() with { InitialState = AdState.Failed, FailReason = "brand_collision" },
                CancellationToken.None);
            await Task.Delay(TimeSpan.FromMilliseconds(20));

            // When the operator retries it...
            var outcome = await repo.RetryAsync(spot.Id, spot.Version, CancellationToken.None);

            // Then it moved back to Approved, its old fail_reason cleared (direct pin — not
            // transitive through the CHECK constraint fact), and state_changed_at moved forward.
            Assert.Equal(AdSpotWriteResult.Updated, outcome.Result);
            Assert.Equal(AdState.Approved, outcome.Spot!.State);
            Assert.Null(outcome.Spot.FailReason);
            Assert.True(outcome.Spot.StateChangedAt > spot.StateChangedAt);
        }

        [Fact]
        public async Task ReadyToRetiredStampsStateChangedAtAndRetiredAt()
        {
            // Given a ready spot...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            await repo.CreateAsync(Draft() with { InitialState = AdState.Approved }, CancellationToken.None);
            var claimed = (await repo.ClaimNextApprovedAsync(CancellationToken.None))!;
            await repo.MarkReadyAsync(claimed.Id, mediaId: 99, CancellationToken.None);
            var ready = (await repo.ListByStateAsync(AdState.Ready, 10, 0, CancellationToken.None)).Items.Single();
            await Task.Delay(TimeSpan.FromMilliseconds(20));

            // When it is retired (refresh, or operator)...
            var outcome = await repo.RetireAsync(ready.Id, ready.Version, CancellationToken.None);

            // Then it moved to Retired, retired_at stamped, state_changed_at moved forward.
            Assert.Equal(AdSpotWriteResult.Updated, outcome.Result);
            Assert.Equal(AdState.Retired, outcome.Spot!.State);
            Assert.NotNull(outcome.Spot.RetiredAt);
            Assert.True(outcome.Spot.StateChangedAt > ready.StateChangedAt);
        }

        [Fact]
        public async Task DraftToRetiredStampsStateChangedAt()
        {
            // Given a draft spot (an operator discard, never rendered)...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(), CancellationToken.None);
            await Task.Delay(TimeSpan.FromMilliseconds(20));

            // When it is retired...
            var outcome = await repo.RetireAsync(spot.Id, spot.Version, CancellationToken.None);

            // Then it moved to Retired directly from Draft.
            Assert.Equal(AdSpotWriteResult.Updated, outcome.Result);
            Assert.Equal(AdState.Retired, outcome.Spot!.State);
        }

        [Fact]
        public async Task ApprovedToRetiredStampsStateChangedAt()
        {
            // Given an approved spot (PLAN T403's own discard-gap ruling: an operator changing their
            // mind before it ever renders)...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft() with { InitialState = AdState.Approved }, CancellationToken.None);
            await Task.Delay(TimeSpan.FromMilliseconds(20));

            // When it is retired...
            var outcome = await repo.RetireAsync(spot.Id, spot.Version, CancellationToken.None);

            // Then it moved to Retired directly from Approved, state_changed_at moved forward.
            Assert.Equal(AdSpotWriteResult.Updated, outcome.Result);
            Assert.Equal(AdState.Retired, outcome.Spot!.State);
            Assert.True(outcome.Spot.StateChangedAt > spot.StateChangedAt);
        }

        [Fact]
        public async Task FailedToRetiredStampsStateChangedAt()
        {
            // Given a failed spot (PLAN T403's own discard-gap ruling: a permanently-failing spot
            // needs an exit)...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(
                Draft() with { InitialState = AdState.Failed, FailReason = "brand_collision" },
                CancellationToken.None);
            await Task.Delay(TimeSpan.FromMilliseconds(20));

            // When it is retired...
            var outcome = await repo.RetireAsync(spot.Id, spot.Version, CancellationToken.None);

            // Then it moved to Retired directly from Failed, state_changed_at moved forward, and
            // fail_reason is cleared (db/43's own ad_spot_fail_reason_iff_failed CHECK demands it —
            // a Failed row's own non-null reason would otherwise violate the CHECK the instant state
            // is no longer Failed; RetireAsync's own remarks).
            Assert.Equal(AdSpotWriteResult.Updated, outcome.Result);
            Assert.Equal(AdState.Retired, outcome.Spot!.State);
            Assert.True(outcome.Spot.StateChangedAt > spot.StateChangedAt);
            Assert.Null(outcome.Spot.FailReason);
        }
    }

    // ---------------------------------------------------------------------
    // AC1 — ready requires media_id (the C# half: MarkReadyAsync's own non-nullable parameter)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioReadyAlwaysCarriesAMediaId(DatabaseFixture db)
    {
        [Fact]
        public async Task MarkReadyAsyncPersistsTheGivenMediaId()
        {
            // Given a spot claimed into Rendering...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            await repo.CreateAsync(Draft() with { InitialState = AdState.Approved }, CancellationToken.None);
            var claimed = (await repo.ClaimNextApprovedAsync(CancellationToken.None))!;

            // When it is marked ready with a media id...
            await repo.MarkReadyAsync(claimed.Id, mediaId: 777, CancellationToken.None);

            // Then the row carries that exact media id — MarkReadyAsync's own `long mediaId`
            // parameter (never nullable) makes the illegal "ready with no media_id" call impossible
            // to even write; there is no code path here to call with a null id.
            var row = await ReadRowAsync(db, claimed.Id);
            Assert.Equal(777, row.MediaId);
        }

        [Fact]
        public async Task MarkReadyAsyncAgainstANonRenderingRowIsRefusedAndTheRowUnchanged()
        {
            // Given a draft spot — never claimed into Rendering...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(), CancellationToken.None);

            // When the render seam is called against it anyway (a stale/duplicate signal)...
            var ok = await repo.MarkReadyAsync(spot.Id, mediaId: 1, CancellationToken.None);

            // Then it is refused — total, never throws — and the row is left exactly as it was.
            Assert.False(ok);
            var row = await ReadRowAsync(db, spot.Id);
            Assert.Equal("draft", row.State);
            Assert.Null(row.MediaId);
        }
    }

    // ---------------------------------------------------------------------
    // db/43's own CHECK constraints — enforced at the DB even bypassing the store entirely
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheDbChecksRefuseIllegalRowsEvenBypassingTheStore(DatabaseFixture db)
    {
        [Fact]
        public async Task ARawInsertOfAReadyRowWithNoMediaIdViolatesTheCheck()
        {
            // Given a direct connection to the database — no AdSpotRepository involved at all...
            await db.ResetAdsAsync();
            await using var conn = await db.StationDataSource.OpenConnectionAsync();

            // When a raw INSERT attempts state = 'ready' with media_id left NULL...
            var ex = await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
                """
                insert into station.ad_spot (brand, title, source, state)
                values ('Brand', 'Title', 'llm'::station.ad_source, 'ready'::station.ad_state)
                """));

            // Then Postgres itself refuses it — ad_spot_ready_requires_media_id (db/43).
            Assert.Equal("23514", ex.SqlState);
        }

        [Fact]
        public async Task ARawInsertOfAFailedRowWithNoFailReasonViolatesTheCheck()
        {
            // Given a direct connection to the database...
            await db.ResetAdsAsync();
            await using var conn = await db.StationDataSource.OpenConnectionAsync();

            // When a raw INSERT attempts state = 'failed' with fail_reason left NULL...
            var ex = await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
                """
                insert into station.ad_spot (brand, title, source, state)
                values ('Brand', 'Title', 'llm'::station.ad_source, 'failed'::station.ad_state)
                """));

            // Then Postgres itself refuses it — ad_spot_fail_reason_iff_failed (db/43).
            Assert.Equal("23514", ex.SqlState);
        }

        [Fact]
        public async Task ARawInsertOfADraftRowCarryingAFailReasonViolatesTheCheck()
        {
            // Given a direct connection to the database...
            await db.ResetAdsAsync();
            await using var conn = await db.StationDataSource.OpenConnectionAsync();

            // When a raw INSERT attempts state = 'draft' but ALSO sets fail_reason...
            var ex = await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
                """
                insert into station.ad_spot (brand, title, source, state, fail_reason)
                values ('Brand', 'Title', 'llm'::station.ad_source, 'draft'::station.ad_state, 'nope')
                """));

            // Then Postgres itself refuses it too — the "iff" runs both directions.
            Assert.Equal("23514", ex.SqlState);
        }
    }

    // ---------------------------------------------------------------------
    // AC1 — the brief upsert, keyed both shapes, incl. the ratified owner-brand cap
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheBriefUpsertIsKeyedOnPackSlugAndBrand(DatabaseFixture db)
    {
        [Fact]
        public async Task AFirstUpsertLandsOneRow()
        {
            // Given no prior briefs...
            await db.ResetAdsAsync();
            var repo = Harness.AdBriefRepo(db);

            // When one owner-authored brief is upserted...
            await repo.UpsertAsync(
                packSlug: null, brand: "Bramble & Fitch", premise: "A cozy hardware shop",
                tone: "warm", structure: null, enabled: true, CancellationToken.None);

            // Then exactly one row exists.
            Assert.Equal(1, await CountAllBriefRowsAsync(db));
        }

        [Fact]
        public async Task AnUpsertWithTheSamePackSlugAndBrandUpdatesInPlace()
        {
            // Given a pack-installed brief...
            await db.ResetAdsAsync();
            var repo = Harness.AdBriefRepo(db);
            var first = await repo.UpsertAsync(
                packSlug: "genwave-catalog", brand: "Bramble & Fitch", premise: "Old premise",
                tone: "warm", structure: null, enabled: true, CancellationToken.None);

            // When the SAME pack re-installs it with a revised premise...
            var second = await repo.UpsertAsync(
                packSlug: "genwave-catalog", brand: "Bramble & Fitch", premise: "New premise",
                tone: "warm", structure: null, enabled: true, CancellationToken.None);

            // Then it updated the SAME row in place — same id, count stays 1, premise moved.
            Assert.Equal(first.Id, second.Id);
            Assert.Equal("New premise", second.Premise);
            Assert.Equal(1, await CountAllBriefRowsAsync(db));
        }

        [Fact]
        public async Task TwoOwnerAuthoredUpsertsForTheSameBrandCollapseToOneRow()
        {
            // Given no prior briefs — RATIFIED 2026-09-02: one owner brief per brand.
            await db.ResetAdsAsync();
            var repo = Harness.AdBriefRepo(db);
            var first = await repo.UpsertAsync(
                packSlug: null, brand: "Bramble & Fitch", premise: "First premise",
                tone: "warm", structure: null, enabled: true, CancellationToken.None);

            // When the owner re-authors the SAME brand's brief — a SECOND call, also NULL pack_slug...
            var second = await repo.UpsertAsync(
                packSlug: null, brand: "Bramble & Fitch", premise: "Second premise",
                tone: "dry", structure: null, enabled: true, CancellationToken.None);

            // Then it updated the SAME row — never a second one. A brand is a brand.
            Assert.Equal(first.Id, second.Id);
            Assert.Equal("Second premise", second.Premise);
            Assert.Equal(1, await CountAllBriefRowsAsync(db));
        }

        [Fact]
        public async Task AnOwnerBriefAndAPackBriefForTheSameBrandAreTwoSeparateRows()
        {
            // Given an owner-authored brief for a brand...
            await db.ResetAdsAsync();
            var repo = Harness.AdBriefRepo(db);
            await repo.UpsertAsync(
                packSlug: null, brand: "Bramble & Fitch", premise: "Owner's own premise",
                tone: "warm", structure: null, enabled: true, CancellationToken.None);

            // When a PACK installs a brief for the SAME brand name (a different pack_slug half of
            // the key)...
            await repo.UpsertAsync(
                packSlug: "genwave-catalog", brand: "Bramble & Fitch", premise: "Pack's own premise",
                tone: "dry", structure: null, enabled: true, CancellationToken.None);

            // Then they are two distinct rows — the cap is scoped to (pack_slug, brand), not brand
            // alone.
            Assert.Equal(2, await CountAllBriefRowsAsync(db));
        }

        [Fact]
        public async Task AnUpdatingUpsertLeavesCreatedAtUntouched()
        {
            // Given an existing brief...
            await db.ResetAdsAsync();
            var repo = Harness.AdBriefRepo(db);
            var first = await repo.UpsertAsync(
                packSlug: null, brand: "Bramble & Fitch", premise: "First premise",
                tone: "warm", structure: null, enabled: true, CancellationToken.None);
            await Task.Delay(TimeSpan.FromMilliseconds(20));

            // When it is upserted again...
            var second = await repo.UpsertAsync(
                packSlug: null, brand: "Bramble & Fitch", premise: "Second premise",
                tone: "warm", structure: null, enabled: true, CancellationToken.None);

            // Then created_at is untouched by the update half.
            Assert.Equal(first.CreatedAt, second.CreatedAt);
        }
    }

    // ---------------------------------------------------------------------
    // AC6 (sad path) — illegal moves are refused, the row unchanged
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioIllegalMovesAreRefused(DatabaseFixture db)
    {
        [Fact]
        public async Task ApprovingAnAlreadyRetiredSpotIsRefused()
        {
            // Given a retired spot...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(), CancellationToken.None);
            var retired = (await repo.RetireAsync(spot.Id, spot.Version, CancellationToken.None)).Spot!;

            // When an approve is attempted against it anyway...
            var outcome = await repo.ApproveAsync(retired.Id, retired.Version, CancellationToken.None);

            // Then it is refused (Conflict — the row exists but isn't Draft) and the row is left
            // exactly as it was.
            Assert.Equal(AdSpotWriteResult.Conflict, outcome.Result);
            Assert.Null(outcome.Spot);
            var row = await ReadRowAsync(db, spot.Id);
            Assert.Equal("retired", row.State);
        }

        [Fact]
        public async Task RetryingADraftSpotIsRefused()
        {
            // Given a draft spot — never failed...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(), CancellationToken.None);

            // When a retry (Failed -> Approved) is attempted against it...
            var outcome = await repo.RetryAsync(spot.Id, spot.Version, CancellationToken.None);

            // Then it is refused — Draft is not a legal FROM state for a retry.
            Assert.Equal(AdSpotWriteResult.Conflict, outcome.Result);
            var row = await ReadRowAsync(db, spot.Id);
            Assert.Equal("draft", row.State);
        }

        [Fact]
        public async Task AStaleVersionIsRefusedAsAConflict()
        {
            // Given a draft spot, approved once (its own version now stale)...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(), CancellationToken.None);
            await repo.ApproveAsync(spot.Id, spot.Version, CancellationToken.None);

            // When a SECOND approve is attempted with the ORIGINAL (now stale) version...
            var outcome = await repo.ApproveAsync(spot.Id, spot.Version, CancellationToken.None);

            // Then it is refused as a Conflict — the caller's view of the row is out of date.
            Assert.Equal(AdSpotWriteResult.Conflict, outcome.Result);
        }

        [Fact]
        public async Task ApprovingAnUnknownIdReturnsNotFound()
        {
            // Given no spot with this id...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);

            // When an approve is attempted against it...
            var outcome = await repo.ApproveAsync(999_999, "1", CancellationToken.None);

            // Then it reports NotFound, distinctly from Conflict (IDOR-safe: existence is checked
            // first).
            Assert.Equal(AdSpotWriteResult.NotFound, outcome.Result);
        }

        [Fact]
        public async Task RetiringARenderingSpotIsRefused()
        {
            // Given a spot claimed into Rendering (PLAN T403's own discard-gap ruling: Rendering
            // stays undiscardable — it is transient by construction, the guardian re-arms it to
            // Approved within one grace, and the discard happens from there)...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            await repo.CreateAsync(Draft() with { InitialState = AdState.Approved }, CancellationToken.None);
            var claimed = (await repo.ClaimNextApprovedAsync(CancellationToken.None))!;

            // When a retire is attempted against it anyway...
            var outcome = await repo.RetireAsync(claimed.Id, claimed.Version, CancellationToken.None);

            // Then it is refused (Conflict) and the row is left exactly as it was.
            Assert.Equal(AdSpotWriteResult.Conflict, outcome.Result);
            var row = await ReadRowAsync(db, claimed.Id);
            Assert.Equal("rendering", row.State);
        }
    }

    // ---------------------------------------------------------------------
    // AC6 — nothing is ever system-deleted
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioNothingIsEverSystemDeleted(DatabaseFixture db)
    {
        [Fact]
        public async Task EveryRowDrivenThroughEveryTransitionThisStoreOffersStillExists()
        {
            // Given four spots driven through every transition this store offers...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);

            await repo.CreateAsync(Draft() with { InitialState = AdState.Approved }, CancellationToken.None);
            var claimedForReady = (await repo.ClaimNextApprovedAsync(CancellationToken.None))!;
            await repo.MarkReadyAsync(claimedForReady.Id, mediaId: 1, CancellationToken.None);

            await repo.CreateAsync(Draft() with { InitialState = AdState.Approved }, CancellationToken.None);
            var claimedForFailure = (await repo.ClaimNextApprovedAsync(CancellationToken.None))!;
            await repo.MarkFailedAsync(claimedForFailure.Id, "tts_timeout", CancellationToken.None);

            var retiredDraft = await repo.CreateAsync(Draft(), CancellationToken.None);
            await repo.RetireAsync(retiredDraft.Id, retiredDraft.Version, CancellationToken.None);

            await repo.CreateAsync(Draft(), CancellationToken.None);

            // When every row's own outcome is inspected together...
            var totalRows = await CountAllSpotRowsAsync(db);

            // Then all four still exist — ready, failed, retired, and still-draft alike.
            Assert.Equal(4, totalRows);
        }
    }

    // ---------------------------------------------------------------------
    // T362 loop law — ListByStateAsync's own live facts (T403's state-scoped paging)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioListByStateAsyncPagesWithAnExactTotal(DatabaseFixture db)
    {
        [Fact]
        public async Task AStateScopedListReturnsOnlyMatchingRows()
        {
            // Given one draft and one approved spot...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            var draft = await repo.CreateAsync(Draft(), CancellationToken.None);
            await repo.CreateAsync(Draft() with { InitialState = AdState.Approved }, CancellationToken.None);

            // When the page is scoped to Draft...
            var page = await repo.ListByStateAsync(AdState.Draft, 10, 0, CancellationToken.None);

            // Then only the draft row comes back.
            Assert.Equal([draft.Id], page.Items.Select(s => s.Id));
        }

        [Fact]
        public async Task TheTotalIsExactAcrossAPartialPage()
        {
            // Given three draft spots...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            for (var i = 0; i < 3; i++) await repo.CreateAsync(Draft(), CancellationToken.None);

            // When a page of 2 is requested...
            var page = await repo.ListByStateAsync(AdState.Draft, limit: 2, offset: 0, CancellationToken.None);

            // Then the total is the exact matching count, not the page's own row count.
            Assert.Equal(2, page.Items.Count);
            Assert.Equal(3, page.Total);
        }

        [Fact]
        public async Task AnOffsetPastTheLastRowStillCarriesTheTrueTotal()
        {
            // Given two draft spots...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            await repo.CreateAsync(Draft(), CancellationToken.None);
            await repo.CreateAsync(Draft(), CancellationToken.None);

            // When a page starts past the last row...
            var page = await repo.ListByStateAsync(AdState.Draft, limit: 10, offset: 50, CancellationToken.None);

            // Then the page is empty but the total is still exact — never derived from Items' count.
            Assert.Empty(page.Items);
            Assert.Equal(2, page.Total);
        }

        [Fact]
        public async Task ANullStateListsEveryRowRegardlessOfState()
        {
            // Given a draft and a retired spot...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            var draft = await repo.CreateAsync(Draft(), CancellationToken.None);
            var toRetire = await repo.CreateAsync(Draft(), CancellationToken.None);
            await repo.RetireAsync(toRetire.Id, toRetire.Version, CancellationToken.None);

            // When the list is unscoped (state = null)...
            var page = await repo.ListByStateAsync(null, 10, 0, CancellationToken.None);

            // Then both rows come back, any state.
            Assert.Equal(2, page.Total);
            Assert.Contains(page.Items, s => s.Id == draft.Id);
            Assert.Contains(page.Items, s => s.Id == toRetire.Id);
        }

        [Fact]
        public async Task ResultsOrderNewestTransitionedFirst()
        {
            // Given two draft spots, the second created after the first...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            var older = await repo.CreateAsync(Draft(), CancellationToken.None);
            await Task.Delay(TimeSpan.FromMilliseconds(20));
            var newer = await repo.CreateAsync(Draft(), CancellationToken.None);

            // When the page is read...
            var page = await repo.ListByStateAsync(AdState.Draft, 10, 0, CancellationToken.None);

            // Then the newest-transitioned row leads.
            Assert.Equal(newer.Id, page.Items[0].Id);
            Assert.Equal(older.Id, page.Items[1].Id);
        }
    }

    // ---------------------------------------------------------------------
    // T362 loop law — ClaimNextApprovedAsync's own live facts (T402's worker claim)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioClaimNextApprovedAsyncClaimsTheOldest(DatabaseFixture db)
    {
        [Fact]
        public async Task ClaimingWithNothingApprovedReturnsNull()
        {
            // Given only a draft spot — nothing approved...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            await repo.CreateAsync(Draft(), CancellationToken.None);

            // When the worker claims...
            var claimed = await repo.ClaimNextApprovedAsync(CancellationToken.None);

            // Then nothing comes back — a legal answer, never an error.
            Assert.Null(claimed);
        }

        [Fact]
        public async Task ClaimingReturnsTheOldestApprovedSpotFirst()
        {
            // Given two approved spots, the first approved well before the second...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            var first = await repo.CreateAsync(Draft() with { InitialState = AdState.Approved }, CancellationToken.None);
            await repo.CreateAsync(Draft() with { InitialState = AdState.Approved }, CancellationToken.None);
            await SetStateChangedAtAsync(db, first.Id, DateTime.UtcNow.AddMinutes(-10));

            // When the worker claims once...
            var claimed = await repo.ClaimNextApprovedAsync(CancellationToken.None);

            // Then it claims the older one, not the newer.
            Assert.Equal(first.Id, claimed!.Id);
        }

        [Fact]
        public async Task ClaimingTwiceInARowClaimsTwoDifferentSpots()
        {
            // Given two approved spots...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            var first = await repo.CreateAsync(Draft() with { InitialState = AdState.Approved }, CancellationToken.None);
            var second = await repo.CreateAsync(Draft() with { InitialState = AdState.Approved }, CancellationToken.None);
            await SetStateChangedAtAsync(db, first.Id, DateTime.UtcNow.AddMinutes(-10));

            // When the worker claims twice, back to back...
            var claimedFirst = await repo.ClaimNextApprovedAsync(CancellationToken.None);
            var claimedSecond = await repo.ClaimNextApprovedAsync(CancellationToken.None);

            // Then each call claimed a DIFFERENT row — never the same spot twice.
            Assert.Equal(first.Id, claimedFirst!.Id);
            Assert.Equal(second.Id, claimedSecond!.Id);
        }
    }

    // ---------------------------------------------------------------------
    // T362 loop law — the stock pass's own live facts (T402's counts/refresh candidates)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioStockCountsAndReadyByAge(DatabaseFixture db)
    {
        async Task<long> MakeReadySpotAsync(DatabaseFixture fixture, AdSpotRepository repo, AdSource source)
        {
            var spot = await repo.CreateAsync(
                Draft(source: source, packSlug: source == AdSource.Pack ? "genwave-catalog" : null)
                    with
                { InitialState = AdState.Approved },
                CancellationToken.None);
            var claimed = (await repo.ClaimNextApprovedAsync(CancellationToken.None))!;
            await repo.MarkReadyAsync(claimed.Id, mediaId: 1, CancellationToken.None);
            return claimed.Id;
        }

        [Fact]
        public async Task CountReadyGeneratedAsyncCountsLlmAndPackButNotOwner()
        {
            // Given one ready llm spot, one ready pack spot, and one ready OWNER spot...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            await MakeReadySpotAsync(db, repo, AdSource.Llm);
            await MakeReadySpotAsync(db, repo, AdSource.Pack);
            await MakeReadySpotAsync(db, repo, AdSource.Owner);

            // When the stock count is read...
            var count = await repo.CountReadyGeneratedAsync(CancellationToken.None);

            // Then only the llm + pack spots count — owner is excluded (SPEC F159.3).
            Assert.Equal(2, count);
        }

        [Fact]
        public async Task ListReadyOlderThanAsyncExcludesOwnerSpotsRegardlessOfAge()
        {
            // Given one ready owner spot, backdated well past any refresh age...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            var ownerId = await MakeReadySpotAsync(db, repo, AdSource.Owner);
            await SetStateChangedAtAsync(db, ownerId, DateTime.UtcNow.AddDays(-365));

            // When the refresh candidates are read for a 30-day age...
            var candidates = await repo.ListReadyOlderThanAsync(TimeSpan.FromDays(30), CancellationToken.None);

            // Then the owner spot is never a candidate — exempt outright (SPEC F159.3).
            Assert.DoesNotContain(candidates, s => s.Id == ownerId);
        }

        [Fact]
        public async Task ListReadyOlderThanAsyncOnlyReturnsSpotsOlderThanTheGivenAge()
        {
            // Given one llm spot fresh, and one llm spot backdated past a 30-day age...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            var freshId = await MakeReadySpotAsync(db, repo, AdSource.Llm);
            var staleId = await MakeReadySpotAsync(db, repo, AdSource.Llm);
            await SetStateChangedAtAsync(db, staleId, DateTime.UtcNow.AddDays(-31));

            // When the refresh candidates are read for a 30-day age...
            var candidates = await repo.ListReadyOlderThanAsync(TimeSpan.FromDays(30), CancellationToken.None);

            // Then only the stale spot is a candidate.
            Assert.Contains(candidates, s => s.Id == staleId);
            Assert.DoesNotContain(candidates, s => s.Id == freshId);
        }
    }

    // ---------------------------------------------------------------------
    // T362 loop law — GetByIdAsync's own live facts (T403's GET /api/ads/{id})
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioGetByIdAsyncReadsAnyRowRegardlessOfState(DatabaseFixture db)
    {
        [Fact]
        public async Task AnExistingRowIsReturned()
        {
            // Given a draft spot...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(), CancellationToken.None);

            // When it is read back by id...
            var found = await repo.GetByIdAsync(spot.Id, CancellationToken.None);

            // Then the exact row comes back.
            Assert.NotNull(found);
            Assert.Equal(spot.Id, found!.Id);
            Assert.Equal(spot.Brand, found.Brand);
        }

        [Fact]
        public async Task AnUnknownIdReturnsNull()
        {
            // Given no spot with this id...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);

            // When it is read back by id...
            var found = await repo.GetByIdAsync(999_999, CancellationToken.None);

            // Then nothing comes back — a legal answer, never an error.
            Assert.Null(found);
        }
    }

    // ---------------------------------------------------------------------
    // T362 loop law — UpdateAsync's own live facts (T403's owner editor PATCH)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioUpdateAsyncEditsDraftAndFailedOnly(DatabaseFixture db)
    {
        static AdSpotEdit Edit(
            string? brand = null, string? title = null, string? brief = null, string? script = null,
            string? voicePlan = null, int? spotSeconds = null, long? bedMediaId = null) =>
            new(brand, title, brief, script, voicePlan, spotSeconds, bedMediaId);

        [Fact]
        public async Task EditingADraftSpotUpdatesTheGivenFields()
        {
            // Given a draft spot...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(), CancellationToken.None);

            // When brand and title are edited...
            var outcome = await repo.UpdateAsync(
                spot.Id, Edit(brand: "New Brand", title: "New Title"), spot.Version, CancellationToken.None);

            // Then the given fields moved and the state stayed Draft (a content edit, not a
            // transition).
            Assert.Equal(AdSpotWriteResult.Updated, outcome.Result);
            Assert.Equal("New Brand", outcome.Spot!.Brand);
            Assert.Equal("New Title", outcome.Spot.Title);
            Assert.Equal(AdState.Draft, outcome.Spot.State);
        }

        [Fact]
        public async Task FieldsLeftNullAreUnchanged()
        {
            // Given a draft spot with a known brief...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(brand: "Original Brand"), CancellationToken.None);

            // When only the title is edited...
            var outcome = await repo.UpdateAsync(
                spot.Id, Edit(title: "New Title"), spot.Version, CancellationToken.None);

            // Then brand/brief are untouched.
            Assert.Equal(AdSpotWriteResult.Updated, outcome.Result);
            Assert.Equal("Original Brand", outcome.Spot!.Brand);
            Assert.Equal(spot.Brief, outcome.Spot.Brief);
        }

        [Fact]
        public async Task EditingAFailedSpotSucceeds()
        {
            // Given a failed spot (the "fix the script before retry" path)...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(
                Draft() with { InitialState = AdState.Failed, FailReason = "brand_collision" },
                CancellationToken.None);

            // When its script is edited...
            var outcome = await repo.UpdateAsync(
                spot.Id, Edit(script: "ANNOUNCER: A brand new, honest line.\nVOICE1: Call today."),
                spot.Version, CancellationToken.None);

            // Then it succeeds, script moved, state stays Failed (edit ≠ retry).
            Assert.Equal(AdSpotWriteResult.Updated, outcome.Result);
            Assert.Equal(AdState.Failed, outcome.Spot!.State);
            Assert.Equal("ANNOUNCER: A brand new, honest line.\nVOICE1: Call today.", outcome.Spot.Script);
        }

        [Fact]
        public async Task EditingAnApprovedSpotIsRefused()
        {
            // Given an approved spot (PLAN T403's own ruling: editing an approved spot would
            // invalidate a render already in flight)...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft() with { InitialState = AdState.Approved }, CancellationToken.None);

            // When an edit is attempted against it anyway...
            var outcome = await repo.UpdateAsync(spot.Id, Edit(title: "New Title"), spot.Version, CancellationToken.None);

            // Then it is refused (Conflict) and the row is left exactly as it was.
            Assert.Equal(AdSpotWriteResult.Conflict, outcome.Result);
            var row = await ReadRowAsync(db, spot.Id);
            Assert.Equal("approved", row.State);
        }

        [Fact]
        public async Task EditingAReadySpotIsRefused()
        {
            // Given a ready spot...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            await repo.CreateAsync(Draft() with { InitialState = AdState.Approved }, CancellationToken.None);
            var claimed = (await repo.ClaimNextApprovedAsync(CancellationToken.None))!;
            await repo.MarkReadyAsync(claimed.Id, mediaId: 1, CancellationToken.None);
            var ready = (await repo.ListByStateAsync(AdState.Ready, 10, 0, CancellationToken.None)).Items.Single();

            // When an edit is attempted against it...
            var outcome = await repo.UpdateAsync(ready.Id, Edit(title: "New Title"), ready.Version, CancellationToken.None);

            // Then it is refused (Conflict) — a rendered spot's content is no longer editable.
            Assert.Equal(AdSpotWriteResult.Conflict, outcome.Result);
        }

        [Fact]
        public async Task AStaleVersionIsRefusedAsAConflict()
        {
            // Given a draft spot, edited once (its own version now stale)...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(), CancellationToken.None);
            await repo.UpdateAsync(spot.Id, Edit(title: "First edit"), spot.Version, CancellationToken.None);

            // When a SECOND edit is attempted with the ORIGINAL (now stale) version...
            var outcome = await repo.UpdateAsync(spot.Id, Edit(title: "Second edit"), spot.Version, CancellationToken.None);

            // Then it is refused as a Conflict.
            Assert.Equal(AdSpotWriteResult.Conflict, outcome.Result);
        }

        [Fact]
        public async Task EditingAnUnknownIdReturnsNotFound()
        {
            // Given no spot with this id...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);

            // When an edit is attempted against it...
            var outcome = await repo.UpdateAsync(999_999, Edit(title: "New Title"), "1", CancellationToken.None);

            // Then it reports NotFound, distinctly from Conflict.
            Assert.Equal(AdSpotWriteResult.NotFound, outcome.Result);
        }

        [Fact]
        public async Task StateChangedAtIsUntouchedByAContentEdit()
        {
            // Given a draft spot...
            await db.ResetAdsAsync();
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(), CancellationToken.None);
            await Task.Delay(TimeSpan.FromMilliseconds(20));

            // When it is edited...
            var outcome = await repo.UpdateAsync(spot.Id, Edit(title: "New Title"), spot.Version, CancellationToken.None);

            // Then state_changed_at is untouched — an edit is not a transition (unlike every
            // ApproveAsync/RetryAsync/RetireAsync fact above, which all assert the opposite).
            Assert.Equal(spot.StateChangedAt, outcome.Spot!.StateChangedAt);
        }
    }
}

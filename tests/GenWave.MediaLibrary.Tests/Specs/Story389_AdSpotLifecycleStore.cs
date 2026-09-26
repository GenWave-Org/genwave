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
        long sponsorId, string title = "Draft spot", AdSource source = AdSource.Llm,
        string? packSlug = null, int spotSeconds = 30) =>
        new(sponsorId, title, Brief: "A cozy hardware shop", Script: null, source, packSlug, spotSeconds,
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
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);

            // When one is created with InitialState = Draft (the default, un-auto-approved path)...
            var spot = await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);

            // Then it lands Draft.
            Assert.Equal(AdState.Draft, spot.State);
        }

        [Fact]
        public async Task ACreatedApprovedSpotLandsInApproved()
        {
            // Given no prior spots...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);

            // When one is created with InitialState = Approved (Station:Ads:AutoApprove's own path,
            // PLAN T400)...
            var spot = await repo.CreateAsync(
                Draft(sponsorId) with { InitialState = AdState.Approved }, CancellationToken.None);

            // Then it lands Approved directly — no separate approve round trip needed.
            Assert.Equal(AdState.Approved, spot.State);
        }

        [Fact]
        public async Task ACreatedFailedSpotCarriesItsFailReason()
        {
            // Given no prior spots...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);

            // When one is created already Failed — STORY-390 AC3's own outcome, a script that never
            // passed validation after its one re-ask...
            var spot = await repo.CreateAsync(
                Draft(sponsorId) with { InitialState = AdState.Failed, FailReason = "brand_collision" },
                CancellationToken.None);

            // Then it lands Failed, with the violated rule's own id.
            Assert.Equal(AdState.Failed, spot.State);
            Assert.Equal("brand_collision", spot.FailReason);
        }

        [Fact]
        public async Task ACreatedSpotStampsStateChangedAtOnCreation()
        {
            // Given no prior spots...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);

            // When one is created...
            var spot = await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);

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
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);

            // When a caller attempts to create a spot already Ready — reachable only via a
            // transition on this store, never at birth...
            var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                repo.CreateAsync(Draft(sponsorId) with { InitialState = AdState.Ready }, CancellationToken.None));

            // Then it is refused before ever reaching Postgres.
            Assert.Contains("Draft, Approved, or Failed", ex.Message);
        }

        [Fact]
        public async Task CreatingFailedWithNoFailReasonIsRejected()
        {
            // Given no prior spots...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);

            // When a caller attempts to create a Failed spot with no reason...
            // Then it is refused — "fail_reason iff Failed" enforced in C#, ahead of db/43's own CHECK.
            await Assert.ThrowsAsync<ArgumentException>(() =>
                repo.CreateAsync(Draft(sponsorId) with { InitialState = AdState.Failed, FailReason = null }, CancellationToken.None));
        }

        [Fact]
        public async Task CreatingADraftSpotCarryingAFailReasonIsRejected()
        {
            // Given no prior spots...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);

            // When a caller attempts to create a Draft spot that ALSO carries a fail reason...
            // Then it is refused — the other half of the "iff" guard.
            await Assert.ThrowsAsync<ArgumentException>(() =>
                repo.CreateAsync(Draft(sponsorId) with { FailReason = "should never be set" }, CancellationToken.None));
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
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);
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
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(sponsorId) with { InitialState = AdState.Approved }, CancellationToken.None);
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
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(sponsorId) with { InitialState = AdState.Approved }, CancellationToken.None);
            var claimed = (await repo.ClaimNextApprovedAsync(CancellationToken.None))!;
            await Task.Delay(TimeSpan.FromMilliseconds(20));

            // When the render completes...
            var ok = await repo.MarkReadyAsync(claimed.Id, mediaId: 4242, renderVersion: 1, CancellationToken.None);

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
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            await repo.CreateAsync(Draft(sponsorId) with { InitialState = AdState.Approved }, CancellationToken.None);
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
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(
                Draft(sponsorId) with { InitialState = AdState.Failed, FailReason = "brand_collision" },
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
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            await repo.CreateAsync(Draft(sponsorId) with { InitialState = AdState.Approved }, CancellationToken.None);
            var claimed = (await repo.ClaimNextApprovedAsync(CancellationToken.None))!;
            await repo.MarkReadyAsync(claimed.Id, mediaId: 99, renderVersion: 1, CancellationToken.None);
            var ready = (await repo.ListByStateAsync(AdState.Ready, null, 10, 0, CancellationToken.None)).Items.Single();
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
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);
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
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(sponsorId) with { InitialState = AdState.Approved }, CancellationToken.None);
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
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(
                Draft(sponsorId) with { InitialState = AdState.Failed, FailReason = "brand_collision" },
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
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            await repo.CreateAsync(Draft(sponsorId) with { InitialState = AdState.Approved }, CancellationToken.None);
            var claimed = (await repo.ClaimNextApprovedAsync(CancellationToken.None))!;

            // When it is marked ready with a media id...
            await repo.MarkReadyAsync(claimed.Id, mediaId: 777, renderVersion: 1, CancellationToken.None);

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
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);

            // When the render seam is called against it anyway (a stale/duplicate signal)...
            var ok = await repo.MarkReadyAsync(spot.Id, mediaId: 1, renderVersion: 1, CancellationToken.None);

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
            // Given a direct connection to the database — no AdSpotRepository involved at all — and a
            // seeded sponsor (ad_spot.sponsor_id is a NOT NULL FK, PLAN T432)...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            await using var conn = await db.StationDataSource.OpenConnectionAsync();

            // When a raw INSERT attempts state = 'ready' with media_id left NULL...
            var ex = await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
                """
                insert into station.ad_spot (sponsor_id, sponsor_name, title, source, state)
                values (@sponsorId, 'Brand', 'Title', 'llm'::station.ad_source, 'ready'::station.ad_state)
                """,
                new { sponsorId }));

            // Then Postgres itself refuses it — ad_spot_ready_requires_media_id (db/43).
            Assert.Equal("23514", ex.SqlState);
        }

        [Fact]
        public async Task ARawInsertOfAFailedRowWithNoFailReasonViolatesTheCheck()
        {
            // Given a direct connection to the database, and a seeded sponsor...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            await using var conn = await db.StationDataSource.OpenConnectionAsync();

            // When a raw INSERT attempts state = 'failed' with fail_reason left NULL...
            var ex = await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
                """
                insert into station.ad_spot (sponsor_id, sponsor_name, title, source, state)
                values (@sponsorId, 'Brand', 'Title', 'llm'::station.ad_source, 'failed'::station.ad_state)
                """,
                new { sponsorId }));

            // Then Postgres itself refuses it — ad_spot_fail_reason_iff_failed (db/43).
            Assert.Equal("23514", ex.SqlState);
        }

        [Fact]
        public async Task ARawInsertOfADraftRowCarryingAFailReasonViolatesTheCheck()
        {
            // Given a direct connection to the database, and a seeded sponsor...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            await using var conn = await db.StationDataSource.OpenConnectionAsync();

            // When a raw INSERT attempts state = 'draft' but ALSO sets fail_reason...
            var ex = await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
                """
                insert into station.ad_spot (sponsor_id, sponsor_name, title, source, state, fail_reason)
                values (@sponsorId, 'Brand', 'Title', 'llm'::station.ad_source, 'draft'::station.ad_state, 'nope')
                """,
                new { sponsorId }));

            // Then Postgres itself refuses it too — the "iff" runs both directions.
            Assert.Equal("23514", ex.SqlState);
        }
    }

    // ---------------------------------------------------------------------
    // AC1 — the brief upsert, keyed on (sponsor_id, premise_key) — PLAN T432 retargets this from
    // the pre-sponsors (pack_slug, brand) key (SPEC F171.6, db/46's own ad_brief_sponsor_id_premise_key)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheBriefUpsertIsKeyedOnSponsorAndPremise(DatabaseFixture db)
    {
        [Fact]
        public async Task AFirstUpsertLandsOneRow()
        {
            // Given no prior briefs...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdBriefRepo(db);

            // When one owner-authored brief is upserted...
            await repo.UpsertAsync(
                packSlug: null, sponsorId: sponsorId, premise: "A cozy hardware shop",
                tone: "warm", structure: null, enabled: true, CancellationToken.None);

            // Then exactly one row exists.
            Assert.Equal(1, await CountAllBriefRowsAsync(db));
        }

        [Fact]
        public async Task AnUpsertWithTheSamePackSlugSponsorAndPremiseUpdatesInPlace()
        {
            // Given a pack-installed brief — premise held CONSTANT across both calls on purpose: the
            // constraint this upsert targets is (sponsor_id, premise_key), not (pack_slug, sponsor_id)
            // — a CHANGED premise folds to a different premise_key and inserts a SECOND row rather
            // than updating this one (see ADifferentPremiseForTheSameSponsorIsALegalSecondRow below;
            // only tone varies here to prove the update-in-place half)...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdBriefRepo(db);
            var first = await repo.UpsertAsync(
                packSlug: "genwave-catalog", sponsorId: sponsorId, premise: "Same premise",
                tone: "warm", structure: null, enabled: true, CancellationToken.None);

            // When the SAME pack re-installs it with the SAME premise but a revised tone...
            var second = await repo.UpsertAsync(
                packSlug: "genwave-catalog", sponsorId: sponsorId, premise: "Same premise",
                tone: "dry", structure: null, enabled: true, CancellationToken.None);

            // Then it updated the SAME row in place — same id, count stays 1, tone moved.
            Assert.Equal(first.Id, second.Id);
            Assert.Equal("dry", second.Tone);
            Assert.Equal(1, await CountAllBriefRowsAsync(db));
        }

        [Fact]
        public async Task TwoOwnerAuthoredUpsertsForTheSameSponsorAndPremiseCollapseToOneRow()
        {
            // Given no prior briefs — premise held constant across both calls (the SAME reasoning
            // as AnUpsertWithTheSamePackSlugSponsorAndPremiseUpdatesInPlace, one section up)...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdBriefRepo(db);
            var first = await repo.UpsertAsync(
                packSlug: null, sponsorId: sponsorId, premise: "Same premise",
                tone: "warm", structure: null, enabled: true, CancellationToken.None);

            // When the owner re-authors the SAME sponsor's SAME-premise brief — a SECOND call, also
            // NULL pack_slug...
            var second = await repo.UpsertAsync(
                packSlug: null, sponsorId: sponsorId, premise: "Same premise",
                tone: "dry", structure: null, enabled: true, CancellationToken.None);

            // Then it updated the SAME row — never a second one. A sponsor+premise is a sponsor+premise.
            Assert.Equal(first.Id, second.Id);
            Assert.Equal("dry", second.Tone);
            Assert.Equal(1, await CountAllBriefRowsAsync(db));
        }

        [Fact]
        public async Task ADifferentPremiseForTheSameSponsorIsALegalSecondRow()
        {
            // Given an owner-authored brief for a sponsor (SPEC F171.6: several angles are legal per
            // sponsor)...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdBriefRepo(db);
            await repo.UpsertAsync(
                packSlug: null, sponsorId: sponsorId, premise: "First angle",
                tone: "warm", structure: null, enabled: true, CancellationToken.None);

            // When a SECOND upsert targets the SAME sponsor with a DIFFERENT premise (folds to a
            // different premise_key)...
            await repo.UpsertAsync(
                packSlug: null, sponsorId: sponsorId, premise: "Second angle",
                tone: "dry", structure: null, enabled: true, CancellationToken.None);

            // Then they are two distinct rows — a different angle is a legal second row, never a
            // collision.
            Assert.Equal(2, await CountAllBriefRowsAsync(db));
        }

        [Fact]
        public async Task AnOwnerBriefAndAPackBriefForTheSameSponsorAndPremiseCollapseToOneRow()
        {
            // Given an owner-authored brief for a sponsor — pack_slug plays no part in the
            // (sponsor_id, premise_key) constraint (PLAN T432, AdBriefRepository.UpsertAsync's own
            // remarks), unlike the pre-sponsors (pack_slug, brand) key this replaced...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdBriefRepo(db);
            var owner = await repo.UpsertAsync(
                packSlug: null, sponsorId: sponsorId, premise: "Shared premise",
                tone: "warm", structure: null, enabled: true, CancellationToken.None);

            // When a PACK installs a brief for the SAME sponsor and the SAME premise (a different
            // pack_slug — but pack_slug is not part of the key)...
            var pack = await repo.UpsertAsync(
                packSlug: "genwave-catalog", sponsorId: sponsorId, premise: "Shared premise",
                tone: "dry", structure: null, enabled: true, CancellationToken.None);

            // Then they collapse to the SAME row — the cap is scoped to (sponsor_id, premise_key)
            // alone.
            Assert.Equal(owner.Id, pack.Id);
            Assert.Equal(1, await CountAllBriefRowsAsync(db));
        }

        [Fact]
        public async Task AnUpdatingUpsertLeavesCreatedAtUntouched()
        {
            // Given an existing brief — premise held constant across both calls (only tone varies)...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdBriefRepo(db);
            var first = await repo.UpsertAsync(
                packSlug: null, sponsorId: sponsorId, premise: "Same premise",
                tone: "warm", structure: null, enabled: true, CancellationToken.None);
            await Task.Delay(TimeSpan.FromMilliseconds(20));

            // When it is upserted again...
            var second = await repo.UpsertAsync(
                packSlug: null, sponsorId: sponsorId, premise: "Same premise",
                tone: "dry", structure: null, enabled: true, CancellationToken.None);

            // Then created_at is untouched by the update half.
            Assert.Equal(first.CreatedAt, second.CreatedAt);
        }

        [Fact]
        public async Task AReinstallNeverFlipsAnExistingRowsEnabledFlag()
        {
            // Given a disabled brief — T405 review RULING (corrects the T398-shipped shape): enabled
            // is PRESERVE-on-conflict, never overwrite; the operator's own lever, never a content
            // upsert's business — premise held constant across both calls on purpose (same reasoning
            // as this scenario's own update-in-place fact)...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdBriefRepo(db);
            await repo.UpsertAsync(
                packSlug: "genwave-catalog", sponsorId: sponsorId, premise: "Same premise",
                tone: "warm", structure: null, enabled: false, CancellationToken.None);

            // When it is upserted again with enabled: true...
            var second = await repo.UpsertAsync(
                packSlug: "genwave-catalog", sponsorId: sponsorId, premise: "Same premise",
                tone: "dry", structure: null, enabled: true, CancellationToken.None);

            // Then the row STAYS disabled — the second call's own `enabled` argument is silently
            // irrelevant to an EXISTING row — while the content still refreshed.
            Assert.False(second.Enabled);
            Assert.Equal("dry", second.Tone);
        }
    }

    // ---------------------------------------------------------------------
    // T405 review F4 — UpsertAllAsync batches every declared brief in ONE transaction
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioUpsertAllAsyncBatchesInOneTransaction(DatabaseFixture db)
    {
        [Fact]
        public async Task AFirstBatchLandsEveryDeclaredBriefEnabled()
        {
            // Given no prior briefs, and three distinct sponsors...
            await db.ResetAdsAndShowsAsync();
            var brambleId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var acmeId = await Harness.SeedSponsorAsync(db, "Acme Filing Co");
            var nikeId = await Harness.SeedSponsorAsync(db, "Nike");
            var repo = Harness.AdBriefRepo(db);
            var briefs = new List<AdBriefUpsertInput>
            {
                new(brambleId, "A cozy hardware shop", "warm", "hook-offer-cta"),
                new(acmeId, "Bureaucracy, but faster", null, null),
                new(nikeId, "The signature swoosh line", null, null),
            };

            // When the whole pack is upserted in one batch...
            var result = await repo.UpsertAllAsync("genwave-catalog", briefs, CancellationToken.None);

            // Then every declared brief landed, ALL enabled (SPEC F162.2's "installed briefs are
            // live by default" — a brand-new pack brief is always born enabled).
            Assert.Equal(3, result.Count);
            Assert.Equal(3, await CountAllBriefRowsAsync(db));
            Assert.All(result, brief => Assert.True(brief.Enabled));
        }

        [Fact]
        public async Task AReinstallBatchPreservesDisabledAndRefreshesContent()
        {
            // Given a batch-installed brief, later disabled by the operator — premise held CONSTANT
            // across both install calls on purpose: UpsertAllAsync conflicts on (sponsor_id,
            // premise_key), the same as UpsertAsync (AdBriefRepository's own remarks), so a changed
            // premise would insert a SECOND row instead of updating this one...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdBriefRepo(db);
            await repo.UpsertAllAsync(
                "genwave-catalog",
                [new AdBriefUpsertInput(sponsorId, "Same premise", "warm", null)],
                CancellationToken.None);
            var briefRow = (await repo.ListAllAsync(CancellationToken.None)).Single();
            await repo.SetEnabledAsync(briefRow.Id, enabled: false, CancellationToken.None);

            // When the SAME pack reinstalls with the SAME premise but a revised tone...
            var reinstalled = await repo.UpsertAllAsync(
                "genwave-catalog",
                [new AdBriefUpsertInput(sponsorId, "Same premise", "dry", null)],
                CancellationToken.None);

            // Then the SAME row updated in place — content refreshed, the operator's own disable
            // survived — "content refreshes, operator state persists," one property.
            var row = Assert.Single(reinstalled);
            Assert.False(row.Enabled);
            Assert.Equal("dry", row.Tone);
            Assert.Equal(1, await CountAllBriefRowsAsync(db));
        }

        [Fact]
        public async Task AFailingBriefMidBatchRollsBackEveryRowInTheBatch()
        {
            // Given a three-brief batch whose SECOND brief targets a sponsor id that does not
            // exist — test-only fault injection (mirrors
            // ScenarioTheDbChecksRefuseIllegalRowsEvenBypassingTheStore's own "reach the real
            // constraint" idiom one section up): ad_brief_sponsor_id_fkey is RESTRICT (db/46), and
            // this is the one honest way left to prove UpsertAllAsync's own transaction rolls
            // EVERYTHING back, never just the offending row — sponsor_id is a non-nullable long now,
            // so a null-fault injection no longer even compiles...
            await db.ResetAdsAndShowsAsync();
            var firstId = await Harness.SeedSponsorAsync(db, "First Sponsor");
            var thirdId = await Harness.SeedSponsorAsync(db, "Third Sponsor");
            const long NonexistentSponsorId = 999_999_999;
            var repo = Harness.AdBriefRepo(db);
            var briefs = new List<AdBriefUpsertInput>
            {
                new(firstId, null, null, null),
                new(NonexistentSponsorId, null, null, null),
                new(thirdId, null, null, null),
            };

            // When the batch upsert is attempted...
            await Assert.ThrowsAsync<PostgresException>(
                () => repo.UpsertAllAsync("genwave-catalog", briefs, CancellationToken.None));

            // Then NOTHING landed — not even the first, otherwise-valid brief; the whole batch is
            // one transaction.
            Assert.Equal(0, await CountAllBriefRowsAsync(db));
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
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);
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
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);

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
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);
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
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
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
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            await repo.CreateAsync(Draft(sponsorId) with { InitialState = AdState.Approved }, CancellationToken.None);
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
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);

            await repo.CreateAsync(Draft(sponsorId) with { InitialState = AdState.Approved }, CancellationToken.None);
            var claimedForReady = (await repo.ClaimNextApprovedAsync(CancellationToken.None))!;
            await repo.MarkReadyAsync(claimedForReady.Id, mediaId: 1, renderVersion: 1, CancellationToken.None);

            await repo.CreateAsync(Draft(sponsorId) with { InitialState = AdState.Approved }, CancellationToken.None);
            var claimedForFailure = (await repo.ClaimNextApprovedAsync(CancellationToken.None))!;
            await repo.MarkFailedAsync(claimedForFailure.Id, "tts_timeout", CancellationToken.None);

            var retiredDraft = await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);
            await repo.RetireAsync(retiredDraft.Id, retiredDraft.Version, CancellationToken.None);

            await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);

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
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var draft = await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);
            await repo.CreateAsync(Draft(sponsorId) with { InitialState = AdState.Approved }, CancellationToken.None);

            // When the page is scoped to Draft...
            var page = await repo.ListByStateAsync(AdState.Draft, null, 10, 0, CancellationToken.None);

            // Then only the draft row comes back.
            Assert.Equal([draft.Id], page.Items.Select(s => s.Id));
        }

        [Fact]
        public async Task TheTotalIsExactAcrossAPartialPage()
        {
            // Given three draft spots...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            for (var i = 0; i < 3; i++) await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);

            // When a page of 2 is requested...
            var page = await repo.ListByStateAsync(AdState.Draft, null, limit: 2, offset: 0, CancellationToken.None);

            // Then the total is the exact matching count, not the page's own row count.
            Assert.Equal(2, page.Items.Count);
            Assert.Equal(3, page.Total);
        }

        [Fact]
        public async Task AnOffsetPastTheLastRowStillCarriesTheTrueTotal()
        {
            // Given two draft spots...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);
            await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);

            // When a page starts past the last row...
            var page = await repo.ListByStateAsync(AdState.Draft, null, limit: 10, offset: 50, CancellationToken.None);

            // Then the page is empty but the total is still exact — never derived from Items' count.
            Assert.Empty(page.Items);
            Assert.Equal(2, page.Total);
        }

        [Fact]
        public async Task ANullStateListsEveryRowRegardlessOfState()
        {
            // Given a draft and a retired spot...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var draft = await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);
            var toRetire = await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);
            await repo.RetireAsync(toRetire.Id, toRetire.Version, CancellationToken.None);

            // When the list is unscoped (state = null)...
            var page = await repo.ListByStateAsync(null, null, 10, 0, CancellationToken.None);

            // Then both rows come back, any state.
            Assert.Equal(2, page.Total);
            Assert.Contains(page.Items, s => s.Id == draft.Id);
            Assert.Contains(page.Items, s => s.Id == toRetire.Id);
        }

        [Fact]
        public async Task ResultsOrderNewestTransitionedFirst()
        {
            // Given two draft spots, the second created after the first...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var older = await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);
            await Task.Delay(TimeSpan.FromMilliseconds(20));
            var newer = await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);

            // When the page is read...
            var page = await repo.ListByStateAsync(AdState.Draft, null, 10, 0, CancellationToken.None);

            // Then the newest-transitioned row leads.
            Assert.Equal(newer.Id, page.Items[0].Id);
            Assert.Equal(older.Id, page.Items[1].Id);
        }

        [Fact]
        public async Task ASponsorScopedListReturnsOnlyThatSponsorsRowsWithAnExactTotal()
        {
            // Given two draft spots under one sponsor and one draft spot under another...
            await db.ResetAdsAndShowsAsync();
            var sponsorA = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var sponsorB = await Harness.SeedSponsorAsync(db, "North Side Grocers");
            var repo = Harness.AdSpotRepo(db);
            var first = await repo.CreateAsync(Draft(sponsorA), CancellationToken.None);
            var second = await repo.CreateAsync(Draft(sponsorA), CancellationToken.None);
            await repo.CreateAsync(Draft(sponsorB), CancellationToken.None);

            // When the page is scoped to sponsor A...
            var page = await repo.ListByStateAsync(null, sponsorA, 10, 0, CancellationToken.None);

            // Then only sponsor A's two rows come back, and the total excludes sponsor B's row.
            Assert.Equal(2, page.Total);
            Assert.Equal([first.Id, second.Id], page.Items.Select(s => s.Id).OrderBy(id => id));
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
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);

            // When the worker claims...
            var claimed = await repo.ClaimNextApprovedAsync(CancellationToken.None);

            // Then nothing comes back — a legal answer, never an error.
            Assert.Null(claimed);
        }

        [Fact]
        public async Task ClaimingReturnsTheOldestApprovedSpotFirst()
        {
            // Given two approved spots, the first approved well before the second...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var first = await repo.CreateAsync(Draft(sponsorId) with { InitialState = AdState.Approved }, CancellationToken.None);
            await repo.CreateAsync(Draft(sponsorId) with { InitialState = AdState.Approved }, CancellationToken.None);
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
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var first = await repo.CreateAsync(Draft(sponsorId) with { InitialState = AdState.Approved }, CancellationToken.None);
            var second = await repo.CreateAsync(Draft(sponsorId) with { InitialState = AdState.Approved }, CancellationToken.None);
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
        async Task<long> MakeReadySpotAsync(
            DatabaseFixture fixture, AdSpotRepository repo, long sponsorId, AdSource source)
        {
            var spot = await repo.CreateAsync(
                Draft(sponsorId, source: source, packSlug: source == AdSource.Pack ? "genwave-catalog" : null)
                    with
                { InitialState = AdState.Approved },
                CancellationToken.None);
            var claimed = (await repo.ClaimNextApprovedAsync(CancellationToken.None))!;
            await repo.MarkReadyAsync(claimed.Id, mediaId: 1, renderVersion: 1, CancellationToken.None);
            return claimed.Id;
        }

        [Fact]
        public async Task CountStockGeneratedAsyncCountsLlmAndPackButNotOwner()
        {
            // Given one ready llm spot, one ready pack spot, and one ready OWNER spot...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            await MakeReadySpotAsync(db, repo, sponsorId, AdSource.Llm);
            await MakeReadySpotAsync(db, repo, sponsorId, AdSource.Pack);
            await MakeReadySpotAsync(db, repo, sponsorId, AdSource.Owner);

            // When the stock count is read...
            var count = await repo.CountStockGeneratedAsync(CancellationToken.None);

            // Then only the llm + pack spots count — owner is excluded (SPEC F159.3).
            Assert.Equal(2, count);
        }

        [Fact]
        public async Task CountStockGeneratedAsyncSpansDraftThroughReadyButNotFailedOrRetired()
        {
            // Given one llm spot in EACH state — ready, rendering, approved, draft, failed, retired
            // (gh-#689: the ready shelf alone left the draft pile unbounded under AutoApprove=false)...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            await MakeReadySpotAsync(db, repo, sponsorId, AdSource.Llm);                                                      // ready
            await repo.CreateAsync(Draft(sponsorId) with { InitialState = AdState.Approved }, CancellationToken.None);
            Assert.NotNull(await repo.ClaimNextApprovedAsync(CancellationToken.None));                            // rendering
            await repo.CreateAsync(Draft(sponsorId) with { InitialState = AdState.Approved }, CancellationToken.None);      // approved
            await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);                                              // draft
            await repo.CreateAsync(
                Draft(sponsorId) with { InitialState = AdState.Failed, FailReason = "format" }, CancellationToken.None);    // failed
            var doomed = await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);
            await repo.RetireAsync(doomed.Id, doomed.Version, CancellationToken.None);                            // retired
            Assert.Equal(6, await CountAllSpotRowsAsync(db));

            // When the stock count is read...
            var count = await repo.CountStockGeneratedAsync(CancellationToken.None);

            // Then exactly the four pipeline states count — failed and retired never do.
            Assert.Equal(4, count);
        }

        [Fact]
        public async Task ListReadyOlderThanAsyncExcludesOwnerSpotsRegardlessOfAge()
        {
            // Given one ready owner spot, backdated well past any refresh age...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var ownerId = await MakeReadySpotAsync(db, repo, sponsorId, AdSource.Owner);
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
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var freshId = await MakeReadySpotAsync(db, repo, sponsorId, AdSource.Llm);
            var staleId = await MakeReadySpotAsync(db, repo, sponsorId, AdSource.Llm);
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
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);

            // When it is read back by id...
            var found = await repo.GetByIdAsync(spot.Id, CancellationToken.None);

            // Then the exact row comes back.
            Assert.NotNull(found);
            Assert.Equal(spot.Id, found!.Id);
            Assert.Equal(spot.SponsorId, found.SponsorId);
            Assert.Equal(spot.SponsorName, found.SponsorName);
        }

        [Fact]
        public async Task AnUnknownIdReturnsNull()
        {
            // Given no spot with this id...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
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
            long? sponsorId = null, string? title = null, string? brief = null, string? script = null,
            string? voicePlan = null, int? spotSeconds = null, long? bedMediaId = null) =>
            new(sponsorId, title, brief, script, voicePlan, spotSeconds, bedMediaId);

        [Fact]
        public async Task EditingADraftSpotUpdatesTheGivenFields()
        {
            // Given a draft spot under one sponsor, and a second sponsor to move it to...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var otherSponsorId = await Harness.SeedSponsorAsync(db, "New Sponsor");
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);

            // When sponsor and title are edited...
            var outcome = await repo.UpdateAsync(
                spot.Id, Edit(sponsorId: otherSponsorId, title: "New Title"), spot.Version, CancellationToken.None);

            // Then the given fields moved — including SponsorName's own refreshed snapshot
            // (SPEC F171.7) — and the state stayed Draft (a content edit, not a transition).
            Assert.Equal(AdSpotWriteResult.Updated, outcome.Result);
            Assert.Equal(otherSponsorId, outcome.Spot!.SponsorId);
            Assert.Equal("New Sponsor", outcome.Spot.SponsorName);
            Assert.Equal("New Title", outcome.Spot.Title);
            Assert.Equal(AdState.Draft, outcome.Spot.State);
        }

        [Fact]
        public async Task FieldsLeftNullAreUnchanged()
        {
            // Given a draft spot with a known sponsor and brief...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Original Sponsor");
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);

            // When only the title is edited...
            var outcome = await repo.UpdateAsync(
                spot.Id, Edit(title: "New Title"), spot.Version, CancellationToken.None);

            // Then sponsor/brief are untouched.
            Assert.Equal(AdSpotWriteResult.Updated, outcome.Result);
            Assert.Equal(sponsorId, outcome.Spot!.SponsorId);
            Assert.Equal("Original Sponsor", outcome.Spot.SponsorName);
            Assert.Equal(spot.Brief, outcome.Spot.Brief);
        }

        [Fact]
        public async Task EditingAFailedSpotSucceeds()
        {
            // Given a failed spot (the "fix the script before retry" path)...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(
                Draft(sponsorId) with { InitialState = AdState.Failed, FailReason = "brand_collision" },
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
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(sponsorId) with { InitialState = AdState.Approved }, CancellationToken.None);

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
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            await repo.CreateAsync(Draft(sponsorId) with { InitialState = AdState.Approved }, CancellationToken.None);
            var claimed = (await repo.ClaimNextApprovedAsync(CancellationToken.None))!;
            await repo.MarkReadyAsync(claimed.Id, mediaId: 1, renderVersion: 1, CancellationToken.None);
            var ready = (await repo.ListByStateAsync(AdState.Ready, null, 10, 0, CancellationToken.None)).Items.Single();

            // When an edit is attempted against it...
            var outcome = await repo.UpdateAsync(ready.Id, Edit(title: "New Title"), ready.Version, CancellationToken.None);

            // Then it is refused (Conflict) — a rendered spot's content is no longer editable.
            Assert.Equal(AdSpotWriteResult.Conflict, outcome.Result);
        }

        [Fact]
        public async Task AStaleVersionIsRefusedAsAConflict()
        {
            // Given a draft spot, edited once (its own version now stale)...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);
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
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
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
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);
            await Task.Delay(TimeSpan.FromMilliseconds(20));

            // When it is edited...
            var outcome = await repo.UpdateAsync(spot.Id, Edit(title: "New Title"), spot.Version, CancellationToken.None);

            // Then state_changed_at is untouched — an edit is not a transition (unlike every
            // ApproveAsync/RetryAsync/RetireAsync fact above, which all assert the opposite).
            Assert.Equal(spot.StateChangedAt, outcome.Spot!.StateChangedAt);
        }
    }

    // ---------------------------------------------------------------------
    // T362 loop law — ListAllAsync's own live facts (T403b's GET /api/ad-briefs)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioListAllAsyncReturnsEveryBriefNewestFirst(DatabaseFixture db)
    {
        [Fact]
        public async Task BothPackAndOwnerBriefsComeBack()
        {
            // Given one owner brief and one pack brief, each for its own sponsor...
            await db.ResetAdsAndShowsAsync();
            var ownerSponsorId = await Harness.SeedSponsorAsync(db, "Owner Sponsor");
            var packSponsorId = await Harness.SeedSponsorAsync(db, "Pack Sponsor");
            var repo = Harness.AdBriefRepo(db);
            await repo.UpsertAsync(
                packSlug: null, sponsorId: ownerSponsorId, premise: null, tone: null, structure: null,
                enabled: true, CancellationToken.None);
            await repo.UpsertAsync(
                packSlug: "genwave-catalog", sponsorId: packSponsorId, premise: null, tone: null,
                structure: null, enabled: true, CancellationToken.None);

            // When every brief is listed...
            var briefs = await repo.ListAllAsync(CancellationToken.None);

            // Then both come back — pack and owner alike.
            Assert.Equal(2, briefs.Count);
            Assert.Contains(briefs, b => b.SponsorId == ownerSponsorId && b.PackSlug is null);
            Assert.Contains(briefs, b => b.SponsorId == packSponsorId && b.PackSlug == "genwave-catalog");
        }

        [Fact]
        public async Task DisabledBriefsAreListedToo()
        {
            // Given a disabled brief — SampleEnabledAsync would skip it, but the admin list must not.
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Disabled Sponsor");
            var repo = Harness.AdBriefRepo(db);
            await repo.UpsertAsync(
                packSlug: null, sponsorId: sponsorId, premise: null, tone: null, structure: null,
                enabled: false, CancellationToken.None);

            // When every brief is listed...
            var briefs = await repo.ListAllAsync(CancellationToken.None);

            // Then the disabled row still comes back.
            Assert.Contains(briefs, b => b.SponsorId == sponsorId && !b.Enabled);
        }

        [Fact]
        public async Task ResultsOrderNewestCreatedFirst()
        {
            // Given two briefs for two sponsors, the second created after the first...
            await db.ResetAdsAndShowsAsync();
            var olderSponsorId = await Harness.SeedSponsorAsync(db, "Older Sponsor");
            var newerSponsorId = await Harness.SeedSponsorAsync(db, "Newer Sponsor");
            var repo = Harness.AdBriefRepo(db);
            await repo.UpsertAsync(
                packSlug: null, sponsorId: olderSponsorId, premise: null, tone: null, structure: null,
                enabled: true, CancellationToken.None);
            await Task.Delay(TimeSpan.FromMilliseconds(20));
            await repo.UpsertAsync(
                packSlug: null, sponsorId: newerSponsorId, premise: null, tone: null, structure: null,
                enabled: true, CancellationToken.None);

            // When every brief is listed...
            var briefs = await repo.ListAllAsync(CancellationToken.None);

            // Then the newest-created row leads.
            Assert.Equal(newerSponsorId, briefs[0].SponsorId);
            Assert.Equal(olderSponsorId, briefs[1].SponsorId);
        }

        [Fact]
        public async Task NoBriefsListsEmpty()
        {
            // Given no briefs at all...
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.AdBriefRepo(db);

            // When every brief is listed...
            var briefs = await repo.ListAllAsync(CancellationToken.None);

            // Then the list is empty — a legal answer, never an error.
            Assert.Empty(briefs);
        }
    }

    // ---------------------------------------------------------------------
    // T362 loop law — CreateOwnerAsync's own live facts (T403b's POST /api/ad-briefs); PLAN T432
    // retargets the cap from (pack_slug, brand) to (sponsor_id, premise_key) — pack_slug plays no
    // part, so a colliding pack-owned row refuses an owner create too (SPEC F171.6)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioCreateOwnerAsyncRefusesADuplicateSponsorAndPremiseAtomically(DatabaseFixture db)
    {
        [Fact]
        public async Task AFirstCreateLandsOneRowWithPackSlugNull()
        {
            // Given a sponsor with no prior briefs...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdBriefRepo(db);

            // When an owner brief is created...
            var created = await repo.CreateOwnerAsync(
                sponsorId, premise: "A cozy hardware shop", tone: "warm", structure: null,
                enabled: true, CancellationToken.None);

            // Then it lands, pack_slug null, exactly one row.
            Assert.NotNull(created);
            Assert.Null(created!.PackSlug);
            Assert.Equal(sponsorId, created.SponsorId);
            Assert.Equal(1, await CountAllBriefRowsAsync(db));
        }

        [Fact]
        public async Task ASecondCreateForTheSameSponsorAndPremiseIsRefusedAndTheRowUnchanged()
        {
            // Given an existing owner brief for a sponsor — premise held CONSTANT across both calls
            // on purpose: the cap this create targets is (sponsor_id, premise_key), so a DIFFERENT
            // premise would never collide at all (ADifferentPremiseForTheSameSponsorIsALegalSecondRow,
            // the upsert scenario two sections up)...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdBriefRepo(db);
            var first = await repo.CreateOwnerAsync(
                sponsorId, premise: "Same premise", tone: "warm", structure: null,
                enabled: true, CancellationToken.None);

            // When a SECOND owner brief is created for the SAME sponsor and the SAME premise...
            var second = await repo.CreateOwnerAsync(
                sponsorId, premise: "Same premise", tone: "dry", structure: null,
                enabled: true, CancellationToken.None);

            // Then it is refused (null back) — never a silent update, never a second row — and the
            // original row's own tone is untouched (the exact behavior UpsertAsync would NOT give:
            // this is why CreateOwnerAsync exists as its own member).
            Assert.NotNull(first);
            Assert.Null(second);
            Assert.Equal(1, await CountAllBriefRowsAsync(db));
            var rows = await repo.ListAllAsync(CancellationToken.None);
            Assert.Equal("warm", rows.Single().Tone);
        }

        [Fact]
        public async Task ACreateCollidingWithAnExistingPackBriefsSponsorAndPremiseIsRefusedToo()
        {
            // Given a PACK-installed brief for a sponsor — pack_slug plays no part in the
            // (sponsor_id, premise_key) cap (AdBriefRepository.CreateOwnerAsync's own remarks), so an
            // owner create colliding with a PACK row is refused exactly like colliding with an owner
            // row above...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdBriefRepo(db);
            await repo.UpsertAsync(
                packSlug: "genwave-catalog", sponsorId: sponsorId, premise: "Shared premise",
                tone: "dry", structure: null, enabled: true, CancellationToken.None);

            // When an OWNER brief is created for the SAME sponsor and the SAME premise...
            var created = await repo.CreateOwnerAsync(
                sponsorId, premise: "Shared premise", tone: "warm", structure: null,
                enabled: true, CancellationToken.None);

            // Then it is refused — the pack row is the only row, untouched.
            Assert.Null(created);
            Assert.Equal(1, await CountAllBriefRowsAsync(db));
        }
    }

    // ---------------------------------------------------------------------
    // T362 loop law — SetEnabledAsync's own live facts (T403b's PATCH /api/ad-briefs/{id})
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioSetEnabledAsyncTogglesAnyBriefById(DatabaseFixture db)
    {
        [Fact]
        public async Task DisablingAnEnabledOwnerBriefFlipsIt()
        {
            // Given an enabled owner brief...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdBriefRepo(db);
            var brief = await repo.CreateOwnerAsync(
                sponsorId, premise: null, tone: null, structure: null, enabled: true,
                CancellationToken.None);

            // When it is disabled...
            var updated = await repo.SetEnabledAsync(brief!.Id, enabled: false, CancellationToken.None);

            // Then the row comes back with enabled flipped.
            Assert.NotNull(updated);
            Assert.False(updated!.Enabled);
        }

        [Fact]
        public async Task EnablingAPackBriefFlipsItToo()
        {
            // Given a disabled pack brief — the toggle is the operator's own lever over pack content
            // too, not owner-only (PLAN T403b's own reading of F162.1).
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdBriefRepo(db);
            var brief = await repo.UpsertAsync(
                packSlug: "genwave-catalog", sponsorId: sponsorId, premise: null, tone: null,
                structure: null, enabled: false, CancellationToken.None);

            // When it is enabled...
            var updated = await repo.SetEnabledAsync(brief.Id, enabled: true, CancellationToken.None);

            // Then it flips, pack_slug untouched.
            Assert.NotNull(updated);
            Assert.True(updated!.Enabled);
            Assert.Equal("genwave-catalog", updated.PackSlug);
        }

        [Fact]
        public async Task AnUnknownIdReturnsNull()
        {
            // Given no brief with this id...
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.AdBriefRepo(db);

            // When it is toggled...
            var updated = await repo.SetEnabledAsync(999_999, enabled: true, CancellationToken.None);

            // Then nothing comes back — a legal 404 signal, never an error.
            Assert.Null(updated);
        }
    }
}

// STORY-402 — the ad cast pick's never-overwrite guard, structurally + against real Postgres (SPEC F167 · PLAN T415)
//
// Pure text assertion first (the Story401_VoicePackDeleteGuardSql precedent, this same directory):
// AdSpotRepository.StampVoicePlanSql is the ONE place the coalesce/state guard text lives
// (StampVoicePlanIfNullAsync references it directly, never a second inline copy), so a mutation
// weakening either guard has nowhere else to hide. A real-Postgres round-trip scenario follows,
// proving the guard's actual BEHAVIOUR against the live schema, not merely its own text (R2's own
// "add a real-Postgres round-trip fact too if cheap") — the Story389_AdSpotLifecycleStore fixture
// family's own posture, one file over.
//
// The state guard admits draft/approved/rendering, not rendering alone (PLAN T442 ruling —
// AdSpotRepository.StampVoicePlanSql's own remarks): a preview render (STORY-424) stamps the SAME
// cast pick on a row that stays draft/approved throughout, never claimed into rendering the way a
// write render's own ClaimNextApprovedAsync does.

using System.Text.Json;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureAdSpotStampVoicePlanSql
{
    public sealed class ScenarioTheGuardIsPartOfTheSingleUpdateStatement
    {
        // Given AdSpotRepository's own stamp guard text, When it is inspected directly.
        [Fact]
        public void ItNeverOverwritesAnExistingPlan() =>
            Assert.Contains("coalesce(voice_plan,", AdSpotRepository.StampVoicePlanSql, StringComparison.Ordinal);

        [Fact]
        public void ItOnlyEverStampsADraftApprovedOrRenderingRow() =>
            Assert.Contains(
                "state in ('draft'::station.ad_state, 'approved'::station.ad_state, 'rendering'::station.ad_state)",
                AdSpotRepository.StampVoicePlanSql, StringComparison.Ordinal);
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheGuardHoldsAgainstRealPostgres(DatabaseFixture db)
    {
        const string FirstPlan = """[{"tag":"ANNOUNCER","voiceId":"af_nova","pace":1.0}]""";
        const string SecondPlan = """[{"tag":"ANNOUNCER","voiceId":"am_onyx","pace":1.0}]""";

        static NewAdSpot Draft(long sponsorId, string? voicePlan = null) =>
            new(
                sponsorId, "Draft spot", Brief: "A cozy hardware shop", Script: null, AdSource.Llm,
                PackSlug: null, SpotSeconds: 30, voicePlan, BedMediaId: null, InitialState: AdState.Approved,
                FailReason: null);

        /// <summary>Reads the ANNOUNCER entry's own voiceId back out of a stamped plan — never a raw
        /// string comparison against what was written, because Postgres's own jsonb storage reformats
        /// (and reorders) object keys on the way back out (proven live by this very file: writing
        /// <c>{"tag":"ANNOUNCER","voiceId":"af_nova","pace":1.0}</c> reads back
        /// <c>{"tag": "ANNOUNCER", "pace": 1.0, "voiceId": "af_nova"}</c>) — a byte-identical
        /// round-trip assumption against a real jsonb column is simply wrong, not a production bug.</summary>
        static string AnnouncerVoiceIdIn(string voicePlanJson)
        {
            using var doc = JsonDocument.Parse(voicePlanJson);
            return doc.RootElement
                .EnumerateArray()
                .Single(entry => entry.GetProperty("tag").GetString() == "ANNOUNCER")
                .GetProperty("voiceId")
                .GetString()!;
        }

        [Fact]
        public async Task ANullPlanOnARenderingRowIsStamped()
        {
            // Given a spot claimed into Rendering with no voice plan yet...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);
            var claimed = (await repo.ClaimNextApprovedAsync(CancellationToken.None))!;
            Assert.Null(claimed.VoicePlan);

            // When the cast pick is stamped...
            var stamped = await repo.StampVoicePlanIfNullAsync(claimed.Id, FirstPlan, CancellationToken.None);

            // Then the row carries the new plan.
            Assert.NotNull(stamped);
            Assert.Equal("af_nova", AnnouncerVoiceIdIn(stamped!.VoicePlan!));
        }

        [Fact]
        public async Task AnExistingPlanOnARenderingRowIsNeverOverwritten()
        {
            // Given a spot claimed into Rendering that already carries a plan (an owner draft's own)...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            await repo.CreateAsync(Draft(sponsorId, FirstPlan), CancellationToken.None);
            var claimed = (await repo.ClaimNextApprovedAsync(CancellationToken.None))!;
            Assert.Equal("af_nova", AnnouncerVoiceIdIn(claimed.VoicePlan!));

            // When a stamp is attempted anyway...
            var stamped = await repo.StampVoicePlanIfNullAsync(claimed.Id, SecondPlan, CancellationToken.None);

            // Then the original plan survives untouched — coalesce won, not the new argument's am_onyx.
            Assert.NotNull(stamped);
            Assert.Equal("af_nova", AnnouncerVoiceIdIn(stamped!.VoicePlan!));
        }

        [Fact]
        public async Task ADraftRowIsStamped()
        {
            // Given a spot still in Draft, never claimed into Rendering and never approved (PLAN T442
            // — a preview render stamps a row exactly like this one)...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(
                new NewAdSpot(
                    sponsorId, "Draft spot", Brief: "A cozy hardware shop", Script: "ANNOUNCER: Hi.", AdSource.Llm,
                    PackSlug: null, SpotSeconds: 30, VoicePlan: null, BedMediaId: null, InitialState: AdState.Draft,
                    FailReason: null),
                CancellationToken.None);

            // When the cast pick is stamped...
            var stamped = await repo.StampVoicePlanIfNullAsync(spot.Id, FirstPlan, CancellationToken.None);

            // Then the row carries the new plan, still in Draft.
            Assert.NotNull(stamped);
            Assert.Equal("af_nova", AnnouncerVoiceIdIn(stamped!.VoicePlan!));
        }

        [Fact]
        public async Task AnApprovedRowNeverClaimedIsStamped()
        {
            // Given an approved spot never claimed into Rendering (PLAN T442 — a preview render can
            // stamp a spot an owner has already approved but never written/rendered)...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);

            // When the cast pick is stamped...
            var stamped = await repo.StampVoicePlanIfNullAsync(spot.Id, FirstPlan, CancellationToken.None);

            // Then the row carries the new plan, still Approved (no claim, no state change).
            Assert.NotNull(stamped);
            Assert.Equal("af_nova", AnnouncerVoiceIdIn(stamped!.VoicePlan!));
        }

        [Fact]
        public async Task ARowInATerminalStateIsNeverStamped()
        {
            // Given a spot already Failed — past the point any cast pick is still meaningful...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(
                new NewAdSpot(
                    sponsorId, "Failed spot", Brief: "A cozy hardware shop", Script: "ANNOUNCER: Hi.", AdSource.Llm,
                    PackSlug: null, SpotSeconds: 30, VoicePlan: null, BedMediaId: null, InitialState: AdState.Failed,
                    FailReason: "the writer refused the copy"),
                CancellationToken.None);

            // When a stamp is attempted against it directly...
            var stamped = await repo.StampVoicePlanIfNullAsync(spot.Id, FirstPlan, CancellationToken.None);

            // Then nothing is returned — the guarded WHERE matched nothing, and nothing was written.
            Assert.Null(stamped);
        }

        [Fact]
        public async Task AReadyRowIsNeverStamped()
        {
            // Given a spot promoted all the way to Ready — ready sits outside the
            // draft/approved/rendering guard exactly like a failed row already does above; a preview
            // render can never reach a spot this far along its own lifecycle...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);
            var claimed = (await repo.ClaimNextApprovedAsync(CancellationToken.None))!;
            var promoted = await repo.MarkReadyAsync(claimed.Id, mediaId: 4242, renderVersion: 1, CancellationToken.None);
            Assert.True(promoted, "arrange: MarkReadyAsync unexpectedly refused the row");

            // When a stamp is attempted against it directly...
            var stamped = await repo.StampVoicePlanIfNullAsync(claimed.Id, FirstPlan, CancellationToken.None);

            // Then nothing is returned — the guarded WHERE matched nothing, and nothing was written.
            Assert.Null(stamped);
        }

        [Fact]
        public async Task ARetiredRowIsNeverStamped()
        {
            // Given a spot retired straight off Draft...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);
            var outcome = await repo.RetireAsync(spot.Id, spot.Version, CancellationToken.None);
            Assert.Equal(AdState.Retired, outcome.Spot!.State);

            // When a stamp is attempted against it directly...
            var stamped = await repo.StampVoicePlanIfNullAsync(spot.Id, FirstPlan, CancellationToken.None);

            // Then nothing is returned — the guarded WHERE matched nothing, and nothing was written.
            Assert.Null(stamped);
        }
    }
}

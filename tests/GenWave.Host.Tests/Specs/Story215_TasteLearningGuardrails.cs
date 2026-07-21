// STORY-215 — The persona learns only from me, and can't spiral
//
// BDD specification — xUnit (SPEC F84.1–F84.7). PLAN T60 stamps the booth log; T70 wires the
// thumb endpoints WITH every guardrail in the same change (F84.4 — no commit has accrual
// without brakes). Entry-point scenarios drive the real thumb routes (WebApplicationFactory);
// the zero-input simulation drives the real ranker loop.

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using Microsoft.AspNetCore.Mvc;

namespace GenWave.Host.Tests.Specs;

/// <summary>
/// Fixed single-page <see cref="IBoothLogReader"/> double (STORY-215, T60) — this file only needs to
/// prove the controller/DTO projects a row's persona stamp through unchanged; the real keyset-paging
/// behavior is Story195_BoothLog.cs's own concern (its own <c>FakeBoothLogReader</c> is file-scoped
/// there, so this file scripts its own minimal double rather than reaching across files).
/// </summary>
file sealed class FixedPageBoothLogReader(BoothLogPage page) : IBoothLogReader
{
    public Task<BoothLogPage> ReadAsync(BoothLogCursor? before, int take, CancellationToken ct) =>
        Task.FromResult(page);
}

public static class FeatureTasteLearningGuardrails
{
    public static class ScenarioThumbNudgesAnArtistRule
    {
        // Arrange (T70): a track airing under an active persona; POST a thumb via the API.

        [Fact(Skip = "Pending T70 — see docs/PLAN.md")]
        public static void AFirstThumbCreatesAnAccruedArtistRule()
        {
            // predicate = artist, source='accrued', weight ±0.2 (F84.1)
            Assert.Fail("pending T70");
        }

        [Fact(Skip = "Pending T70 — see docs/PLAN.md")]
        public static void ARepeatThumbNudgesByOneStep()
        {
            Assert.Fail("pending T70");
        }

        [Fact(Skip = "Pending T70 — see docs/PLAN.md")]
        public static void WeightClampsAtTheBounds()
        {
            // six same-direction thumbs on distinct airings ⇒ weight is 1.0, not 1.2 (F84.1)
            Assert.Fail("pending T70");
        }
    }

    public static class ScenarioBoothLogAttribution
    {
        // Arrange (T60/T70): a row aired under persona A; persona B is now active; thumb the row.

        [Fact]
        public static async Task TrackRowsCarryTheOnAirPersonaId()
        {
            // Given a booth-log track row stamped with the persona that was active when it aired
            // (F84.6) — the reader/API path never re-derives this from whatever persona happens to
            // be active NOW; it simply reflects whatever the write path stamped.
            const long personaAId = 7;
            var stampedRow = new BoothLogEntry(1, DateTime.UtcNow, "track-started",
                "Started 'Night Drive' by The Waveforms", PersonaId: personaAId);
            var reader = new FixedPageBoothLogReader(new BoothLogPage([stampedRow], NextBefore: null));
            var controller = new BoothLogController(reader);

            // When the admin feed reads it back...
            var page = Assert.IsType<BoothLogPageDto>(
                Assert.IsType<OkObjectResult>(await controller.List(before: null, take: 10, CancellationToken.None)).Value);

            // Then the API row carries that same persona id — stamped at air time, additive
            // column/payload (F84.6).
            Assert.Equal(personaAId, page.Entries.Single().PersonaId);
        }

        [Fact(Skip = "Pending T70 — see docs/PLAN.md")]
        public static void TheRuleAccruesToThePersonaOnAirAtTheAiring()
        {
            // persona A, never the now-active B (F84.1)
            Assert.Fail("pending T70");
        }
    }

    public static class ScenarioCapAndEviction
    {
        // Arrange (T70): a persona at the 50 accrued-rule cap plus authored/operator rows.

        [Fact(Skip = "Pending T70 — see docs/PLAN.md")]
        public static void TheWeakestAccruedRuleIsEvictedInTheSameTransaction()
        {
            // lowest |weight| leaves; row count stays 50 (F84.3)
            Assert.Fail("pending T70");
        }

        [Fact(Skip = "Pending T70 — see docs/PLAN.md")]
        public static void AuthoredAndOperatorRulesAreNeverEvicted()
        {
            // the card's signature can't be crowded out (F84.3)
            Assert.Fail("pending T70");
        }
    }

    public static class SadPathStructuralGuardrails
    {
        [Fact(Skip = "Pending T70 — see docs/PLAN.md")]
        public static void FiveHundredPicksWithZeroInputWriteNoTasteRows()
        {
            // the ranker/feeder have no persona_taste write path (F84.2 structural)
            Assert.Fail("pending T70");
        }

        [Fact(Skip = "Pending T70 — see docs/PLAN.md")]
        public static void FiveHundredPicksKeepEveryArtistWithinRotationShare()
        {
            // no self-reinforcement spiral (F84.2 simulation)
            Assert.Fail("pending T70");
        }

        [Fact(Skip = "Pending T70 — see docs/PLAN.md")]
        public static void ADoubleTapMovesTheWeightOnce()
        {
            // idempotent per (persona, airing, direction) (F84.5)
            Assert.Fail("pending T70");
        }

        [Fact(Skip = "Pending T70 — see docs/PLAN.md")]
        public static void NowPlayingAndBoothLogTapsOnTheSameAiringMoveTheWeightOnce()
        {
            Assert.Fail("pending T70");
        }

        [Fact(Skip = "Pending T70 — see docs/PLAN.md")]
        public static void AnUnstampedRowRejectsAThumb()
        {
            // rows predating the stamp (or persona-less airings) are not thumbable (F84.6)
            Assert.Fail("pending T70");
        }

        [Fact(Skip = "Pending T70 — see docs/PLAN.md")]
        public static void APersonaThumbNeverWritesMediaRating()
        {
            // F84.7 disjointness, half one
            Assert.Fail("pending T70");
        }

        [Fact(Skip = "Pending T70 — see docs/PLAN.md")]
        public static void AnF33VoteNeverWritesPersonaTaste()
        {
            // F84.7 disjointness, half two
            Assert.Fail("pending T70");
        }
    }
}

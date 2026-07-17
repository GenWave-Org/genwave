// STORY-116 — Acceptance gate: track rating end-to-end + regression (Epic S / SPEC F33, closes gitea-#188)
//
// BDD specification — xUnit. S8 ran 2026-07-12 against an isolated scratch-stack smoke (own -p
// s8smoke project, own .env, non-colliding ports 18000/18080/13000, empty MEDIA_DIR so the F27
// boot seed supplies real content — MEMORY.md's scratch-compose-smoke-recipe, the E10→R13 gate
// discipline) — never the operator's live station (a separate `mrdgenwave` compose project at a
// different path entirely; confirmed untouched throughout this run). Every fact below is one of:
//   (1) a real, always-attempted, self-skipping HTTP-only check — Story013/Story082/Story094/
//       Story108's guarded-live-check idiom, [Trait("Category","Integration")] (at the class
//       level, mirroring Story108) so it is excluded from the `--filter "Category!=Integration"`
//       wall run and opportunistically real whenever someone runs the FULL suite against a
//       reachable deployment;
//   (2) a real, always-run, non-Skip repo-content assertion (Story102/Story107's grep-assert
//       idiom) — no live stack needed, so it deliberately has NO Integration trait and stays IN
//       the filtered wall (the one exception to the "whole file is Category=Integration" shape);
//   (3) Skip-pinned with THIS SESSION's dated s8smoke scratch-stack evidence, Category=Integration; or
//   (4) Skip-pinned with the EXACT operator procedure for what genuinely needs the real station,
//       a real browser, or a human decision (issue closure), Category=Integration.
// No fact in this file keeps a bare "pending S8" reason.

using System.Net;
using System.Net.Http.Json;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAcceptanceGateTrackRating
{
    // ---------------------------------------------------------------------
    // Shared live-stack helper (Story013/Story082/Story094/Story108's guarded-live-check idiom)
    // ---------------------------------------------------------------------

    static class LiveApi
    {
        public const string BaseUrl = "http://localhost:8080";

        public static async Task<bool> IsReachableAsync()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                using var response = await http.GetAsync(BaseUrl + "/health");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Repo root, resolved relative to the test assembly's build output (Story074/102/107's convention).</summary>
    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static string MediaRepositorySourceText =>
        File.ReadAllText(Path.Combine(RepoRoot, "src", "GenWave.MediaLibrary", "Catalog", "MediaRepository.cs"));

    // ---------------------------------------------------------------------
    // (a)+(b) — vote clamping + the F33.1 ETag-survives-a-vote proof
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioVotingEndToEnd
    {
        [Fact]
        public async Task AnonymousVoteRequestIsRejectedLive()
        {
            // AC-a/b partial — deny-by-default re-verified against a REAL running deployment: a
            // pure auth-boundary check, no side effects (401 fires in auth middleware before
            // RatingController is ever constructed, so no row is ever touched). Self-skips when
            // localhost:8080 isn't up (Story013's guarded-live-check idiom).
            if (!await LiveApi.IsReachableAsync()) return;

            using var http = new HttpClient { BaseAddress = new Uri(LiveApi.BaseUrl), Timeout = TimeSpan.FromSeconds(10) };
            using var response = await http.PostAsJsonAsync("/api/media/999999999/vote", new { direction = "up" });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        const string ClampingSkip =
            "S8(a) server half — s8smoke, scratch stack, 2026-07-12: the F27 boot-seeded safe row " +
            "(media id 1, default score 50) took 60 consecutive POST /api/media/1/vote " +
            "{\"direction\":\"up\"} calls and settled at {\"score\":100} (never 101+); a further " +
            "110 consecutive {\"direction\":\"down\"} calls on the same row settled at " +
            "{\"score\":0} (never negative) — the LEAST/GREATEST clamp (F33.3) holds at both " +
            "rails through the real endpoint, repeated well past each rail. The Live-page IN-" +
            "PLACE chip update (no refetch — S6's useLiveRatings folding the vote response body " +
            "into state) is a browser-rendering behavior no curl can observe; it is pinned by " +
            "S6's own jest suite (live-rating.spec.tsx, 16 tests, green in this run's admin-ui " +
            "wall) and remains an operator eyeball item (operator checklist item 1).";

        [Fact(Skip = ClampingSkip)]
        public void LiveVotesClampAndUpdateInPlace()
        {
            // S8(a): repeated Live-page votes land clamped [0,100] with in-place chip updates.
        }

        const string EtagSkip =
            "S8(b) — s8smoke, scratch stack, 2026-07-12: inserted a second row directly via SQL " +
            "into the main-scope library (media id 2, library_id=1, state=ready/measurable/" +
            "eligible). GET /api/media/2 returned ETag W/\"934\"; POST /api/media/2/vote " +
            "{\"direction\":\"up\"} returned {\"score\":51} (the F33.2 unrated-default-50, " +
            "first-vote-51 shape S2's spec already pins at the repository layer); PATCH " +
            "/api/media/2 with If-Match: W/\"934\" (the PRE-VOTE ETag) returned 204 (NOT " +
            "409/412) with a fresh ETag W/\"936\" — the vote never bumped library.media's xmin " +
            "(F33.1), so the open tag-edit's ETag survived the vote. This reconfirms S2's real-" +
            "Postgres xmin-stability spec through the actual HTTP PATCH pipeline " +
            "(MediaController), not just the repository call directly.";

        [Fact(Skip = EtagSkip)]
        public void AVoteDoesNotInvalidateAnOpenTagEditEtag()
        {
            // S8(b): GET detail ETag → vote → PATCH with the pre-vote ETag still succeeds (F33.1).
        }
    }

    // ---------------------------------------------------------------------
    // (c)+(h) — never-play out-of-scope success + the aged-out-row Catalog find
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioNeverPlayEndToEnd
    {
        const string OutOfScopeSkip =
            "S8(c) — s8smoke, scratch stack, 2026-07-12 (re-proving S3's own s3smoke shape for " +
            "the gate record, per this dispatch's instruction): the F27 boot-seeded safe row " +
            "(media id 1, library_id=2 \"safe\", OUTSIDE the default main scope [1]) — PUT " +
            "/api/media/1/never-play {\"neverPlay\":true} returned 200 {\"neverPlay\":true} " +
            "(NOT 403 — F33.5's deliberate F23.4 exception holds through the real pipeline, not " +
            "just RatingController unit specs); GET /internal/safe-track went from 200 (a real " +
            "annotate: line) to 204 No Content immediately after; PUT .../never-play " +
            "{\"neverPlay\":false} (un-X) returned 200, and the very next GET /internal/safe-" +
            "track returned 200 with the same annotate: line again — F33.6 exclusion and " +
            "restoration both confirmed live, one poll each direction.";

        [Fact(Skip = OutOfScopeSkip)]
        public void XOnAnOutOfScopeSafeRowSucceedsAndSuppressesSelection()
        {
            // S8(c): X a seeded safe-library row (out of main scope) — no 403; /internal/safe-track
            // stops returning it; un-X restores (F33.5–F33.6).
        }

        const string AgedOutSkip =
            "S8(h) — the UI half (a row no longer inside the play-history ring becoming " +
            "findable via the Catalog's never-play filter, and the restore control) is pinned by " +
            "S7's jest suite (catalog-rating.spec.tsx, green in this run's admin-ui wall) — the " +
            "dispatch names this as already-proven UI behavior, not to be re-derived here. The " +
            "SERVER half is curled per the dispatch's own instruction: s8smoke, 2026-07-12 — PUT " +
            "/api/media/2/never-play {\"neverPlay\":true} on the main-scope row, then GET " +
            "/api/media?never-play=true (no library-id param — the default Catalog browse scope) " +
            "returned exactly that row ({\"mediaId\":\"2\",...,\"neverPlay\":true}) — findable " +
            "independent of whether it is still inside the bounded play-history ring the Live " +
            "page renders (F33.10/F33.12); PUT .../never-play {\"neverPlay\":false} (restore) " +
            "then the identical GET returned [] — the row left the flagged-only view the instant " +
            "it was restored.";

        [Fact(Skip = AgedOutSkip)]
        public void AnAgedOutFlaggedRowIsFindableAndRestorableFromTheCatalog()
        {
            // S8(h): ?never-play=true finds a row no longer in the history ring; restore from Catalog (F33.10/F33.12).
        }
    }

    // ---------------------------------------------------------------------
    // (d) — the gitea-#188 standalone guarantee under a real bulk operation
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioStandaloneGuarantee
    {
        const string Skip =
            "S8(d) — s8smoke, scratch stack, 2026-07-12: inserted two more rows via SQL into the " +
            "safe library (media ids 3, 4), then voted id 3 down to score 1 and id 4 up to score " +
            "100 through the real vote endpoint. `select media_id, score, never_play from " +
            "library.media_rating order by media_id` before any bulk call: (1,0,f) (2,51,f) " +
            "(3,1,f) (4,100,f). POST /api/media/eligibility {\"eligible\":false,\"filter\":" +
            "{\"libraryId\":2}} -> 200 {\"affected\":3} (`select id,library_id,eligible from " +
            "library.media where library_id=2` confirmed all three flipped to eligible=false); " +
            "the SAME rating SELECT immediately after was byte-identical: (1,0,f) (2,51,f) " +
            "(3,1,f) (4,100,f). Flipped back {\"eligible\":true} on the same filter (200 " +
            "{\"affected\":3}) and re-ran the rating SELECT a third time — still byte-identical. " +
            "A full false-then-true bulk round-trip across three rows left every score and every " +
            "never_play flag untouched (F33.7) through the real POST /api/media/eligibility " +
            "endpoint, not just the repository unit specs.";

        [Fact(Skip = Skip)]
        public void ABulkEligibilityFlipLeavesEveryScoreAndFlagUntouched()
        {
            // S8(d): bulk flip across a library; SELECT proves rating rows byte-identical (F33.7).
        }
    }

    // ---------------------------------------------------------------------
    // (e) — SafeScope depletion warns truthfully and degrades to mksafe, never crashes
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioDegradedModesStillDegrade
    {
        const string Skip =
            "S8(e) — s8smoke, scratch stack, 2026-07-12: with three eligible, unflagged rows in " +
            "the safe library (GET /api/status showed safeScope.playable=3), PUT .../never-play " +
            "{\"neverPlay\":true} on all three (ids 1, 3, 4) — the VERY NEXT GET /api/status " +
            "showed safeScope.playable drop to 0 (the F31.4/F31.5 badge's truth source, within " +
            "the same request cycle); GET /internal/safe-track returned 204. `docker compose -p " +
            "s8smoke logs engine --tail` over the following ~10s showed no exception/crash/" +
            "traceback (grepped the full engine log for fatal|exception|crash|traceback — zero " +
            "hits) and captured the engine's own transition line: `[mksafe:3] Switch to " +
            "safe_blank with transition.` — the real F4.4/F18.5 degrade path firing, not a stall " +
            "or unexplained dead air; `docker compose -p s8smoke ps engine` reported Up/healthy " +
            "throughout. Un-flagging all three rows afterward recovered GET /api/status to " +
            "playable=3 within the very next poll — onset AND recovery both proven, the same " +
            "pattern R10 established for the depleted-vs-empty badge pair.";

        [Fact(Skip = Skip)]
        public void AnAllFlaggedSafeScopeWarnsAndDrainsToMksafe()
        {
            // S8(e): depleted warning within one poll; forced drain → mksafe, no crash (F33.6–F33.7).
            // Scratch-stack only — never the operator's live station.
        }
    }

    // ---------------------------------------------------------------------
    // (f) — the ledger discipline: no score term in selection SQL, uniform distribution
    // ---------------------------------------------------------------------

    public sealed class ScenarioLedgerDiscipline
    {
        [Fact]
        public void SelectionSqlPinsTheNeverPlayOnlyPredicateWithNoScoreTerm()
        {
            // Real, always-run, non-Skip repo-content assertion (Story102/107's grep-assert
            // idiom) — no live stack needed. Pins the EXACT WHERE-clause fragments S3 shipped for
            // both GetRandomReadyAsync (the shared selection call behind main rotation,
            // /internal/safe-track, and /media/random) and GetStatusCountsAsync's playable count:
            // never_play is the only rating-table term either predicate references; score never
            // appears (F33.8). A future edit that adds a score term to either predicate breaks
            // one of these literal matches. The deeper behavioral guarantee — that divergent
            // scores don't skew a real 200-draw sample against real Postgres — is Story111's
            // ScenarioScoreNeverEntersSelection.DivergentScoresDoNotSkewSelection; not re-derived
            // here (DRY).
            var source = MediaRepositorySourceText;

            // GetRandomReadyAsync's predicate (main rotation / safe-track / media-random, F33.6).
            Assert.Contains(
                "where state = 'ready' and measurable and eligible and not coalesce(r.never_play, false) ",
                source, StringComparison.Ordinal);
            Assert.Contains(
                "and id <> all(@exclude) and library_id = any(@libraryIds) ",
                source, StringComparison.Ordinal);
            Assert.Contains("order by random() limit 1", source, StringComparison.Ordinal);

            // GetStatusCountsAsync's playable filter (feeds /api/status + the F31.4/F31.5 badges).
            Assert.Contains("where state = 'ready' and measurable and eligible", source, StringComparison.Ordinal);
            Assert.Contains("and not coalesce(r.never_play, false)", source, StringComparison.Ordinal);
            Assert.Contains(")::int as playable", source, StringComparison.Ordinal);
        }

        const string DistributionSkip =
            "S8(f) distribution spot-check — s8smoke, scratch stack, 2026-07-12: three unflagged, " +
            "eligible safe-library rows with scores 0 (id 1), 1 (id 3), and 100 (id 4) — a wider " +
            "spread than the dispatch's minimum two. 40 consecutive GET /internal/safe-track " +
            "calls, tallied by track_id: id 1 -> 15, id 3 -> 11, id 4 -> 14 (uniform-random " +
            "expectation over three rows is ~13.3 each) — no gross skew toward the score-100 row " +
            "and no starvation of the score-0/score-1 rows. This is the spot-check the dispatch " +
            "asked for, not a statistical proof — Story111's 200-draw repository-level spec " +
            "(against real Postgres) is the stronger presence proof and is not repeated here.";

        [Trait("Category", "Integration")]
        [Fact(Skip = DistributionSkip)]
        public void DistributionSpotCheckIsUniformAcrossDivergentScores()
        {
            // S8(f): a live distribution spot-check across scores confirms no selection skew.
        }
    }

    // ---------------------------------------------------------------------
    // (g) — tts:*/drain entries are structurally unvotable
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioUnvotableEntries
    {
        const string Skip =
            "S8(g) — the UI half (tts:* rows and the drain card rendering NO vote/never-play " +
            "controls) is pinned by S6's jest suite (live-rating.spec.tsx, green in this run's " +
            "admin-ui wall). The wire half: Story112's F33.9 spec already proves GET " +
            "/api/ratings silently skips a tts:abc id at the controller level. s8smoke, scratch " +
            "stack, 2026-07-12 adds the ROUTE-CONSTRAINT proof no existing spec covered: POST " +
            "/api/media/tts:abc123/vote and PUT /api/media/tts:abc123/never-play both returned " +
            "404 from ASP.NET Core's own routing layer (the `{id:long}` constraint rejects a " +
            "non-numeric segment before RatingController is ever constructed) — structurally " +
            "unvotable, not merely application-logic-unvotable; GET /api/ratings?ids=2,tts:abc " +
            "returned a single entry for id 2 only, reconfirming Story112's proof live.";

        [Fact(Skip = Skip)]
        public void TtsAndDrainEntriesAreUnvotable()
        {
            // S8(g): tts:*/drain rows render no controls (UI, S6-pinned) and 404 at the route
            // constraint for vote/never-play (server, s8smoke-pinned) — structurally unvotable.
        }
    }

    // ---------------------------------------------------------------------
    // (i) — full regression wall green
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioRegressionWall
    {
        const string DotnetEvidence =
            "S8(i) dotnet half — RUN 2026-07-12. `dotnet build GenWave.sln`: Build succeeded, " +
            "0 Warning(s), 0 Error(s). `dotnet test GenWave.sln --filter \"Category!=" +
            "Integration\"`: 541 passed, 0 failed, 69 skipped, 610 total across five projects " +
            "(Core 61/0/0/61, Orchestration 29/0/3/32, Tts 53/0/11/64, MediaLibrary 12/0/26/38, " +
            "Host 386/0/29/415 passed/failed/skipped/total — this file's own Category=Integration " +
            "facts are excluded from this filtered count by design, same as every prior gate; the " +
            "one exception is this file's OWN SelectionSqlPinsTheNeverPlayOnlyPredicateWithNoScore" +
            "Term repo-content fact just above, deliberately not Integration-tagged, which is the " +
            "\"+1\" over the old stub's contribution). Separately, `dotnet " +
            "test tests/GenWave.MediaLibrary.Tests` (unfiltered, exercising the project's OWN " +
            "self-bootstrapping DatabaseFixture — an isolated `docker compose -p genwave-libtest` " +
            "project, confirmed absent via `docker ps -a`/`docker compose ls` beforehand): 212 " +
            "passed, 0 failed, 47 skipped, 259 total; `docker ps -a` clean afterward. F2-F32 " +
            "gates stand: no prior epic's AcceptanceGate*.cs spec file was touched by this epic " +
            "(`git diff --stat d1b1e5a^..9902211` shows only this file plus new Epic-S-only spec " +
            "files touched under tests/; the one shared-infra edit was DatabaseFixture.cs gaining " +
            "a TRUNCATE CASCADE for the new child table, S1) — and every one of them is still " +
            "green in the run above. Zero touches to the production compose project either run " +
            "(confirmed distinct: the host's real deployment runs under a separate `mrdgenwave` " +
            "compose project at a different path entirely).";

        [Fact(Skip = DotnetEvidence)]
        public void FullDotnetSuiteIsGreen()
        {
            // AC-i — dotnet build zero-warnings + dotnet test green (CI wall + MediaLibrary's own harness)
        }

        const string AdminUiEvidence =
            "S8(i) admin-ui half — RUN 2026-07-12 from admin-ui/. `npx tsc --noEmit`: clean, zero " +
            "output. `npx jest`: 31 suites passed, 294 passed, 11 todo, 305 total (the one pre-" +
            "existing, harmless React act() warning in catalog-selection-toolbar.spec.tsx carried " +
            "unchanged since Q12/R13 — not a failure, not introduced here). `npm run build`: " +
            "green, 12 routes compiled (/, /_not-found, /catalog, /catalog/[mediaId], /dashboard, " +
            "/healthz, /icon.png, /libraries, /live, /login, /safe-content, /settings — same " +
            "route count as R13's wall). `grep -rn \"window.confirm(\" admin-ui/app admin-ui/" +
            "components`: zero call sites. `grep -rlE \"fonts.googleapis|fonts.gstatic\" admin-" +
            "ui/.next/static admin-ui/.next/server/app` (post-build): zero hits.";

        [Fact(Skip = AdminUiEvidence)]
        public void AdminUiTscJestAndBuildAreGreen()
        {
            // AC-i — tsc/jest/next build green; window.confirm grep zero; no external font/CDN request
        }
    }

    // ---------------------------------------------------------------------
    // (j) — Gitea issue closure is the operator's call
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioIssueClosureIsOperatorOwned
    {
        const string Skip =
            "S8(j) — Gitea state checked 2026-07-12 via the API (read-only; this gate never " +
            "closes issues, per instruction and the MEMORY.md house rule). gitea-#188 \"Implement Track " +
            "Rating\" (label genwave-2.0) is OPEN. Operator to close after reviewing this gate's " +
            "evidence (the runnable wall above + the operator checklist in docs/PLAN.md's Epic S " +
            "block-quote) and completing the live procedure items. This gate leaves the issue " +
            "exactly as found.";

        [Fact(Skip = Skip)]
        public void Issue188ClosesOnlyByOperatorDecision()
        {
            // AC-j — the epic isn't done while gitea-#188 is open; closing it is the operator's decision
        }
    }
}

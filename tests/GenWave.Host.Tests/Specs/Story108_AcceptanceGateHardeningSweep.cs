// STORY-108 — Acceptance gate: hardening sweep end-to-end + regression (Epic R / SPEC F29–F32)
//
// BDD specification — xUnit. R13 ran 2026-07-11/12 against isolated scratch-stack smokes only
// (own -p project, own .env, own ports/volumes, never the operator's live station) — the LIVE
// half is operator-pinned per this gate's own dispatch, matching the E10/W7/L8/K6/M8/N6/P9/Q12
// pattern. Every fact below is one of:
//   (1) a real, always-attempted, self-skipping HTTP-only check — Story013/Story082/Story094's
//       guarded-live-check idiom, [Trait("Category","Integration")] so it is excluded from the
//       `--filter "Category!=Integration"` wall run and opportunistically real whenever someone
//       runs the FULL suite against a reachable deployment;
//   (2) Skip-pinned with THIS SESSION's dated scratch-stack evidence (project names r1smoke
//       through r11smoke, r12 = docs-only) — the Story082/094 idiom; or
//   (3) Skip-pinned with the EXACT operator procedure for the bits that genuinely need the live
//       station (real main-library content mid-gap, listener-side icy metadata, a human eyeballing
//       a browser tab, live re-proofs, issue closure).
// See docs/PLAN.md's Epic R block-quote for the consolidated operator checklist. No fact in this
// file keeps a bare "pending R13" reason.

using System.Net;
using System.Net.Http.Json;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAcceptanceGateHardeningSweep
{
    // ---------------------------------------------------------------------
    // Shared live-stack helpers (Story013/Story082/Story094's guarded-live-check idiom): a fact
    // that talks to a running stack tries it first and self-skips (returns without asserting)
    // rather than xUnit-Skip, so it stays a real assertion on any box where the stack is present.
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

    /// <summary>Guarded live-stack helper for the Next.js admin-ui origin (favicon is served there, not by the api).</summary>
    static class LiveAdminUi
    {
        public const string BaseUrl = "http://localhost:3000";

        public static async Task<bool> IsReachableAsync()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                using var response = await http.GetAsync(BaseUrl + "/login");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }

    // ---------------------------------------------------------------------
    // (a)+(b) — {StationName} expansion at the endpoint path + distinct boot-seed title
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioStationNameExpansionAndSeedTitle
    {
        const string ExpansionSkip =
            "R13(a) — r1smoke, scratch stack, 2026-07-11: POST /api/safe-segments with the raw " +
            "{StationName} template returned 201; the stub synth received \"You are listening to " +
            "Smoke Station. Smoke check.\" — no literal placeholder reached synthesis. The seed " +
            "path (SafeLoopSeeder) was confirmed expanded the same way. Story095/096's own specs " +
            "already pin the expansion LOGIC unit-level (the Story078/079 hole R1 closed); this " +
            "fact is the end-to-end confirmation that a real POST through the real pipeline never " +
            "regresses back to the pre-R1 literal-placeholder bug (gitea-#184).";

        [Fact(Skip = ExpansionSkip)]
        public void UiGeneratedSegmentSpeaksTheStationName()
        {
            // AC-a — F29.1: no literal {StationName} reaches synthesis on the endpoint path
        }

        const string SeedTitleSkip =
            "R13(b) — r1smoke, scratch stack, 2026-07-11: fresh-boot seed row title was " +
            "\"Please Stand By (Station Default)\", artist \"Smoke Station\" (F29.3, gitea-#185); a " +
            "manual POST /api/safe-segments with no Title field produced \"Please Stand By\" " +
            "unchanged — the two consumers of SafeSegmentAuthor.AuthorAsync stay distinguishable " +
            "and the manual default did not drift.";

        [Fact(Skip = SeedTitleSkip)]
        public void FreshSeedTitleIsStationDefaultAndManualDefaultUnchanged()
        {
            // AC-b — F29.3: boot-seed title carries the disambiguating suffix, manual default does not
        }
    }

    // ---------------------------------------------------------------------
    // (c) — voices dropdown live + Kokoro-down 502/fallback
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioVoicesLiveAndDegraded
    {
        [Fact]
        public async Task AnonymousVoicesRequestIsRejectedLive()
        {
            // AC-c partial — deny-by-default (F18.7's posture) re-verified against a REAL running
            // deployment, not just Story097's WebApplicationFactory coverage: pure read, no side
            // effects, safe to run unattended against any reachable stack. Self-skips when
            // localhost:8080 isn't up (Story013's guarded-live-check idiom).
            if (!await LiveApi.IsReachableAsync()) return;

            using var http = new HttpClient { BaseAddress = new Uri(LiveApi.BaseUrl), Timeout = TimeSpan.FromSeconds(10) };
            using var response = await http.GetAsync("/api/voices");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        const string DropdownSkip =
            "R13(c) remainder — r2smoke, scratch stack, 2026-07-11: GET /api/voices was 401 " +
            "anonymous, 200 with 67 real voices from the pinned kokoro-fastapi image cookie-" +
            "authed, still 200 (warm cache) with kokoro stopped, and 502 ProblemDetails once the " +
            "cache went cold with kokoro still down. r3smoke, same stack, real browser: the Voice " +
            "select rendered \"Station default\" first/selected plus the real listing, and on a " +
            "listing failure fell back to the shipped free-text input with a visible notice — " +
            "generation stayed possible either way (F29.4-5). Story097's specs already pin the " +
            "proxy/cache/502 logic against a stub Kokoro; this fact is the real-container, real-" +
            "browser confirmation.";

        [Fact(Skip = DropdownSkip)]
        public void VoicesDropdownListsRealVoicesAndFallsBackWhenKokoroIsDown()
        {
            // AC-c — F29.4-5: real Kokoro listing renders; Kokoro-down degrades to free-text, never blocks generation
        }
    }

    // ---------------------------------------------------------------------
    // (d) — recorded drain gap ≈ configured value; cutback within one cycle
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioSafeTrackGapLive
    {
        const string GapMatchesSkip =
            "R13(d) first half — r4smoke, scratch stack, 2026-07-11: a 55 s drain recording showed " +
            "five consecutive ~7.42 s silences between safe segments (configured " +
            "GW_SAFE_GAP_SECONDS=7.0 + segment lead/trail padding accounts for the extra ~0.42 s). " +
            "r5smoke, same stack: PUT GW_SAFE_GAP_SECONDS=3 (F19 allowlist, engine-" +
            "restart applyMode) -> engine-config reflected 3 -> engine restart -> re-recorded gaps " +
            "measured 3.42 s, same padding offset, confirming R5's knob actually reaches the " +
            "engine-side blank(duration=...) the R4 --check spike verdict (docs/MEMORY.md " +
            "2026-07-11, 'R4 spike verdict') picked (candidate 1, append). Numbers match the " +
            "configured value within the expected padding delta at both settings.";

        [Fact(Skip = GapMatchesSkip)]
        public void RecordedDrainGapMatchesTheConfiguredValue()
        {
            // AC-d first half — F29.6: recorded inter-safe-track gap ≈ GW_SAFE_GAP_SECONDS + padding
        }

        const string CutbackSkip =
            "R13(d) second half — NOT PROVEN this session (the dispatch's own evidence bank flags " +
            "it explicitly): the scratch stack used for r4smoke/r5smoke had no real main-library " +
            "content to cut back TO mid-gap, only the safe rotation, so the fallback(track_sensitive" +
            "=false, [main, safe]) preemption timing was never actually exercised — only the " +
            "engine's --check typecheck and the gap-duration recording were. This is the one " +
            "genuinely unproven mechanic in the whole gate (operator checklist item 2, docs/" +
            "PLAN.md). Operator procedure after deploying this branch: during a real drain (main " +
            "library empty or all-ineligible, matching Epic P's operator-testing drain pattern, " +
            "docs/MEMORY.md 2026-07-10), re-enable/re-populate main content WHILE a safe-track gap " +
            "is actively playing (append(safe, blank(duration=gap)) is `is_ready()` while " +
            "streaming, so fallback should preempt it immediately, same as preempting any other " +
            "safe track) and confirm main cuts back within one source-switch cycle, not waiting " +
            "out the rest of the gap.";

        [Fact(Skip = CutbackSkip)]
        public void CutbackToMainHappensWithinOneSourceSwitchCycleMidGap()
        {
            // AC-d second half — F29.6: main preempts the gap's blank() the instant it is ready again
        }
    }

    // ---------------------------------------------------------------------
    // (e) — seeded segment airs with artist = station name across now-playing/history/Icecast
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioArtistFidelityLive
    {
        const string NowPlayingSkip =
            "R13(e) — r6smoke, scratch stack, 2026-07-11: /api/now-playing returned " +
            "{\"title\":\"Please Stand By (Station Default)\",\"artist\":\"Gap Smoke FM\"} for an " +
            "engine-initiated safe play; /api/play-history matched; a raw telnet " +
            "output.icecast.metadata read confirmed the engine frame itself carries artist/title " +
            "(the actual root-caused layer per docs/MEMORY.md 2026-07-11 'R6 root cause': " +
            "PlayoutFeeder.TickAsync's cache-once-forever guard, fixed via feederOwnedIds so a " +
            "miss self-heals on the next advance). This is the telnet-frame / server-side " +
            "observable, deliberately NOT the listener-side icy StreamTitle sampling — see the " +
            "sibling fact below for that distinct, still-unproven half.";

        [Fact(Skip = NowPlayingSkip)]
        public void SeededSegmentCarriesTheStationNameAsArtistAcrossNowPlayingAndHistory()
        {
            // AC-e first half — F29.9/F24.1: artist round-trips through /api/now-playing + /api/play-history
        }

        const string IcyMetadataSkip =
            "R13(e) remainder, operator procedure — the r6smoke evidence above proves the ENGINE " +
            "frame carries artist/title (telnet output.icecast.metadata + the api's own now-" +
            "playing/history reads, which are fed from the same feeder path); it does NOT prove " +
            "what an actual Icecast LISTENER sees, which rides the mp3/ogg stream's own icy " +
            "StreamTitle metadata block, a separate propagation path this gate cannot sample " +
            "without a real streaming client. Also flagged live during R6 (docs/MEMORY.md 2026-07-" +
            "11, 'R6 root cause', final paragraph): a SECOND real defect was found and NOT fixed — " +
            "Liquidsoap's own output.icecast.metadata was observed to retain the PREVIOUS track's " +
            "artist across a track boundary when the new track's annotate: correctly omits an " +
            "empty artist; two engine-side spikes to fix it both regressed track_id detection " +
            "worse than the bug and were reverted (engine/genwave.liq is byte-identical to pre-R6). " +
            "Operator procedure: point a real stream client (e.g. `ffprobe -v quiet -show_entries " +
            "format_tags=StreamTitle icy://localhost:8000/stream` or a browser's now-playing " +
            "overlay) at the live Icecast mount during a drain and confirm the branded artist " +
            "shows, not a stale prior track's artist — this is operator checklist item 3 " +
            "(docs/PLAN.md) and also the trigger for filing the metadata-bleed follow-up as a new " +
            "issue (checklist item 6).";

        [Fact(Skip = IcyMetadataSkip)]
        public void IcecastListenerMetadataShowsTheBrandedArtistDuringADrain()
        {
            // AC-e second half — F24: the LISTENER-side icy StreamTitle, distinct from the server-side telnet frame
        }
    }

    // ---------------------------------------------------------------------
    // (f) — the P9 repro is green with no api restart
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioMainScopeLivenessRepro
    {
        const string Skip =
            "R13(f) — r7smoke, scratch stack, 2026-07-11: the exact P9 repro re-run against the " +
            "R7 fix. Reenrich against a library outside main scope 403'd as expected; a live PUT " +
            "Station:Scope:LibraryIds=[1,2] (widening scope, no api restart) was followed " +
            "immediately by the same reenrich call returning 202; Catalog browse against the newly" +
            "-in-scope library went from 0 rows to 1. Confirms F30's fix (StationContext sheds " +
            "Scope; Orchestrator/MediaController/ReenrichController read IOptionsMonitor at use " +
            "time, killing the transitional hard-coded LibraryScope([1L])) actually closes the " +
            "P9(h) side-finding (docs/MEMORY.md 2026-07-10) live, not just in Story102's in-" +
            "process specs. Story102's own ThePNineReproPasses fact stays pinned in that file for " +
            "the same reason this one does: re-running it live means writing a real scope change " +
            "to whatever deployment it targets, which is not something to do unattended against " +
            "the operator's real station.";

        [Fact(Skip = Skip)]
        public void ThePNineReproPassesWithNoRestart()
        {
            // AC-f — F30: a live main-scope PUT reaches Orchestrator/MediaController/ReenrichController without an api restart
        }
    }

    // ---------------------------------------------------------------------
    // (g) — double-toggle succeeds; forced conflicts toast and recover
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioPatchEtagChainLive
    {
        const string Skip =
            "R13(g) — r8smoke, scratch stack, 2026-07-11: PATCH /api/media/{id} returned 204 with " +
            "ETag: W/\"761\" (a real xmin, not a placeholder); a chained PATCH using that ETag " +
            "returned 204 with ETag: W/\"762\"; a third PATCH reusing the now-stale W/\"761\" " +
            "returned 409 (F31.1 — every successful PATCH now carries the RETURNING xmin as its " +
            "own ETag, closing the gitea-#181 stale-version hole). r9smoke, same stack, real browser " +
            "(next dev + Playwright per docs/MEMORY.md 2026-07-11 'R9 follow-ups'): the eligibility " +
            "toggle double-flip (off, immediately back on) passed live — no error toast, no stuck " +
            "checkbox, DB confirmed both writes; a forced out-of-scope 403 surfaced a real toast " +
            "where the pre-R9 code silently swallowed it (F28.9 closes the silent-swallow class). " +
            "Not re-run as an unattended fact: a live PATCH chain mutates real catalog rows on " +
            "whatever deployment it targets, which this gate is not authorized to do unattended.";

        [Fact(Skip = Skip)]
        public void DoubleToggleSucceedsAndForcedConflictToastsAndRecovers()
        {
            // AC-g — F31.1-F31.3: PATCH ETag chain lets an immediate retry succeed; a real conflict surfaces a toast, never swallowed
        }
    }

    // ---------------------------------------------------------------------
    // (h) — depleted-SafeScope badges on settings + dashboard within one poll
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioDepletedSafeScopeBadgesLive
    {
        const string Skip =
            "R13(h) — r10smoke, scratch stack, 2026-07-11, real browser: depleting SafeScope's " +
            "playable set (all safe-scope rows ineligible) produced the settings-page picker badge " +
            "and the dashboard SafeScope tile's warning state within one poll interval; re-adding " +
            "an eligible row cleared both within one poll (recovery, not just onset); the F25.4 " +
            "empty-scope badge and this depleted-but-non-empty badge stayed visibly distinct the " +
            "whole time (no state collision). Optional real-deployment reconfirmation is operator " +
            "checklist item 4 (docs/PLAN.md) — same underlying poll/status contract Q2 shipped, so " +
            "this is a UI-state confirmation, not a new wire seam.";

        [Fact(Skip = Skip)]
        public void DepletedSafeScopeBadgesShowOnSettingsAndDashboardWithinOnePoll()
        {
            // AC-h — F29.7 (gitea-#186): depleted-vs-empty SafeScope states are visibly distinct and self-recover within one poll
        }
    }

    // ---------------------------------------------------------------------
    // (i) — favicon legible in both browser chromes
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioFaviconLive
    {
        [Fact]
        public async Task IconPngAndFaviconIcoServeWithCorrectContentTypes()
        {
            // AC-i partial — the App Router file-convention wiring, re-verified against a REAL
            // running admin-ui, not just the build-output check in the wall below. Pure GET, no
            // auth needed (favicon must serve on the unauthenticated /login page too), no side
            // effects. Self-skips when localhost:3000 isn't up.
            //
            // 2026-07-12: the mark changed from R11's icon.svg to the operator's GenWave-logo.png
            // shipped as app/icon.png (see wireless-favicon.spec.ts's amendment note) — this fact
            // probes the CURRENT asset paths; the old /icon.svg probe failed for real against the
            // rebuilt deployment (text/html 404), which is exactly what a live check is for.
            if (!await LiveAdminUi.IsReachableAsync()) return;

            using var http = new HttpClient { BaseAddress = new Uri(LiveAdminUi.BaseUrl), Timeout = TimeSpan.FromSeconds(10) };

            using var icon = await http.GetAsync("/icon.png");
            Assert.Equal(HttpStatusCode.OK, icon.StatusCode);
            Assert.StartsWith("image/png", icon.Content.Headers.ContentType?.MediaType ?? "", StringComparison.OrdinalIgnoreCase);

            using var favicon = await http.GetAsync("/favicon.ico");
            Assert.Equal(HttpStatusCode.OK, favicon.StatusCode);
            Assert.Contains("icon", favicon.Content.Headers.ContentType?.MediaType ?? "", StringComparison.OrdinalIgnoreCase);
        }

        const string ChromeSkip =
            "R13(i) remainder — r11smoke, scratch stack, 2026-07-11 (icon.svg era) and the live " +
            "deployment, 2026-07-12 (icon.png, the operator's logo): /login's HTML carried both " +
            "icon links; both served 200 with the content-types the fact above now " +
            "checks live; auth stayed intact (favicon serving didn't leak an authenticated route). " +
            "What no HTTP check can see is actual tab-icon LEGIBILITY against a real browser chrome " +
            "— an operator eyeball call, not an assertion. Operator procedure: open the admin-ui in " +
            "a light-chrome browser profile and a dark-chrome one (or toggle OS-level light/dark, " +
            "which most browser chromes follow) and confirm the tab icon reads clearly in both " +
            "(this is operator checklist item 5, docs/PLAN.md).";

        [Fact(Skip = ChromeSkip)]
        public void TabFaviconIsLegibleInBothBrowserChromes()
        {
            // AC-i remainder — design-aesthetic/SKILL.md's Wireless mark stays legible in light + dark browser chrome
        }
    }

    // ---------------------------------------------------------------------
    // (j) — zero unreconciled CI-gate claims
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioNoUnreconciledCiGateClaims
    {
        const string Skip =
            "R13(j) — already a REAL, always-run, non-Skip fact, not duplicated here: " +
            "FeatureSmokeTestManualGateDocs.ScenarioNoStaleCiClaims.NoUnreconciledCiGateClaimRemains" +
            "InRepoDocs (Story107) greps README.md + every docs/*.md file for CI-gate claims on " +
            "every wall run, filtered or not — it is green in today's wall below. R12's own gate " +
            "evidence (docs/PLAN.md, story:STORY-107): 3/3 facts passing, sweep clean except the " +
            "one intentionally-flagged PROJECT.md V1 criterion (out of scope for /explore, not this " +
            "gate). Re-asserting the same grep here would just be two facts proving one thing — the " +
            "DRY call is to point at Story107's fact rather than fork its regex.";

        [Fact(Skip = Skip)]
        public void NoUnreconciledCiGateClaimRemainsAnywhereInRepoDocs()
        {
            // AC-j — F32: see Story107's ScenarioNoStaleCiClaims for the real, always-run assertion
        }
    }

    // ---------------------------------------------------------------------
    // (k) — full regression wall green
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioRegressionWall
    {
        const string DotnetEvidence =
            "R13(k) dotnet half — RUN 2026-07-11. `dotnet build GenWave.sln`: Build succeeded, " +
            "0 Warning(s), 0 Error(s). `dotnet test GenWave.sln --filter \"Category!=" +
            "Integration\"`: 519 passed, 0 failed, 69 skipped, 588 total across five projects (Core " +
            "60/0/0/60, Orchestration 29/0/3/32, Tts 52/0/11/63, MediaLibrary 11/0/26/37, Host 367/" +
            "0/29/396 passed/failed/skipped/total — this file's own Category=Integration facts, " +
            "including its two live guarded checks, are excluded from this filtered count by " +
            "design, same as Story082/094). Separately, `dotnet test tests/GenWave." +
            "MediaLibrary.Tests` (unfiltered, exercising the project's OWN self-bootstrapping " +
            "DatabaseFixture — an isolated `docker compose -p genwave-libtest` project on port " +
            "55433, confirmed distinct from the production `genwave` project via `docker ps -a` " +
            "before running — compose.yaml's `db` service publishes NO port and is on the `data` " +
            "network only): 183 passed, 0 failed, 47 skipped, 230 total, `docker ps -a` clean of " +
            "both projects afterward. Zero touches to the production compose project either run.";

        [Fact(Skip = DotnetEvidence)]
        public void FullDotnetSuiteIsGreen()
        {
            // AC-k — dotnet build zero-warnings + dotnet test green (CI wall + MediaLibrary's own harness)
        }

        const string AdminUiEvidence =
            "R13(k) admin-ui half — RUN 2026-07-11 from admin-ui/. `npx tsc --noEmit`: clean, zero " +
            "output. `npx jest`: 29 suites passed, 268 passed, 11 todo, 279 total (one pre-existing, " +
            "harmless React act() console warning in catalog-selection-toolbar.spec.tsx's " +
            "eligibility path — CatalogToolbar's onBusyChange(false) firing after unmount in the " +
            "test; not a failure, not introduced here — carried unchanged from Q12). `npm run " +
            "build`: green, 12 routes compiled (/, /_not-found, /catalog, /catalog/[mediaId], " +
            "/dashboard, /healthz, /icon.svg, /libraries, /live, /login, /safe-content, /settings — " +
            "one more than Q12's 11 because R11 added the /icon.svg route). `grep -rn " +
            "\"window.confirm(\" admin-ui/app admin-ui/components`: zero call sites. `grep -rl " +
            "\"fonts.googleapis|fonts.gstatic\" admin-ui/.next/static admin-ui/.next/server/app` " +
            "(post-build): zero hits — fonts stay self-hosted local woff2 (Q1, unchanged by Epic R).";

        [Fact(Skip = AdminUiEvidence)]
        public void AdminUiTscJestAndBuildAreGreen()
        {
            // AC-k — tsc/jest/next build green; window.confirm grep zero; no external font/CDN request
        }
    }

    // ---------------------------------------------------------------------
    // (l) — Gitea issue closure is the operator's call
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioIssueClosureIsOperatorOwned
    {
        const string Skip =
            "R13(l) — Gitea state checked 2026-07-11 via the API (read-only; this gate never closes " +
            "issues, per instruction and the MEMORY.md house rule). All ten targeted issues are " +
            "OPEN: gitea-#179 (smoke_test.sh CI-gate docs, created 2026-07-10), gitea-#180 (live main-scope " +
            "PUT / IOptions.Value, 2026-07-10), gitea-#181 (eligibility toggle stale xmin, 2026-07-10), " +
            "gitea-#182 (safe-rotation gap, 2026-07-10), gitea-#183 (voices dropdown, 2026-07-10), gitea-#184 " +
            "({StationName} not expanded, 2026-07-10), gitea-#185 (boot-seed title, 2026-07-10), gitea-#186 " +
            "(depleted-SafeScope warning, 2026-07-10), gitea-#192 (artist metadata on the safe " +
            "announcement, 2026-07-11), gitea-#193 (favicon, 2026-07-11). Operator to close after " +
            "reviewing this gate's evidence (the runnable wall above + the operator checklist in " +
            "docs/PLAN.md's Epic R block-quote) and completing the live procedures items 1-6. This " +
            "gate leaves every issue exactly as found.";

        [Fact(Skip = Skip)]
        public void TheTenTargetedIssuesCloseOnlyByOperatorDecision()
        {
            // AC-l — the epic isn't done while any of gitea-#179-186,gitea-#192,gitea-#193 is open; closing them is the operator's decision
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — degraded modes never crash
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioDegradedModesStillDegrade
    {
        const string Skip =
            "Sad-path gate — composes evidence already gathered above rather than re-deriving it: " +
            "r2smoke/r3smoke (2026-07-11) showed Kokoro-down degrading to a 502 ProblemDetails on " +
            "POST /api/safe-segments and the Voice dropdown falling back to free-text, never a " +
            "crash and never a stuck form; r10smoke (2026-07-11) showed a depleted SafeScope " +
            "degrading to the F4.4 mksafe silence backstop with a truthful warning badge, never " +
            "undegraded dead air with no explanation. mksafe's own ops-error-only leaf is untouched " +
            "by R4's deliberate gap (docs/MEMORY.md 2026-07-11 'R4 spike verdict' — the two paths " +
            "stay visibly distinct code, not shared). Every degrade observed this session surfaced " +
            "a truthful signal (toast, badge, or ProblemDetails) rather than silence or a crash.";

        [Fact(Skip = Skip)]
        public void KokoroDownAndDepletedScopeDegradeNeverCrash()
        {
            // voices 502 + form fallback; generate 502 ProblemDetails; drain -> mksafe per
            // F4.4 -- never a crash, never undegraded silence.
        }
    }
}

// STORY-094 — Acceptance gate: Wireless redesign end-to-end + regression (Epic Q / SPEC F28)
//
// BDD specification — xUnit. Q12 ran 2026-07-11. The operator's live production stack was NOT up
// during this run and the dispatch's own guardrail forbids bringing it up ("no compose commands
// against it, no wipes, no drains" — the LIVE half of this gate is operator-pinned by design,
// matching the E10/W7/L8/K6/M8/N6/P9 pattern). Every fact below is one of:
//   (1) a real, always-attempted, self-skipping HTTP-only check — Story013/Story082's
//       guarded-live-check idiom, [Trait("Category","Integration")] so it is excluded from the
//       `--filter "Category!=Integration"` wall run and opportunistically real whenever someone
//       runs the FULL suite against a reachable deployment;
//   (2) Skip-pinned with today's dated wall evidence (the runnable half — dotnet + admin-ui); or
//   (3) Skip-pinned with the EXACT operator procedure to run after deploying this branch
//       (`docker compose down && ./build.sh && ./launch.sh`).
// See docs/PLAN.md's Epic Q block-quote for the consolidated operator checklist.
//
// The UI-side STRUCTURAL logic this gate cares about (theme cookie/toggle, poll pause/resume,
// drain-state rendering, 502-toast rendering) is already unit-proven in admin-ui jest specs — green
// as part of the wall (app-shell.spec.tsx, dashboard-page.spec.tsx, live-on-air-view.spec.tsx,
// safe-content-page.spec.tsx, safe-content-redesign.spec.tsx). What remains unprovable here is the
// LIVE, real-browser half: an actual paint, an actual poll tick against a moving on-air track, an
// actual forced drain/Kokoro-down observed by a human. Those stay operator procedures.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAcceptanceGateWirelessRedesign
{
    // ---------------------------------------------------------------------
    // Shared live-stack helper (Story013/Story082's guarded-live-check idiom): a fact that talks
    // to the running stack tries it first and self-skips (returns without asserting) rather than
    // xUnit-Skip, so it stays a real assertion on any box where the stack + ADMIN_PASSWORD are
    // actually present.
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

        /// <summary>Reads ADMIN_PASSWORD from the environment, falling back to the repo-root .env.</summary>
        public static string? ReadAdminPassword()
        {
            var fromEnv = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");
            if (!string.IsNullOrEmpty(fromEnv)) return fromEnv;

            var envFilePath = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".env"));
            if (!File.Exists(envFilePath)) return null;

            try
            {
                foreach (var line in File.ReadAllLines(envFilePath))
                {
                    var trimmed = line.Trim();
                    if (!trimmed.StartsWith("ADMIN_PASSWORD=", StringComparison.Ordinal)) continue;
                    var value = trimmed["ADMIN_PASSWORD=".Length..];
                    return value.Length > 0 ? value : null;
                }
            }
            catch (IOException)
            {
                return null;
            }

            return null;
        }

        /// <summary>Cookie-authenticated client, or null when the stack/password isn't available.</summary>
        public static async Task<HttpClient?> TryLoginAsync()
        {
            if (!await IsReachableAsync()) return null;

            var password = ReadAdminPassword();
            if (password is null) return null;

            var handler = new HttpClientHandler { CookieContainer = new CookieContainer() };
            var http = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(10) };

            var login = await http.PostAsJsonAsync("/api/auth/login", new { password });
            if (login.IsSuccessStatusCode) return http;

            http.Dispose();
            return null;
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — (d) /api/status truthfulness on the live deployment
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioStatusTruthfulOnLiveStack
    {
        [Fact]
        public async Task SafeScopeMatchesTheLiveOverlay()
        {
            // Q12(d) — pure reads, no side effects: GET /api/status's safeScope.libraryIds must equal
            // whatever Station:SafeScope:LibraryIds currently resolves to via GET /api/settings (the
            // Q2/StatusController IOptionsMonitor live-read contract). Self-skips when the live stack
            // or ADMIN_PASSWORD isn't available, so this stays a real, always-attempted assertion
            // rather than a static Skip pin.
            using var http = await LiveApi.TryLoginAsync();
            if (http is null) return;

            var status = await http.GetFromJsonAsync<JsonElement>("/api/status");
            var statusLibraryIds = status.GetProperty("safeScope").GetProperty("libraryIds")
                .EnumerateArray().Select(e => e.GetInt64()).ToArray();

            var settings = await http.GetFromJsonAsync<JsonElement>("/api/settings");
            var safeScopeSetting = settings.EnumerateArray()
                .FirstOrDefault(e => e.GetProperty("key").GetString() == "Station:SafeScope:LibraryIds");

            if (safeScopeSetting.ValueKind == JsonValueKind.Undefined) return;

            var rawValue = safeScopeSetting.GetProperty("value").GetString() ?? "[]";
            using var parsed = JsonDocument.Parse(rawValue);
            var settingLibraryIds = parsed.RootElement.EnumerateArray().Select(e => e.GetInt64()).ToArray();

            Assert.Equal(settingLibraryIds, statusLibraryIds);
        }

        const string CatalogCountsSkip =
            "Q12(d) first half — needs a raw grouped count straight from Postgres to cross-check " +
            "against, and the production db service publishes no port (compose.yaml: `networks: " +
            "[data]`, no `ports:`) — only `docker compose exec` reaches it, which this gate is not " +
            "authorized to run against the live project. Operator procedure after deploying this " +
            "branch (`docker compose down && ./build.sh && ./launch.sh`): (1) log in at " +
            "http://localhost:3000/login, open DevTools → Network, GET http://localhost:8080/api/status " +
            "(cookie-authed) and note catalog.{ready,enriching,failed,unavailable}; (2) " +
            "`docker compose exec db psql -U genwave -d genwave -c \"select state, count(*) from " +
            "library.media group by state\"`; (3) confirm the two sets agree 1:1 (`ready`↔ready, " +
            "`discovered`↔enriching, `failed`↔failed, `unavailable`↔unavailable — StatusController.cs " +
            "and MediaRepository.GetStatusCountsAsync's grouped query, unchanged by Epic Q).";

        [Fact(Skip = CatalogCountsSkip)]
        public void CatalogCountsMatchDirectDbCounts()
        {
            // AC-d — GET /api/status catalog counts == direct DB grouped counts
        }

        const string LiveScopePutSkip =
            "Q12(d) second half — a live PUT to Station:SafeScope:LibraryIds changes production " +
            "config (fires the IOptionsMonitor reload token StatusController reads from) even when " +
            "the round-trip writes back the SAME value; per this gate's own guardrail the live half " +
            "is operator-pinned by design, so an unattended write against the operator's real station " +
            "is not this gate's call to make, even a value-preserving one. Operator procedure: on the " +
            "Settings page (http://localhost:3000/settings), note the current 'Scope' section SafeScope " +
            "value, PUT it back unchanged via `PUT /api/settings/Station:SafeScope:LibraryIds` (or the " +
            "form's Save button) and confirm the very next `GET /api/status` reflects it with no api " +
            "restart (the Q2/StatusController IOptionsMonitor contract) — or, more usefully, make a " +
            "real curation change if one is already due and confirm the same round-trip.";

        [Fact(Skip = LiveScopePutSkip)]
        public void LiveScopePutIsVisibleOnNextGet()
        {
            // AC-d — a live SafeScope PUT is reflected on the next GET, no api restart
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — (a) every shipped flow through the redesigned console
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioShippedFlowsSurviveTheRedesign
    {
        const string Skip =
            "Q12(a) — a full click-through of the redesigned console is a human/browser exercise, not " +
            "an in-process C# assertion; the wire contracts each flow calls are unchanged (byte-" +
            "compatible request bodies per Q7-Q10's own acceptance) and are covered by the shipped " +
            "Host.Tests endpoint specs plus admin-ui jest specs — this fact is the LIVE walk proving " +
            "the new presentation didn't break the wiring. Operator procedure after deploying this " +
            "branch: log in at http://localhost:3000/login; browse/filter the Catalog tab; open a " +
            "track, edit a tag (confirm the If-Match 409-and-reload path if a stale edit is handy), " +
            "move it to another library (confirm the X-Out-Of-Scope dialog+toast on a cross-scope " +
            "move), reanalyze it (confirm the 202 toast); select two+ rows in Catalog and run a bulk " +
            "action from the selection toolbar (Reassign / Re-enrich / Eligibility — confirm the " +
            "count-bearing confirm dialog); switch to the Libraries tab and create/rename/delete a " +
            "scratch library (confirm the 409 dependentMediaCount dialog on a library that still has " +
            "media); on Settings, save a live-applyable field (e.g. Loudness) and confirm no restart " +
            "is needed, then save a restart-flagged field and confirm its badge says so; on Safe " +
            "content, generate a segment (confirm the success toast prepends the new row); sign out " +
            "(F28.15) and confirm the session cookie no longer authenticates. Every success/failure " +
            "surface must be a toast or inline error per F28.9 — no bare browser alert/confirm.";

        [Fact(Skip = Skip)]
        public void EveryShippedFlowCompletesAgainstUnchangedEndpoints()
        {
            // AC-a — SPEC F28.9 feedback behavior across the full write surface
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — (b) both themes paint correctly + toggle live-switches
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioBothThemesPaintCorrectlyLive
    {
        const string Skip =
            "Q12(b) — the cookie-read/data-theme wiring and no-flash first render are already unit-" +
            "proven by admin-ui's app-shell.spec.tsx (green in the wall below); what remains is an " +
            "actual browser paint, which no jsdom/xUnit check can see. Operator procedure: (1) with " +
            "no genwave-theme cookie set (DevTools → Application → Cookies → delete it), load " +
            "http://localhost:3000/login and confirm it paints the system-preferred theme with no " +
            "flash of the other theme; (2) set the cookie to `dark` and reload — confirm dark paints " +
            "immediately on first paint (view-source / inspect <html data-theme=\"dark\">); repeat for " +
            "`light`; (3) log in, click the theme toggle (aria-label 'Switch to light theme' / 'Switch " +
            "to dark theme') in the sidebar footer and confirm the whole console re-paints instantly " +
            "with NO Network request for a page navigation (DevTools → Network stays quiet other than " +
            "the poll calls) — only the genwave-theme cookie's value changes; (4) reload the page and " +
            "confirm the toggled theme persisted from the cookie.";

        [Fact(Skip = Skip)]
        public void FirstRenderMatchesTheThemeCookieInBothThemes()
        {
            // AC-b — no wrong-theme flash on first paint, light and dark
        }

        [Fact(Skip = Skip)]
        public void ToggleSwitchesLiveWithoutAReload()
        {
            // AC-b — theme toggle re-paints in place, no navigation, cookie updated
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — (c) real on-air advance within one poll interval + hidden-tab pause
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioOnAirAdvanceReflectsWithinOnePollInterval
    {
        const string Skip =
            "Q12(c) — the poll/pause/resume MECHANICS (5 s cadence, Page Visibility pause+immediate-" +
            "resume-fetch) are already unit-proven by admin-ui's dashboard-page.spec.tsx and live-on-" +
            "air-view.spec.tsx (green in the wall below, driven by fake timers + a mocked " +
            "visibilitychange event). What remains unprovable in-process is a REAL track change on the " +
            "REAL engine surfacing through a REAL poll tick — needs the live stack + a human clock. " +
            "Operator procedure: open http://localhost:3000/dashboard and http://localhost:3000/live " +
            "side by side; wait for a natural on-air crossfade (or note the current elapsed reading); " +
            "within 5 s (usePoll's intervalMs, SPEC F28.8) of the actual track change confirm BOTH " +
            "pages' Now Playing card title/artist update and the elapsed readout resets near 0 — DevTools " +
            "→ Network should show a fresh GET /api/now-playing roughly every 5 s. For the hidden-tab " +
            "half: switch away from the Live tab for >15 s and confirm (Network tab) polling STOPS " +
            "while hidden; switch back and confirm an immediate GET fires (not a wait for the next " +
            "5 s tick) and the on-screen state catches up right away.";

        [Fact(Skip = Skip)]
        public void ARealTrackAdvanceAppearsOnDashboardAndLiveWithinOnePollInterval()
        {
            // AC-c — F28.7/F28.8: dashboard + Live both reflect a real advance within one 5 s tick
        }

        [Fact(Skip = Skip)]
        public void HiddenTabPausesPollingAndResumesImmediatelyOnReturn()
        {
            // AC-c — F28.8: Page Visibility pause/resume observed against the real poll loop
        }
    }

    // ---------------------------------------------------------------------
    // (e) Regression wall — pinned with today's dated evidence (running dotnet-in-dotnet / npm-in-
    //     dotnet is not the house style; see docs/PLAN.md's Epic Q block-quote for the same numbers).
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioRegressionWall
    {
        const string DotnetEvidence =
            "Q12(e) dotnet half — RUN 2026-07-11. `dotnet build GenWave.sln`: Build succeeded, 0 " +
            "Warning(s), 0 Error(s). `dotnet test GenWave.sln --filter \"Category!=Integration\"`: " +
            "477 passed, 0 failed, 65 skipped, 542 total across five projects (Core 60/0/0/60, " +
            "Orchestration 29/0/3/32, Tts 52/0/11/63, MediaLibrary 11/0/26/37, Host 325/0/25/350 " +
            "passed/failed/skipped/total — Host's Category=Integration facts, including this file's " +
            "own live-only classes, are excluded from this filtered count by design, same as " +
            "Story082's file). Separately, `dotnet " +
            "test tests/GenWave.MediaLibrary.Tests` (unfiltered, exercising the project's OWN " +
            "self-bootstrapping DatabaseFixture — an isolated `docker compose -p genwave-libtest` " +
            "project on port 55433, initialised from the SAME db/01-library.sh + 06-station-settings-" +
            "migration.sh as production, torn down with `down -v` on completion; confirmed distinct " +
            "from the production `genwave` project before running — compose.yaml's `db` service " +
            "publishes NO port and is on the `data` network only): 181 passed, 0 failed, 47 skipped, " +
            "228 total, no containers left behind afterward (`docker ps -a` clean of both projects post-" +
            "run). Zero touches to the production compose project either run.";

        [Fact(Skip = DotnetEvidence)]
        public void FullDotnetSuiteIsGreen()
        {
            // AC-e — dotnet build zero-warnings + dotnet test green (CI wall + MediaLibrary's own harness)
        }

        const string AdminUiEvidence =
            "Q12(e) admin-ui half — RUN 2026-07-11 from admin-ui/. `npx tsc --noEmit`: clean, zero " +
            "output. `npx jest`: 25 suites passed, 232 passed, 11 todo, 243 total (one pre-existing, " +
            "harmless React act() console warning in catalog-selection-toolbar.spec.tsx's eligibility " +
            "path — CatalogToolbar's onBusyChange(false) firing after unmount in the test; not a " +
            "failure, not introduced by Q12, out of this gate's scope to fix). `npm run build`: green, " +
            "11 routes compiled (/, /_not-found, /catalog, /catalog/[mediaId], /dashboard, /healthz, " +
            "/libraries, /live, /login, /safe-content, /settings). `grep -rn \"window.confirm(\" " +
            "admin-ui/app admin-ui/components`: zero call sites (the only remaining mentions of the " +
            "literal string live in __specs__ comments/descriptions documenting its removal, and one " +
            "doc-comment in MoveToLibraryAction.tsx — no live call site). `grep -rl " +
            "\"fonts.googleapis|fonts.gstatic\" admin-ui/.next/static admin-ui/.next/server/app` " +
            "(post-build): zero hits; Fraunces/Source Sans 3 ship as self-hosted local woff2 under " +
            "`.next/static/media/*.woff2` (Q1's next/font/local wiring).";

        [Fact(Skip = AdminUiEvidence)]
        public void AdminUiTscJestAndBuildAreGreen()
        {
            // AC-e — tsc/jest/next build green; window.confirm grep zero; no external font/CDN request
        }
    }

    // ---------------------------------------------------------------------
    // (g) Gitea issue state — read-only; the gate never closes issues (MEMORY.md house rule)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioIssueClosureIsOperatorOwned
    {
        const string Skip =
            "Q12(g) — Gitea state checked 2026-07-11 via the API (read-only; this gate never closes " +
            "issues, per instruction and the MEMORY.md house rule). gitea-#174 'Admin UI needs a new look' " +
            "(label genwave-2.0) is OPEN, created 2026-07-10, zero comments — operator to close after " +
            "reviewing this gate's evidence (the runnable wall above + the operator checklist in " +
            "docs/PLAN.md's Epic Q block-quote) and completing the live procedures (a)-(c),(f) once " +
            "the stack is deployed. This gate leaves the issue exactly as found.";

        [Fact(Skip = Skip)]
        public void Issue174ClosureIsTheOperatorsCall()
        {
            // AC-g — the epic isn't done while gitea-#174 is open; closing it is the operator's decision
        }
    }

    // ── SAD PATH (degraded states, live) ─────────────────────────────────

    [Trait("Category", "Integration")]
    public sealed class ScenarioDegradedStatesRenderTruthfully
    {
        const string DrainSkip =
            "Q12(f) first half — the drain-state RENDER logic (NowPlayingCard's `state.kind === " +
            "\"drain\"` → 'Safe rotation — drain state.') is already unit-proven by admin-ui's " +
            "dashboard-page.spec.tsx and live-on-air-view.spec.tsx (green in the wall above); an " +
            "actual forced drain on THIS host's live station is not authorized by this gate (same " +
            "posture as P9(a)/N6 — forcing a drain risks real dead air on the operator's broadcast). " +
            "Operator procedure: either (preferred, matches the P9/N6 pattern) stand up a throwaway " +
            "compose project against scratch .env/volumes (`docker compose -p genwave-q12check --env-" +
            "file .env.q12check up -d`) and force a drain there, or — on THIS host, briefly — pause " +
            "the feeder just long enough to observe one drain tick; watch http://localhost:3000/" +
            "dashboard and confirm the Now Playing card shows 'Safe rotation — drain state.' within " +
            "one poll interval, then let normal programming resume.";

        [Fact(Skip = DrainSkip)]
        public void DrainStateShowsOnTheDashboardCard()
        {
            // AC-f — drain state renders truthfully on the dashboard card, live
        }

        const string KokoroSkip =
            "Q12(f) second half — the 502-to-error-toast RENDER logic is already unit-proven by admin-" +
            "ui's safe-content-page.spec.tsx and safe-content-redesign.spec.tsx (green in the wall " +
            "above, mocked 502 response); re-confirms P6/P9(e)'s prior live proof that a Kokoro-down " +
            "POST /api/safe-segments 502s with nothing persisted. Stopping the shared kokoro container " +
            "is disruptive to run as a routine assertion (it takes down real TTS-authoring capability " +
            "for the duration) — same judgment call P9(e) made. Operator procedure: `docker compose " +
            "stop kokoro`; open http://localhost:3000/safe-content, fill the Generate form, submit " +
            "(button reads 'Generating…' and disables); confirm an error toast appears (message text " +
            "from the 502 ProblemDetails' `detail`, or 'Unexpected error (502)' if the body doesn't " +
            "parse) and the form RE-ENABLES with the Generate button clickable again — not stuck " +
            "disabled (the K5 stuck-Saving… regression class must stay dead); confirm no new row " +
            "appears in the segment list. `docker compose start kokoro` afterward to restore.";

        [Fact(Skip = KokoroSkip)]
        public void KokoroDownGenerateSurfacesAnErrorToastNotAStuckForm()
        {
            // AC-f — F27.3 degrade surfaces as a toast; the form recovers, not a stuck disabled state
        }
    }
}

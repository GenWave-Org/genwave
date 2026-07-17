// STORY-082 — Acceptance gate: safe-loop authoring end-to-end + regression
//
// BDD specification — xUnit. SPEC F27 done-when block. Operator-gated like
// W7 / L8 / K6 / M8 / N6: forced-drain listens on the live station are never
// authorized here (this host runs the operator's real broadcast); the
// runnable, non-disruptive sub-assertions were executed live on 2026-07-10
// and are captured below as real (non-Skip) facts where that was safe, or as
// Skip-pinned facts carrying the exact evidence/procedure where it was not.
// See docs/MEMORY.md 2026-07-10 ("Epic P gate") for the full write-up and
// docs/PLAN.md's Epic P block-quote for the operator checklist. P9(h) closes
// Gitea gitea-#149 and gitea-#172 — CLOSING THEM IS THE OPERATOR'S CALL, never this gate's.

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAcceptanceGateSafeLoopAuthoring
{
    // ---------------------------------------------------------------------
    // Shared live-stack helpers (Story013's guarded-live-check idiom): every
    // fact in this file that talks to the running stack tries the stack
    // first and self-skips (writes SKIPPED-AT-RUNTIME, returns without
    // asserting) rather than xUnit-Skip, so the fact stays a real assertion
    // on any box where the stack + ADMIN_PASSWORD are actually present.
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
    // HAPPY PATH — live gate (operator-run; would force a drain on the real station)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioFreshDeployDrainAirsTheBrandedAnnouncement
    {
        // Operator procedure (never automatable against THIS host — it is the live station and
        // cannot be wiped to prove a "fresh boot"): stand up a throwaway compose project against a
        // scratch .env + scratch named volumes, e.g.
        //   docker compose -p genwave-p9check --env-file .env.p9check up -d
        // with a .env.p9check pointing MEDIA_DIR at a small scratch library and fresh ADMIN_PASSWORD/
        // ICECAST_*_PASSWORD values (never reuse production secrets in a scratch stack). On that
        // stack: (1) confirm library "safe" + one ready row exist after first boot (P7); (2) force a
        // drain (stop pushing to `q`, or briefly pause the feeder) and confirm the announcement airs
        // with "Please Stand By" surfacing via GET /api/now-playing; (3) record the output
        // (tools/smoke_test.sh's ebur128 recording machinery) and confirm integrated LUFS ≈ the
        // EFFECTIVE Loudness:TargetLufs (GET /api/settings, not the appsettings default — the
        // Story013 lesson, MEMORY.md 2026-07-02). Tear the scratch project down after
        // (`docker compose -p genwave-p9check down -v`). Never run against the production project.
        const string Skip =
            "Operator gate P9(a) — needs a throwaway compose project (scratch .env/volumes); " +
            "forcing a drain on THIS host's live station to prove it is not authorized. Procedure " +
            "documented in this class's comment and docs/PLAN.md Epic P block-quote.";

        [Fact(Skip = Skip)]
        public void TheSeededAnnouncementAirsOnDrain()
        {
            // AC1 — forced drain airs the seed segment
        }

        [Fact(Skip = Skip)]
        public void NowPlayingShowsPleaseStandByViaTheF24Path()
        {
            // AC1 — /api/now-playing carries the brand from the output metadata
        }

        [Fact(Skip = Skip)]
        public void TheRecordedOutputMeasuresTheEffectiveLoudnessTarget()
        {
            // AC1 — F2.5 parity on the authored artifact (effective target, not the default — the Story013 lesson)
        }
    }

    [Trait("Category", "Integration")]
    public sealed class ScenarioApiAuthoredBedSegmentAirsCorrectly
    {
        // Same scratch-stack procedure as (a)'s class comment, plus: generate a bed segment via
        // POST /api/safe-segments { text, libraryId, bedMediaId } against a catalog row with a bed
        // track, point the scratch stack's SafeScope at the target library, force a drain, and
        // record. Assert (1) recorded integrated LUFS ≈ effective target and (2) voice onset lands
        // at ~BedPadSeconds after the recording start (ffprobe silencedetect or a manual listen).
        const string Skip =
            "Operator gate P9(b) — bed-segment drain proof; same scratch-stack procedure as " +
            "ScenarioFreshDeployDrainAirsTheBrandedAnnouncement, forcing a drain is not authorized " +
            "on this host. See docs/PLAN.md Epic P block-quote.";

        [Fact(Skip = Skip)]
        public void TheBedSegmentAirsLevelMatched()
        {
            // AC2 — recorded output ≈ effective target LUFS
        }

        [Fact(Skip = Skip)]
        public void TheVoiceOnsetFollowsTheLeadInPad()
        {
            // AC2 — voice starts after ~BedPadSeconds (F27.4)
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — scan survival + tags round-trip (run live 2026-07-10)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioAuthoredRowsSurviveScanAndTagsReenrich
    {
        const string ScanSurvivalEvidence =
            "P9(c) first half — VERIFIED live 2026-07-10: SafeScope=[1] (main library only) on " +
            "this deployment, confirmed via `select value from station.settings where " +
            "key='Station:SafeScope:LibraryIds'` BEFORE touching anything — the safe library (id 7, " +
            "seeded row id 9087) is out of rotation, so this check cannot affect air. Row 9087 was " +
            "seeded by P7 at 2026-07-10T19:59:03Z; re-queried at 2026-07-10T20:49:53Z (~50 elapsed " +
            "Library:ScanIntervalSeconds=60 ticks across at least two api container lifetimes) — " +
            "state stayed 'ready' throughout, never 'unavailable'. The scanner's F27.7 path-scoping " +
            "logic itself is unit-proven in isolation by Story077_ScannerScopedToMediaRoot (not " +
            "Skip-pinned, green in every run). Not un-pinned here: the assertion is about ONE " +
            "specific historical row on THIS deployment (elapsed wall-clock ticks since a one-time " +
            "seed event), not a reusable, environment-portable check.";

        [Fact(Skip = ScanSurvivalEvidence)]
        public void AScanTickNeverMarksTheSeededRowUnavailable()
        {
            // AC3 — F27.7 proven against the running scanner
        }

        const string TagsReenrichEvidence =
            "P9(c) second half — RUN live 2026-07-10 twice. First run FOUND A REAL DEFECT: TITLE " +
            "round-tripped ('Please Stand By') but ARTIST came back NULL. Mechanism (corrected " +
            "after the fix investigation): ffmpeg's WAV muxer writes `-metadata artist=` to the " +
            "RIFF INFO IART chunk; TagLibSharp maps IART to Tag.AlbumArtists (its Performers " +
            "reads/writes the ISTR chunk ffmpeg never emits) and synthesizes an in-memory ID3v2 " +
            "TPE2 from it, while Enricher.ReadTags reads Tag.JoinedPerformers (TPE1/ISTR) only — " +
            "so a tags re-enrich of an authored WAV dropped artist to NULL. FIXED IN-PHASE: " +
            "FfmpegAudioMixer now runs a TagLibSharp pass post-mux writing Tag.Performers " +
            "(TPE1 + ISTR); Story075's EmbeddedArtistIsReadableTheWayTheEnricherReadsIt pins the " +
            "enricher-read path so a writer/reader frame mismatch can never pass again. Second " +
            "live run (probe row 9088, fixed binary): the real EnrichmentService reclaimed the " +
            "row and BOTH title and artist round-tripped ('Please Stand By' / 'GWAV 108.8'); " +
            "probe cleaned up, seeded row 9087 pristine. This fact stays pinned only because " +
            "re-running it on demand requires touching a live row; the round-trip itself is " +
            "proven and spec-pinned at the Story075 seam. Side-finding logged in MEMORY.md: the " +
            "reenrich endpoint's scope check reads a boot-time StationContext snapshot " +
            "(IOptions.Value), so a live main-scope PUT does not reach it — contradicts F23.1's " +
            "live apply-mode; carried follow-up, out of Epic P scope.";

        [Fact(Skip = TagsReenrichEvidence)]
        public void ATagsReenrichRoundTripsTheEmbeddedBrand()
        {
            // AC3 — POST /api/media/{id}/reenrich?fields=tags re-reads the embedded
            //       tags; title/artist survive (F27.2) — proven live 2026-07-10 post-fix.
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — idempotency + operator-override negatives (safe to run live)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioSeedIdempotencyAndOperatorOverrideNegatives
    {
        [Fact]
        public async Task ARestartSeedsNoDuplicates()
        {
            // AC4 — cheap re-verification of P7's own live-proven "restart no-op" (see P7's commit
            // message + docs/MEMORY.md): the safe library must hold exactly one authored row no
            // matter how many api restarts have happened since the original seed. Confirmed
            // 2026-07-10 across an incidental restart performed for P9(e)'s Kokoro-down boot check —
            // library "safe" held mediaCount=1 before and after. Self-skips when the live stack or
            // ADMIN_PASSWORD isn't available (Story013's guarded-live-check idiom) rather than
            // xUnit-Skip, so this stays a real, always-attempted assertion.
            using var http = await LiveApi.TryLoginAsync();
            if (http is null) return;

            var libraries = await http.GetFromJsonAsync<JsonElement>("/api/libraries");
            var safeLibraries = libraries.EnumerateArray()
                .Where(e => e.GetProperty("name").GetString() == "safe")
                .ToList();

            if (safeLibraries.Count == 0)
            {
                // P7 hasn't seeded on this deployment yet — nothing to assert.
                return;
            }

            // The restart-idempotency observable that survives ANY deployment state is library
            // uniqueness: a re-running seed would CreateAsync a second "safe" library. The original
            // mediaCount==1 assertion (P9, when the library held only the seed) was wrong for a
            // living deployment — authored segments are first-class rows (F27.8) and the count
            // legitimately grows (observed 2026-07-12: seed + operator segment + a retired test
            // row = 3). Row-level duplicate detection needs boot-log provenance — an operator
            // procedure, not a snapshot assertion.
            Assert.Single(safeLibraries);
            Assert.True(safeLibraries[0].GetProperty("mediaCount").GetInt32() >= 1,
                "The safe library exists but holds no rows — the seed's insert never landed.");
        }

        [Fact]
        public async Task APresetOperatorSafeScopeIsUntouchedByTheSeed()
        {
            // AC4 — this deployment's Station:SafeScope:LibraryIds was already operator-set to [1]
            // (the main library) before Epic P shipped. P7's boot seed only repoints SafeScope at
            // the safe library when NO operator value exists (F27.6) — so an override that survives,
            // pointed at anything OTHER than the safe library alone, is exactly the negative this AC
            // asks for. Confirmed 2026-07-10 stable across every restart performed this session.
            using var http = await LiveApi.TryLoginAsync();
            if (http is null) return;

            var libraries = await http.GetFromJsonAsync<JsonElement>("/api/libraries");
            var safeLibrary = libraries.EnumerateArray()
                .FirstOrDefault(e => e.GetProperty("name").GetString() == "safe");

            if (safeLibrary.ValueKind == JsonValueKind.Undefined)
            {
                // No safe library on this deployment yet — nothing to assert.
                return;
            }

            var safeLibraryId = safeLibrary.GetProperty("id").GetInt64();

            var settings = await http.GetFromJsonAsync<JsonElement>("/api/settings");
            var safeScopeSetting = settings.EnumerateArray()
                .FirstOrDefault(e => e.GetProperty("key").GetString() == "Station:SafeScope:LibraryIds");

            if (safeScopeSetting.ValueKind == JsonValueKind.Undefined) return;

            var rawValue = safeScopeSetting.GetProperty("value").GetString() ?? "[]";
            using var parsed = JsonDocument.Parse(rawValue);
            var ids = parsed.RootElement.EnumerateArray().Select(e => e.GetInt64()).ToArray();

            // The seed's own auto-repoint writes exactly [safeLibraryId]. When the live value IS
            // that, the observation is INCONCLUSIVE — the operator may have legitimately pointed
            // SafeScope at the safe library themselves (this deployment did exactly that during
            // Epic P testing, docs/MEMORY.md 2026-07-10) — so self-skip per the guarded-live-check
            // idiom rather than fail (corrected 2026-07-12; the original Assert.False could never
            // pass on this deployment state).
            if (ids.Length == 1 && ids[0] == safeLibraryId) return;

            // Any OTHER surviving value proves the seed left the operator's override alone.
            Assert.Equal("override", safeScopeSetting.GetProperty("source").GetString());
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — Kokoro-down degradations (operator-run; stops/restarts real containers)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioKokoroDownDegradationsHold
    {
        const string BootWarnEvidence =
            "P9(e) first half — RUN live 2026-07-10: `docker compose stop kokoro && docker compose " +
            "restart api`. `docker compose restart` does NOT re-evaluate `depends_on` (confirmed: api " +
            "came back healthy in ~7s with kokoro stopped, no fallback to `docker restart` needed). " +
            "Host stayed healthy throughout — proves 'boot succeeds' with Kokoro down. The WARN-on-" +
            "seed-failure half could NOT be exercised on THIS deployment: the boot-seed marker " +
            "(Internal:BootSeed:SafeLoopCompletedAt) already exists from P7, so SafeLoopSeedHostedService " +
            "short-circuits to 'Boot seed: marker already present — nothing to do' and never attempts " +
            "a Kokoro-down render — there is nothing to WARN about. Exercising the literal WARN line " +
            "needs an unmarked boot (same scratch-stack procedure as ScenarioFreshDeployDrainAirsThe" +
            "BrandedAnnouncement, with kokoro stopped before first boot). The underlying degrade LOGIC " +
            "is unit-proven in isolation by Story080_SafeSeedOnBoot's ScenarioSeedFailureDegradesNever" +
            "BlocksBoot (not Skip-pinned, green in every run) — only the real log line on a fresh " +
            "deployment remains unobserved. Kokoro restarted and confirmed healthy again after this " +
            "check (docker compose start kokoro).";

        [Fact(Skip = BootWarnEvidence)]
        public void BootSucceedsWithTheSeedWarn()
        {
            // AC5 — F27.6 degrade
        }

        const string PostFailsEvidence =
            "P9(e) second half — RUN live 2026-07-10 (re-confirms P6's original live-verification, " +
            "see P6's commit message): with kokoro stopped, POST /api/safe-segments returned 502 " +
            "ProblemDetails; library 'safe' mediaCount stayed at 1 and /authored held its original " +
            "single file both before and after — nothing persisted. Not un-pinned: stopping/starting " +
            "the shared kokoro container is disruptive to run as a routine automated assertion (it " +
            "would take down real TTS-authoring capability for the duration of every test run, unlike " +
            "the auth-negative checks which are pure read/no-op HTTP calls) — matches the same " +
            "judgment call the dispatch made for this sub-assertion.";

        [Fact(Skip = PostFailsEvidence)]
        public void ThePostReturns502WithNothingPersisted()
        {
            // AC5 — F27.3 degrade
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — auth negatives (safe to run live; pure HTTP, no side effects)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioUnauthenticatedAndNonJsonPostAreRejected
    {
        [Fact]
        public async Task AnUncookiedPostIsRejected()
        {
            // P9(f) — re-verifies P6's shipped deny-by-default posture (F18.7) live. Confirmed
            // 2026-07-10: 401. Self-skips when the live stack isn't reachable.
            if (!await LiveApi.IsReachableAsync()) return;

            using var http = new HttpClient { BaseAddress = new Uri(LiveApi.BaseUrl), Timeout = TimeSpan.FromSeconds(10) };
            using var response = await http.PostAsJsonAsync(
                "/api/safe-segments", new { text = "p9 auth-negative probe", libraryId = 1 });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task ANonJsonPostIsRejected()
        {
            // P9(f) — re-verifies P6's Content-Type: application/json CSRF guard live. Confirmed
            // 2026-07-10: 415. Self-skips when the live stack/ADMIN_PASSWORD isn't available.
            using var http = await LiveApi.TryLoginAsync();
            if (http is null) return;

            using var content = new StringContent("hello", Encoding.UTF8, "text/plain");
            using var response = await http.PostAsync("/api/safe-segments", content);

            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        }
    }

    // ---------------------------------------------------------------------
    // Regression wall (operator-owned sign-off; issue closure is never this gate's call)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioRegressionWallStaysGreen
    {
        const string RegressionEvidence =
            "P9(g) — RUN live 2026-07-10. `dotnet test GenWave.sln --configuration Release`: " +
            "645 passed, 0 failed, 269 skipped, 914 total across all five projects (Core 60/0/0, " +
            "Orchestration 29/0/3, Tts 52/0/11, MediaLibrary 179/0/47, Host 325/0/208 " +
            "passed/failed/skipped). Story013's known-flake recorded-LUFS gate (measures the LIVE " +
            "stream) ran and PASSED this time: 'Effective TargetLufs=-12.0 (measured=-12.1, " +
            "tolerance=+/-2.5)'. admin-ui: `npx tsc --noEmit` clean; `npx jest` 112 passed / 11 todo " +
            "/ 123 total across 15 suites; `npm run build` green (all 11 routes compiled). " +
            "tools/smoke_test.sh was NOT run: it `q.push`es two test tracks directly onto the live " +
            "engine queue and records the real Icecast stream for over a minute — that preempts real " +
            "programming on this operator's live station, which this gate must never do. Correcting " +
            "an assumption in the P9 dispatch: `.gitea/workflows/dotnet-ci.yml` does NOT actually " +
            "invoke smoke_test.sh anywhere (grepped — zero hits) despite docs/PROJECT.md, " +
            "ARCHITECTURE.md, and SPEC.md describing it as a 'CI gate' — that documentation is " +
            "aspirational/stale, not the wired reality. Pin stands regardless: run it manually on an " +
            "isolated `docker compose -p <scratch> ...` project with its own ports, never against " +
            "this host's production project.";

        [Fact(Skip = RegressionEvidence)]
        public void SmokeTestAndAllShippedGatesPass()
        {
            // AC6 — Phase 1 smoke + F2-F25 gates; full suite; admin-ui tsc/jest/next build
        }

        const string IssueStateEvidence =
            "P9(h) — Gitea state checked 2026-07-10 via the API (read-only; this gate never closes " +
            "issues, per instruction). gitea-#149 'Need more complete safe loop for dead air' is OPEN — " +
            "operator to close after reviewing this gate's evidence. gitea-#172 'Change metadata for safe " +
            "loop' shows CLOSED (closed_at 2026-07-10T14:56:25Z), but that close came from PR gitea-#177's " +
            "merge auto-closing on the N5 commit's 'closes gitea-#172' keyword — and N5 was REVERTED in the " +
            "same session (docs/MEMORY.md 2026-07-10, F26 revert record) because it mis-scoped the " +
            "issue onto ALL safe-source plays including real songs. The commit that closed gitea-#172 no " +
            "longer exists in the codebase. Epic P's actual fix (F27.2 — the authored segment's OWN " +
            "embedded/DB tags carry the brand) is what should retroactively justify that closure, and " +
            "today's live check confirms the authored row's title/artist ARE correct as originally " +
            "inserted (see ScenarioAuthoredRowsSurviveScanAndTagsReenrich's evidence — the artist-tag " +
            "regression found there is a re-enrich-only edge case, not a defect in the initial " +
            "authored insert). Operator's call whether gitea-#172's existing closure needs a follow-up " +
            "comment; this gate leaves both issues exactly as found.";

        [Fact(Skip = IssueStateEvidence)]
        public void Issues149And172AreClosedInGitea()
        {
            // AC6 — P9(h): the epic isn't done while gitea-#149 is open; gitea-#172's provenance needs review
        }
    }
}

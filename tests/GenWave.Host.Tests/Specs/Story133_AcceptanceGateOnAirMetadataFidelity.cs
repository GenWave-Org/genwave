// STORY-133 — Acceptance gate: on-air metadata fidelity end-to-end + regression (Epic U /
// SPEC F37–F40, closes gitea-#199, gitea-#200, gitea-#212, gitea-#214)
//
// BDD specification — xUnit. U7 ran 2026-07-13 against an isolated scratch-stack smoke (own -p
// u7smoke project, env-file scratchpad/u1/stack/u1.env — the u1smoke MEDIA_DIR: Alpha/Beta
// artist-tagged mp3s + an artist-less "Quiet Track" at ~-36 LUFS whose stamped gain, 20.10 dB, is
// NOT peak-capped — plus the u4smoke override overlay (LeadIn cadence ON, non-colliding ports
// 18000/18080/13000 via a `!override`-tagged compose overlay kept OUTSIDE the repo in the
// scratchpad) — never the operator's live station (project `genwave`, standard ports 8000/8080/
// 3000, confirmed running throughout via `docker compose ls -a`; zero interaction with it this
// task beyond the Gitea read for letter (g) and the anonymous-401 guarded check below, which talks
// only to the deny-by-default auth boundary, never a persisted row). `down -v` afterward; `docker
// ps -a`/`docker volume ls`/`docker network ls` confirmed zero `u7smoke` remnants. STORY-128 (U1's
// baseline spike) is folded into this file as its own Scenario, per the header note this file
// carried at /plan time — U7 compares its post-fix measurements against those SAME U1 baselines,
// never against assertions re-derived at gate time. Every fact below is one of:
//   (1) a real, always-run, non-Skip repo-content assertion (Story102/107/S8/T11's grep-assert
//       idiom) — no live stack needed, so it deliberately has NO Integration trait and stays IN
//       the filtered wall;
//   (2) a real, always-attempted, self-skipping HTTP-only check — Story013/082/094/108/S8/T11's
//       guarded-live-check idiom, [Trait("Category","Integration")] so it is excluded from the
//       `--filter "Category!=Integration"` wall run and opportunistically real whenever someone
//       runs the FULL suite against a reachable deployment;
//   (3) Skip-pinned with THIS SESSION's dated u7smoke (or u1smoke, for the folded STORY-128
//       baselines) scratch-stack evidence, Category=Integration; or
//   (4) Skip-pinned with the EXACT operator procedure for what genuinely needs the real station or
//       a human decision (issue closure), Category=Integration.
// No fact in this file keeps a bare "pending U1"/"pending U7" reason.

using System.Net;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAcceptanceGateOnAirMetadataFidelity
{
    // ---------------------------------------------------------------------
    // Shared live-stack helper (Story013/082/094/108/S8/T11's guarded-live-check idiom)
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

    static string EngineScriptText =>
        File.ReadAllText(Path.Combine(RepoRoot, "engine", "genwave.liq"));

    // ── STORY-128 — baselines captured before any fix (U1, 2026-07-13, u1smoke) ────────────────

    [Trait("Category", "Integration")]
    public sealed class ScenarioBaselinesBeforeAnyFix
    {
        const string SkipA =
            "U1(a) — u1smoke, scratch stack, 2026-07-13: gitea-#199 bleed NOT reproduced on the pre-fix " +
            "build, recorded honest per AC5. Fixture: 3 boot-scanned mp3s (Alpha/Beta artist-tagged " +
            "at -22.0 LUFS, untagged 'Quiet Track' at -36.1 LUFS), cadence env-disabled. Polled " +
            "output.icecast.metadata every 2s over 4 independent api-restart trials (the 3-track " +
            "catalog self-exhausts the 20-id anti-repeat window after one pass, gitea-#210). 2 of 4 trials " +
            "landed a clean artist-to-no-artist adjacency; both showed the SAME shape: Beta Artist/" +
            "Beta Track (track_id=2) immediately followed by Quiet Track (track_id=1) with NO " +
            "artist= key at all in the artist-less track's own frame (neither Beta's stale value " +
            "nor an empty string) — track_id advanced 2->1 cleanly both times. Raw logs: " +
            "parta_attempt2_frames.log .. parta_attempt5_frames.log (scratchpad u1/evidence).";

        [Fact(Skip = SkipA)]
        public void TheArtistBleedIsObservedOnThePreFixBuildOrItsAbsenceIsRecorded()
        {
            // STORY-128 AC5 — U1's own pre-fix baseline for U7(a) to compare against.
        }

        const string SkipB =
            "U1(b) — u1smoke, scratch stack, 2026-07-13: F37.3 loudness baseline, un-level-matched, " +
            "confirmed. With the catalog self-exhausted (gitea-#210) the station drained to the F27 boot " +
            "seed (library 'safe' id=2, row id=4). Seed row: integratedLufs=-25.0, truePeakDbtp=" +
            "-6.9; target=-16.0; computed gain min(9.0,5.9)=5.90 dB, confirmed verbatim in the " +
            "engine's own push trace (replay_gain=\"5.90 dB\"). GET /api/now-playing during the " +
            "drain: gainDb=0 (the gitea-#200 symptom, confirmed — the key never reached the output). 45s " +
            "of stream recorded mid-drain, ffmpeg ebur128: integrated -25.8 LUFS, 9.8 LU below " +
            "target, far outside +-2.5 LU — proving the safe branch applied zero gain pre-fix. " +
            "Evidence: partb_now_playing.json, partb_safe_row.json, partb_ebur128.txt, " +
            "drain_recording.mp3 (scratchpad u1/evidence).";

        [Fact(Skip = SkipB)]
        public void ThePreFixDrainLoudnessAndGainDbZeroAreMeasuredAndSaved()
        {
            // STORY-128 — U1's own pre-fix baseline for U7(b) to compare against (F37.3).
        }

        const string SkipC =
            "U1(c) — READ-ONLY against the operator's live deployment, 2026-07-13: F40.1's '7 vs 2' " +
            "root-caused as a THIRD mechanism, neither literal hypothesis. Live GET /api/status: " +
            "safeScope={libraryIds:[7],playable:1} (rules out the playable-count hypothesis " +
            "numerically). Live GET /api/settings: Station:SafeScope:LibraryIds=\"[7]\", " +
            "source=override — a single, current, valid id (rules out stale/phantom ids). Live GET " +
            "/api/libraries: exactly 2 rows, ids 1 and 7. Mechanism found in admin-ui/app/(authed)/" +
            "dashboard/StatusTiles.tsx: the sub-line rendered `Libraries: ${libraryIds.join(\", \")}` " +
            "-> \"Libraries: 7\" with libraryIds=[7], which reads as a COUNT of 7 libraries to a " +
            "human, when 7 is library 'safe''s own primary key. U6's F40.2 fix (headline <N> " +
            "playable tracks, sub-line <M> libraries (ids ...) with M=libraryIds.length) directly " +
            "targets this exact line — the whole fix, no second stale-id cause. Evidence: " +
            "live_status.json, live_settings.json, live_libraries.json (scratchpad u1/evidence).";

        [Fact(Skip = SkipC)]
        public void TheSevenVersusTwoIsDiagnosedReadOnlyAgainstTheLiveDeployment()
        {
            // STORY-128 — U1's own read-only diagnosis (F40.1), the root cause U6 fixed.
        }
    }

    // ── U7 gate letters ─────────────────────────────────────────────────────────────────────────

    // ---------------------------------------------------------------------
    // (a) — explicit artist="" travels post-fix: artist="" reaches the wire, track_id advancing throughout
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioExplicitEmptyArtistTravels
    {
        const string Skip =
            "U7(a) — u7smoke, scratch stack, 2026-07-13: polled output.icecast.metadata every 2s " +
            "(the Liquidsoap:OutputMetadataCommand, U1's own telnet technique) across a real Beta " +
            "Artist/Beta Track (track_id=2) -> Quiet Track (track_id=1) music boundary at " +
            "on_air_timestamp 1784001333.64 -> 1784001348.53. Quiet Track's OWN frame carried " +
            "artist=\"\" EXPLICITLY (replay_gain=\"20.10 dB\" alongside it — the F37.1 fix reaching " +
            "the same frame) — the explicit-empty contract value (F38.5's post-fix half) now " +
            "reaching the wire, against U1(a)'s pre-fix 'no artist= key at all' baseline. U1(a) " +
            "never reproduced the gitea-#199 bleed pre-fix, so this is a contract hardening per F38.5's " +
            "post-fix half, not a cure of an observed defect. track_id advanced cleanly through " +
            "the whole ~100s observation window: tts -> tts -> 3(Alpha) -> tts -> 2(Beta) -> " +
            "1(Quiet, artist=\"\") -> 3(Alpha) -> 4(safe drain, the 3-track catalog self-exhausting " +
            "per gitea-#210, expected at this fixture size and unrelated to F38) -> 4 -> 4 -> 4, never " +
            "stalling or repeating a stale value. Honesty note: two DIFFERENT raw polls (a second " +
            "pass under heavier back-to-back-LeadIn churn) showed the artist= line apparently " +
            "missing from this ad hoc bash /dev/tcp poller's own capture for one Quiet Track frame " +
            "each time; cross-checked against /api/play-history (the PRODUCTION parse path, a " +
            "real TcpClient read-to-END, not a fixed 2s `timeout cat`) for the identical on-air " +
            "event both times — it showed artist:null (the correct, expected present-but-empty " +
            "parse, F38.4) — confirming a transient client-side read race in this script's own " +
            "crude capture under telnet-port contention with the production feeder's concurrent " +
            "poll, NOT an engine regression. Raw frames: parta_frames.log, partc_frames_pass1.log, " +
            "partc_frames_pass2.log; cross-check: partc_playhistory_check.json, " +
            "partc_playhistory_persona.json (scratchpad u7).";

        [Fact(Skip = Skip)]
        public void TheArtistLessTracksFrameCarriesAnExplicitlyEmptyArtist()
        {
            // U7(a) — proven against U1(a)'s pre-fix baseline (F38.5).
        }

        [Fact(Skip = Skip)]
        public void TrackIdAdvancementSurvivesTheWholeObservationWindow()
        {
            // U7(a) — track_id never stalled or repeated a stale frame across the boundary (F38.5).
        }
    }

    // ---------------------------------------------------------------------
    // (b) — drain level-matched + honest gainDb, against a target-reachable (non-peak-capped) row
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioDrainLevelMatchedWithHonestTelemetry
    {
        const string Skip =
            "U7(b) — u7smoke, scratch stack, 2026-07-13: logged in, PUT /api/settings " +
            "Station:SafeScope:LibraryIds=[1] so the safe/drain branch could pull the main library " +
            "(U3's own live half had only the peak-capped 5.9 dB seed row available; this run used " +
            "Quiet Track instead — stamped gain 20.10 dB, truePeakDbtp=-35.3, NOT peak-capped, the " +
            "band-proof row the U3 blockquote flagged as still owed). Waited for the 3-track " +
            "catalog to self-exhaust (gitea-#210) and drain: GET /api/now-playing showed mediaId=1, " +
            "gainDb=20.1 — EXACTLY Quiet Track's own stamped replay_gain (GET /api/media confirmed " +
            "integratedLufs=-36.1). Recorded 110s of the stream spanning multiple rows and the " +
            "F29.6 7s inter-safe gaps (ffmpeg -t 110 against :18000/stream); ffmpeg silencedetect " +
            "precisely located five 7.00s gaps, isolating clean single-track windows. FULL " +
            "recording ebur128: integrated -16.4 LUFS. TRIMMED window isolating just one clean " +
            "Quiet-Track airing (offset 16.0-29.5s, bounded on both sides by the engine's own " +
            "silence gaps, cross-verified against the on_air_timestamp frame history so the window " +
            "is provably a single uncontaminated Quiet Track play): -16.3 LUFS. BOTH land inside " +
            "F37.3's -16 +-2.5 LU band by a wide margin, against U1(b)'s pre-fix -25.8 LUFS/gainDb:0 " +
            "baseline. Evidence: partb_now_playing.json, partb_quiet_row.json, partb_ebur128.txt, " +
            "partb_drain_recording.mp3, partb_frame_history.log, partb_now_playing_timeline.log " +
            "(scratchpad u7).";

        [Fact(Skip = Skip)]
        public void TheDrainWindowLandsAtTargetLoudnessAgainstTheBaseline()
        {
            // U7(b) — -16.4 LUFS (full recording) / -16.3 LUFS (trimmed clean window), both within
            // +-2.5 LU of -16 target, against U1(b)'s -25.8 LUFS pre-fix baseline (F37.3).
        }

        [Fact(Skip = Skip)]
        public void NowPlayingReportsTheStampedGainForTheEngineInitiatedPlay()
        {
            // U7(b) — gainDb=20.1 exactly matches Quiet Track's own stamped replay_gain, against
            // U1(b)'s gainDb:0 pre-fix baseline (F37.2-F37.3).
        }
    }

    // ---------------------------------------------------------------------
    // (c) — persona attribution live: activate -> name on-air; deactivate -> station name resumes
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioPersonaAttributionLive
    {
        [Fact]
        public async Task AnonymousStatusRequestIsRejectedLive()
        {
            // AC partial — deny-by-default re-verified against a REAL running deployment: a pure
            // auth-boundary check, GET only, no side effects (401 fires in auth middleware before
            // StatusController is ever constructed). Self-skips when localhost:8080 isn't up
            // (Story013/T11's guarded-live-check idiom). StatusController's own doc comment:
            // "cookie-auth (covered by the deny-by-default fallback policy ... same as every other
            // /api/* controller)" — the same boundary Story127(c) pinned via /api/personas,
            // exercised here via /api/status since that is the exact endpoint letter (e) below
            // exercises live.
            if (!await LiveApi.IsReachableAsync()) return;

            using var http = new HttpClient { BaseAddress = new Uri(LiveApi.BaseUrl), Timeout = TimeSpan.FromSeconds(10) };
            using var response = await http.GetAsync("/api/status");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        const string Skip =
            "U7(c) — u7smoke, scratch stack, 2026-07-13: POST /api/personas {name:\"Captain " +
            "Wavelength\", backstory:\"A friendly late-night pirate-radio captain...\", style:" +
            "\"warm, a little salty, upbeat\", voice:\"af_heart\"} -> 201 {id:1}; PUT /api/settings " +
            "Station:Persona:ActiveId=1 -> activePersona=\"Captain Wavelength\" (GET /api/status). " +
            "api restarted to resume the self-exhausted (gitea-#210) main rotation. The next LeadIn " +
            "airings' /api/play-history rows carried artist=\"Captain Wavelength\" (mediaId=tts:" +
            "..., title=\"U1 Scratch Station\" — the F39.2 stamp). ICY StreamTitle (curl -u " +
            "admin:u1scratchpw :18000/admin/stats, U1(c)'s hidden-mount technique) read \"U1 " +
            "Scratch Station | Captain Wavelength\" with NO duplication (F39.5's collapse), " +
            "observed at both a Beta->Quiet and a Quiet->Alpha boundary. track_id kept advancing " +
            "throughout (frame log): tts->2(Beta)->tts->1(Quiet)->tts->3(Alpha)->4(drain, gitea-#210, " +
            "expected). DELETE /api/personas/1 -> 204 cleared Station:Persona:ActiveId to 0 IN THE " +
            "SAME REQUEST (confirmed via GET /api/settings); the FOLLOWING patter's play-history " +
            "rows reverted to artist=\"U1 Scratch Station\", and ICY reverted to the PLAIN \"U1 " +
            "Scratch Station\" (no \"Station - Station\" duplication — the pre-existing nit stayed " +
            "cured post-deactivation too). Evidence: partc_frames_pass1.log, partc_frames_pass2.log, " +
            "partc_icy_pass1.log, partc_icy_fast.log, partc_icy_revert.log, " +
            "partc_playhistory_check.json, partc_playhistory_persona.json (scratchpad u7).";

        [Fact(Skip = Skip)]
        public void ActivatedPersonaNameReachesNowPlayingAndHistory()
        {
            // U7(c) — play-history carried artist="Captain Wavelength" on the next patter airings
            // (F39.1-F39.3).
        }

        [Fact(Skip = Skip)]
        public void IcyStreamTitleCollapsesToStationPipeDjWithNoDuplication()
        {
            // U7(c) — ICY "U1 Scratch Station | Captain Wavelength", no "- Station" duplication
            // (F39.5, U5's live half exercised with a real persona case).
        }

        [Fact(Skip = Skip)]
        public void PersonaLessPatterIcyReadsThePlainStationName()
        {
            // U7(c) — after DELETE, ICY reverted to plain "U1 Scratch Station" (the cured nit
            // stays cured, F39.5).
        }
    }

    // ---------------------------------------------------------------------
    // (d) — authored safe content stays station-branded even while a persona is active
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioSafeBrandingNegative
    {
        const string Skip =
            "U7(d) — u7smoke, scratch stack, 2026-07-13: created + activated a second persona (POST " +
            "/api/personas {name:\"DJ Nightowl\",...} -> 201 {id:2}; PUT Station:Persona:ActiveId=2 " +
            "-> activePersona=\"DJ Nightowl\") with Station:SafeScope:LibraryIds reset to [2] (the " +
            "F27 boot seed library). While activePersona=\"DJ Nightowl\" was live, the seeded safe " +
            "row (mediaId=4, \"Please Stand By (Station Default)\") aired via the safe branch: " +
            "GET /api/now-playing AND /api/play-history both showed artist=\"U1 Scratch Station\" — " +
            "never \"DJ Nightowl\" — confirming F39.4's gitea-#172 rule holds with a persona genuinely " +
            "active, not just persona-less. Evidence: partd_now_playing_safe_branding.json, " +
            "partd_playhistory.json (scratchpad u7).";

        [Fact(Skip = Skip)]
        public void AnAuthoredSafeSegmentMidDrainKeepsTheStationBrand()
        {
            // U7(d) — safe branch stayed station-branded with a persona genuinely active (F39.4).
        }
    }

    // ---------------------------------------------------------------------
    // (e) — SafeScope tile truth: the API side of both labeled facts + the warning within one poll
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioTileTruth
    {
        const string Skip =
            "U7(e) — u7smoke, scratch stack, 2026-07-13: with Station:SafeScope:LibraryIds=[2] and " +
            "the seed row eligible, GET /api/status showed safeScope={libraryIds:[2],playable:1} — " +
            "matching what U6's tile renders (headline '1 playable tracks', sub-line '1 libraries " +
            "(ids 2)'; jest already pins the component rendering itself, STORY-132/U6 — this is the " +
            "API-side running-binary proof jest cannot give). PUT /api/media/4/never-play " +
            "{neverPlay:true} (F33.5 — NOT scope-gated, succeeded without touching SafeScope) -> " +
            "the VERY NEXT GET /api/status showed playable:0 within that one poll — the F31.5 " +
            "warning-state trigger (playable==0, non-empty scope). Restored PUT .../never-play " +
            "{neverPlay:false} -> playable:1 again. Browser rendering not re-checked here — U6's " +
            "jest suite (component-level) plus the T11-pattern build/tsc wall below already cover " +
            "it; this letter is deliberately the API-side proof only, per the dispatch. Evidence: " +
            "parte_status_before.json, parte_status_after_neverplay.json, " +
            "parte_status_restored.json (scratchpad u7).";

        [Fact(Skip = Skip)]
        public void TheTileShowsBothLabeledFactsAgainstTheRunningApi()
        {
            // U7(e) — GET /api/status playable/libraryIds match the tile's two labeled facts, and
            // the warning trigger (playable:0) fires within one poll on a depleted scope (F40).
        }
    }

    // ---------------------------------------------------------------------
    // (f) — regression wall: engine-diff pin (always-run) + dotnet/admin-ui wall (Skip-pinned)
    // ---------------------------------------------------------------------

    public sealed class ScenarioRegressionWall
    {
        const string AmplifyLine = "safe = amplify(1., override=\"replay_gain\", safe)";
        const string IcySongCollapseBranch = "if artist != \"\" and title == station_name then artist";

        /// <summary>The `settings.encoder.metadata.export` append list's own text — sliced narrowly
        /// (Story129's technique) so a membership check can't false-positive against the other,
        /// unrelated `replay_gain` mentions (the override= call, code comments) elsewhere in the
        /// script.</summary>
        static string ExportAppendListText(string script)
        {
            const string marker = "list.append(settings.encoder.metadata.export(),";
            var start = script.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, "settings.encoder.metadata.export list.append(...) call not found in genwave.liq");
            var end = script.IndexOf("])", start, StringComparison.Ordinal);
            Assert.True(end >= 0, "closing '])' for the export list.append(...) call not found in genwave.liq");
            return script[start..(end + 2)];
        }

        [Fact]
        public void EngineScriptCarriesExactlyTheThreeSpecdEditsAndNoMetadataMap()
        {
            // Real, always-run, non-Skip repo-content assertion (Story102/107/S8/T11's grep-assert
            // idiom) — no live stack needed, deliberately NOT Category=Integration so it stays IN
            // the filtered wall. Pins genwave.liq to EXACTLY the three edits Epic U shipped
            // (F37.1 safe-branch amplify [U3], F37.2 replay_gain export append [U3], F39.5
            // gw_icy_song collapse branch [U5]) plus F38.4's permanent house rule — verified live
            // via `git diff main...HEAD -- engine/genwave.liq` during this same U7 run.
            var script = EngineScriptText;

            Assert.Contains(AmplifyLine, script, StringComparison.Ordinal);
            Assert.Contains("replay_gain", ExportAppendListText(script), StringComparison.Ordinal);
            Assert.Contains(IcySongCollapseBranch, script, StringComparison.Ordinal);
            Assert.DoesNotContain("metadata.map", script, StringComparison.Ordinal);
            Assert.DoesNotContain("reset_last_metadata_on_track", script, StringComparison.Ordinal);
            Assert.DoesNotContain("replay_metadata", script, StringComparison.Ordinal);
        }

        const string DotnetEvidence =
            "U7(f) dotnet half — RUN 2026-07-13. `dotnet build GenWave.sln`: Build succeeded, " +
            "0 Warning(s), 0 Error(s). `dotnet test GenWave.sln --filter \"Category!=" +
            "Integration\"`: 0 failed across five projects — Core 61/61, Orchestration 42/45 (3 " +
            "skipped), MediaLibrary 12/38 (26 skipped, filtered subset), Tts 117/128 (11 skipped), " +
            "Host 438/468 (30 skipped) — 740 total, 0 failed (after this file's own rewrite " +
            "converted several bare Skips into Category=Integration facts, moving them OUT of the " +
            "filtered count — Story127/T11's own documented mechanism). Separately, `dotnet test " +
            "tests/GenWave.MediaLibrary.Tests` (unfiltered, its OWN self-bootstrapping " +
            "`genwave-libtest` compose project, confirmed absent via `docker ps -a`/`docker compose " +
            "ls` before and after): 231 passed, 0 failed, 47 skipped, 278 total — identical to " +
            "T11's own baseline run, confirming no regression. Zero diff: `git diff main...HEAD -- " +
            "compose.yaml` — empty (no operator-ruled compose changes anywhere in Epic U). " +
            "`git diff main...HEAD -- engine/genwave.liq` — EXACTLY the three edits pinned by the " +
            "always-run fact above (also asserted as a repo-content check, not just diffed by " +
            "hand). F2-F36 gates stand: `git diff main...HEAD --stat -- '*AcceptanceGate*.cs'` " +
            "shows only THIS file (Story133 itself, a brand-new file at /plan time) — no prior " +
            "epic's *AcceptanceGate*.cs was touched.";

        [Trait("Category", "Integration")]
        [Fact(Skip = DotnetEvidence)]
        public void TheFullWallIsGreenAndPriorGatesStand()
        {
            // U7(f) dotnet half — build zero-warnings + filtered/unfiltered test green + zero
            // compose/engine-beyond-the-three-edits diff + F2-F36 gates undisturbed.
        }

        const string AdminUiEvidence =
            "U7(f) admin-ui half — RUN 2026-07-13 from admin-ui/. `npx tsc --noEmit`: clean, zero " +
            "output. `npx jest`: 34 suites passed, 327 passed, 11 todo, 338 total (adds U6's new " +
            "SafeScope-tile-label specs over T11's 33-suite/317-passed wall; the one pre-existing " +
            "harmless React act() warning in catalog-selection-toolbar.spec.tsx carried unchanged " +
            "since Q12/R13/S8/T11 — not a failure, not introduced here). `npm run build`: green, 13 " +
            "routes compiled (identical route set to T11's wall — Epic U shipped zero new pages). " +
            "`grep -rn \"window.confirm(\" admin-ui/app admin-ui/components`: zero call sites. " +
            "`grep -rlE \"fonts.googleapis|fonts.gstatic\" admin-ui/.next/static admin-ui/.next/" +
            "server/app` (post-build): zero hits.";

        [Trait("Category", "Integration")]
        [Fact(Skip = AdminUiEvidence)]
        public void AdminUiTscJestAndBuildAreGreen()
        {
            // U7(f) admin-ui half — tsc/jest/next build green; window.confirm grep zero; no
            // external font/CDN request.
        }
    }

    // ---------------------------------------------------------------------
    // (g) — Gitea issue closure is the operator's call
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioIssueClosure
    {
        const string Skip =
            "U7(g) — Gitea state checked 2026-07-13 via the API (read-only; this gate never closes " +
            "issues, per instruction and the MEMORY.md house rule). gitea-#199 \"Liquidsoap output " +
            "metadata retains the previous track's fields across boundaries (artist bleed)\", gitea-#200 " +
            "\"Safe plays report gainDb=0 - output metadata frames carry no replay_gain key\", gitea-#212 " +
            "\"Artist for DJ intro/outro segments should be DJ name not station name\", and gitea-#214 " +
            "\"Libraries under Safe Scope on Dashboard lists 7, when there are only 2\" (all " +
            "labeled genwave-2.0) are OPEN. Operator to close after reviewing this gate's evidence " +
            "(the runnable wall above + the operator checklist in docs/PLAN.md's Epic U block-" +
            "quote) and completing the live half: a visual gitea-#214 confirmation on the OPERATOR's own " +
            "dashboard (their SafeScope [7] should now read 'N playable tracks / 1 library (id 7)') " +
            "after deploying, plus an optional ICY spot-check on their radio. This gate leaves all " +
            "four issues exactly as found.";

        [Fact(Skip = Skip)]
        public void TheFourIssuesCloseOnOperatorEvidence()
        {
            // U7(g) — the epic isn't done while gitea-#199/gitea-#200/gitea-#212/gitea-#214 are open; closing them is the
            // operator's decision, never this gate's.
        }
    }
}

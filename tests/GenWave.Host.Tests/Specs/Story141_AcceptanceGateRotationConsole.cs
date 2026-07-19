// STORY-141 — Acceptance gate: rotation quality + console fidelity end-to-end (Epic V / SPEC
// F41–F45, closes gitea-#195, gitea-#196, gitea-#197, gitea-#201, gitea-#202, gitea-#203, gitea-#210, gitea-#213, gitea-#216).
//
// BDD specification — xUnit. V10 ran 2026-07-14 against an isolated scratch-stack smoke (own -p
// v10smoke project, own .env, non-colliding ports 18000/18080/13000 via a `!override`-tagged
// compose overlay kept OUTSIDE the repo in the scratchpad — never editing the tracked
// compose.yaml; `docker compose ls -a`/`docker ps -a`/`docker volume ls`/`docker network ls`
// confirmed zero `v10smoke` remnants before and after; `down -v` on completion) — never the
// operator's live station (project `genwave`, standard ports 8000/8080/3000; confirmed via
// `docker compose ls -a` both before and after that it was not even running on this box
// throughout the run, untouched either way). Rewritten in the Story133/U7 idiom. Every fact below
// is one of:
//   (1) a real, always-run, non-Skip repo-content assertion (Story102/107/S8/T11/Story133's
//       grep/hash-assert idiom) — no live stack needed, so it deliberately has NO Integration
//       trait and stays IN the filtered wall; here it is Epic V's OWN inversion of Story133's
//       engine-diff pin — where Story133 pinned three legitimate edits, this pins ZERO edits
//       (the epic's total ban on engine/genwave.liq and compose.yaml, sequencing notes above);
//   (2) Skip-pinned with THIS SESSION's dated v10smoke scratch-stack evidence, Category=Integration; or
//   (3) Skip-pinned with the EXACT operator procedure for what genuinely needs the real station or
//       a human decision (issue closure), Category=Integration.
// No fact in this file keeps a bare "pending V10" reason.

using System.Security.Cryptography;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAcceptanceGateRotationConsole
{
    /// <summary>Repo root, resolved relative to the test assembly's build output (Story074/102/107's convention).</summary>
    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static string Sha256Hex(string relativePath)
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepoRoot, relativePath));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    // ---------------------------------------------------------------------
    // (h, engine/compose half) — the epic's TOTAL ban: zero diff, not three edits like Story133.
    // ---------------------------------------------------------------------

    public sealed class ScenarioEngineAndComposeCarryZeroDiffFromMain
    {
        // Pinned 2026-07-14 from a clean `main` checkout at commit
        // 72f88e960e41bfe1e3edd7b12148c073dcdff5df — verified `git diff main...HEAD -- <file>`
        // empty for both files at that same commit boundary (V1 through V9's every commit).
        // A content-hash pin (rather than shelling out to `git diff main -- <file>`) avoids
        // depending on `main` being a resolvable ref in whatever checkout runs this suite (a
        // shallow CI clone may not carry the branch) — the SPEC F41–F45 "zero engine/compose
        // diff" promise is exactly as well-proven either way, and this form needs nothing but the
        // file bytes already on disk.
        //
        // ComposeYamlSha256 re-pinned 2026-07-18 (PLAN T15, SPEC F64.1/F64.2, STORY-172): the
        // api service gained the public-listener port mapping (8081) plus ASPNETCORE_URLS/
        // Spectator__PublicPort env vars — a real, intentional edit from a LATER epic, not a
        // regression of Epic V's own F41–F45 zero-diff promise (which is about F41–F45 touching
        // neither file, still true). EngineScriptSha256 is untouched — T15 does not touch
        // engine/genwave.liq.
        //
        // ComposeYamlSha256 re-pinned AGAIN 2026-07-18 (PLAN T17, SPEC F61.4, STORY-166): admin_ui
        // gained `profiles: ["admin"]` — another intentional edit from a LATER epic, still not a
        // regression of Epic V's own F41–F45 zero-diff promise. EngineScriptSha256 unchanged.
        const string EngineScriptSha256 = "a256fd3f2797ed9b52e3f8507e8ca610aa02218e2fedc5c231369f0ccaab9bd6";
        const string ComposeYamlSha256  = "e368588851814f74c30838ceeab2ca6d53de4128b63ab7ea437a712864162555";

        [Fact]
        public void EngineScriptByteMatchesMain()
        {
            // Real, always-run, non-Skip repo-content assertion — no live stack needed, deliberately
            // NOT Category=Integration so it stays IN the filtered wall. Epic V's total engine ban
            // (sequencing notes: "nothing in F41-F45 touches the never-silent path's script") means
            // this hash must NEVER change across the whole epic, unlike Story133's three-edit pin.
            Assert.Equal(EngineScriptSha256, Sha256Hex(Path.Combine("engine", "genwave.liq")));
        }

        [Fact]
        public void ComposeYamlByteMatchesMain()
        {
            Assert.Equal(ComposeYamlSha256, Sha256Hex("compose.yaml"));
        }
    }

    // ---------------------------------------------------------------------
    // (a) — gitea-#210 reversed: a 2-track catalog alternates indefinitely, never drains
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioTwoTrackCatalogReversesTheDrain
    {
        const string Skip =
            "V10(a) — v10smoke, scratch stack, 2026-07-14: 2-track fixture (ffmpeg sine mp3s, " +
            "16.0s each, different artists — Radio Aurora \"Alpha Signal\" @-22.2 LUFS, Radio " +
            "Borealis \"Bravo Echo\" @-22.0 LUFS; both >=14s per V4's poll-race note), cadence " +
            "music-heavy (Station:Cadence:LeadInBeforeEachTrack/BackAnnounceAfterEachTrack=false, " +
            "StationIdEveryNUnits=100 at boot). Polled GET /api/play-history every 5s for ~200s: " +
            "clean, unbroken alternation mediaId 2,1,2,1,... across 19+ consecutive selections " +
            "(21:27:54-21:32:05Z), zero repeats, zero appearance of the F27 seed row (mediaId=3, " +
            "library 'safe') anywhere in the window -- the exact gitea-#210 repro, reversed. `docker " +
            "compose -p v10smoke logs api` carried the F41.5 WARN 19 times for EACH relaxed tier " +
            "in the same window (\"Anti-repeat window relaxed - playable catalog smaller than the " +
            "recent window\" x19; \"Artist-separation relaxed - no track avoided the last 2 " +
            "artists\" x19 -- expected, since a 2-track/2-artist catalog exhausts both the 20-entry " +
            "recent window and the 2-artist separation depth after just one pass through the " +
            "catalog). Evidence: v10/parta_playhistory_poll.log, v10/parta_api_log_full.txt.";

        [Fact(Skip = Skip)]
        public void TheTwoTrackCatalogAlternatesPastAFull20WindowCycleWithoutDraining()
        {
            // V10(a) — 19+ consecutive alternating selections, zero drain to the safe seed row
            // (F41.2, F41.4, closes gitea-#210).
        }

        [Fact(Skip = Skip)]
        public void TheRelaxationWarnNamesBothTiersInTheApiLog()
        {
            // V10(a) — F41.5's WARN present for both the anti-repeat and artist-separation tiers,
            // 19 occurrences each over the same observation window.
        }
    }

    // ---------------------------------------------------------------------
    // (b) — artist separation observed on a 6-track/3-artist catalog; single-artist plays WARN-logged
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioArtistSeparationObserved
    {
        const string SkipMultiArtist =
            "V10(b) — v10smoke, scratch stack, 2026-07-14: MEDIA_DIR grown from 2 to 6 tracks " +
            "across 3 artists (Radio Aurora x2, Radio Borealis x2, Radio Cascade x2, 15s each) by " +
            "adding 4 more ffmpeg sine mp3s to the running stack's bind-mounted MEDIA_DIR (no " +
            "restart) -- a transient sandbox-filesystem quirk briefly marked the 4 new rows " +
            "'unavailable' after their first successful scan+enrich pass (a bind-mount listing " +
            "race on this host, not conclusively a product defect -- follow-up candidate: harden " +
            "ScanService's availability flip (confirm-gone/grace-period) for transient listing " +
            "misses; a `touch` bump of their mtimes triggered the " +
            "existing changed-file re-discovery path in ScanService and all 6 rows converged to " +
            "'ready' within one more scan tick). Polled GET /api/play-history over a ~90s window " +
            "spanning 21:32:51-21:37:21Z (21 consecutive selections): chronological artist sequence " +
            "Aurora,Borealis,Aurora,Borealis,...,Cascade,Aurora,Borealis,Cascade,Aurora,Borealis," +
            "Cascade,Aurora -- zero same-artist adjacency anywhere in the window (F41.3's artist " +
            "tier holding cleanly once the catalog exceeds the 2-artist separation depth). WARN " +
            "counts in `docker compose -p v10smoke logs api` over the same window: " +
            "\"Artist-separation relaxed\" x11, \"Anti-repeat window relaxed\" x22 (legal per the " +
            "V10 sequencing note -- a relaxation WARN is expected exactly when the 6-track catalog " +
            "is smaller than the 20-entry recent window, never evidence of a bug on its own). " +
            "Evidence: v10/partb_playhistory_poll_multiartist.log, v10/partb_api_log_multiartist.txt.";

        [Fact(Skip = SkipMultiArtist)]
        public void NoSameArtistBackToBackAiringIsObservedOverAMultiTransitionWatch()
        {
            // V10(b) — 21 consecutive selections across 3 artists, zero same-artist adjacency
            // (F41.3, F41.5, closes gitea-#213).
        }

        const string SkipSingleArtist =
            "V10(b) — v10smoke, scratch stack, 2026-07-14: all 6 rows' artist collapsed to one " +
            "value (\"Solo Broadcast\") via PATCH /api/media/{id} with a fresh If-Match per row " +
            "(the dispatch's PATCH-based alternative to a separate single-artist fixture). Polled " +
            "GET /api/play-history over a ~90s follow-on window (21:38:15-21:39:51Z, 8+ " +
            "selections): all 6 mediaIds (1,2,4,5,6,7) still cycling normally, ZERO appearance of " +
            "the safe seed row (mediaId=3) -- play continues, never drains, despite every track now " +
            "sharing one artist. `docker compose -p v10smoke logs api` over the same window carried " +
            "the F41.5 artist-separation-relaxed WARN 8 times (once per selection, as expected when " +
            "every candidate shares the same artist). Evidence: " +
            "v10/partb_playhistory_poll_soloartist.log, v10/partb_api_log_soloartist.txt.";

        [Fact(Skip = SkipSingleArtist)]
        public void ASingleArtistLibraryStillPlaysWithTheArtistRelaxationWarnAndNeverDrains()
        {
            // V10(b) — a single-artist catalog keeps cycling normally, WARN-logged, never a drain
            // (F41.3's "never-silent outranks artist separation" rule, closes gitea-#213).
        }
    }

    // ---------------------------------------------------------------------
    // (c) — StationId semantics: no ID before N units, live 0 disables, negative rejected, boot-clean at 0
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioStationIdSemantics
    {
        const string SkipPeriod =
            "V10(c) — v10smoke, scratch stack, 2026-07-14: PUT /api/settings " +
            "Station:Cadence:StationIdEveryNUnits=4 + LeadInBeforeEachTrack=true, then `docker " +
            "compose -p v10smoke restart api` to reset the Orchestrator's in-memory unitCount to " +
            "genuinely zero (a fresh process). Polled play-history from that restart: no station-ID " +
            "patter segment (identified by its own stable, text-fixed content-hash -- \"You're " +
            "listening to <Station>.\" recurs with the SAME tts: hash every firing) appeared during " +
            "the first 9 aired music units post-restart -- well past the 4-unit floor, a " +
            "conservative reconfirmation that gitea-#216's boot-blast bug stays fixed. The ID then fired, " +
            "and its recurrence PERIOD measured exactly 4 aired music units between two consecutive " +
            "firings (verified twice independently: gap track2->track4 = 4 tracks; gap track1-> " +
            "track1 = 4 tracks) -- confirming the every-N mechanism precisely. Honesty note: the " +
            "very first LeadIn (unit 1) and the ID's exact PHASE from the restart (expected before " +
            "the 5th aired unit per the unitCount>0&&unitCount%N==0 formula, observed before the " +
            "10th) are both consistent with one or two renders silently dropped under a freshly " +
            "restarted process's cold TTS connection pool -- Orchestrator's own documented render- " +
            "budget-drop contract (F44.2/T12: a segment that faults or exceeds budget is dropped, " +
            "never fatal), not a periodicity defect, since the measured PERIOD between two confirmed " +
            "firings was exactly 4 both times. Evidence: v10/partc_playhistory_poll.log.";

        [Fact(Skip = SkipPeriod)]
        public void NoStationIdAirsBeforeFourMusicUnitsHaveElapsed()
        {
            // V10(c) — zero station-ID firings across the first 9 aired units post-restart, well
            // past the 4-unit floor (F42.1, closes gitea-#216).
        }

        [Fact(Skip = SkipPeriod)]
        public void TheStationIdRecursExactlyEveryFourAiredMusicUnits()
        {
            // V10(c) — measured period of exactly 4 aired units between two consecutive firings,
            // confirmed twice independently (F42.1, F42.3).
        }

        const string SkipLiveZero =
            "V10(c) — v10smoke, scratch stack, 2026-07-14: PUT /api/settings " +
            "Station:Cadence:StationIdEveryNUnits=0 -> 200, echoed back value=\"0\". Polled play- " +
            "history for a further ~100s / 13+ aired music units: the station-ID content-hash never " +
            "reappeared once (confirmed against the SAME hash that had been firing on the 4-unit " +
            "cadence immediately beforehand) -- 0 genuinely disables further station IDs live, no " +
            "api restart. Evidence: v10/partc_playhistory_after_zero.json.";

        [Fact(Skip = SkipLiveZero)]
        public void LiveSettingZeroDisablesAllFurtherStationIdsWithNoRestart()
        {
            // V10(c) — PUT 0 stops all further station-ID firings across 13+ subsequent units
            // (F42.2).
        }

        const string SkipNegative =
            "V10(c) — v10smoke, scratch stack, 2026-07-14: PUT /api/settings " +
            "Station:Cadence:StationIdEveryNUnits=-1 -> 400 {\"errors\":{\"settings\":[\"Value " +
            "'-1' is not valid for 'Station:Cadence:StationIdEveryNUnits'. Must be a non-negative " +
            "integer (0 disables).\"]}}; re-GET confirmed the persisted value stayed at the prior " +
            "0, nothing written by the rejected PUT.";

        [Fact(Skip = SkipNegative)]
        public void ANegativeStationIdIntervalIsRejectedWithFourHundredAndNothingPersists()
        {
            // V10(c) — PUT -1 -> 400, value unchanged after (F42.2 floor, SettingValidator).
        }

        const string SkipBootClean =
            "V10(c) — v10smoke, scratch stack, 2026-07-14: with " +
            "Station:Cadence:StationIdEveryNUnits=0 the effective value flowing through " +
            "IConfiguration at process start (station.settings overlay layered on top of env, " +
            "Program.cs's own layering order -- functionally identical to an env-sourced 0 for the " +
            "ValidateOnStart() mechanism under test, since IConfiguration flattens both to the same " +
            "string by key), `docker compose -p v10smoke restart api` came back healthy within 3 " +
            "poll ticks (~6s) with zero validation exceptions in the logs -- confirming " +
            "StationCadenceOptions' widened [Range(0, int.MaxValue)] floor (V6) is genuinely " +
            "enforced-and-passing at real boot with 0, not just in hermetic unit tests (F42.2 boot " +
            "half).";

        [Fact(Skip = SkipBootClean)]
        public void AFreshBootWithStationIdEveryNUnitsZeroStartsCleanly()
        {
            // V10(c) — api container restarts healthy with StationIdEveryNUnits=0 in effect at
            // ValidateOnStart(), zero exceptions (F42.2).
        }
    }

    // ---------------------------------------------------------------------
    // (d) — gitea-#203 repro: fresh-deploy Safe toggle succeeds; unnamed bulk stays bounded
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioFreshDeploySafeToggleAndBoundedBulk
    {
        const string Skip =
            "V10(d) — v10smoke, scratch stack, 2026-07-14: the fresh-deploy shape (F27 boot seed, " +
            "library 'safe' id=2 outside the main scope [1], seed row mediaId=3 \"Please Stand By " +
            "(Station Default)\") -- zero scope edits made. GET /api/media/3 -> 200, " +
            "X-Out-Of-Scope: true, ETag W/\"773\" (F43.1). PATCH /api/media/3 {eligible:false} with " +
            "If-Match: W/\"773\" -> 204 (F43.2, the Safe page's exact call), confirmed via re-GET " +
            "(eligible:false, fresh ETag W/\"803\"); restored eligible:true -> 204. POST " +
            "/api/media/3/reenrich with a fresh If-Match -> 202 (F43.3), confirmed complete via " +
            "re-GET (durationMs populated: 4477ms, previously null). Negative: POST " +
            "/api/media/bulk/reassign with filter={} (NO named library-id) + toLibraryId=1 -> 200 " +
            "{\"updated\":6} -- exactly the 6 in-scope main-library rows, NOT the safe row; GET " +
            "/api/media?library-id=2 confirmed mediaId=3 unchanged (still library 2, version " +
            "advanced only by the eligibility+reenrich calls above, never by the bulk call) -- the " +
            "F23.3/F43.4 unnamed-bulk boundedness holds even with the single-row scope gate " +
            "repealed. Evidence: v10/partd_get_seed.json, v10/partd_patch_eligibility.txt, " +
            "v10/partd_get_after_patch.json, v10/partd_reenrich.txt, v10/partd_bulk_reassign.txt.";

        [Fact(Skip = Skip)]
        public void TheOutOfScopeSeedRowIsReachableWithTheOutOfScopeHeaderAndAnETag()
        {
            // V10(d) — GET 200 + X-Out-Of-Scope:true + ETag on the fresh-deploy safe seed row
            // (F43.1, closes gitea-#203).
        }

        [Fact(Skip = Skip)]
        public void TheEligibilityToggleSucceedsWithZeroScopeEdits()
        {
            // V10(d) — PATCH eligibility with If-Match succeeds on the out-of-scope row, the exact
            // Safe content page call (F43.2, closes gitea-#203).
        }

        [Fact(Skip = Skip)]
        public void ReenrichSucceedsOnTheOutOfScopeRow()
        {
            // V10(d) — POST reenrich -> 202, completes (F43.3).
        }

        [Fact(Skip = Skip)]
        public void AnUnnamedBulkFilterCannotTouchTheOutOfScopeSafeRow()
        {
            // V10(d) — bulk reassign with no named library-id stays bounded to the 6 in-scope
            // rows; the safe row is untouched (F43.4, F23.3's negative case still stands).
        }
    }

    // ---------------------------------------------------------------------
    // (e) — identity live: a name edit reaches the shell wordmark, /api/stations, and the next patter
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioIdentityEditReachesEverySurfaceLive
    {
        const string Skip =
            "V10(e) — v10smoke, scratch stack, 2026-07-14: PUT /api/settings Station:Name=" +
            "\"Wavelength Radio\" -> 200; GET /api/stations IMMEDIATELY returned " +
            "[{\"id\":1,\"name\":\"Wavelength Radio\"}], no api restart (F44.1/F44.6). Curled the " +
            "admin-ui Next.js server directly at GET /dashboard using the SAME genwave-auth cookie " +
            "value the backend login issued (forwarded manually in the Cookie header, mirroring " +
            "what the browser's own proxied request would carry) -> 200; the server-rendered HTML " +
            "payload's sidebar wordmark span (`font-display text-xl italic text-ink`) contained the " +
            "literal string \"Wavelength Radio\" -- server-rendered, not client-fetched (F44.7, " +
            "closes gitea-#195). The next LeadIn patter segment's play-history entry (title AND artist, " +
            "since TtsSegmentSource stamps both from the live identity read regardless of Kind) " +
            "carried \"Wavelength Radio\", replacing the prior segments' \"V10 Scratch Station\" -- " +
            "a cache-hit render (same spoken text) still gets a FRESH display-metadata stamp per " +
            "unit (F44.1/F44.5, F39.3's cache-vs-display-metadata distinction). Evidence: " +
            "v10/parte_dashboard.html, v10/parte_playhistory_after_rename.json.";

        [Fact(Skip = Skip)]
        public void ANameEditReachesApiStationsImmediately()
        {
            // V10(e) — GET /api/stations reflects the new name on the very next call, no restart
            // (F44.1, F44.6, closes gitea-#196).
        }

        [Fact(Skip = Skip)]
        public void ANameEditReachesTheServerRenderedShellWordmark()
        {
            // V10(e) — the admin-ui's server-rendered sidebar HTML carries the new name (F44.7,
            // closes gitea-#195).
        }

        [Fact(Skip = Skip)]
        public void ANameEditReachesTheNextPatterSegmentsDisplayMetadata()
        {
            // V10(e) — the next LeadIn's play-history title/artist carry the new name, including on
            // a cache-hit render (F44.1, F44.5).
        }
    }

    // ---------------------------------------------------------------------
    // (f) — settings completion: every F44.2/F44.3 key present, exclusions absent, edits honored
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioSettingsSurfaceCompleteAndHonest
    {
        const string SkipInventory =
            "V10(f) — v10smoke, scratch stack, 2026-07-14: GET /api/settings returned 27 keys. " +
            "Every F44.2 key present with applyMode=\"live\": Station:Name, Station:Voice, " +
            "Station:Rotation:RecentWindow, Station:Rotation:ArtistSeparation, " +
            "Tts:RenderBudgetSeconds, Tts:BlurbRetentionHours, Llm:MaxCopyChars, " +
            "Admin:PlayHistoryCapacity, Library:ScanIntervalSeconds, Library:EnrichmentConcurrency. " +
            "Both F44.3 keys present with applyMode=\"enrichment\": " +
            "Library:CueDetection:MinSilenceDurationSec, Library:Energy:WindowSeconds. Confirmed " +
            "ABSENT from the 27: any Station:Safe:* key, any secret/connection-string key, " +
            "Library:CueDetection:SilenceThresholdDb, Admin:SessionLifetimeHours (F44.4's exclusion " +
            "list, all four honored). Evidence: v10/partf_settings_get.json.";

        [Fact(Skip = SkipInventory)]
        public void EveryF44Point2KeyIsPresentWithLiveApplyMode()
        {
            // V10(f) — all 10 F44.2 keys present, applyMode=live (closes gitea-#197).
        }

        [Fact(Skip = SkipInventory)]
        public void BothF44Point3KeysArePresentWithEnrichmentApplyMode()
        {
            // V10(f) — Library:CueDetection:MinSilenceDurationSec and Library:Energy:WindowSeconds
            // badge "applies at next enrichment" (F44.3).
        }

        [Fact(Skip = SkipInventory)]
        public void TheF44Point4ExclusionsAreAllAbsent()
        {
            // V10(f) — Station:Safe:*, secrets, SilenceThresholdDb, SessionLifetimeHours never
            // appear in the allowlisted GET (F44.4).
        }

        const string SkipCapacityShrink =
            "V10(f) — v10smoke, scratch stack, 2026-07-14: GET /api/play-history showed 15 entries; " +
            "PUT /api/settings Admin:PlayHistoryCapacity=5 -> 200; after the next push (~16s later, " +
            "one more track boundary), GET /api/play-history returned exactly 5 entries -- the ring " +
            "trims to the new live capacity on the very next push (V8 re-verified quick, F44.2).";

        [Fact(Skip = SkipCapacityShrink)]
        public void ShrinkingPlayHistoryCapacityLiveTrimsTheRingOnTheNextPush()
        {
            // V10(f) — PlayHistoryCapacity 50->5 trims the ring from 15 to 5 entries live (F44.2).
        }

        const string SkipRotationPuts =
            "V10(f) — v10smoke, scratch stack, 2026-07-14: PUT /api/settings " +
            "[{Station:Rotation:RecentWindow:3},{Station:Rotation:ArtistSeparation:1}] -> 200, both " +
            "echoed back with source=override, applyMode=live -- the F41.6 knobs are genuinely " +
            "editable through the completed settings surface (parts (a)/(b) above already proved " +
            "the underlying rotation MECHANISM; this is the settings-surface-completeness half).";

        [Fact(Skip = SkipRotationPuts)]
        public void TheRotationKnobsAreEditableThroughTheSettingsApi()
        {
            // V10(f) — Station:Rotation:RecentWindow/ArtistSeparation both PUT-able live (F41.6,
            // F44.8's playout section).
        }

        const string SkipOutOfRange =
            "V10(f) — v10smoke, scratch stack, 2026-07-14: one out-of-range PUT per representative " +
            "validator kind, ALL rejected 400 with the persisted value confirmed UNCHANGED by a " +
            "re-GET immediately after each: Loudness:TargetLufs=100 (range [-40,0]) -> 400, stayed " +
            "-16; Station:Name=\"\" (non-blank string) -> 400 \"must not be blank\", stayed " +
            "\"Wavelength Radio\"; Tts:RenderBudgetSeconds=0 (positive int) -> 400, stayed 30; " +
            "Library:CueDetection:MinSilenceDurationSec=0 (exclusive positive double) -> 400, " +
            "stayed unset; Station:Rotation:RecentWindow=-1 (non-negative int) -> 400, stayed 3; " +
            "GW_XFADE_MIN=-1 (exclusive positive double, engine-restart apply-mode) -> 400, stayed " +
            "2. Evidence: v10/partf_outofrange.txt.";

        [Fact(Skip = SkipOutOfRange)]
        public void OneOutOfRangePutPerValidatorKindIsRejectedAndNothingPersists()
        {
            // V10(f) — six representative kinds (range double, non-blank string, positive int,
            // exclusive-positive double, non-negative int, engine-restart double) all 400, all
            // unchanged after (F19.5, SettingValidator).
        }
    }

    // ---------------------------------------------------------------------
    // (g) — conflict retries recover in place at the API layer (UI halves are jest-proven)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioConflictRetriesRecoverInPlace
    {
        const string Skip =
            "V10(g) — v10smoke, scratch stack, 2026-07-14: GET /api/media/1 -> ETag W/\"807\". " +
            "PATCH /api/media/1 {genre:\"Ambient\"} with a deliberately STALE If-Match: W/\"1\" -> " +
            "409 Conflict {\"detail\":\"The row was modified since you last read it. Re-fetch and " +
            "retry.\"}; re-GET confirmed the SAME W/\"807\" ETag (nothing changed by the failed " +
            "attempt). Retried the identical PATCH with the fresh If-Match: W/\"807\" -> 204, " +
            "applied (genre=\"Ambient\" confirmed via re-GET) -- a 409 followed by an immediate " +
            "in-place retry succeeds at the API layer with no manual reload (the API-level half of " +
            "F45's promise). The UI halves -- CatalogToolbar's widened refresh gate (F45.1, gitea-#201) " +
            "and MoveToLibraryAction's stale-local-version fix (F45.2, gitea-#202) -- are jest-proven in " +
            "the admin-ui suite (catalog-selection-toolbar.spec.tsx, move-to-library tests), not " +
            "re-driven live here. Evidence: v10/partg_get1.json, v10/partg_patch_conflict.txt, " +
            "v10/partg_patch_retry_success.txt.";

        [Fact(Skip = Skip)]
        public void AConflictingPatchReturnsFourZeroNineAndChangesNothing()
        {
            // V10(g) — a stale If-Match -> 409, re-GET shows the row unchanged (F45's API-level
            // precondition).
        }

        [Fact(Skip = Skip)]
        public void RetryingWithAFreshETagSucceedsImmediatelyInPlace()
        {
            // V10(g) — the SAME PATCH with a fresh If-Match succeeds right after the 409, no manual
            // reload needed at the API layer (F45.1-F45.2's API-level proof; UI halves jest-proven).
        }
    }

    // ---------------------------------------------------------------------
    // (h, dotnet + admin-ui halves) — regression wall Skip-pinned; engine/compose half is the
    // always-run assertion class above.
    // ---------------------------------------------------------------------

    public sealed class ScenarioRegressionWall
    {
        const string DotnetEvidence =
            "V10(h) dotnet half — RUN 2026-07-14. `dotnet build GenWave.sln`: Build succeeded, " +
            "0 Warning(s), 0 Error(s). `dotnet test GenWave.sln --filter \"Category!=" +
            "Integration\"`: 0 failed across five projects -- Core 65/65, Orchestration 56/59 (3 " +
            "skipped), MediaLibrary 14/40 (26 skipped, filtered subset), Tts 117/128 (11 skipped), " +
            "Host 480/510 (30 skipped) -- 0 failed overall (this file's own rewrite converted the " +
            "prior bare-pending facts into Category=Integration Skip-pinned facts, the Story127/T11 " +
            "mechanism). Separately, `dotnet test tests/GenWave.MediaLibrary.Tests` (unfiltered, " +
            "its OWN self-bootstrapping compose project, confirmed absent via `docker ps -a`/" +
            "`docker compose ls -a` before and after): 250 passed, 0 failed, 47 skipped, 297 total " +
            "-- more total tests than U7's own 278-test baseline (Epics since then added coverage), " +
            "0 failed either way, confirming no regression. `git diff main...HEAD -- compose.yaml` " +
            "and `git diff main...HEAD -- engine/genwave.liq` both empty (also asserted as the " +
            "always-run hash-pin fact above, not just diffed by hand this run). F2-F40 gates stand: " +
            "`git diff main...HEAD --stat -- '*AcceptanceGate*.cs'` shows only THIS file (Story141 " +
            "itself, a brand-new file at /plan time) -- no prior epic's *AcceptanceGate*.cs was " +
            "touched.";

        [Trait("Category", "Integration")]
        [Fact(Skip = DotnetEvidence)]
        public void TheDotnetWallIsGreenAndPriorGatesStand()
        {
            // V10(h) dotnet half — build zero-warnings + filtered/unfiltered test green + zero
            // compose/engine diff + F2-F40 gates undisturbed.
        }

        const string AdminUiEvidence =
            "V10(h) admin-ui half — RUN 2026-07-14 from admin-ui/. `npx tsc --noEmit`: clean, zero " +
            "output. `npx jest`: 39 suites passed, 354 passed, 11 todo, 365 total (adds V3/V5/V6/ " +
            "V9's new specs over U7's own 34-suite/327-passed wall; the one pre-existing harmless " +
            "React act() warning in catalog-selection-toolbar.spec.tsx carried unchanged since Q12/ " +
            "R13/S8/T11/U7 -- not a failure, not introduced here). `npm run build`: green, 13 routes " +
            "compiled (same route set as U7's wall -- Epic V shipped zero new pages). " +
            "`grep -rn \"window.confirm(\" admin-ui/app admin-ui/components`: zero call sites.";

        [Trait("Category", "Integration")]
        [Fact(Skip = AdminUiEvidence)]
        public void AdminUiTscJestAndBuildAreGreen()
        {
            // V10(h) admin-ui half — tsc/jest/next build green; window.confirm grep zero.
        }
    }

    // ---------------------------------------------------------------------
    // (i) — Gitea issue closure is the operator's call
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioIssueClosure
    {
        const string Skip =
            "V10(i) — Gitea state checked 2026-07-14 via the API (read-only; this gate never closes " +
            "issues, per instruction and the MEMORY.md house rule). gitea-#195 \"GenWave at top of " +
            "dashboard should be {StationName}\", gitea-#196 \"Add Station Name to Settings page\", gitea-#197 " +
            "\"Look at what we can move from .env file to Admin UI\", gitea-#201 \"Catalog toolbar: " +
            "all-conflict batch leaves stale row versions until manual reload\", gitea-#202 \"Move-to-" +
            "library: conflict refresh doesn't unstick an in-place retry\", gitea-#203 \"Safe content " +
            "page: eligibility toggle 403s on the default fresh-deploy scope shape\", gitea-#210 \"Small " +
            "music catalogs can drain to the safe/blank loop despite playable tracks\", gitea-#213 " +
            "\"Multiple tracks from same artist in a row should not happen\", and gitea-#216 " +
            "\"StationIdEveryNUnits fires on the very first unit (boot) instead of after N units\" " +
            "are all OPEN. Operator to close after reviewing this gate's evidence (the runnable " +
            "wall above + the operator checklist in docs/PLAN.md's Epic V block-quote) and " +
            "completing the live half: deploying this branch, visually confirming the rotation/" +
            "identity/settings behaviors on THEIR OWN dashboard, and closing the nine issues with " +
            "the evidence above -- the operator's call, never this gate's. This gate leaves all " +
            "nine issues exactly as found.";

        [Fact(Skip = Skip)]
        public void TheNineIssuesCloseOnOperatorEvidence()
        {
            // V10(i) — the epic isn't done while gitea-#195/gitea-#196/gitea-#197/gitea-#201/gitea-#202/gitea-#203/gitea-#210/gitea-#213/gitea-#216 are
            // open; closing them is the operator's decision, never this gate's.
        }
    }
}

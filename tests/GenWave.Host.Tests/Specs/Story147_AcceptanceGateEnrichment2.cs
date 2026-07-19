// STORY-147 — Acceptance gate: Enrichment 2.0 end-to-end (Epic X / SPEC F46–F51,
// closes gitea-#190, gitea-#191, gitea-#208, gitea-#218).
//
// BDD specification — xUnit. X10 ran 2026-07-14 against an isolated scratch-stack smoke (own -p
// x10smoke project, own .env, non-colliding ports 18000/18080/13000 via a `!override`-tagged
// compose overlay kept OUTSIDE the repo in the scratchpad — never editing the tracked
// compose.yaml; `docker compose ls -a`/`docker ps -a`/`docker volume ls`/`docker network ls`
// confirmed zero `x10smoke` remnants before and after; `down -v` on completion) — never the
// operator's live station (project `genwave`, standard ports 8000/8080/3000; confirmed via
// `docker compose ls -a` both before and after that it was not even running on this box
// throughout the run, untouched either way). Rewritten in the Story133/Story141 idiom. Every fact
// below is one of:
//   (1) a real, always-run, non-Skip repo-content assertion (Story102/107/S8/T11/Story133/
//       Story141's grep/hash-assert idiom) — no live stack needed, so it deliberately has NO
//       Integration trait and stays IN the filtered wall; here it is the SAME zero-diff hash pin
//       Story141 introduced for Epic V, carried forward unchanged into Epic X (the sequencing
//       notes' "zero engine/compose diffs" ban stands for F46–F51 too);
//   (2) Skip-pinned with THIS SESSION's dated x10smoke scratch-stack evidence, Category=Integration; or
//   (3) Skip-pinned with the EXACT operator procedure for what genuinely needs a human decision
//       (issue closure), Category=Integration.
// No fact in this file keeps a bare "pending X10" reason.

using System.Security.Cryptography;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAcceptanceGateEnrichment2
{
    /// <summary>Repo root, resolved relative to the test assembly's build output (Story074/102/107/141's convention).</summary>
    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static string Sha256Hex(string relativePath)
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepoRoot, relativePath));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    // ---------------------------------------------------------------------
    // (f, engine/compose half) — the epic's TOTAL ban continues unchanged from Epic V.
    // ---------------------------------------------------------------------

    public sealed class ScenarioEngineAndComposeCarryZeroDiffFromMain
    {
        // Pinned 2026-07-14 — identical hashes to Story141's own pins (V10 ran the same day):
        // F46–F51 touch neither file, so the byte content is unchanged since Epic V shipped.
        //
        // ComposeYamlSha256 re-pinned 2026-07-18 (PLAN T15, SPEC F64.1/F64.2, STORY-172): the
        // api service gained the public-listener port mapping (8081) plus ASPNETCORE_URLS/
        // Spectator__PublicPort env vars — a real, intentional edit from a LATER epic, not a
        // regression of F46–F51 touching neither file (still true). EngineScriptSha256 is
        // untouched — T15 does not touch engine/genwave.liq.
        //
        // ComposeYamlSha256 re-pinned AGAIN 2026-07-18 (PLAN T17, SPEC F61.4, STORY-166): admin_ui
        // gained `profiles: ["admin"]` — another intentional edit from a LATER epic, not a
        // regression of F46–F51 touching neither file. EngineScriptSha256 unchanged.
        //
        // ComposeYamlSha256 re-pinned YET AGAIN 2026-07-18 (PLAN T21, SPEC F62.12 addendum,
        // STORY-179): the api service gained Icecast__StatsUrl/Icecast__AdminPassword env vars for
        // the spectator listener-count poll — another intentional edit from a LATER epic, not a
        // regression of F46–F51 touching neither file. EngineScriptSha256 unchanged.
        // ComposeYamlSha256 re-pinned 2026-07-19 (kokoro image bump): the kokoro service moved to
        // kokoro-fastapi-cpu v0.6.0 and gained a mem_limit backstop for the upstream RSS leak
        // (remsky/Kokoro-FastAPI#453) — an intentional ops edit from outside this epic, not a
        // regression of its zero-diff promise. EngineScriptSha256 is untouched.
        //
        const string EngineScriptSha256 = "a256fd3f2797ed9b52e3f8507e8ca610aa02218e2fedc5c231369f0ccaab9bd6";
        const string ComposeYamlSha256  = "bcff1c88105845cef82314dd774336095b1df38ec07e084038547bc374ea1b25";

        [Fact]
        public void EngineScriptByteMatchesMain()
        {
            // Real, always-run, non-Skip repo-content assertion — no live stack needed, deliberately
            // NOT Category=Integration so it stays IN the filtered wall. Epic X's own total ban
            // (sequencing notes: "zero engine/genwave.liq and compose.yaml diffs") mirrors Epic V's.
            Assert.Equal(EngineScriptSha256, Sha256Hex(Path.Combine("engine", "genwave.liq")));
        }

        [Fact]
        public void ComposeYamlByteMatchesMain()
        {
            Assert.Equal(ComposeYamlSha256, Sha256Hex("compose.yaml"));
        }
    }

    // ---------------------------------------------------------------------
    // (a) — real ffmpeg->aubio BPM lands on a music fixture; reenrich re-measures
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioRealBpmChain
    {
        const string SkipLanding =
            "X10(a) — x10smoke, scratch stack, 2026-07-14: the exact 128 BPM click+drone fixture " +
            "proven in X3's own smoke (byte-identical copy of x3smoke's fixture-track.mp3, `ffmpeg " +
            "-c copy` re-tagged artist=\"Radio Testers\" title=\"Click Track One\" date=1982, media " +
            "id=3) enriched through the production api image's real ffmpeg-decode-to-WAV -> `aubio " +
            "tempo` chain to bpm=128.1 -- byte-for-byte the same measurement X3's own smoke recorded " +
            "for this audio, confirming determinism. Two other fixtures (a sine-tone \"Bohemian " +
            "Rhapsody\"/\"Queen\" row with no date, and a blank-artist sine tone) measured " +
            "bpm=107.7/108.9 respectively through the same real chain -- plausible incidental tempo " +
            "reads on non-percussive test tones, not the fixture under test for this fact. Evidence: " +
            "x10/evidence/final_db_dump.txt.";

        [Fact(Skip = SkipLanding)]
        public void RealBpmLandsOnTheDesignatedFixtureThroughTheProductionBinary()
        {
            // X10(a) — bpm=128.1 measured via the real ffmpeg->aubio chain in the epic's own api
            // image, matching X3's own smoke value exactly (F46.1, F46.5, F46.2).
        }

        const string SkipReenrich =
            "X10(a) — x10smoke, scratch stack, 2026-07-14: `POST /api/media/3/reenrich?fields=bpm` " +
            "(no If-Match required -- ReenrichController takes no concurrency token on this " +
            "endpoint, confirmed against the shipped source) -> 202. Immediately after: bpm NULL, " +
            "state unchanged ('ready') -- the sentinel reset. ~3 minutes later (one backfill tick " +
            "reclaimed the row): bpm_analyzed_at advanced from 2026-07-15T04:45:47.527003Z to " +
            "2026-07-15T04:48:43.03632Z, bpm back to 128.1 -- the exact same value, since the re-" +
            "measurement decodes the identical audio bytes (F46.3, F46.4).";

        [Fact(Skip = SkipReenrich)]
        public void ReenrichBpmTokenResetsThenTheBackfillLoopRemeasuresWithAnAdvancedTimestamp()
        {
            // X10(a) — 202 Accepted, sentinel reset confirmed immediately, re-measurement confirmed
            // on the next backfill tick with an advanced bpm_analyzed_at and the same bpm (F46.3/F46.4).
        }
    }

    // ---------------------------------------------------------------------
    // (b) — track_energy present for every measured row instantly; loudness reenrich re-derives
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioTrackEnergyGeneratedColumn
    {
        const string SkipInstant =
            "X10(b) — x10smoke, scratch stack, 2026-07-14: every one of the 4 boot-time rows (3 " +
            "fixtures + the F27 seed) carried a non-null track_energy the instant loudness landed, " +
            "matching clamp((lufs+36)/30, 0, 1) exactly: row1 lufs=-21.8 -> 0.47333333333333333; " +
            "row2 lufs=-22.0 -> 0.4666666666666667; row3 lufs=-23.8 -> 0.4066666666666666; seed " +
            "row4 lufs=-25.0 -> 0.36666666666666664 -- zero backfill tick needed, a STORED " +
            "generated column (F47.1). Evidence: x10/evidence/parta_api_log_boot.txt (DB dump).";

        [Fact(Skip = SkipInstant)]
        public void TrackEnergyIsPresentForEveryMeasuredRowTheInstantLoudnessLands()
        {
            // X10(b) — track_energy computed with zero delay for every row with a measured
            // integrated_lufs, matching the DDL's clamp((lufs+36)/30,0,1) formula (F47.1).
        }

        const string SkipPreMigrationProof =
            "X10(b) — x10smoke, scratch stack, 2026-07-14: simulated the pre-existing-DB upgrade " +
            "path on the LIVE running stack -- `ALTER TABLE library.media DROP COLUMN " +
            "track_energy` via `docker compose exec -T db psql` (role library_svc), confirmed gone " +
            "via `\\d library.media` (only intro_energy/outro_energy/energy_analyzed_at remained). " +
            "Re-applied `db/10-enrichment2-migration.sh` by piping it into the SAME container " +
            "(`docker compose exec -T db bash < db/10-enrichment2-migration.sh`, mirroring " +
            "DatabaseFixture.RunFileInContainer's exact mechanism) -- NOTICEs confirmed bpm/" +
            "bpm_analyzed_at/year_lookup_at/media_year already existed (skipped, idempotent), " +
            "track_energy re-added. Immediate re-SELECT: all 5 rows' track_energy reappeared with " +
            "the IDENTICAL values as before the drop (0.47333.../0.46666.../0.40666.../" +
            "0.36666.../0.47333...) -- instant backfill confirmed for every measured row, zero " +
            "extra ffmpeg pass, zero write-path change (F47.2, F1's upgrade-path promise). " +
            "Evidence: x10/evidence/partb_pre_drop.txt, partb_drop_column.txt, " +
            "partb_migration_reapply.txt, partb_post_migration.txt.";

        [Fact(Skip = SkipPreMigrationProof)]
        public void DroppingAndReapplyingTheMigrationInstantlyBackfillsTrackEnergyForEveryRow()
        {
            // X10(b) — the pre-existing-DB upgrade path: drop track_energy, re-apply db/10, and
            // every already-measured row's track_energy reappears instantly with the same value
            // (F47.2).
        }

        const string SkipLoudnessReenrich =
            "X10(b) — x10smoke, scratch stack, 2026-07-14: `POST /api/media/1/reenrich?fields=" +
            "loudness` -> 202. Immediately after: integrated_lufs AND track_energy BOTH NULL " +
            "together (the generated column follows its source instantly), state='discovered'. " +
            "~12s later (one enrichment-worker pass reclaimed the 'discovered' row): integrated_lufs " +
            "back to -21.8 (deterministic re-measurement of the same audio), track_energy back to " +
            "0.47333333333333333 = (-21.8+36)/30, enriched_at advanced from 2026-07-15T04:45:47.331997Z " +
            "to 2026-07-15T04:49:07.814012Z -- confirming a genuine re-measurement, not a no-op " +
            "(F47.2, F20.10's loudness reenrich contract).";

        [Fact(Skip = SkipLoudnessReenrich)]
        public void LoudnessReenrichNullsAndRederivesTrackEnergyTogetherWithAnAdvancedTimestamp()
        {
            // X10(b) — reenrich loudness nulls integrated_lufs and track_energy together, then the
            // next enrichment pass re-derives both with an advanced enriched_at (F47.2).
        }
    }

    // ---------------------------------------------------------------------
    // (c) — ONE real MusicBrainz lookup; kill switch; low-confidence stub skip-and-stamp
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioMusicBrainzYearLookup
    {
        const string SkipRealLookup =
            "X10(c) — x10smoke, scratch stack, 2026-07-14: exactly ONE lookup-eligible row at boot " +
            "-- \"Bohemian Rhapsody\"/\"Queen\", no date tag (media id=2); the other two fixtures " +
            "were deliberately NOT eligible (a dated 1982 row; a blank-artist row) -- so the real " +
            "kill-switch-enabled Library:YearLookup:Endpoint (default musicbrainz.org) received " +
            "EXACTLY one claim. `docker compose logs api -t` over the whole ~20-minute run shows " +
            "exactly one \"Backfilling release year for 1 ready rows\" line touching the real " +
            "endpoint, with exactly one Start/Sending/Received/End HTTP quadruple for `GET https://" +
            "musicbrainz.org/ws/2/recording?*` (200 after 1037.86ms) -- confirmed by grepping the " +
            "full log for \"musicbrainz.org\" (2 lines total: Start + Sending for that single " +
            "request; the Received/End lines don't repeat the redacted URL). Result: year=1993 + " +
            "year_lookup_at stamped on row 2; the dated row (year=1982, tagged) and the blank-" +
            "artist row (year_lookup_at NULL throughout) were never touched (F48.3's claim " +
            "predicate). ⚠️ Honesty note: 1993 is NOT the historically \"correct\" earliest " +
            "release year for Bohemian Rhapsody (1975) -- the query carried no album qualifier " +
            "(BuildLuceneQuery only adds `AND release:\"...\"` when an album tag is present, which " +
            "this fixture deliberately had none of), so MusicBrainz's recording search matched " +
            "several distinct \"Bohemian Rhapsody\"/\"Queen\" recording MBIDs (studio original, " +
            "live versions, remasters) at tied top scores; MusicBrainzYearLookup.SelectYear's tie-" +
            "break (`candidate.Score > best.Score`, strictly greater) keeps whichever qualifying " +
            "candidate the endpoint returned FIRST among ties, and F48.2's contract is \"earliest " +
            "release year among THAT candidate's own releases\" -- not \"earliest across every " +
            "matching recording by the artist\". The code executed its documented contract exactly; " +
            "real-world MusicBrainz data for a decades-old classic with many pressings is " +
            "genuinely ambiguous under a title+artist-only query with no album to disambiguate. Not " +
            "a defect -- SPEC F48.2's own wording already scopes the promise to the best-scoring " +
            "candidate's releases. Evidence: x10/evidence/final_api_log_timestamped.txt, " +
            "x10/evidence/final_db_dump.txt.";

        [Fact(Skip = SkipRealLookup)]
        public void ExactlyOneRealMusicBrainzRequestFillsTheOneEligibleRowsYear()
        {
            // X10(c) — one lookup-eligible row at boot, exactly one real HTTP round trip to
            // musicbrainz.org, year landed + sentinel stamped; the two ineligible fixtures
            // untouched (F48.1-F48.3, closes gitea-#208).
        }

        const string SkipKillSwitch =
            "X10(c) — x10smoke, scratch stack, 2026-07-14: immediately after the one real lookup " +
            "completed, `PUT /api/settings Library:YearLookup:Enabled=false` -> 200. Added a NEW " +
            "year-less tagged fixture (\"Later Arrival\"/\"Late Song\", media id=5) into MEDIA_DIR. " +
            "Waited >4 backfill ticks (Library:ScanIntervalSeconds overlaid to 5s for this scratch " +
            "stack): the row fully enriched otherwise (bpm=116.3, loudness/energy measured, " +
            "state='ready') but year_lookup_at stayed NULL throughout -- `docker compose logs api " +
            "--since 1m` immediately after the PUT carried ZERO \"Backfilling release year\" lines " +
            "and ZERO musicbrainz.org hits (the claim query is skipped entirely before even being " +
            "issued, per BackfillYearLookupAsync's own early-return -- not merely a rejected " +
            "attempt). Confirms the kill switch stops claiming before the very next tick, with no " +
            "api restart, while every OTHER enrichment path continues unaffected (F48.5).";

        [Fact(Skip = SkipKillSwitch)]
        public void DisablingTheKillSwitchStopsAllFurtherClaimsWithNoApiRestart()
        {
            // X10(c) — Library:YearLookup:Enabled=false live -> zero further claim queries issued
            // (not just rejected), confirmed by log absence across 4+ ticks on a freshly-added
            // eligible row; other enrichment (bpm/loudness) unaffected (F48.5).
        }

        const string SkipLowConfidence =
            "X10(c) — x10smoke, scratch stack, 2026-07-14: repointed `Library:YearLookup:Endpoint` " +
            "live to a local stub server (127.0.0.1:19999, host.docker.internal via extra_hosts " +
            "host-gateway) returning score=100 but a DELIBERATELY MISMATCHED artist-credit " +
            "(\"A Totally Different Artist\") regardless of query, then `PUT " +
            "Library:YearLookup:Enabled=true`. Two year-less rows got claimed against the stub on " +
            "the next two backfill ticks: the row left over from the kill-switch fact above (media " +
            "id=5, \"Later Arrival\"/\"Late Song\", year_lookup_at stamped 2026-07-15T04:50:03.08Z) " +
            "and a freshly-added row (media id=6, \"Local Test Artist\"/\"Local Test Song\", " +
            "year_lookup_at stamped 2026-07-15T04:50:08.12Z). BOTH stayed year=NULL -- " +
            "MusicBrainzYearLookup.MatchesArtist rejected the score=100 candidate purely on the " +
            "normalized-artist-equality mismatch, exactly as F48.2 requires. The stub's own access " +
            "log confirmed the descriptive User-Agent rode automatically with no per-call wiring: " +
            "\"GenWave/1.0 (+https://github.com/GenWave-Org/genwave)\" on both hits, and the exact " +
            "lucene query shape (`artist:\"Later Arrival\" AND recording:\"Late Song\"&fmt=json&" +
            "limit=5`, `artist:\"Local Test Artist\" AND recording:\"Local Test Song\"&fmt=json&" +
            "limit=5`). Evidence: x10/evidence/partc_stub_hits.log, partc_after_stub_wait.txt.";

        [Fact(Skip = SkipLowConfidence)]
        public void ALowConfidenceArtistMismatchSkipsTheYearAndStillStampsTheSentinel()
        {
            // X10(c) — a score=100 candidate with a mismatched artist is rejected; year stays NULL,
            // year_lookup_at gets stamped anyway (skip-and-stamp, F48.2/F48.3), descriptive UA rides
            // automatically.
        }
    }

    // ---------------------------------------------------------------------
    // (d) — decade/year/year-missing filters + column toggle; browser halves cited from X7/X9
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioYearFiltersAndDtoFields
    {
        const string Skip =
            "X10(d) — x10smoke, scratch stack, 2026-07-14: `GET /api/media?year=1982` -> exactly " +
            "the tagged fixture (media id=3, \"Click Track One\"), carrying bpm=128.1 and " +
            "trackEnergy=0.4066666666666666 on the AdminMediaDto. `GET /api/media?decade=1990` -> " +
            "exactly media id=2 (year=1993, inside the 1990-1999 BETWEEN range). `GET /api/media?" +
            "year-missing=true` -> exactly the 3 unresolved main-scope rows (ids 1, 5, 6), each " +
            "carrying its own bpm/trackEnergy. `GET /api/media?year=1982&decade=1980` (both named) " +
            "-> 400 {\"title\":\"Conflicting year filters.\",\"detail\":\"Name at most one of year, " +
            "decade, or year-missing=true.\"} (F49.1). The column-visibility toggle (localStorage " +
            "persistence, default-hidden Year/BPM/Energy columns, reload survival) and the decade/" +
            "year/year-missing filter-chip UI are jest-proven in X7's own admin-ui suite " +
            "(catalog-column-toggle / catalog-year-filters specs) and were Playwright-verified " +
            "live during THIS epic's own X7/X9 orchestrator smokes on this exact shipped code " +
            "(2026-07-14 sessions, cited per the dispatch's own instruction rather than re-driving " +
            "a browser here) -- this fact re-verifies the API/DTO half end-to-end on the gate's " +
            "OWN stack for honesty. Evidence: x10/evidence/partd_filter_year1982.json, " +
            "partd_filter_decade1990.json, partd_filter_yearmissing.json, partd_filter_conflict.json.";

        [Fact(Skip = Skip)]
        public void YearDecadeAndYearMissingFiltersReturnExactlyTheExpectedRowsWithBpmAndEnergy()
        {
            // X10(d) — year/decade/year-missing filters return exactly the expected rows through
            // the production binary, each carrying bpm/trackEnergy on the DTO; naming more than one
            // -> 400 (F49.1, F49.2, closes gitea-#218's filter half). Column toggle: jest + X7/X9's own
            // Playwright smokes on this code (F49.3, F49.4).
        }
    }

    // ---------------------------------------------------------------------
    // (e) — duration live on push/history; drain shows none; an honest ring-eviction finding
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioDurationLiveOnPushAndHistory
    {
        const string Skip =
            "X10(e) — x10smoke, scratch stack, 2026-07-14: a freshly-pushed track's very first " +
            "on-air advance and its play-history entry both carried the row's real duration_ms " +
            "(e.g. media id=1/5/6 -> durationMs=15046; media id=3 -> durationMs=30074, matching " +
            "each fixture's own duration_ms exactly), while every tts:* patter segment across 45+ " +
            "history entries carried durationMs=null with zero exceptions (F50.2, F50.6). Forced a " +
            "drain by PATCHing all 5 main-scope rows eligible=false (fresh If-Match each): the F27 " +
            "safe/authored segment (media id=4, \"Please Stand By\") came on-air next and its now-" +
            "playing/history entries carried durationMs=null (F50.6's engine-initiated-play " +
            "contract -- duration never rides the engine's own annotate line, so this branch always " +
            "stamps null regardless of whether the row itself has a measured duration_ms). ⚠️ " +
            "Honesty note -- a real, reproduced, NON-blocking defect found by this gate: on repeat " +
            "airings of the SAME media id, durationMs intermittently reverted to null even though " +
            "duration_ms stayed populated in the DB the entire time and the row was never " +
            "reenriched (confirmed across the final 50-entry play-history snapshot: media id=2 " +
            "showed durationMs=15046 on 2 airings and null on 1; media id=3 showed 30074 on 3 " +
            "airings and null on 1; media id=5 showed 15046 on 3 airings and null on 1, " +
            "interleaved, never a one-way degradation). Root cause, pinned by source read: " +
            "`PlayoutFeeder.Remember` (src/GenWave.Core/Playout/PlayoutFeeder.cs, ~line 210) " +
            "evicts `pushedMeta`/`feederOwnedIds` BY ID VALUE once the `recent` ring exceeds " +
            "Station:Rotation:RecentWindow (default 20) -- but the SAME id can occupy multiple ring " +
            "slots when a catalog is small enough to repeat within that window (this scratch " +
            "catalog: 5-6 tracks, heavy TTS-segment churn between each, so >20 total advances -- " +
            "music and tts:* alike -- accumulate within minutes). Evicting the id's OLDEST ring " +
            "occurrence removes the dictionary entry GLOBALLY, even while a NEWER occurrence of " +
            "that same id is still in-window or about to air again; the next time that id comes " +
            "on-air, `feederOwnedIds.Contains(mediaId)` wrongly reads false, so the tick treats a " +
            "genuine feeder-pushed repeat as \"engine-initiated\" and re-derives its metadata from " +
            "`EngineMetadata.ExtractAnnotations()` instead -- which recovers title/artist correctly " +
            "(both ride the annotate line) but ALWAYS stamps DurationMs: null by design for that " +
            "branch (duration never rides the annotate line at all). This is PRE-EXISTING logic " +
            "(`Remember`'s ring/eviction shape shipped in Epic V's V4 for Station:Rotation:" +
            "RecentWindow), newly EXPOSED rather than introduced by X8 -- X8's own DurationMs " +
            "plumbing (MediaRow -> MediaReference -> MediaItem -> pushedMeta) is byte-correct end " +
            "to end by source read and by the clean first-airing/drain evidence above. Non-" +
            "blocking: never-silent holds, no exception, title/artist never corrupted, and the " +
            "defect requires a catalog small enough to repeat an id inside the configured " +
            "RecentWindow -- exactly the V10/gitea-#210 small-catalog scenario, not a general-population " +
            "risk at a real station sized above its own RecentWindow. Recorded as a follow-up " +
            "candidate (not fixed in this gate-only task, not filed as a Gitea issue by this run -- " +
            "the V10(b) precedent for its own discovered, non-blocking scan-availability quirk). " +
            "Evidence: x10/evidence/parte_play_history_final.json, debug_nowplaying_watch.log, " +
            "parte_drain_watch.log, parte_drain_watch2.log.";

        [Fact(Skip = Skip)]
        public void DurationRidesTheFirstPushAndHistoryAndDrainPlaysCarryNone()
        {
            // X10(e) — a track's first on-air advance and its history entry both carry the real
            // duration_ms; tts:* and drain/safe-rotation plays always carry null (F50.2, F50.4-
            // F50.6, closes gitea-#218's duration half). Honesty note above: a pre-existing PlayoutFeeder
            // ring-eviction defect (Epic V's V4, not X8) intermittently nulls duration on REPEAT
            // airings inside a catalog smaller than Station:Rotation:RecentWindow -- a follow-up
            // candidate, not a blocker for this fact's own claim.
        }
    }

    // ---------------------------------------------------------------------
    // (f, dotnet + admin-ui halves) — regression wall Skip-pinned; engine/compose half is the
    // always-run assertion class above.
    // ---------------------------------------------------------------------

    public sealed class ScenarioRegressionWall
    {
        const string DotnetEvidence =
            "X10(f) dotnet half — RUN 2026-07-14. `dotnet build GenWave.sln`: Build succeeded, " +
            "0 Warning(s), 0 Error(s). `dotnet test GenWave.sln --filter \"Category!=" +
            "Integration\"`: 0 failed across five projects -- Core 79/79, Orchestration 56/59 (3 " +
            "skipped), MediaLibrary 32/58 (26 skipped, filtered subset), Tts 117/128 (11 skipped), " +
            "Host 502/532 (30 skipped -- this file's own rewrite converts the prior 8 bare-pending " +
            "facts into 2 always-run passing facts + 13 Category=Integration Skip-pinned facts, the " +
            "Story127/T11/Story141 mechanism: net +2 passed / -8 skipped versus the pre-rewrite " +
            "500/538 run, since the 13 Integration-tagged facts are no longer selected by the " +
            "filter at all) -- 0 failed overall. Separately, " +
            "`dotnet test tests/GenWave.MediaLibrary.Tests` (unfiltered, " +
            "its OWN self-bootstrapping compose project `genwave-libtest`, confirmed absent via " +
            "`docker ps -a`/`docker compose ls -a` before and after): 300 passed, 0 failed, 47 " +
            "skipped, 347 total -- more total tests than V10's own 278-test baseline (Epic X added " +
            "coverage), 0 failed either way, confirming no regression. `git diff main...HEAD -- " +
            "compose.yaml` and `git diff main...HEAD -- engine/genwave.liq` both empty (also " +
            "asserted as the always-run hash-pin fact above, byte-identical to Story141's own pins " +
            "-- F46-F51 touch neither file). F2-F45 gates stand: `git diff main...HEAD --stat -- " +
            "'*AcceptanceGate*.cs'` shows only THIS file (Story147 itself, added at /plan time, " +
            "rewritten by this task) -- no prior epic's *AcceptanceGate*.cs was touched.";

        [Trait("Category", "Integration")]
        [Fact(Skip = DotnetEvidence)]
        public void TheDotnetWallIsGreenAndPriorGatesStand()
        {
            // X10(f) dotnet half — build zero-warnings + filtered/unfiltered test green + zero
            // compose/engine diff + F2-F45 gates undisturbed.
        }

        const string AdminUiEvidence =
            "X10(f) admin-ui half — RUN 2026-07-14 from admin-ui/. `npx tsc --noEmit`: clean, zero " +
            "output. `npx jest`: 41 suites passed, 379 passed, 11 todo, 390 total (adds X7/X9's new " +
            "specs over V10's own 34-suite/327-passed wall; the one pre-existing harmless React " +
            "act() warning in catalog-selection-toolbar.spec.tsx carried unchanged since Q12/R13/" +
            "S8/T11/U7/V10 -- not a failure, not introduced here). `npm run build`: green, 13 routes " +
            "compiled (same route set as V10 -- Epic X shipped zero new pages, only new columns/" +
            "filters on the existing catalog page). `grep -rn \"window.confirm(\" admin-ui/app " +
            "admin-ui/components`: zero call sites.";

        [Trait("Category", "Integration")]
        [Fact(Skip = AdminUiEvidence)]
        public void AdminUiTscJestAndBuildAreGreen()
        {
            // X10(f) admin-ui half — tsc/jest/next build green; window.confirm grep zero.
        }
    }

    // ---------------------------------------------------------------------
    // (g) — Gitea issue closure is the operator's call; gitea-#191 closes on zero-code evidence (F51/F32)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioIssueClosureIsTheOperatorsCall
    {
        const string Skip =
            "X10(g) — Gitea state checked 2026-07-14 via the API (read-only; this gate never closes " +
            "issues, per instruction and the MEMORY.md house rule). gitea-#190 \"Add BPM & energy " +
            "detection to enrichment service\", gitea-#191 \"Scheduled enrichment/metadata service " +
            "runs\", gitea-#208 \"Add release year to metadata if not already present\", and gitea-#218 " +
            "\"Track duration\" are all OPEN. gitea-#190/gitea-#208/gitea-#218 " +
            "close on the live evidence in the facts above (real BPM chain, track_energy generated " +
            "column, real MusicBrainz year fill + kill switch + low-confidence skip, year/decade " +
            "filters, duration on push/history/drain). gitea-#191 is DIFFERENT: it ships ZERO code (SPEC " +
            "F51, the F32 precedent -- \"the smoke-test gate reconciliation\" closed gitea-#179 the same " +
            "way, by reconciling documentation against already-shipped behavior rather than adding " +
            "a feature). Its evidence comment must point at SPEC F51.1/F51.2 directly: the scan " +
            "already runs on a live-editable Library:ScanIntervalSeconds (F19/F44); every claim " +
            "predicate shipped across this epic and prior ones (state='discovered'; the per-" +
            "analyzer *_analyzed_at IS NULL backfills, F46.3/F48.3 included) already guarantees only " +
            "new-or-incomplete rows are ever reclaimed; \"force enrichment of all tracks\" already " +
            "exists as bulk re-enrich (F20.12) -- the issue's own \"I don't see the value [of a " +
            "dedicated surface]\" is satisfied by the EXISTING claim-predicate behavior, not a new " +
            "one. Operator to close all four after reviewing this gate's evidence (the runnable " +
            "wall above + the operator checklist in docs/PLAN.md's Epic X block-quote) and " +
            "completing the live half: deploying this branch, spot-checking a real MB-filled year " +
            "on their own catalog, confirming the catalog columns/filters and now-playing card " +
            "progress on their real station -- the operator's call, never this gate's. This gate " +
            "leaves all four issues exactly as found.";

        [Fact(Skip = Skip)]
        public void TheFourIssuesCloseOnOperatorEvidenceAndNinetyOneCitesSpecF51WithZeroCode()
        {
            // X10(g) — the epic isn't done while gitea-#190/gitea-#191/gitea-#208/gitea-#218 are open; closing them is the
            // operator's decision, never this gate's. gitea-#191 in particular ships zero code -- its
            // whole task surface is this evidence comment, pointing at SPEC F51 + the shipped
            // claim-predicate behavior (the F32 precedent).
        }
    }
}

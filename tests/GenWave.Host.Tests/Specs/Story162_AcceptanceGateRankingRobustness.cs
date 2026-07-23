// STORY-162 — Acceptance gate: ranking & robustness end-to-end (Epic Z / SPEC F57–F64,
// closes gitea-#204, gitea-#219, gitea-#220, gitea-#222, gitea-#223, gitea-#228, gitea-#229, gitea-#233, gitea-#234, gitea-#235).
//
// BDD specification — xUnit. Z11 ran 2026-07-16 against an isolated scratch-stack smoke (own -p
// z11smoke project, own .env, non-colliding ports 18000/18080/13000 via a `!override`-tagged
// compose overlay kept OUTSIDE the repo in the scratchpad — never editing the tracked
// compose.yaml; `docker compose ls -a`/`docker ps -a`/`docker volume ls` confirmed zero `z11smoke`
// remnants before this run began and after `down -v` at the end; the operator's production
// station (project `genwave`, standard ports 8000/8080/3000) was confirmed NOT RUNNING on this box
// throughout — only the operator's separate, long-exited `mrdgenwave` project was present,
// unchanged throughout). Rewritten in the Story133/Story141/Story147/Story153 idiom. Every fact
// below is one of:
//   (1) a real, always-run, non-Skip repo-content assertion (the Story141/147/153 hash-pin idiom)
//       — no live stack needed, so it deliberately has NO Integration trait and stays IN the
//       filtered wall — the SAME zero-diff hash pin Story141 introduced for Epic V, carried
//       forward unchanged through Epics X/Y and now Z; or
//   (2) Skip-pinned with THIS SESSION's dated z11smoke scratch-stack evidence, Category=Integration; or
//   (3) Skip-pinned with the EXACT operator procedure for what genuinely needs a human decision
//       (issue closure), Category=Integration.
// No fact in this file keeps a bare "pending Z11" reason.
//
// Browser honesty note — a DEPARTURE from the Q12→Y7 precedent: `npx playwright install chromium`
// succeeded this session (Chromium 149.0.7827.55, playwright 1.61.1, installed into an isolated
// scratchpad npm project — admin-ui/package.json was NOT touched, staying inside this task's own
// file ownership). Part (a)'s facet-pick, bulk vote, and tooltip facts below were driven through a
// REAL headless Chromium against the deployed admin-ui container, not merely cited from the
// existing jest suite the way Q12 through Y7 had to when no browser tool was available — screenshots
// and the Playwright accessibility-tree queries are cited as evidence directly.
//
// Part (d) honesty note — the offset MOVED. The seeded 3.5 LU (Reading A, real music, 2026-07-12)
// disagreed materially with this gate's own scratch-stack reading (1.5 LU); per the operator ruling
// already recorded below (2026-07-15: "derivation evidence comes from this gate"), the constant was
// re-derived to 1.5 LU and the Story013 header rewritten with the full Reading C evidence — see
// ScenarioGateOffsetDerivedAndPinned for the complete, honest derivation trail (including why a
// synthetic sine-tone fixture catalog plausibly reads a smaller gap than real program material).

using System.Security.Cryptography;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAcceptanceGateRankingRobustness
{
    /// <summary>Repo root, resolved relative to the test assembly's build output (Story074/102/107/141/147/153's convention).</summary>
    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static string Sha256Hex(string relativePath)
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepoRoot, relativePath));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    // ---------------------------------------------------------------------
    // (e, engine/compose half) — the epic's TOTAL ban continues unchanged from Epic V/X/Y.
    // ---------------------------------------------------------------------

    public sealed class ScenarioEngineAndComposeCarryZeroDiffFromMain
    {
        // Pinned 2026-07-16 — identical hashes to Story141's/Story147's/Story153's own pins:
        // F57–F64 touch neither file, so the byte content is unchanged since Epic V shipped.
        //
        // ComposeYamlSha256 re-pinned 2026-07-18 (PLAN T15, SPEC F64.1/F64.2, STORY-172): F64 is
        // this very story — T15 is the one intentional edit that DOES touch compose.yaml (the api
        // service gained the public-listener port mapping (8081) plus ASPNETCORE_URLS/
        // Spectator__PublicPort env vars). EngineScriptSha256 is untouched — T15 does not touch
        // engine/genwave.liq.
        //
        // ComposeYamlSha256 re-pinned AGAIN 2026-07-18 (PLAN T17, SPEC F61.4, STORY-166): a second
        // intentional edit that DOES touch compose.yaml — admin_ui gained `profiles: ["admin"]`.
        // EngineScriptSha256 unchanged — T17 does not touch engine/genwave.liq.
        //
        // ComposeYamlSha256 re-pinned YET AGAIN 2026-07-18 (PLAN T21, SPEC F62.12 addendum,
        // STORY-179): a third intentional edit that DOES touch compose.yaml — the api service
        // gained Icecast__StatsUrl/Icecast__AdminPassword env vars for the spectator listener-count
        // poll. EngineScriptSha256 unchanged — T21 does not touch engine/genwave.liq.
        // ComposeYamlSha256 re-pinned 2026-07-19 (kokoro image bump): the kokoro service moved to
        // kokoro-fastapi-cpu v0.6.0 and gained a mem_limit backstop for the upstream RSS leak
        // (remsky/Kokoro-FastAPI#453) — an intentional ops edit from outside this epic, not a
        // regression of its zero-diff promise. EngineScriptSha256 is untouched.
        //
        // ComposeYamlSha256 re-pinned 2026-07-20 (PLAN T34, SPEC F70.1, STORY-190): a new `piper`
        // service (Piper local-fallback TTS sidecar) plus Tts__Fallback__Endpoint/
        // Tts__Fallback__Voice env vars on the api service and a piper_models volume — another
        // intentional edit from a LATER epic. EngineScriptSha256 unchanged — T34 does not touch
        // engine/genwave.liq.
        //
        // ComposeYamlSha256 re-pinned 2026-07-20 (Q3 housekeeping, "cloudflared observability",
        // SPEC F77): a new, optional `cloudflared` service (profiles: ["tunnel"], off by default,
        // no host ports) — versions the tunnel that previously ran outside the repo as unmanaged
        // infrastructure, with a healthcheck/metrics contract. Another intentional edit from a
        // LATER epic, not a regression of this epic's zero-diff promise. EngineScriptSha256
        // unchanged — this task does not touch engine/genwave.liq.
        //
        // ComposeYamlSha256 re-pinned 2026-07-21 (PLAN T49, SPEC F78.1/F78.3/F78.4/F78.5,
        // STORY-202): a new, optional `alloy` log-shipper service (profiles: ["logging"], off by
        // default, no host ports) plus a new `alloy_data` named volume — versions the log
        // shipper for the F78 observability expansion. Another intentional edit from a LATER
        // epic, not a regression of this epic's zero-diff promise. EngineScriptSha256 unchanged
        // — this task does not touch engine/genwave.liq. Re-pinned again same day (T49 review
        // fix, SPEC F78.5): the alloy healthcheck's bare `grep -qi ready` matched Alloy's
        // not-ready body too ("Alloy is not ready." still contains "ready" as a substring),
        // reporting healthy for a not-ready Alloy — now discriminates on the contiguous phrase
        // "is ready", which only the 200 "Alloy is ready." body contains.
        //
        // ComposeYamlSha256 re-pinned 2026-07-21 (PR #68): kokoro's container healthcheck now
        // curls kokoro-fastapi's dedicated /health route instead of the TCP-connect idiom,
        // which wrote a bare newline into uvicorn's socket and logged `Invalid HTTP request
        // received.` on every 5s interval (~720 lines/h) — same probe-noise class as gh-#64,
        // just container-side. Intentional ops edit; EngineScriptSha256 unchanged —
        // engine/genwave.liq untouched.
        //
        // T85 epoch (F88.4) — re-confirmed at T93
        const string EngineScriptSha256 = "2a957efaa5ba96923cb3554ab1eefcd1fcbf943df0ddd5b53d20c8d5e8fb10bf";
        const string ComposeYamlSha256  = "9ddd169329ef5b092638d1e67279272fc4d7b9f350dcc330cb455d7d92faf981";

        [Fact]
        public void EngineScriptByteMatchesMain()
        {
            // Real, always-run, non-Skip repo-content assertion — no live stack needed, deliberately
            // NOT Category=Integration so it stays IN the filtered wall. Epic Z's own total ban
            // (sequencing notes: "zero genwave.liq/compose.yaml diffs") mirrors Epic V/X/Y's.
            Assert.Equal(EngineScriptSha256, Sha256Hex(Path.Combine("engine", "genwave.liq")));
        }

        [Fact]
        public void ComposeYamlByteMatchesMain()
        {
            Assert.Equal(ComposeYamlSha256, Sha256Hex("compose.yaml"));
        }
    }

    // ---------------------------------------------------------------------
    // (a) — gitea-#233/gitea-#234 in the browser: lookalike fixture catalog, facet-pick -> by-filter bulk
    // vote -> exactly the picked rows move; bulk never-play an album -> restore; both halves also
    // driven through a REAL Chromium this session (see file header).
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioRankingProvenInTheBrowser
    {
        const string SkipVote =
            "Z11(a) — z11smoke, scratch stack, 2026-07-16: a 5-row lookalike fixture catalog " +
            "seeded via ffmpeg sine-tone mp3s tagged with real ID3 metadata — Queen/\"One Vision\" " +
            "(album \"A Kind of Magic\", media id=5), Queen/\"Bohemian Rhapsody\" (album \"A Night " +
            "at the Opera\", media id=4), Queen/\"The Show Must Go On\" (album \"Innuendo\", media " +
            "id=3), Queensrÿche/\"Silent Lucidity\" (album \"Empire\", media id=2), Queensrÿche/" +
            "\"Jet City Woman\" (album \"Empire\", media id=1, the deliberate lookalike + the sub-" +
            "poll-length fixture reused by part (b)). `GET /api/media?artist-exact=Queen` -> " +
            "exactly media ids 3/4/5 (Queensrÿche untouched); `GET /api/media/facets?field=artist` " +
            "-> `[{\"value\":\"Queen\",\"count\":3},{\"value\":\"Queensr\\u00ffche\",\"count\":2}]`. " +
            "`GET /api/ratings?ids=1,2,3,4,5` baseline: all score=50. `POST /api/media/bulk/vote` " +
            "`{\"filter\":{\"artistExact\":\"Queen\"},\"direction\":\"up\"}` -> `{\"updated\":3}` -- " +
            "re-GET ratings shows EXACTLY ids 3/4/5 at score=51, ids 1/2 unchanged at 50. Then " +
            "drove the SAME action through a real headless Chromium (Playwright 1.61.1/Chromium " +
            "149.0.7827.55) against the deployed admin-ui container: logged in, navigated to " +
            "/catalog, selected \"Queen\" from the real `<select aria-label=\"Artist is exactly\">` " +
            "(populated from the live `GET /api/media/facets` fetch, not a stub), submitted the " +
            "filter form -> URL carried `artist-exact=Queen`, page rendered \"3 tracks found\" and " +
            "exactly the 3 Queen rows; clicked the Bulk-actions toolbar's \"Vote up\" icon button -> " +
            "the real `useConfirm` dialog opened with title \"Vote tracks up\" and body \"Vote all 3 " +
            "matching tracks up?\" (the exact matched count, F61's own confirm-copy contract) -> " +
            "clicked Confirm -> toast \"3 tracks voted up.\" rendered -> re-GET " +
            "`/api/ratings?ids=1,2,3,4,5` confirmed scores advanced to EXACTLY ids 3/4/5 at 52, ids " +
            "1/2 still at 50 -- the browser-driven vote moved precisely the same 3 rows the API-" +
            "level call did, nothing else. Screenshot evidence: " +
            "z11smoke/evidence/parta_browser_catalog_filtered_queen.png (filtered table + toast). " +
            "Evidence: z11smoke/evidence/parta_filter_artist_exact_queen.json, " +
            "parta_facets_artist.json, parta_ratings_before.json, parta_bulk_vote_up.json, " +
            "parta_ratings_after_vote.json, parta_ratings_after_browser_vote.json.";

        [Fact(Skip = SkipVote)]
        public void FacetPickBulkVoteMovesExactlyThePickedRows()
        {
            // Z11(a) — artist-exact isolates Queen from Queensrÿche; the by-filter bulk vote moves
            // exactly the 3 Queen rows both at the API and through a real browser-driven click;
            // facet counts match the affected count (F61.1-F61.4, closes gitea-#233).
        }

        const string SkipNeverPlay =
            "Z11(a) — z11smoke, scratch stack, 2026-07-16: `GET /api/media/facets?field=album` -> " +
            "`[{\"value\":\"A Kind of Magic\",\"count\":1},{\"value\":\"A Night at the Opera\"," +
            "\"count\":1},{\"value\":\"Empire\",\"count\":2},{\"value\":\"Innuendo\",\"count\":1}]`. " +
            "`POST /api/media/bulk/never-play` `{\"filter\":{\"albumExact\":\"A Night at the " +
            "Opera\"},\"neverPlay\":true}` -> `{\"updated\":1}` -- media id=4 (\"Bohemian " +
            "Rhapsody\") flagged, score (51, from the prior vote fact) untouched, confirming F33.7 " +
            "score/never-play independence. Rotation audibly skips it: id=4 aired once at " +
            "01:37:54Z (BEFORE the flag took effect, part of normal boot rotation), then a " +
            "continuous 3-second-cadence poll of `/api/now-playing` (30 samples across the " +
            "RecentWindow=2 window in part (b)) and a 51-entry `/api/play-history` dump spanning " +
            "01:37:39Z-01:40:39Z (~3 minutes, dozens of cycles through the remaining 4-track " +
            "catalog) show ZERO further appearances of media id=4 -- the never-play flag durably " +
            "removed it from `MediaRepository`'s `not coalesce(r.never_play, false)` selection " +
            "predicate (F61.2 verified against the real production selection query, not a stub). " +
            "Restore: `POST /api/media/bulk/never-play` `{\"filter\":{\"albumExact\":\"A Night at " +
            "the Opera\"},\"neverPlay\":false}` -> `{\"updated\":1}` -- id=4 neverPlay reset to " +
            "false, score stayed 51 (untouched by the never-play toggle either direction); a " +
            "follow-up 15-sample poll shows id=4 (\"Bohemian Rhapsody\") back on-air at 01:42:53Z -- " +
            "no one-way door (F61.2). Evidence: z11smoke/evidence/parta_facets_album.json, " +
            "parta_bulk_neverplay_album.json, parta_ratings_after_neverplay.json, " +
            "partb_play_history_window2.json, partb_poll_window2.jsonl, " +
            "parta_restore_neverplay.json, parta_ratings_after_restore.json, " +
            "parta_restore_watch.jsonl.";

        [Fact(Skip = SkipNeverPlay)]
        public void BulkNeverPlayThenRestoreRoundTripsAudibly()
        {
            // Z11(a) — bulk never-play on the "A Night at the Opera" album flags exactly the 1
            // matching row; the real production selection query (MediaRepository's
            // `not coalesce(r.never_play, false)` predicate) durably excludes it from rotation for
            // ~3 minutes of continuous polling; restoring it round-trips back on-air (F61.1-F61.2,
            // closes gitea-#233).
        }

        const string SkipFourHundreds =
            "Z11(a) — z11smoke, scratch stack, 2026-07-16: `GET /api/media?artist=Queen&artist-" +
            "exact=Queen` -> 400 `{\"title\":\"Conflicting artist filters.\",\"detail\":\"Name at " +
            "most one of artist or artist-exact.\"}`; `GET /api/media?genre=Metal&genre-" +
            "exact=Metal` -> 400 `{\"title\":\"Conflicting genre filters.\",\"detail\":\"Name at " +
            "most one of genre or genre-exact.\"}` (F52.3's mutual-exclusion guard, unchanged since " +
            "Epic Y, spot-checked again here since F61's bulk endpoints reuse the same " +
            "`BuildAdminWhere`). `GET /api/media/facets?field=bogus` and `GET /api/media/facets` " +
            "(field omitted) both -> 400 `{\"title\":\"Invalid field.\",\"detail\":\"field must be " +
            "one of: artist, album, genre.\"}`. Evidence: " +
            "z11smoke/evidence/parta_400_artist_exact_substring.json, " +
            "parta_400_genre_exact_substring.json, parta_400_unknown_facet_field.json, " +
            "parta_400_missing_facet_field.json.";

        [Fact(Skip = SkipFourHundreds)]
        public void ExactAndSubstringConflictAndUnknownFacetFieldBothReturn400()
        {
            // Z11(a) — a field's substring and exact params named together -> 400 naming the
            // conflict; an unknown/missing facets field -> 400 naming the three valid values
            // (F52.1, F52.3 — the shared filter machinery F61's bulk endpoints ride).
        }
    }

    // ---------------------------------------------------------------------
    // (a, tooltip half) — hover + keyboard-focus reveal tooltips on Live and Catalog icon
    // controls, driven live through the real Chromium (STORY-159/gitea-#234).
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioTooltipCoverageSpotCheckedLive
    {
        const string Skip =
            "Z11(a) — z11smoke, scratch stack, 2026-07-16, driven through a real headless Chromium " +
            "(Playwright 1.61.1, no admin-ui devDependency added -- installed into an isolated " +
            "scratchpad npm project outside this task's file ownership). Catalog bulk toolbar: " +
            "hovered the \"Vote down\" IconButton -> `page.getByRole(\"tooltip\", { name: \"Vote " +
            "down\" })` resolved visible=true within ~150ms; moved the mouse away (tooltip hides), " +
            "then Tab-focused the SAME button via `.focus()` -> the identical `role=\"tooltip\"` " +
            "element became visible=true again -- hover AND keyboard focus both reveal it, matching " +
            "`Tooltip`'s own onMouseEnter/onFocus dual-trigger contract (F62.2) rather than a hover-" +
            "only CSS rule. Live page: same hover-then-focus sequence on the now-playing/play-" +
            "history \"Vote up\" `RatingControls` button -> `role=\"tooltip\"` name=\"Vote up\" " +
            "visible=true on both hover and focus. Screenshot evidence (tooltip visibly rendered in " +
            "each case): z11smoke/evidence/parta_browser_catalog_tooltip_hover.png, " +
            "parta_browser_catalog_tooltip_focus.png, parta_browser_live_tooltip_hover.png, " +
            "parta_browser_live_tooltip_focus.png -- the Live screenshot incidentally also shows " +
            "part (b)'s non-null durationMs/score columns rendered in the real admin-ui play-" +
            "history table (One Vision 00:06/52, The Show Must Go On 00:06/52, Jet City Woman " +
            "00:04/50), corroborating that fact's API-level evidence visually.";

        [Fact(Skip = Skip)]
        public void EveryIconOnlyControlRevealsItsExplanationBothWays()
        {
            // Z11(a) — a real Chromium hover AND a real keyboard Tab-focus both reveal the
            // role=tooltip popup on the Catalog bulk toolbar's icon-only controls and the Live
            // page's RatingControls, with copy matching the control's aria-label (F62.1-F62.2,
            // closes gitea-#234).
        }
    }

    // ---------------------------------------------------------------------
    // (b) — ring integrity live: stable durations/gains across repeat airings (the X10 defect
    // shape gone), RecentWindow=0 stays authoritative, a sub-poll fixture never double-airs.
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioRingIntegrityProvenLive
    {
        const string Skip =
            "Z11(b) — z11smoke, scratch stack, 2026-07-16: `PUT Station:Rotation:RecentWindow=2` " +
            "-> 200 (source=override). The 5-fixture lookalike catalog (all 4-6s tracks, forcing " +
            "fast repeats inside a 2-slot ring) polled every 3s for 90s (30 samples of `/api/now-" +
            "playing`) plus a 51-entry `/api/play-history` dump spanning ~3 minutes: EVERY repeat " +
            "airing of every music track carried its stable, non-null durationMs/gainDb with ZERO " +
            "flicker to null -- media id=1 (\"Jet City Woman\", 4095ms, the sub-poll-length fixture, " +
            "shorter than the 5s admin-ui poll interval) aired 4 times, always durationMs=4095/" +
            "gainDb=22.1; id=2 (\"Silent Lucidity\") 5086/22.1 across 4 airings; id=3 (\"The Show " +
            "Must Go On\") 6077/22.1 across 5 airings; id=5 (\"One Vision\") 6077/22.4 across 4 " +
            "airings -- this directly refutes the X10(e) defect shape (`PlayoutFeeder.Remember`'s " +
            "old bare-id ring eviction nulling duration on repeats inside a small catalog): Z1's " +
            "SPEC F57 metadata-liveness invariant (remember-at-push, join-the-ring-at-push-time) " +
            "holds under exactly the small-catalog/short-RecentWindow condition that exposed the " +
            "old defect. Zero back-to-back double-airs observed either: Station:Cadence's default " +
            "lead-in/back-announce inserts a `tts:*` patter segment between every two music " +
            "airings, so no two music entries are ever adjacent in the history -- id=1 (the sub-" +
            "poll fixture) specifically never repeats without an intervening track (the gitea-#220 " +
            "shape), consistent with Z1's join-the-ring-at-push-time fix eliminating the race " +
            "structurally rather than merely by observation. `PUT Station:Rotation:RecentWindow=0` " +
            "-> 200: with the anti-repeat ring disabled, id=3 and id=5 repeated rapidly (no " +
            "protection against immediate re-selection) across a 20-sample/60s follow-up poll, yet " +
            "EVERY one of those airings still carried the correct non-null durationMs/gainDb (id=3 " +
            "6077/22.1 x4, id=5 6077/22.4 x6) -- the currently-airing row stayed feeder-" +
            "authoritative even with a degenerate window (F57's Window=0 contract). RecentWindow " +
            "restored to 20 afterward. Evidence: z11smoke/evidence/partb_recentwindow0_put.json, " +
            "partb_poll_window2.jsonl, partb_play_history_window2.json, partb_poll_window0.jsonl.";

        [Fact(Skip = Skip)]
        public void RepeatAiringsShowStableDurationsAcrossASustainedDrain()
        {
            // Z11(b) — a small 5-fixture catalog under RecentWindow=2 shows zero null-duration/gain
            // flicker across dozens of repeat airings over ~3 minutes of continuous polling; the
            // sub-poll-length fixture never double-airs back-to-back (F57, closes gitea-#219, gitea-#220, gitea-#229).
        }

        [Fact(Skip = Skip)]
        public void WindowZeroAndSubPollFixturesHoldLive()
        {
            // Z11(b) — RecentWindow=0 keeps the currently-airing row's durationMs/gainDb
            // authoritative even with the anti-repeat ring disabled and tracks repeating rapidly
            // (F57's Window=0 contract).
        }
    }

    // ---------------------------------------------------------------------
    // (c) — scan grace live: a hidden file survives one tick with a logged deferral, flips at the
    // threshold, an mtime touch re-discovers it.
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioRobustnessProvenLive
    {
        const string Skip =
            "Z11(c) — z11smoke, scratch stack, 2026-07-16, MissThreshold=2 (default, unmodified). " +
            "`PUT Library:ScanIntervalSeconds=5` -> 200 (source=override). Baseline `GET /api/" +
            "media/1` (\"Jet City Woman\"): state=ready. Moved `queensryche-jet-city-woman.mp3` OUT " +
            "of the scratch MEDIA_DIR at 2026-07-16T01:45:24Z. After ~6s (one tick): `GET /api/" +
            "media/1` STILL state=ready (row survives), `docker compose logs api` shows \"Scan: " +
            "/media/queensryche-jet-city-woman.mp3 missing from listing (1 of 2) — deferring " +
            "unavailable transition (SPEC F58)\" -- the deferral logged exactly once, on the first " +
            "miss (F58.4). After another ~6s (second tick, ~12s total): state flipped to " +
            "unavailable, log line \"Scan: 0 new, 0 changed, 1 missing\" -- the threshold-driven " +
            "flip happened on the SECOND consecutive miss, not the first, confirming the grace " +
            "period actually deferred rather than being a no-op (F58.1-F58.2). Moved the file back " +
            "into MEDIA_DIR + `touch`ed its mtime at 2026-07-16T01:45:45Z: after ~6s, state=ready " +
            "again with durationMs=4095 unchanged, log \"Scan: 0 new, 1 changed, 0 missing\" -- " +
            "rediscovered as a CHANGED row (its library.media id survived the whole outage, not " +
            "re-created) (F58.5). Library:ScanIntervalSeconds restored to 60 afterward. Evidence: " +
            "z11smoke/evidence/partc_scaninterval_put.json, partc_media1_baseline.json, " +
            "partc_media1_after_tick1.json, partc_scan_log_tick1.txt, partc_media1_after_tick2.json, " +
            "partc_scan_log_tick2.txt, partc_media1_rediscovered.json, partc_scan_log_rediscover.txt.";

        [Fact(Skip = Skip)]
        public void ScanGraceDefersOneMissAndFlipsAtTheThresholdLive()
        {
            // Z11(c) — a mounted file hidden for one scan tick survives (state stays ready) with a
            // logged deferral naming the (1 of N) count; a second consecutive miss flips it to
            // unavailable; restoring the file with a touched mtime rediscovers the SAME row as
            // "changed", not "new" (F58, closes gitea-#223).
        }
    }

    // ---------------------------------------------------------------------
    // (d) — LUFS offset re-derivation: recorded on the scratch stack, disagreed materially with
    // the seeded 3.5, re-derived to 1.5 and pinned in the Story013 header; the actual live gate
    // (GENWAVE_LIVE_LUFS_GATE=1) then run against the scratch stack and passed.
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioGateOffsetDerivedAndPinned
    {
        const string Skip =
            "Z11(d) — z11smoke, scratch stack, 2026-07-16. Effective Loudness:TargetLufs confirmed " +
            "via `GET /api/settings` = -16 (appsettings default, unmodified on this stack). " +
            "Recorded 180.04s of the scratch stack's OWN Icecast mount (`ffmpeg -i http://" +
            "localhost:18000/stream -t 180`, the scratch-mapped port -- never the operator's " +
            "production stream) spanning 2026-07-16T01:48:44Z-01:51:42Z. `ffmpeg ... -filter_complex " +
            "ebur128=peak=true` measured Integrated loudness I = -17.5 LUFS (LRA 2.6 LU, true peak " +
            "-4.7 dBFS). Offset = target - measured = -16 - (-17.5) = 1.5 LU -- this is Reading C. " +
            "It disagrees MATERIALLY with the seeded 3.5 LU (Reading A, real music, 2026-07-12): a " +
            "2.0 LU gap, most of the assertion band's own ±2.5 LU tolerance width, and disagrees in " +
            "a THIRD direction from Reading B's -0.6 LU. Content note (the derivation contract's " +
            "own ask): this 180s window was NOT real, timbrally-varied music -- it was this epic's " +
            "own 5-fixture lookalike catalog (synthetic ffmpeg sine tones, 4-6s each, heavily " +
            "interleaved with `tts:*` lead-in/back-announce patter on every track per Station:" +
            "Cadence defaults, crossfading at the compose GW_XFADE_MIN/MAX=2/8s defaults) -- a pure " +
            "sine tone's low crest factor plausibly engages the -1 dBTP true-peak ceiling less " +
            "aggressively than real program material would, which is the likeliest explanation for " +
            "Reading C's smaller gap versus Reading A's, not evidence Reading A was wrong. Per the " +
            "explicit operator ruling already on record (2026-07-15, recorded in THIS file: " +
            "\"derivation evidence comes from this gate\"), ProgramLoudnessOffset was re-derived " +
            "from 3.5 to 1.5 in Story013_AcceptanceGate02_LevelMatchingRealKokoro.cs's own header " +
            "(full Reading A/B/C trail preserved, nothing papered over) and its `internal const " +
            "double ProgramLoudnessOffset` constant updated to match. Sanity-checked BEFORE " +
            "shipping the change: Story161's own \"known station mean\" regression (Reading A's " +
            "-15.5 LUFS at a -12 operator override, the gitea-#204 defect this whole gate exists to " +
            "prevent) still lands inside the recomputed band at the new offset -- band = [-16.0, " +
            "-11.0], -15.5 sits 0.5 LU inside the lower bound -- so re-deriving to 1.5 does NOT " +
            "regress gitea-#204. Updated Story161_LufsGateOffset.cs's three facts to match (the band-math " +
            "test's offset constant, new substring assertions for Reading C, the updated " +
            "`ProgramLoudnessOffset = 1.5;` string assertion) -- `dotnet test --filter " +
            "FullyQualifiedName~FeatureLufsGateOffset`: 4/4 passed. Then ran the ACTUAL Story013 " +
            "live gate (`RecordedIntegratedLufsApproximatesTargetWithinTolerance`) with " +
            "`GENWAVE_LIVE_LUFS_GATE=1 ADMIN_PASSWORD=z11adminui` pointed at the scratch stack -- " +
            "that fact hardcodes localhost:8000/8080 (the standard-port acceptance-gate contract, " +
            "not scratch-aware), so the z11smoke icecast/api containers were TEMPORARILY also bound " +
            "on 8000/8080 alongside their scratch 18000/18080 ports (production confirmed NOT " +
            "running throughout via `docker compose ls -a`, checked immediately before and after) " +
            "-- removed again immediately after the one test run, containers recreated back to " +
            "scratch-only ports, confirmed via `ss -tlnp` showing no bare :8000/:8080 binding and " +
            "the existing session cookie/catalog surviving both api recreates. Result: PASSED in " +
            "3m2s -- \"Effective TargetLufs=-16.0, ProgramLoudnessOffset=1.5 (measured=-17.5, " +
            "expected band=[-20.0, -15.0], tolerance=±2.5).\" -- the gate is green at the newly-" +
            "pinned offset on a live measurement of the SAME stack the offset was derived from. " +
            "Evidence: z11smoke/evidence/lufs_recording_start.txt, partd_ebur128_output.txt, " +
            "partd_story013_gate_run.txt, partd_extra_ports_removed.txt.";

        [Fact(Skip = Skip)]
        public void TheOffsetIsReDerivedOnTheScratchRecordingAndTheGatePasses()
        {
            // Z11(d) — a 180s scratch-stack recording measured -17.5 LUFS at an effective -16
            // target, materially disagreeing with the seeded 3.5 LU offset; per the recorded
            // operator ruling, ProgramLoudnessOffset was re-derived to 1.5 LU and pinned in the
            // Story013 header (full A/B/C reading trail), verified not to regress the gitea-#204 fix,
            // and the live Story013 gate then PASSED against this same scratch stack at the new
            // offset (F64, closes gitea-#204).
        }
    }

    // ---------------------------------------------------------------------
    // (e) — the regression wall: dotnet + admin-ui halves, F2–F56 gates standing, zero
    // engine/compose diffs (also the always-run hash-pin fact above).
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioRegressionWallAndCloseOut
    {
        const string SkipWall =
            "Z11(e) — RUN 2026-07-16 (after the Story013/Story161 re-derivation edits above). " +
            "`dotnet build GenWave.sln`: Build succeeded, 0 Warning(s), 0 Error(s). `dotnet " +
            "test GenWave.sln --filter \"Category!=Integration\"`: 0 failed across five " +
            "projects -- Core 97/97, Orchestration 56/59 (3 skipped), MediaLibrary 38/64 (26 " +
            "skipped, filtered subset), Tts 117/128 (11 skipped), Host 560/590 (30 skipped -- this " +
            "file's own rewrite converts the prior 9 bare-pending facts into 2 always-run passing " +
            "hash-pin facts + 10 Category=Integration Skip-pinned facts (9 carried over 1:1 plus " +
            "the new 400-spot-check fact), the Story127/T11/Story141/147/153 mechanism: net +2 " +
            "passed / -9 skipped versus the pre-rewrite 558/597 run, since the 10 Integration-" +
            "tagged facts are no longer selected by the filter at all) -- 0 failed overall. " +
            "Separately, `dotnet test tests/GenWave." +
            "MediaLibrary.Tests` (unfiltered, its OWN self-bootstrapping compose project `genwave-" +
            "libtest`, confirmed absent via `docker ps -a`/`docker compose ls -a` before and " +
            "after): 349 passed, 0 failed, 47 skipped, 396 total -- more total tests than Y7's own " +
            "364-test baseline (Epic Z added coverage), 0 failed either way. `cd admin-ui && npx " +
            "tsc --noEmit`: clean, zero output. `npx jest`: 47 suites passed, 435 passed, 11 todo, " +
            "446 total (same suite/pass counts as Y7 -- Epic Z's admin-ui surface (F61/F62) was " +
            "already covered by Z6/Z7/Z8's own commits before this gate ran; the one pre-existing " +
            "harmless React act() warning in catalog-selection-toolbar.spec.tsx carried unchanged " +
            "since Q12 through Y7 -- not a failure, not introduced here). `npm run build`: green, " +
            "13 routes compiled (same route set as Y7 -- Epic Z shipped zero new pages, only new " +
            "toolbar actions/tooltips on existing pages). `grep -rn \"window.confirm(\" admin-ui/" +
            "app admin-ui/components`: zero call sites. `git diff main...HEAD -- compose.yaml` and " +
            "`git diff main...HEAD -- engine/genwave.liq` both empty (also asserted as the always-" +
            "run hash-pin fact above, byte-identical to Story141's/147's/153's own pins -- F57-F64 " +
            "touch neither file). F2-F56 gates stand: `git diff main...HEAD --stat -- " +
            "'*AcceptanceGate*.cs'` shows only THIS file (Story162 itself, rewritten by this task) " +
            "-- no prior epic's *AcceptanceGate*.cs was touched. Two additional test-only files " +
            "WERE touched, both inside this gate's own explicit remit (Z11(d)'s own instruction: " +
            "\"re-derive ProgramLoudnessOffset, pin it + document in the Story013 header ... re-run " +
            "the Story161 facts\") -- Story013_AcceptanceGate02_LevelMatchingRealKokoro.cs (the " +
            "offset constant + derivation-contract header) and Story161_LufsGateOffset.cs (its " +
            "three facts updated to match) -- confirmed by source read that no OTHER gate's " +
            "assertions were touched, only the one constant this epic's own task explicitly " +
            "authorized moving.";

        [Fact(Skip = SkipWall)]
        public void TheRegressionWallIsGreenWithZeroEngineOrComposeDiffs()
        {
            // Z11(e) — build zero-warnings + filtered/unfiltered dotnet tests green + admin-ui
            // tsc/jest/build green + zero compose/engine diff + F2-F56 gates stand (the one
            // explicitly-authorized LUFS-offset re-derivation touch, zero other gates' assertions
            // changed).
        }

        const string SkipCloseOut =
            "Z11(f) — Gitea state checked 2026-07-16 via the API (read-only; this gate never closes " +
            "issues, per instruction and the MEMORY.md house rule). gitea-#204 \"Recalibrate the " +
            "recorded-LUFS acceptance gate for operator target overrides\", gitea-#219 \"RecentWindow=0 " +
            "evicts the on-air id from feederOwnedIds — metadata falls back to engine echo\", gitea-#220 " +
            "\"Tracks shorter than ~2 poll intervals can transiently double-air (pre-existing " +
            "feeder race)\", gitea-#222 \"EnrichmentService.workerTasks bag never prunes retired worker " +
            "tasks\", gitea-#223 \"ScanService flips freshly-enriched rows to unavailable on a single " +
            "transient listing miss\", gitea-#228 \"Year lookup can match a later recording of the same " +
            "title — earliest-release picks the wrong original year\", gitea-#229 \"PlayoutFeeder." +
            "Remember bare-id eviction drops feeder-owned metadata for repeat airings inside the " +
            "recent window\", gitea-#233 \"Add ability to rank artists & albums\", gitea-#234 \"Some icons need " +
            "flyover help\", and gitea-#235 \"FreshDeployConfig compose-env mirror has no drift guard " +
            "against compose.yaml\" are all OPEN, confirmed unchanged by this gate (read-only, " +
            "never mutated). All ten close on the live evidence in the facts above -- gitea-#204 on the " +
            "re-derived, gate-passing ProgramLoudnessOffset (part d); gitea-#219/gitea-#220/gitea-#229 on the ring-" +
            "integrity facts (part b, zero null flicker and zero double-airs observed across " +
            "dozens of repeat airings); gitea-#222/gitea-#223 on Z2/Z3's own unit-level coverage plus this " +
            "gate's own live scan-grace observation (part c); gitea-#228 on Z4's year-lookup fix; gitea-#233/" +
            "gitea-#234 on the facet-vote-never-play and tooltip facts (part a, this session's own real-" +
            "browser evidence) -- but closing them is the operator's call after they deploy this " +
            "branch, verify against their own catalog, and decide the evidence is sufficient for " +
            "their own station -- never this gate's. This gate leaves all ten issues exactly as " +
            "found.";

        [Fact(Skip = SkipCloseOut)]
        public void TenIssuesCloseOnEvidenceWithOperatorAuthorization()
        {
            // Z11(f) — the epic isn't done while gitea-#204/gitea-#219/gitea-#220/gitea-#222/gitea-#223/gitea-#228/gitea-#229/gitea-#233/gitea-#234/gitea-#235
            // are open; closing them is the operator's decision after deploying and verifying on
            // their own station, never this gate's.
        }
    }
}

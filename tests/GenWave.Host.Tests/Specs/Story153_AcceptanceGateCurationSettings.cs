// STORY-153 — Acceptance gate: curation & settings fidelity end-to-end (Epic Y / SPEC F52–F56,
// closes gitea-#189, gitea-#221, gitea-#224, gitea-#225, gitea-#227, gitea-#230, gitea-#231).
//
// BDD specification — xUnit. Y7 ran 2026-07-15 against an isolated scratch-stack smoke (own -p
// y7smoke project, own .env, non-colliding ports 18000/18080 via a `!override`-tagged compose
// overlay kept OUTSIDE the repo in the scratchpad — never editing the tracked compose.yaml;
// `docker compose ls -a`/`docker ps -a`/`docker volume ls`/`docker network ls` confirmed zero
// `y7smoke` remnants before and after; `down -v` on completion) — never the operator's live
// station (project `genwave`, standard ports 8000/8080/3000; confirmed via `docker compose ls -a`
// both before and after that it was not even running on this box throughout the run — only the
// operator's separate, long-exited `mrdgenwave`/`mrd-genwave-ui` containers were present, both
// unchanged). Rewritten in the Story133/Story141/Story147 idiom. Every fact below is one of:
//   (1) a real, always-run, non-Skip repo-content assertion (Story102/107/S8/T11/Story133/
//       Story141/Story147's grep/hash-assert idiom) — no live stack needed, so it deliberately has
//       NO Integration trait and stays IN the filtered wall; here it is the SAME zero-diff hash
//       pin Story141 introduced for Epic V, carried forward unchanged through Epic X and now
//       Epic Y (the sequencing notes' "zero engine/compose diffs" ban stands for F52–F56 too);
//   (2) Skip-pinned with THIS SESSION's dated y7smoke scratch-stack evidence, Category=Integration; or
//   (3) Skip-pinned with the EXACT operator procedure for what genuinely needs a human decision
//       (issue closure), Category=Integration.
// No fact in this file keeps a bare "pending Y7" reason.
//
// Honesty note on the browser half: no Playwright/browser-automation tool was available in this
// session (checked: no MCP browser tool registered; `npx playwright --version` reports the
// package not installed). Every fact below that has a DOM-rendering component cites the specific
// jest spec(s) that already prove that rendering on this exact shipped code (the X10(d) precedent
// for citing prior-verified browser halves when a live browser cannot be driven this session) —
// and separately drives the real data path (the production api binary, the real Kokoro container,
// the real Postgres-backed settings store) via curl, so every fact's non-DOM half is still a live,
// production-binary proof, never a unit-test-only claim.

using System.Security.Cryptography;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAcceptanceGateCurationSettings
{
    /// <summary>Repo root, resolved relative to the test assembly's build output (Story074/102/107/141/147's convention).</summary>
    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static string Sha256Hex(string relativePath)
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepoRoot, relativePath));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    // ---------------------------------------------------------------------
    // (e, engine/compose half) — the epic's TOTAL ban continues unchanged from Epic V/X.
    // ---------------------------------------------------------------------

    public sealed class ScenarioEngineAndComposeCarryZeroDiffFromMain
    {
        // Pinned 2026-07-15 — identical hashes to Story141's and Story147's own pins (V10/X10 ran
        // the same values): F52–F56 touch neither file, so the byte content is unchanged since
        // Epic V shipped.
        //
        // ComposeYamlSha256 re-pinned 2026-07-18 (PLAN T15, SPEC F64.1/F64.2, STORY-172): the
        // api service gained the public-listener port mapping (8081) plus ASPNETCORE_URLS/
        // Spectator__PublicPort env vars — a real, intentional edit from a LATER epic, not a
        // regression of F52–F56 touching neither file (still true). EngineScriptSha256 is
        // untouched — T15 does not touch engine/genwave.liq.
        //
        // ComposeYamlSha256 re-pinned AGAIN 2026-07-18 (PLAN T17, SPEC F61.4, STORY-166): admin_ui
        // gained `profiles: ["admin"]` — another intentional edit from a LATER epic, not a
        // regression of F52–F56 touching neither file. EngineScriptSha256 unchanged.
        //
        // ComposeYamlSha256 re-pinned YET AGAIN 2026-07-18 (PLAN T21, SPEC F62.12 addendum,
        // STORY-179): the api service gained Icecast__StatsUrl/Icecast__AdminPassword env vars for
        // the spectator listener-count poll — another intentional edit from a LATER epic, not a
        // regression of F52–F56 touching neither file. EngineScriptSha256 unchanged.
        // ComposeYamlSha256 re-pinned 2026-07-19 (kokoro image bump): the kokoro service moved to
        // kokoro-fastapi-cpu v0.6.0 and gained a mem_limit backstop for the upstream RSS leak
        // (remsky/Kokoro-FastAPI#453) — an intentional ops edit from outside this epic, not a
        // regression of its zero-diff promise. EngineScriptSha256 is untouched.
        //
        // ComposeYamlSha256 re-pinned 2026-07-20 (PLAN T34, SPEC F70.1, STORY-190): a new `piper`
        // service (Piper local-fallback TTS sidecar) plus Tts__Fallback__Endpoint/
        // Tts__Fallback__Voice env vars on the api service and a piper_models volume — another
        // intentional edit from a LATER epic, not a regression of F52–F56 touching neither file.
        // EngineScriptSha256 unchanged.
        //
        // ComposeYamlSha256 re-pinned 2026-07-20 (Q3 housekeeping, "cloudflared observability",
        // SPEC F77): a new, optional `cloudflared` service (profiles: ["tunnel"], off by default,
        // no host ports) — versions the tunnel that previously ran outside the repo as unmanaged
        // infrastructure, with a healthcheck/metrics contract. Another intentional edit from a
        // LATER epic, not a regression of F52–F56 touching neither file. EngineScriptSha256
        // unchanged — this task does not touch engine/genwave.liq.
        //
        // ComposeYamlSha256 re-pinned 2026-07-21 (PLAN T49, SPEC F78.1/F78.3/F78.4/F78.5,
        // STORY-202): a new, optional `alloy` log-shipper service (profiles: ["logging"], off by
        // default, no host ports) plus a new `alloy_data` named volume — versions the log
        // shipper for the F78 observability expansion. Another intentional edit from a LATER
        // epic, not a regression of F52–F56 touching neither file. EngineScriptSha256 unchanged
        // — this task does not touch engine/genwave.liq. Re-pinned again same day (T49 review
        // fix, SPEC F78.5): the alloy healthcheck's bare `grep -qi ready` matched Alloy's
        // not-ready body too ("Alloy is not ready." still contains "ready" as a substring),
        // reporting healthy for a not-ready Alloy — now discriminates on the contiguous phrase
        // "is ready", which only the 200 "Alloy is ready." body contains.
        //
        const string EngineScriptSha256 = "a256fd3f2797ed9b52e3f8507e8ca610aa02218e2fedc5c231369f0ccaab9bd6";
        const string ComposeYamlSha256  = "3c414ed2d8c0e09e2969a5ae5f1f741f431993207fb21738103e0166204c14d5";

        [Fact]
        public void EngineScriptByteMatchesMain()
        {
            // Real, always-run, non-Skip repo-content assertion — no live stack needed, deliberately
            // NOT Category=Integration so it stays IN the filtered wall. Epic Y's own total ban
            // (sequencing notes: "zero engine/compose diffs is a gate assertion") mirrors Epic V/X's.
            Assert.Equal(EngineScriptSha256, Sha256Hex(Path.Combine("engine", "genwave.liq")));
        }

        [Fact]
        public void ComposeYamlByteMatchesMain()
        {
            Assert.Equal(ComposeYamlSha256, Sha256Hex("compose.yaml"));
        }
    }

    // ---------------------------------------------------------------------
    // (a) — the gitea-#189 facet sweep: lookalike catalog, exact filter chips, by-filter eligibility
    // sweep, facet counts, eligible=false remainder, and the two 400 spot-checks.
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioFacetSweepAndExactFilters
    {
        const string SkipSweep =
            "Y7(a) — y7smoke, scratch stack, 2026-07-15: a 5-row lookalike fixture catalog seeded " +
            "via ffmpeg sine-tone mp3s — Queen/\"One Vision\" (genre Rock, album \"A Kind of Magic\", " +
            "media id=5), Queen/\"The Show Must Go On\" (genre Metal, album \"Innuendo\", media " +
            "id=4), Queensrÿche/\"Silent Lucidity\" (genre Metal, album \"Empire\", media id=3 — the " +
            "deliberate lookalike), Local Test Artist/Pop (media id=2), Another Test Artist/Jazz " +
            "(media id=1). `GET /api/media?artist-exact=Queen` -> exactly media ids 4 and 5 " +
            "(Queensrÿche untouched) -- the case-insensitive EQUALITY match the whole feature " +
            "exists for (F52.3). `POST /api/media/eligibility` with " +
            "`{\"filter\":{\"artistExact\":\"Queen\"},\"eligible\":false}` -> `{\"affected\":2}` -- " +
            "exactly the two picked rows, through the SAME `BuildAdminWhere` the browse call used " +
            "(F52.4). Post-sweep full catalog dump confirms ids 4/5 eligible=false and ids 1/2/3 " +
            "(including the lookalike) still eligible=true, unchanged. `GET " +
            "/api/media?genre-exact=Rock&genre-exact=Metal` -> exactly ids 3, 4, 5 (Rock OR Metal " +
            "across the multi-genre catalog; Pop/Jazz excluded) -- the F52.3 OR-match proof. " +
            "`GET /api/media/facets?field=artist` -> `[{\"value\":\"Another Test " +
            "Artist\",\"count\":1},{\"value\":\"Local Test Artist\",\"count\":1},{\"value\":\"Queen" +
            "\",\"count\":2},{\"value\":\"Queensrÿche\",\"count\":1}]` -- Queen's facet count (2) " +
            "matches the sweep's affected count (2) exactly, and Queensrÿche is a genuinely distinct " +
            "group despite the shared prefix. `GET /api/media?eligible=false` -> exactly ids 4 and 5 " +
            "-- the ineligible remainder is findable, confirming the sweep's own effect rather than " +
            "trusting the affected count alone. Facet endpoint's browser-picker half (artist/album " +
            "single-selects, genre multi-select, filter chips, hasBulkFilter arming on exact " +
            "params) is jest-proven end to end in `catalog-facet-pickers.spec.tsx` on this exact " +
            "shipped code (13 specs, all passing in this session's own admin-ui wall) -- no browser " +
            "automation tool was available this session (see file header), so the DOM half is cited " +
            "from that suite rather than re-driven live; the data path above is the live, " +
            "production-binary half of the same claim. Evidence: y7smoke/evidence/parta_media_boot.json, " +
            "parta_facets_artist.json, parta_facets_album.json, parta_facets_genre.json, " +
            "parta_filter_artist_exact_queen.json, parta_bulk_eligibility_sweep.json, " +
            "parta_eligible_false.json, parta_media_post_sweep.json, " +
            "parta_filter_genre_exact_rock_metal.json.";

        [Fact(Skip = SkipSweep)]
        public void TheFacetSweepFlipsExactlyThePickedRowsAndSparesTheLookalike()
        {
            // Y7(a) — artist-exact isolates Queen from Queensrÿche; the by-filter eligibility sweep
            // affects exactly the 2 Queen rows; the lookalike and every other row stay untouched
            // (F52.3, F52.4, closes gitea-#189).
        }

        [Fact(Skip = SkipSweep)]
        public void FacetCountsMatchTheBulkAffectedCounts()
        {
            // Y7(a) — the artist facet's Queen count (2) matches the bulk eligibility sweep's
            // affected count (2) exactly; eligible=false afterward finds precisely those 2 rows
            // (F52.1, F52.4).
        }

        const string SkipFourHundreds =
            "Y7(a) — y7smoke, scratch stack, 2026-07-15: `GET /api/media?artist=Queen&artist-" +
            "exact=Queen` -> 400 `{\"title\":\"Conflicting artist filters.\",\"detail\":\"Name at " +
            "most one of artist or artist-exact.\"}`; `GET /api/media?genre=Metal&genre-" +
            "exact=Metal` -> 400 `{\"title\":\"Conflicting genre filters.\",\"detail\":\"Name at " +
            "most one of genre or genre-exact.\"}` (F52.3's mutual-exclusion guard, the F49.1 " +
            "precedent). `GET /api/media/facets?field=bogus` and `GET /api/media/facets` (field " +
            "omitted entirely) both -> 400 `{\"title\":\"Invalid field.\",\"detail\":\"field must " +
            "be one of: artist, album, genre.\"}` (F52.1). Evidence: " +
            "y7smoke/evidence/parta_400_exact_substring.json, parta_400_genre_exact_substring.json, " +
            "parta_400_unknown_facet_field.json, parta_400_missing_facet_field.json.";

        [Fact(Skip = SkipFourHundreds)]
        public void ExactAndSubstringConflictAndUnknownFacetFieldBothReturn400()
        {
            // Y7(a) — a field's substring and exact params named together -> 400 naming the
            // conflict; an unknown/missing facets field -> 400 naming the three valid values
            // (F52.1, F52.3).
        }
    }

    // ---------------------------------------------------------------------
    // (b) — ceilings live: over-ceiling PUTs (int + double), both bounds named, values unchanged
    // on re-GET; an over-ceiling env var boots healthy (F53.2).
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioCeilingsHoldLiveAndBootIsNeverBricked
    {
        const string Skip =
            "Y7(b) — y7smoke, scratch stack, 2026-07-15: `PUT /api/settings` " +
            "`[{\"key\":\"Library:EnrichmentConcurrency\",\"value\":\"999999\"}]` (int kind, " +
            "ceiling 32) -> 400 `\"Value '999999' is not valid for " +
            "'Library:EnrichmentConcurrency'. Must be an integer between 1 and 32 (workers).\"` -- " +
            "both bounds named. `[{\"key\":\"Admin:PlayHistoryCapacity\",\"value\":\"5001\"}]` (a " +
            "second int kind, ceiling 5000) -> the same shape, both bounds named. " +
            "`[{\"key\":\"Library:CueDetection:MinSilenceDurationSec\",\"value\":\"61\"}]` (double " +
            "kind, exclusive floor 0 / ceiling 60) -> 400 `\"Must be greater than 0 and at most " +
            "60.\"`. Re-`GET /api/settings` after all three rejected PUTs shows every value " +
            "unchanged (EnrichmentConcurrency=4, PlayHistoryCapacity=50, " +
            "MinSilenceDurationSec=0.5, all source=default) -- nothing partially written (F53.1, " +
            "F53.3). Separately: recreated the `api` container with " +
            "`Library__EnrichmentConcurrency=999999` (an F53.1-over-ceiling value) as a plain env " +
            "var for one boot cycle -- the container came up `healthy` (docker healthcheck passing, " +
            "`GET /health` -> 200), `GET /api/settings` showed the value bound and live " +
            "(source=default, value=999999, no boot crash, no validation error) -- confirming F53.2: " +
            "boot validation is deliberately NOT tightened to match the settings-API-only ceiling. " +
            "The env override was then removed and the api container recreated again against the " +
            "normal overlay -- confirmed EnrichmentConcurrency back to 4, the 5-row catalog and the " +
            "part-(a) eligibility flips both survived the two api recreates untouched (persisted in " +
            "Postgres, not in-process state). Evidence: y7smoke/evidence/partb_400_int_ceiling.json, " +
            "partb_400_int_ceiling2.json, partb_400_double_ceiling.json, " +
            "partb_settings_after_rejected_puts.json, partb_overceiling_boot_log.txt, " +
            "partb_settings_after_restore.json.";

        [Fact(Skip = Skip)]
        public void CeilingsRejectLiveAndAnOverCeilingEnvBootsHealthy()
        {
            // Y7(b) — representative over-ceiling PUTs (two int kinds, one double kind) all 400
            // with both bounds named, nothing persisted; an over-ceiling env value still boots a
            // healthy api container (F53.1–F53.3, closes gitea-#221).
        }
    }

    // ---------------------------------------------------------------------
    // (c) — dropdowns live: Kokoro's real voice list, persona names, None deactivates, the
    // fallback path's real trigger condition exercised via a live blocked fetch.
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioVoiceAndPersonaDropdownsAreReal
    {
        const string Skip =
            "Y7(c) — y7smoke, scratch stack, 2026-07-15: `GET /api/voices` against the REAL " +
            "`ghcr.io/remsky/kokoro-fastapi-cpu:v0.2.1` container returned its actual 67-voice list " +
            "(af_alloy .. zm_yunyang) -- not a stub. `POST /api/personas` " +
            "`{\"name\":\"DJ Nova\",\"voice\":\"af_nova\"}` -> 201, `GET /api/personas` -> " +
            "`[{\"id\":1,\"name\":\"DJ Nova\",...,\"voice\":\"af_nova\"}]` -- persona names come " +
            "from the real station-schema-backed API, af_nova drawn from the real voice list above. " +
            "`PUT Station:Persona:ActiveId=1` -> 200, activated (source=override); `PUT " +
            "Station:Persona:ActiveId=0` -> 200, deactivated back to None (F54.3) -- both round-" +
            "tripped live. Fallback trigger proven at the real network boundary rather than waiting " +
            "out CachedVoiceLister's ~5-minute TTL: `PUT Tts:Endpoint=" +
            "http://kokoro-unreachable.invalid:8880` (a syntactically valid but unreachable host -- " +
            "CachedVoiceLister's own documented contract is that a live endpoint repoint is a " +
            "deliberate cache-miss-and-refetch, F36.4) -> 200, then `GET /api/voices` -> 502 " +
            "`{\"title\":\"Voices listing unavailable.\",\"detail\":\"The TTS backend could not be " +
            "reached. Try again shortly.\"}` -- the exact upstream-failure signal " +
            "`VoiceSettingControl`'s fetch-failure branch degrades on. Restored `Tts:Endpoint` to " +
            "`http://kokoro:8880` -> `GET /api/voices` served the real list again. The DOM half of " +
            "the fallback degrade (free-text input + notice for Voice; number input for ActiveId) " +
            "is jest-proven in `settings-semantic-controls.spec.tsx`'s \"fetch failure degrades to " +
            "the shipped fallbacks\" scenario (2 specs, both passing in this session's own admin-ui " +
            "wall) -- no browser automation tool was available this session (see file header) to " +
            "re-drive the render live, so that half is cited rather than re-observed; the 502-at-" +
            "the-real-network-boundary above is the live, production-binary half of the same claim. " +
            "Evidence: y7smoke/evidence/partc_voices.json, partc_persona_create.json, " +
            "partc_personas_after_create.json, partc_settings_after_persona_toggle.json, " +
            "partc_voices_blocked_fetch.json, partc_voices_restored.json.";

        [Fact(Skip = Skip)]
        public void VoiceAndPersonaDropdownsAreRealAndDegradeToFallbacks()
        {
            // Y7(c) — GET /api/voices is Kokoro's real 67-voice list; GET /api/personas serves a
            // real created persona by name; ActiveId=0 deactivates; a live-repointed unreachable
            // Tts:Endpoint reproduces the exact 502 the fallback UI (jest-proven) degrades on
            // (F54.2–F54.4, closes gitea-#224, gitea-#225).
        }
    }

    // ---------------------------------------------------------------------
    // (d) — no blanks, no unexplained keys: seeded defaults, override precedence, help-text
    // coverage, and the ArtistSeparation/RecentWindow coupling notice + capped save.
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioNoBlanksAndEveryKeyExplainsItself
    {
        const string SkipDefaults =
            "Y7(d) — y7smoke, scratch stack, 2026-07-15: a fresh boot's `GET /api/settings` (30 " +
            "keys total, matching `SETTINGS_HELP_KEYS`'/`StationSettingsAllowlist.All`'s count " +
            "exactly) showed all four previously-blank keys with real seeded values and " +
            "source=\"default\": Library:YearLookup:Endpoint=\"https://musicbrainz.org/ws/2\", " +
            "Library:YearLookup:MinScore=\"90\", Library:CueDetection:MinSilenceDurationSec=\"0.5\", " +
            "Library:Energy:WindowSeconds=\"12.0\" -- these four keys carry no empty strings; the " +
            "only empty values in the 30-key dump are the two documented honest blanks, " +
            "Llm:Endpoint and Llm:Model (F55.1; the seed==C#-" +
            "initializer drift guard is the always-green xUnit fact " +
            "`FeatureSettingsDefaultsMatchInitializers` in Story151_SeededDefaults.cs, part of this " +
            "session's own filtered wall). `PUT Library:YearLookup:MinScore=75` -> 200, re-GET " +
            "shows value=\"75\", source=\"override\" -- the override wins over the seeded default, " +
            "seeding changed defaults not precedence (F55.1 AC5). Every key carrying help text is " +
            "jest-proven by `settings-help-coverage.spec.tsx`'s parity guard (imports " +
            "SETTINGS_HELP_KEYS, asserts every one of the 30 renders a help sentence -- passing in " +
            "this session's own admin-ui wall) backed by the live 30-key count above matching that " +
            "list 1:1 -- no browser automation tool was available this session (see file header) to " +
            "re-render the page, so the rendering half is cited rather than re-observed; the data-" +
            "level 30-key parity above is the live half of the same claim. Evidence: " +
            "y7smoke/evidence/partd_settings_full.json, partd_override_wins.json.";

        [Fact(Skip = SkipDefaults)]
        public void TheFourSeededDefaultsShowAndEveryKeyCarriesHelpText()
        {
            // Y7(d) — the four previously-blank keys show real seeded values with source=default;
            // an override on one wins with source=override; the live 30-key set matches
            // SETTINGS_HELP_KEYS 1:1, backing the jest-proven full help-text coverage (F55.1,
            // F55.3, closes gitea-#230, gitea-#231).
        }

        const string SkipCoupling =
            "Y7(d) — y7smoke, scratch stack, 2026-07-15: starting from the defaults " +
            "(RecentWindow=20, ArtistSeparation=2, uncapped), `PUT " +
            "[{\"key\":\"Station:Rotation:RecentWindow\",\"value\":\"5\"},{\"key\":\"Station:" +
            "Rotation:ArtistSeparation\",\"value\":\"10\"}]` (a deliberately capped shape, " +
            "separation 10 > window 5) -> 200, both persisted verbatim on re-GET (source=override) " +
            "-- no server-side rejection exists for this shape, exactly F56.4's \"legal and " +
            "harmless\" contract; no new validator was added, none is needed (F56.2). The live " +
            "inline notice itself (`RotationCouplingNotice`, rendered exactly when the FORM's " +
            "current pre-submit ArtistSeparation exceeds RecentWindow, clearing when edited back " +
            "below the threshold, and never blocking submit) is jest-proven end to end in " +
            "`rotation-coupling-notice.spec.tsx` (6 specs, all passing in this session's own admin-" +
            "ui wall, including the \"submission proceeds with the notice showing\" spec that " +
            "exercises the exact capped-PUT path proven live above) -- no browser automation tool " +
            "was available this session (see file header) to re-render the notice live, so that " +
            "half is cited rather than re-observed; the real capped-PUT persistence above is the " +
            "live, production-binary half of the same claim. Evidence: " +
            "y7smoke/evidence/partd_capped_rotation_put.json, partd_capped_rotation_after.json.";

        [Fact(Skip = SkipCoupling)]
        public void TheCouplingNoticeAppearsExactlyWhenSeparationExceedsWindow()
        {
            // Y7(d) — a capped ArtistSeparation > RecentWindow shape PUTs and re-GETs cleanly (no
            // new server-side rule); the jest-proven inline notice renders exactly when the form
            // holds that same capped shape and never blocks submission (F56.1, F56.2, F56.4,
            // closes gitea-#227).
        }
    }

    // ---------------------------------------------------------------------
    // (e) — the regression wall: dotnet + admin-ui halves, F2–F51 gates standing, zero
    // engine/compose diffs (also the always-run hash-pin fact above).
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioRegressionWall
    {
        const string Skip =
            "Y7(e) — RUN 2026-07-15. `dotnet build GenWave.sln`: Build succeeded, 0 Warning(s), " +
            "0 Error(s). `dotnet test GenWave.sln --filter \"Category!=Integration\"`: 0 failed " +
            "across five projects -- Core 83/83, Orchestration 56/59 (3 skipped), MediaLibrary " +
            "32/58 (26 skipped, filtered subset), Tts 117/128 (11 skipped), Host 536/566 (30 " +
            "skipped -- this file's own rewrite converts the prior 8 bare-pending facts into 2 " +
            "always-run passing facts + 9 Category=Integration Skip-pinned facts, net +2 passed / " +
            "-6 skipped versus the pre-rewrite 534/572 (38 skipped) run, since the 9 Integration-" +
            "tagged facts are no longer selected by the filter at all -- the Story127/T11/" +
            "Story141/Story147 mechanism) -- 0 failed overall. Separately, `dotnet test " +
            "tests/GenWave.MediaLibrary.Tests` (unfiltered, its OWN self-bootstrapping compose " +
            "project `genwave-libtest`, confirmed absent via `docker ps -a`/`docker compose ls -a` " +
            "before and after): 317 passed, 0 failed, 47 skipped, 364 total -- more total tests " +
            "than X10's own 347-test baseline (Epic Y added coverage), 0 failed either way. `cd " +
            "admin-ui && npx tsc --noEmit`: clean, zero output. `npx jest`: 45 suites passed, 414 " +
            "passed, 11 todo, 425 total (adds Y1-Y6's new specs over X10's own 41-suite/379-passed " +
            "wall; the one pre-existing harmless React act() warning in " +
            "catalog-selection-toolbar.spec.tsx carried unchanged since Q12/R13/S8/T11/U7/V10/X10 " +
            "-- not a failure, not introduced here). `npm run build`: green, 13 routes compiled " +
            "(same route set as X10 -- Epic Y shipped zero new pages, only new fields/pickers on " +
            "existing pages). `git diff main...HEAD -- compose.yaml` and `git diff main...HEAD -- " +
            "engine/genwave.liq` both empty (also asserted as the always-run hash-pin fact above, " +
            "byte-identical to Story141's and Story147's own pins -- F52-F56 touch neither file). " +
            "F2-F51 gates stand with one honest, purely mechanical exception: `git diff " +
            "main...HEAD --stat -- '*AcceptanceGate*.cs'` shows THIS file (Story153 itself, " +
            "rewritten by this task, as expected) plus a 5-line ADDITIVE-ONLY change to " +
            "Story012_AcceptanceGate01_RenderAheadGracefulSkip.cs's fake IMediaCatalog double -- " +
            "Y1 widened IMediaCatalog with GetFacetsAsync, so every pre-existing fake " +
            "implementation across the test suite (including this §0.1 gate's own render-ahead " +
            "double) needed a trivial stub method to keep compiling; confirmed by source read that " +
            "zero gate ASSERTIONS were touched, only a new no-op method body was added ('Not " +
            "exercised by this gate' per its own comment) -- the same category of mechanical, non-" +
            "behavioral ripple a widened shared interface always produces, not a rewritten or " +
            "weakened gate.";

        [Fact(Skip = Skip)]
        public void TheRegressionWallIsGreenWithZeroEngineComposeDiffs()
        {
            // Y7(e) — build zero-warnings + filtered/unfiltered dotnet tests green + admin-ui
            // tsc/jest/build green + zero compose/engine diff + F2-F51 gates stand (one honest,
            // purely mechanical interface-ripple touch, zero assertions changed).
        }
    }

    // ---------------------------------------------------------------------
    // (f) — Gitea issue closure is the operator's call; this gate leaves all seven exactly as
    // found.
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioIssueClosureIsTheOperatorsCall
    {
        const string Skip =
            "Y7(f) — Gitea state checked 2026-07-15 via the API (read-only; this gate never closes " +
            "issues, per instruction and the MEMORY.md house rule). gitea-#189 \"Eligible/Ineligible " +
            "Filtering\", gitea-#221 \"Live-editable int settings have floors but no ceilings (fat-finger " +
            "surface)\", gitea-#224 \"Station:Voice needs to be dropdown like Personas:Voice\", gitea-#225 " +
            "\"Station:Persona:ActiveId needs to be dropdown with Persona name not Id\", gitea-#227 " +
            "\"ArtistSeparation is silently capped by RecentWindow (shared ring) — document/hint " +
            "the coupling\", gitea-#230 \"Library:YearLookup:Enabled help text doesn't make sense\", and " +
            "gitea-#231 \"Some Library: entries need defaults/ranges\" are all OPEN, confirmed unchanged " +
            "by this gate (read-only, never mutated). All seven close on the live evidence in the " +
            "facts above (the gitea-#189 facet sweep sparing the lookalike; the F53 ceilings; the real " +
            "voice/persona dropdowns; the seeded defaults, override precedence, and help-text " +
            "coverage; the ArtistSeparation/RecentWindow coupling notice and capped save) -- but " +
            "closing them is the operator's call after they deploy this branch, verify against " +
            "their own catalog and Kokoro instance, and decide the evidence is sufficient for their " +
            "own station -- never this gate's. This gate leaves all seven issues exactly as found.";

        [Fact(Skip = Skip)]
        public void TheSevenIssuesCloseOnlyOnOperatorAuthorization()
        {
            // Y7(f) — the epic isn't done while gitea-#189/gitea-#221/gitea-#224/gitea-#225/gitea-#227/gitea-#230/gitea-#231 are open;
            // closing them is the operator's decision after deploying and verifying on their own
            // station, never this gate's.
        }
    }
}

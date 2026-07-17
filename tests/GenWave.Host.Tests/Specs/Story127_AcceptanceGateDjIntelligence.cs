// STORY-127 — Acceptance gate: DJ intelligence end-to-end + regression (Epic T / SPEC F34-F36,
// closes gitea-#175, gitea-#176, gitea-#178)
//
// BDD specification — xUnit. T11 ran 2026-07-13 against an isolated scratch-stack smoke (own -p
// t11smoke project, own .env, non-colliding ports 18000/18080/13000, empty MEDIA_DIR so the F27
// boot seed supplied real content before two ffmpeg-generated sine-wave mp3s (plus 24 short filler
// tracks added later to keep the Orchestrator's 20-id anti-repeat window from self-exhausting a
// tiny catalog — see MEMORY.md 2026-07-13 and issue gitea-#210) were dropped in to give the orchestrator
// real music to lead into, a mock OpenAI-compatible completions server (`mock_server.py`, kept in
// the scratchpad, never in compose.yaml) on the compose `core` network as `llmmock`/`llmmock2`, and
// a Kokoro-compatible wav stub `wavmock` for the one-shot Tts:Endpoint repoint proof — the
// E10->S8 gate discipline) — never the operator's live station (a separate `mrdgenwave` compose
// project at a different path entirely; confirmed exited/untouched throughout this run). Every
// fact below is one of:
//   (1) a real, always-run, non-Skip repo-content assertion (Story102/107/S8's grep-assert idiom)
//       — no live stack needed, so it deliberately has NO Integration trait and stays IN the
//       filtered wall;
//   (2) a real, always-attempted, self-skipping HTTP-only check — Story013/082/094/108/S8's
//       guarded-live-check idiom, [Trait("Category","Integration")] so it is excluded from the
//       `--filter "Category!=Integration"` wall run and opportunistically real whenever someone
//       runs the FULL suite against a reachable deployment;
//   (3) Skip-pinned with THIS SESSION's dated t11smoke scratch-stack evidence, Category=Integration; or
//   (4) Skip-pinned with the EXACT operator procedure for what genuinely needs the real station,
//       real Ollama copy quality (the F34 kill criterion, permanently out of gate scope), or a
//       human decision (issue closure), Category=Integration.
// No fact in this file keeps a bare "pending T11" reason.

using System.Net;
using System.Net.Http.Json;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAcceptanceGateDjIntelligence
{
    // ---------------------------------------------------------------------
    // Shared live-stack helper (Story013/082/094/108/S8's guarded-live-check idiom)
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

    static string KokoroTtsSynthesizerSourceText =>
        File.ReadAllText(Path.Combine(RepoRoot, "src", "GenWave.Tts", "KokoroTtsSynthesizer.cs"));

    static string LlmCopyWriterSourceText =>
        File.ReadAllText(Path.Combine(RepoRoot, "src", "GenWave.Tts", "LlmCopyWriter.cs"));

    // ---------------------------------------------------------------------
    // (a) — mock LLM copy airs through a real render, annotation shape unchanged
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioBlurbPathEndToEnd
    {
        const string Skip =
            "T11(a) — t11smoke, scratch stack, 2026-07-13: Llm:Endpoint PUT live to " +
            "http://llmmock:9000 (a python OpenAI-compatible /v1/chat/completions stub on the " +
            "compose `core` network, request log exposed via GET /__requests) with Kokoro up " +
            "in-stack; two ffmpeg-generated sine-wave mp3s (~20s each, real ID3 tags) enriched into " +
            "the scratch MEDIA_DIR gave the orchestrator real music to lead into. Once ready, " +
            "llmmock's /__requests captured the completions POST for both LeadIn and BackAnnounce " +
            "renders — system prompt matched LlmCopyWriter.BuildSystemPrompt's baked scaffold " +
            "verbatim, user content matched BuildUserContent's Station/Local " +
            "time/Segment/Title/Artist/Album/Genre/Year lines verbatim — and Kokoro's own container " +
            "log showed the real synthesis call. `docker compose -p t11smoke exec engine` telnet " +
            "`output.icecast.metadata` showed the blurb airing as a normal " +
            "`track_id=\"tts:<hash>\"` push carrying station_id/station_name/artist/title exactly " +
            "like every prior TTS push (Story055's own annotation-builder pin, untouched by this " +
            "epic — T1 left SegmentRequest and LiquidsoapAnnotationBuilder alone); the new " +
            "MediaItem.Album/Genre/Year fields (T1) showed up only on the REAL MUSIC entries " +
            "(`T11 Test Track One`/`Two`, each carrying album/artist/date/genre) they were added " +
            "for, never on the `tts:` rows — annotation shape unchanged. `GET /api/status` showed " +
            "llm.enabled=true and lastOutcome=\"ok\" immediately after.";

        [Fact(Skip = Skip)]
        public void MockLlmCopyAirsThroughARealRender()
        {
            // T11(a): mock's request log shows the completions call; engine metadata shows the
            // blurb airing as a normal tts: push with the pre-Epic-T annotation shape (AC1).
        }
    }

    // ---------------------------------------------------------------------
    // (b) — mock stopped mid-run: template patter continues in cadence, one WARN, never a stall
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioFallbackLadderLive
    {
        const string Skip =
            "T11(b) — t11smoke, scratch stack, 2026-07-13: with music rotating and Llm:Endpoint " +
            "live and reachable, `docker compose -p t11smoke stop llmmock` (killing the mock mid-" +
            "run, not a config change) — the very next four LeadIn/BackAnnounce render attempts " +
            "each logged exactly ONE `warn: GenWave.Tts.LlmCopyWriter[0] LLM completion for " +
            "{Kind} failed; falling back to template` (api logs), never more than one per attempt " +
            "(F34.4's exactly-one-WARN contract). `output.icecast.metadata` across that same window " +
            "showed continuous on_air pushes every few seconds — music tracks and `tts:` template " +
            "pushes interleaved with no gap in on_air_timestamp progression — the SAME cadence as " +
            "before the mock died, just serving template copy instead of LLM copy; a `curl " +
            ":18000/stream` spot-check captured ~200KB over 6s with zero `silencedetect` hits. `GET " +
            "/api/status` showed lastOutcome=\"failed\" (llm.enabled stayed true) — no stall, no " +
            "dead air, the fallback ladder degrading exactly as designed.";

        [Fact(Skip = Skip)]
        public void StoppingTheMockMidRunContinuesTemplatePatterInCadence()
        {
            // T11(b): WARN logged once per attempt, patter continues from templates within the
            // same cadence, no stall (AC2).
        }
    }

    // ---------------------------------------------------------------------
    // (c) — persona lifecycle through the running binary
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioPersonaLifecycle
    {
        [Fact]
        public async Task AnonymousPersonaRequestIsRejectedLive()
        {
            // AC3 partial — deny-by-default re-verified against a REAL running deployment: a pure
            // auth-boundary check, no side effects (401 fires in auth middleware before
            // PersonaController is ever constructed, so no row is ever touched). Self-skips when
            // localhost:8080 isn't up (Story013's guarded-live-check idiom).
            if (!await LiveApi.IsReachableAsync()) return;

            using var http = new HttpClient { BaseAddress = new Uri(LiveApi.BaseUrl), Timeout = TimeSpan.FromSeconds(10) };
            using var response = await http.GetAsync("/api/personas");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        const string Skip =
            "T11(c) — t11smoke, scratch stack, 2026-07-13: POST /api/personas " +
            "{\"name\":\"Retro Rick\",\"backstory\":\"A retro AM disc jockey broadcasting from " +
            "1975, remembers the moon landing like it was yesterday.\",\"style\":\"warm, folksy, a " +
            "little nostalgic\",\"voice\":\"am_adam\"} -> 201 {id:1}; PUT /api/settings " +
            "{\"key\":\"Station:Persona:ActiveId\",\"value\":\"1\"} -> 200. The very next LeadIn/" +
            "BackAnnounce completions request captured by llmmock's /__requests carried the exact " +
            "system-prompt appendix `Backstory: A retro AM disc jockey ...` / `Style: warm, " +
            "folksy, ...` (LlmCopyWriter.BuildPersonaSection, F35.2/F35.3); Kokoro's own container " +
            "log showed `Using voice path: /app/api/src/voices/v1_0/am_adam.pt` for that render " +
            "(the station default is af_heart) and the render landed under `blurbs/` as a NEW hash " +
            "file distinct from the pre-persona render (hash = text|voice|stationId, same canned " +
            "mock reply, only the voice changed) — the persona's voice reached the real Kokoro " +
            "synth call through the production TtsSegmentSource path, not just a preview. `GET " +
            "/api/status` showed activePersona=\"Retro Rick\". DELETE /api/personas/1 -> 204 in " +
            "the SAME request cleared `Station:Persona:ActiveId` to 0 (confirmed via GET " +
            "/api/settings) and the FOLLOWING completions request's system prompt had reverted to " +
            "the bare neutral scaffold with no Backstory/Style section; `GET /api/status` showed " +
            "activePersona=null again — full create->activate->prompt-carries-it->voice-switches-" +
            "> delete-clears-ActiveId->neutral-resumes lifecycle proven through the running binary.";

        [Fact(Skip = Skip)]
        public void CreateActivatePromptVoiceDeleteClearsAndNeutralResumes()
        {
            // T11(c): full lifecycle through the production binary (AC3).
        }
    }

    // ---------------------------------------------------------------------
    // (d) — previews: real LLM text / real wav, honest 502 when down, nothing persisted
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioPreviews
    {
        const string Skip =
            "T11(d) — t11smoke, scratch stack, 2026-07-13: with llmmock and Kokoro both up, POST " +
            "/api/personas/preview {\"kind\":\"LeadIn\",\"backstory\":\"A late-night jazz " +
            "host\",\"style\":\"smooth and low-key\"} -> 200 {\"text\":\"Mock DJ A speaking: great " +
            "tune coming up on the air.\"} (the mock's own canned reply, proving the REAL " +
            "LlmCopyWriter ran, not a template substitution); POST /api/tts/preview " +
            "{\"text\":\"...\"} -> 200 audio/wav, 187272 bytes, real RIFF/WAVE PCM (`file` " +
            "confirmed). Stopped llmmock: the identical personas/preview call returned 502 " +
            "{\"title\":\"Persona preview failed.\"}; stopped Kokoro instead (llmmock back up): " +
            "the identical tts/preview call returned 502 {\"title\":\"TTS preview generation " +
            "failed.\"} (Kokoro restarted cleanly afterward, healthy within ~35s). Across all four " +
            "preview calls, `GET /api/media` stayed at a stable row count (26, unchanged) and " +
            "`/tts/genwave-1`'s top-level (forever-cache) file count stayed at 8 both before and " +
            "after — the preview synth artifact is written and then deleted by " +
            "TtsPreviewController in the same request, exactly as documented, and never reaches " +
            "the tts cache, the catalog, or rotation.";

        [Fact(Skip = Skip)]
        public void CopyAndAudioPreviewsWorkAnd502WhenDownWithNothingPersisted()
        {
            // T11(d): 200 {text}/wav when up, 502 ProblemDetails when down, nothing persisted (AC4).
        }
    }

    // ---------------------------------------------------------------------
    // (e) — live endpoint repoint: the mechanism (repo-content) + the mid-run proof (live)
    // ---------------------------------------------------------------------

    public sealed class ScenarioLiveRepoint
    {
        [Fact]
        public void KokoroAndLlmClientsReadTheEndpointFreshPerCallWithNoBootFrozenBaseAddress()
        {
            // Real, always-run, non-Skip repo-content assertion (Story102/107/S8's grep-assert
            // idiom) — no live stack needed. Pins the EXACT mechanism the T11(e) live-repoint
            // proof below depends on: both HTTP-calling classes read Tts:Endpoint/Llm:Endpoint from
            // IOptionsMonitor<T>.CurrentValue INSIDE the call (never cached at construction) and
            // build an absolute request URI per call via EndpointUri.Combine — neither ever
            // ASSIGNS HttpClient.BaseAddress (checked as the "BaseAddress =" assignment pattern,
            // not a bare mention — both files' own doc comments explain, in prose, why they don't
            // do this, which would otherwise false-positive a naive substring check), which would
            // freeze the endpoint at DI-registration time and make a live PUT /api/settings edit
            // invisible until an api restart (SPEC F36.1-F36.2). A future edit that reintroduces a
            // boot-frozen BaseAddress assignment on either class breaks one of these literal matches.
            var kokoroSource = KokoroTtsSynthesizerSourceText;
            var llmSource = LlmCopyWriterSourceText;

            Assert.Contains("optionsMonitor.CurrentValue", kokoroSource, StringComparison.Ordinal);
            Assert.Contains(
                "EndpointUri.Combine(cfg.Endpoint, \"/v1/audio/speech\")", kokoroSource, StringComparison.Ordinal);
            Assert.DoesNotContain("BaseAddress =", kokoroSource, StringComparison.Ordinal);

            Assert.Contains("optionsMonitor.CurrentValue", llmSource, StringComparison.Ordinal);
            Assert.Contains(
                "EndpointUri.Combine(cfg.Endpoint, \"/v1/chat/completions\")", llmSource, StringComparison.Ordinal);
            Assert.DoesNotContain("BaseAddress =", llmSource, StringComparison.Ordinal);
        }

        const string Skip =
            "T11(e) — t11smoke, scratch stack, 2026-07-13: PUT /api/settings " +
            "{\"key\":\"Llm:Endpoint\",\"value\":\"http://llmmock2:9000\"}," +
            "{\"key\":\"Llm:Model\",\"value\":\"mock-model-b\"} -> 200; the next LeadIn/" +
            "BackAnnounce render's completions request landed on llmmock2 (model=\"mock-model-" +
            "b\" in the captured body) while llmmock (the original endpoint) received nothing " +
            "further — `GET /api/status` showed model=\"mock-model-b\", lastOutcome=\"ok\". " +
            "Separately, PUT Tts:Endpoint to http://wavmock:9000 (a Kokoro-compatible wav stub) " +
            "and POST /api/tts/preview returned the stub's fixed 48044-byte clip (distinct from " +
            "Kokoro's own ~120-190KB real renders) — proving the repoint reached the real preview " +
            "call — then PUT Tts:Endpoint back to http://kokoro:8880 immediately, and the next " +
            "tts/preview call returned a genuine 120030-byte Kokoro render, confirming patter " +
            "wasn't left broken. `docker inspect t11smoke-api-1` showed the SAME StartedAt " +
            "timestamp (21:20:41Z) and RestartCount=0 across every one of these live repoints — no " +
            "api restart at any point (F36.2's exact contract).";

        [Trait("Category", "Integration")]
        [Fact(Skip = Skip)]
        public void EndpointSwapsMidRunTakeEffectWithoutRestart()
        {
            // T11(e): Tts+Llm endpoints repoint mid-run via live PUT; next render follows; no
            // restart (AC5).
        }
    }

    // ---------------------------------------------------------------------
    // (f) — status tile truth: neutral/ok/warning within one poll; zero LLM traffic while idle
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioStatusTruth
    {
        const string Skip =
            "T11(f) — t11smoke, scratch stack, 2026-07-13: fresh boot, Llm:Endpoint unset -> GET " +
            "/api/status llm={enabled:false,lastOutcome:null} (neutral tile per " +
            "StatusTiles.tsx's llmTileVariant). PUT Llm:Endpoint live to http://llmmock:9000 WHILE " +
            "the catalog was still empty of main-scope music (only the F27 boot-seed safe segment " +
            "playing, which never triggers a LeadIn/BackAnnounce render — Orchestrator's music " +
            "branch has nothing to lead into): 65 consecutive seconds later, llmmock's " +
            "/__requests still showed {\"count\":0} — enabled=true, mock reachable, zero LLM " +
            "traffic while genuinely idle. Once real music was enriched and rotating, the next " +
            "render's completions call succeeded and the VERY NEXT GET /api/status showed " +
            "lastOutcome=\"ok\" (ok tile). Stopping llmmock mid-run (T11(b)) flipped the VERY NEXT " +
            "GET /api/status to lastOutcome=\"failed\" (warning tile) within one poll; restarting " +
            "the mock and waiting for the next render flipped it back to \"ok\" within one poll. " +
            "All three tile states (neutral/ok/warning) and the idle-sends-zero-traffic guarantee " +
            "confirmed live, each within a single GET.";

        [Fact(Skip = Skip)]
        public void TileTransitionsWithinOnePollAndIdleSendsZeroLlmTraffic()
        {
            // T11(f): neutral (disabled) -> ok (successful render) -> warning (failed render),
            // each within one poll; idle station sends the LLM zero requests (AC6).
        }
    }

    // ---------------------------------------------------------------------
    // (g) — blurb GC sweeps aged files; forever-cache untouched
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioBlurbGcLive
    {
        const string Skip =
            "T11(g) — t11smoke, scratch stack, 2026-07-13: `docker compose -p t11smoke exec api " +
            "ls -la /tts/genwave-1/blurbs/` showed three fresh LLM-authored blurb files. `touch -d " +
            "'2 days ago'` aged ONE blurb file (older than the default Tts:BlurbRetentionHours=24) " +
            "and, for the control, ONE forever-cache file at /tts/genwave-1's ROOT (a templated-" +
            "kind render, outside blurbs/) to the same 2-day-old mtime. Waited for the next fresh " +
            "LLM render (which opportunistically sweeps blurbs/ per F34.6): the aged blurb file " +
            "was gone, the two NON-aged blurb files were untouched, and the aged forever-cache " +
            "root file was untouched (same 2-day-old mtime, same byte count) — the sweep never " +
            "reaches outside blurbs/, exactly as designed.";

        [Fact(Skip = Skip)]
        public void AgedBlurbFilesSweepWhileTheForeverCacheSurvives()
        {
            // T11(g): blurbs/ GC sweeps aged files on the next fresh render; the forever-cache
            // (templated-kind renders outside blurbs/) is never touched (AC7).
        }
    }

    // ---------------------------------------------------------------------
    // (h) — full regression wall green; zero engine/compose diff; F2-F33 gates stand
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioRegressionWall
    {
        const string DotnetEvidence =
            "T11(h) dotnet half — RUN 2026-07-13. `dotnet build GenWave.sln`: Build succeeded, " +
            "0 Warning(s), 0 Error(s). `dotnet test GenWave.sln --filter \"Category!=" +
            "Integration\"`: 0 failed across five projects (this file's own Category=Integration " +
            "facts are excluded from this filtered count by design, same as every prior gate; the " +
            "one exception is this file's OWN " +
            "KokoroAndLlmClientsReadTheEndpointFreshPerCallWithNoBootFrozenBaseAddress repo-" +
            "content fact just above, deliberately not Integration-tagged, which is this run's " +
            "\"+1\" over the old stub's contribution). Separately, `dotnet test " +
            "tests/GenWave.MediaLibrary.Tests` (unfiltered, exercising the project's OWN self-" +
            "bootstrapping DatabaseFixture — an isolated `docker compose -p genwave-libtest` " +
            "project, confirmed absent via `docker ps -a`/`docker compose ls` beforehand): 231 " +
            "passed, 0 failed, 47 skipped, 278 total; `docker ps -a` clean afterward. Zero diff: " +
            "`git diff --stat main -- compose.yaml engine/genwave.liq` — empty (no operator-" +
            "ruled compose/engine changes anywhere in Epic T, F36.3's reversal held). F2-F33 gates " +
            "stand: `git diff --stat b9fb911..fd0bc4c -- '*AcceptanceGate*.cs'` (the pre-T11 state " +
            "of this branch) showed only two mechanical constructor-signature fixups — " +
            "Story012/Story013 updating call sites for Orchestrator's new IActivePersonaAccessor " +
            "parameter (T6) and KokoroTtsSynthesizer's IOptionsMonitor<TtsOptions> parameter (T8) " +
            "— no prior epic's assertions changed; every one of those gates is still green in the " +
            "run above. Nothing in this run touched the production compose project.";

        [Fact(Skip = DotnetEvidence)]
        public void FullDotnetSuiteIsGreen()
        {
            // AC8 dotnet half — build zero-warnings + filtered/unfiltered test green + zero
            // engine/compose diff + F2-F33 gates undisturbed.
        }

        const string AdminUiEvidence =
            "T11(h) admin-ui half — RUN 2026-07-13 from admin-ui/. `npx tsc --noEmit`: clean, zero " +
            "output. `npx jest`: 33 suites passed, 317 passed, 11 todo, 328 total (the one pre-" +
            "existing, harmless React act() warning in catalog-selection-toolbar.spec.tsx carried " +
            "unchanged since Q12/R13/S8 — not a failure, not introduced here). `npm run build`: " +
            "green, 13 routes compiled (adds /personas over S8's 12-route wall — T10's new page; " +
            "every other route unchanged). `grep -rn \"window.confirm(\" admin-ui/app admin-ui/" +
            "components`: zero call sites. `grep -rlE \"fonts.googleapis|fonts.gstatic\" admin-" +
            "ui/.next/static admin-ui/.next/server/app` (post-build): zero hits.";

        [Fact(Skip = AdminUiEvidence)]
        public void AdminUiTscJestAndBuildAreGreen()
        {
            // AC8 admin-ui half — tsc/jest/next build green; window.confirm grep zero; no external
            // font/CDN request.
        }
    }

    // ---------------------------------------------------------------------
    // (i) — Gitea issue closure is the operator's call
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioIssueClosure
    {
        const string Skip =
            "T11(i) — Gitea state checked 2026-07-13 via the API (read-only; this gate never " +
            "closes issues, per instruction and the MEMORY.md house rule). gitea-#175 \"Add LLM support " +
            "for DJ blurbs\", gitea-#176 \"Add DJ 'personas'\", and gitea-#178 \"Add local TTS support (not in " +
            "same stack)\" (all labeled genwave-2.0) are OPEN. Operator to close after reviewing " +
            "this gate's evidence (the runnable wall above + the operator checklist in " +
            "docs/PLAN.md's Epic T block-quote) and completing the real-Ollama listening test (the " +
            "F34 kill criterion, never a gate assertion). This gate leaves all three issues " +
            "exactly as found.";

        [Fact(Skip = Skip)]
        public void Issues175And176And178CloseOnOperatorEvidence()
        {
            // AC9 — the epic isn't done while gitea-#175/gitea-#176/gitea-#178 are open; closing them is the
            // operator's decision, never this gate's.
        }
    }
}

// STORY-256 — Never someone else's voice (gh-#276, audible half)
//
// SPEC F99.1, F99.5. When kokoro dies mid-show the fallback currently speaks the DJ's line in
// a different voice — a DJ whose voice changes mid-show is the single most inhuman artifact
// the station ships, and gh-#276's OOM makes it a real duty cycle rather than a rare path.
//
// The ruling: RIGHT VOICE OR NO SPEECH. This overturns F70's standing "a wrong voice beats
// silence".
//
// ⚠️ Never-silent (F6.3) is untouched and always was about the STREAM, not the mic. Music
// continues uninterrupted; only the break is dropped. The specs below pin both halves,
// because a change that accidentally stopped the music would be a far worse bug than the one
// being fixed.
//
// Serving cached evergreen audio in the DJ's real voice was considered at /design and
// REJECTED: it needs a notion of which segments are re-airable, and "the DJ repeated
// themselves" is its own inhuman artifact.
//
// T147 does NOT yet deliver F99.1 on the DEPLOYED path (T147 review finding F2). compose.yaml
// still ships `Tts__Fallback__Endpoint: http://piper:5000` today, so a live station still
// legally substitutes Piper on a Kokoro failure — SPEC F99.2's deliberate opt-in substitution.
// A structural hop-refusal was considered here and REJECTED: it would break that same opt-in.
// What T147 actually pins is the structural HALVES that are true regardless of that shipped
// default: the null-never-throws render contract (a voice that cannot be produced never fakes
// one), persona+cause Information logging (F99.5's audit trail), and drop legibility (an
// operator can tell "the engine is down" from "the DJ has nothing to say"). Every scenario
// below arranges the ALREADY-legal-today opt-out — no fallback chain configured at all
// (TtsFallbackChain.Resolve folds absent config to Empty) — which is a real, supported
// posture, but proves nothing about the SHIPPED default. Facts that would need "no substitute
// engine is ever asked" to be true of the deployed default (not merely of this opt-out
// fixture) are marked `Pending T148` below: T148, in this SAME PR, flips the shipped default
// itself to an empty chain (compose stops shipping the fallback endpoint/sidecar) — at that
// point those facts describe the real deployed posture and go live.

namespace GenWave.Tts.Tests.Specs;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureNeverSomeoneElsesVoice
{
    // ------------------------------------------------------------------
    // Shared fixture helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// No fallback configured — <see cref="TtsFallbackChain.Resolve"/> folds this straight to
    /// <see cref="TtsFallbackChain.Empty"/>, so <see cref="FallbackTtsSynthesizer"/> is a
    /// transparent pass-through to the primary: no health read, no hop ever attempted. This is
    /// already legal today (a station simply never sets <c>Tts:Fallback:Endpoint</c>/
    /// <c>Profiles</c>) and is the posture F99.1 requires — shipping it as the DEFAULT is T148's
    /// job, not this one's.
    /// </summary>
    static TestOptionsMonitor<TtsFallbackOptions> NoFallbackConfigured() =>
        new(new TtsFallbackOptions());

    /// <summary>
    /// <paramref name="substitute"/> stands in for a would-be substitute engine (a real Piper
    /// sidecar, in production) — registered as a hop renderer exactly as it would be in DI, so a
    /// spec can prove it is never CALLED, not merely that its output goes unused.
    /// </summary>
    static FallbackTtsSynthesizer BuildRouter(FakeTtsSynthesizer primary, FakeProfileRenderer substitute) =>
        new(primary, [substitute], new FakeDependencyHealth(), NoFallbackConfigured(),
            NullLogger<FallbackTtsSynthesizer>.Instance);

    static TtsSegmentSource BuildSource(
        FallbackTtsSynthesizer router, string cacheRoot, ILogger<TtsSegmentSource>? logger = null) =>
        new(
            new TemplateCopyWriter(new PatterTemplateRenderer()),
            router,
            new FakeLoudnessAnalyzer(),
            new FakeCueAnalyzer(),
            NoCorrections.Provider(),
            NoCorrections.PersonaCache(),
            NoCorrections.PronunciationProvider(),
            NoCorrections.PersonaPronunciationCache(),
            NoCorrections.PersonaPaceCache(),
            new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" }),
            logger ?? NullLogger<TtsSegmentSource>.Instance);

    // LeadIn (a DJ-spoken kind, ARCHITECTURE.md "while a persona is on air it is that persona's
    // voice reading the DJ-spoken kinds") with no Track — PatterTemplateRenderer's documented
    // safe fallback ("Coming up next."), same shape Story258_QualityYouCanSee's own fixture uses.
    static SegmentRequest DjBreakRequest(string? personaName = "Rusty Strings") =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave", null, DateTimeOffset.UtcNow, "test-station", PersonaName: personaName);

    /// <summary>
    /// Shared fixture every Scenario below arranges identically (T147 review, non-blocking
    /// cleanup finding): a cache root swept in <see cref="Dispose"/>, a primary engine primed to
    /// fail with the derived scenario's own exception (only the cause-field fact below needs it
    /// to specifically be an <see cref="IOException"/> — the rest only need "any failure"), and a
    /// substitute engine standing by so a scenario can prove it is never called. Collapses what
    /// were four byte-identical <c>Dispose</c> bodies plus a thrice-repeated field triple into one
    /// place.
    /// </summary>
    public abstract class VoiceIntegrityScenario(Exception primaryFailure) : IDisposable
    {
        protected readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        protected readonly FakeTtsSynthesizer primary = new() { ThrowOnNextCall = primaryFailure };
        protected readonly FakeProfileRenderer substitute = new(DependencyNames.Piper);

        public void Dispose()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(primary.OutputDirectory)) Directory.Delete(primary.OutputDirectory, recursive: true);
        }
    }

    // ------------------------------------------------------------------
    // HAPPY PATH
    // ------------------------------------------------------------------

    public sealed class ScenarioTheBreakIsDropped() : VoiceIntegrityScenario(new HttpRequestException("kokoro down"))
    {
        [Fact]
        public async Task No_segment_is_produced_when_the_dj_voice_cannot_be_rendered()
        {
            // Given a DJ whose own voice cannot be produced (the primary engine throws), with no
            // fallback chain configured...
            var source = BuildSource(BuildRouter(primary, substitute), cacheRoot);

            // When a break comes due
            var item = await source.RenderAsync(DjBreakRequest(), CancellationToken.None);

            // Then the break does not air.
            Assert.Null(item);
        }

        [Fact(Skip = "Pending T148 — the shipped-default flip makes this pinnable")]
        public async Task No_other_voice_is_ever_asked_to_speak_that_line()
        {
            // The substitute engine must not be CALLED, not merely have its output discarded — but
            // under THIS fixture's empty chain (no Profiles, no legacy Endpoint) the hop-execution
            // loop `substitute` lives in has zero hops to iterate whether or not a short-circuit
            // guard exists ahead of it. A reviewer's mutation test proved this: deleting
            // FallbackTtsSynthesizer's IsEmpty short-circuit left this fact green regardless,
            // because chain.Hops.Count is 0 either way. It is a fact about THIS FIXTURE, not yet a
            // pin on production code — it becomes one once T148 flips the SHIPPED default itself to
            // an empty chain (see file header).
            var source = BuildSource(BuildRouter(primary, substitute), cacheRoot);

            // When the break comes due
            await source.RenderAsync(DjBreakRequest(), CancellationToken.None);

            Assert.Equal(0, substitute.CallCount);
        }
    }

    public sealed class ScenarioTheStreamIsUntouched() : VoiceIntegrityScenario(new HttpRequestException("kokoro down"))
    {
        // Music_continues_when_a_break_is_dropped was DELETED here (T147 review finding F3): it
        // re-asserted the exact same Assert.Null(item) as
        // ScenarioTheBreakIsDropped.No_segment_is_produced_when_the_dj_voice_cannot_be_rendered
        // above, pinning nothing new about MUSIC itself — RenderAsync returning null says nothing
        // about what the feeder does next. The genuine "music never waits or stalls on a dropped
        // break" claim is already pinned one layer up, at the Orchestrator, by
        // Story243_DjsHandOffAudibly.ScenarioFailedPieceDegradesThatBoundaryOnly
        // .BothFailedMeansCleanCutAndMusicNeverWaits (every item the feeder pulls is a plain music
        // track once a render returns null). This file's job is only the render CONTRACT half
        // (returns null, never throws) — the fact below still pins that.

        [Fact]
        public async Task The_drop_does_not_fault_the_playout_loop()
        {
            // A voice-integrity drop is a decision, not an exception escaping to the feeder.
            var source = BuildSource(BuildRouter(primary, substitute), cacheRoot);

            // When the break comes due
            var act = async () => await source.RenderAsync(DjBreakRequest(), CancellationToken.None);

            // Then no exception ever propagates out of the render call.
            var ex = await Record.ExceptionAsync(act);
            Assert.Null(ex);
        }
    }

    public sealed class ScenarioTheDropIsLegible() : VoiceIntegrityScenario(new IOException("kokoro down"))
    {
        readonly CapturingLogger<TtsSegmentSource> logger = new();

        [Fact]
        public async Task The_drop_logs_at_information()
        {
            // Given a break dropped for voice integrity...
            var source = BuildSource(BuildRouter(primary, substitute), cacheRoot, logger);

            // When it is dropped
            await source.RenderAsync(DjBreakRequest(), CancellationToken.None);

            // Then one Information line records the drop — the same render-outcome fact F100.2
            // already emits on any failure, never Debug (F100.1's "an epic fact at Debug may as
            // well not exist" ground).
            Assert.Contains(logger.Entries, e =>
                e.Level == LogLevel.Information
                && e.Message.Contains("TTS render outcome", StringComparison.Ordinal)
                && e.Message.Contains("outcome=failure", StringComparison.Ordinal));
        }

        [Fact]
        public async Task The_line_names_the_persona()
        {
            // Given a break dropped for voice integrity, for a named persona...
            var source = BuildSource(BuildRouter(primary, substitute), cacheRoot, logger);

            // When it is dropped
            await source.RenderAsync(DjBreakRequest("Rusty Strings"), CancellationToken.None);

            // Then the line names that persona.
            Assert.Contains(logger.Entries, e =>
                e.Level == LogLevel.Information
                && e.Message.Contains("persona=\"Rusty Strings\"", StringComparison.Ordinal));
        }

        [Fact]
        public async Task The_line_names_the_cause()
        {
            // "the engine is down" must be distinguishable from "nothing to say" (F99.5). The
            // outcome line's cause field is the exception's own type name — a concrete, greppable
            // fact — never the F92.4/F107.6 "copy wasn't LLM-authored" WARN's fixed text, so an
            // engine outage and a copy-availability drop never collide on the same field value.
            var source = BuildSource(BuildRouter(primary, substitute), cacheRoot, logger);

            await source.RenderAsync(DjBreakRequest(), CancellationToken.None);

            Assert.Contains(logger.Entries, e =>
                e.Level == LogLevel.Information && e.Message.Contains("cause=IOException", StringComparison.Ordinal));
        }

        [Fact]
        public async Task An_unauthored_copy_drop_is_never_logged_as_an_engine_failure()
        {
            // F99.5's discrimination ("engine down" vs "DJ has nothing to say") was previously
            // asserted only in the comment on The_line_names_the_cause above (T147 review finding
            // F4) — this turns that claim into a regression guard. Given a SignOff whose copy never
            // became LLM-authored: BuildSource's TemplateCopyWriter always returns
            // FreshPerAiring:false, so this hits the F92.4/F107.6 not-LLM-authored guard at
            // TtsSegmentSource.cs:93-101, which returns null after a single LogWarning WITHOUT ever
            // reaching the synthesizer (primary is primed to throw, but is never even asked) or
            // LogRenderOutcome...
            var source = BuildSource(BuildRouter(primary, substitute), cacheRoot, logger);
            var request = new SegmentRequest(
                SegmentKind.SignOff, "af_heart", "GenWave", null, DateTimeOffset.UtcNow, "test-station",
                PersonaName: "Rusty Strings");

            // When the drop happens
            await source.RenderAsync(request, CancellationToken.None);

            // Then no render-outcome line ever claims this was an engine failure: this drop's cause
            // is "the DJ has nothing to say", never "the engine is down", and the two must never
            // collide on the same outcome=failure field value.
            Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("outcome=failure", StringComparison.Ordinal));
        }
    }

    // -------------------------------------------------------------------------------------
    // ENTRY POINT — the operator-facing half of F99.5.
    // -------------------------------------------------------------------------------------
    public static class ScenarioTheHealthSurfaceShowsIt
    {
        [Fact(Skip = "Pending T149 — see docs/PLAN.md")]
        public static void The_degraded_voice_state_is_visible_on_the_health_endpoint()
        {
            // Drive the real endpoint; an operator with no log stack must still be able to
            // tell why the DJ is quiet.
            Assert.Fail("pending T149");
        }

        [Fact(Skip = "Pending T149 — see docs/PLAN.md")]
        public static void A_healthy_station_reports_no_degraded_voice_state()
        {
            Assert.Fail("pending T149");
        }
    }

    // -------------------------------------------------------------------------------------
    // SAD PATH
    // -------------------------------------------------------------------------------------
    public sealed class ScenarioRecovery() : VoiceIntegrityScenario(new IOException("kokoro down"))
    {
        [Fact]
        public async Task The_next_break_airs()
        {
            // No operator action, no restart — recovery is automatic (F99.1). Given the engine
            // down for one break, on a station with no fallback chain (the drop itself is pinned by
            // ScenarioTheBreakIsDropped above — arranged here, not re-asserted)...
            var source = BuildSource(BuildRouter(primary, substitute), cacheRoot);
            var request = DjBreakRequest();
            await source.RenderAsync(request, CancellationToken.None);

            // When the next break comes due, on the SAME router/source — no restart, no operator
            // intervention: FakeTtsSynthesizer.ThrowOnNextCall was already consumed by the failed
            // attempt above, standing in for the engine having recovered on its own by the next
            // render...
            var recovered = await source.RenderAsync(request, CancellationToken.None);

            // Then it airs.
            Assert.NotNull(recovered);
        }

        [Fact]
        public async Task It_airs_in_the_djs_own_voice()
        {
            var source = BuildSource(BuildRouter(primary, substitute), cacheRoot);
            var request = DjBreakRequest();
            await source.RenderAsync(request, CancellationToken.None);
            await source.RenderAsync(request, CancellationToken.None);

            // In the DJ's own configured voice — the primary is the only engine this fallback-less
            // router ever asks, so there is no other voice it could have used.
            Assert.Equal(request.Voice, primary.LastVoice);
        }

        [Fact(Skip = "Pending T148 — the shipped-default flip makes this pinnable")]
        public async Task No_substitute_was_asked()
        {
            // Same F1 ground as
            // ScenarioTheBreakIsDropped.No_other_voice_is_ever_asked_to_speak_that_line above:
            // vacuous under this fixture's empty chain (zero hops to iterate regardless of any
            // guard) — true of the FIXTURE, not yet of production code, until T148 flips the
            // shipped default itself.
            var source = BuildSource(BuildRouter(primary, substitute), cacheRoot);
            var request = DjBreakRequest();
            await source.RenderAsync(request, CancellationToken.None);
            await source.RenderAsync(request, CancellationToken.None);

            Assert.Equal(0, substitute.CallCount);
        }
    }
}

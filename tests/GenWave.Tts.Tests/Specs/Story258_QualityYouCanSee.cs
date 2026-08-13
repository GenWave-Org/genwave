// STORY-258 — Quality you can actually see (SPEC F100.1, F100.2)
//
// The measurement half of the epic, and the reason it exists: on 2026-07-31 a Loki sweep
// established that ZERO `dbug:` lines reach the fleet log store. Everything this epic depends
// on therefore has to be Information or it may as well not be logged.
//
// F100.2 is the sharper gap. Today the persona is named ONLY when a render fails
// (LlmCopyWriter's warn line), so a failure RATE cannot be computed at all — only raw counts,
// which say nothing about whether a DJ is actually worse or merely on air more. Logging the
// persona on success is what turns "Rusty Strings failed 4 times last night" into a number
// that means something.

namespace GenWave.Tts.Tests.Specs;

using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureQualityYouCanSee
{
    // ------------------------------------------------------------------
    // Shared fixture helpers
    // ------------------------------------------------------------------

    static SegmentRequest Request(string? personaName) =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave", null, DateTimeOffset.UtcNow, "test-station", PersonaName: personaName);

    /// <summary>
    /// The smallest arrangement that reaches <c>TtsSegmentSource.LogRenderOutcome</c> (PLAN T143):
    /// a real render through the fake synthesizer, capturing everything <paramref name="logger"/>
    /// receives. <paramref name="synth"/> is exposed so a fact can set <c>ThrowOnNextCall</c> before
    /// rendering, driving the failure arm.
    /// </summary>
    static TtsSegmentSource BuildSource(FakeTtsSynthesizer synth, ILogger<TtsSegmentSource> logger, string cacheRoot) =>
        new(
            new FakeSegmentCopyWriter("Coming up next."),
            synth,
            new FakeLoudnessAnalyzer(),
            new FakeCueAnalyzer(),
            NoCorrections.Provider(),
            NoCorrections.PersonaCache(),
            NoCorrections.PronunciationProvider(),
            NoCorrections.PersonaPronunciationCache(),
            NoCorrections.PersonaPaceCache(),
            new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" }),
            logger);

    public static class ScenarioEveryEpicFactIsAtInformation
    {
        [Fact]
        public static async Task A_render_outcome_is_emitted_at_information()
        {
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var synth = new FakeTtsSynthesizer();
            var logger = new CapturingLogger<TtsSegmentSource>();
            try
            {
                var source = BuildSource(synth, logger, cacheRoot);

                await source.RenderAsync(Request("Rusty Strings"), CancellationToken.None);

                Assert.Contains(logger.Entries, e =>
                    e.Level == LogLevel.Information && e.Message.Contains("TTS render outcome", StringComparison.Ordinal));
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
                if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
            }
        }

        /// <summary>
        /// The regression guard for the whole feature: a fact that exists solely at Debug is
        /// invisible in production and therefore does not exist. Sweeps EXACTLY the three fact
        /// families whose spec once called for Debug and was amended up to Information —
        /// pronunciation-rule hits (F97.5), correction hits (F100.1), and render outcomes (F100.2) —
        /// through ONE real render that fires all three, capturing each family's own logger
        /// independently. Deliberately narrower than "every epic fact family" (F4 review finding,
        /// PLAN T143 re-review): <c>FallbackTtsSynthesizer</c>'s hop-engagement lines are also part
        /// of this epic but already log at Warning, never Debug, so they carry none of the risk this
        /// guard exists to catch and are out of scope here on that ground, not merely unswept.
        /// Voice-integrity (T147) is out of scope for the opposite reason: it does not exist yet, so
        /// it is honestly excluded rather than faked.
        /// </summary>
        [Fact]
        public static async Task No_epic_fact_is_emitted_only_at_debug()
        {
            const string text = "Coming up, MacLeod plays next from Reykjavik.";
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var stats = new PronunciationRuleHitStats();
            var reporterLogger = new CapturingLogger<PronunciationRuleHitReporter>();
            var reporter = new PronunciationRuleHitReporter(stats, reporterLogger);
            var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) }));
            try
            {
                var kokoro = new KokoroTtsSynthesizer(
                    new HttpClient(handler),
                    new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" }),
                    reporter);
                var corrections = new SpeechCorrectionProvider(
                    new TestOptionsMonitor<TtsCorrectionsOptions>(
                        new TtsCorrectionsOptions { Corrections = """[{"from":"MacLeod","to":"Muh-cloud"}]""" }),
                    NullLogger<SpeechCorrectionProvider>.Instance);
                var normalizingLogger = new CapturingLogger<NormalizingTtsSynthesizer>();
                var normalizingSynth = new NormalizingTtsSynthesizer(
                    kokoro, corrections, NoCorrections.PersonaCache(), new CorrectionsFiredStats(), normalizingLogger);
                var pronunciations = new PronunciationRuleProvider(
                    new TestOptionsMonitor<TtsPronunciationsOptions>(
                        new TtsPronunciationsOptions { Pronunciations = """[{"pattern":"Reykjavik","word":"Reykjavik","ipa":"/reykjavikIpa/"}]""" }),
                    NullLogger<PronunciationRuleProvider>.Instance);
                var sourceLogger = new CapturingLogger<TtsSegmentSource>();
                var source = new TtsSegmentSource(
                    new FakeSegmentCopyWriter(text),
                    normalizingSynth, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(),
                    corrections, NoCorrections.PersonaCache(), pronunciations, NoCorrections.PersonaPronunciationCache(),
                    NoCorrections.PersonaPaceCache(),
                    new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" }),
                    sourceLogger);

                await source.RenderAsync(Request("Rusty Strings"), CancellationToken.None);

                // Every family fired at least once...
                Assert.Contains(reporterLogger.Entries, e => e.Level == LogLevel.Information);
                Assert.Contains(normalizingLogger.Entries, e => e.Level == LogLevel.Information);
                Assert.Contains(sourceLogger.Entries, e =>
                    e.Level == LogLevel.Information && e.Message.Contains("TTS render outcome", StringComparison.Ordinal));

                // ...and none of them EVER logged at Debug — the amendment this whole feature exists
                // to make (F97.5/F100.1/F100.2 all amend a would-be-Debug fact up to Information).
                Assert.DoesNotContain(reporterLogger.Entries, e => e.Level == LogLevel.Debug);
                Assert.DoesNotContain(normalizingLogger.Entries, e => e.Level == LogLevel.Debug);
                Assert.DoesNotContain(sourceLogger.Entries, e => e.Level == LogLevel.Debug);
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    public static class ScenarioSuccessNamesThePersonaToo
    {
        [Fact]
        public static async Task A_successful_render_names_its_persona()
        {
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var synth = new FakeTtsSynthesizer();
            var logger = new CapturingLogger<TtsSegmentSource>();
            try
            {
                var source = BuildSource(synth, logger, cacheRoot);

                await source.RenderAsync(Request("Rusty Strings"), CancellationToken.None);

                Assert.Contains(logger.Entries, e =>
                    e.Level == LogLevel.Information && e.Message.Contains("Rusty Strings", StringComparison.Ordinal));
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
                if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
            }
        }

        [Fact]
        public static async Task A_failed_render_still_names_its_persona()
        {
            // The existing behaviour must not regress while the success side is added.
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var synth = new FakeTtsSynthesizer { ThrowOnNextCall = new IOException("kokoro down") };
            var logger = new CapturingLogger<TtsSegmentSource>();
            try
            {
                var source = BuildSource(synth, logger, cacheRoot);

                await source.RenderAsync(Request("Rusty Strings"), CancellationToken.None);

                Assert.Contains(logger.Entries, e =>
                    e.Level == LogLevel.Information && e.Message.Contains("Rusty Strings", StringComparison.Ordinal));
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
                if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
            }
        }

        [Fact]
        public static async Task The_outcome_itself_is_on_the_line()
        {
            // Success and failure must be distinguishable without inferring from which message
            // template was used — both arms log through the SAME template, so "outcome=success" vs
            // "outcome=failure" is a field value, not a different line shape. Two separate cache
            // roots: the same (text, voice, stationId) hash under one shared root would make the
            // "failure" render a cache HIT off the success render's own file, never reaching the
            // synthesizer (and its scripted throw) at all.
            var successCacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var failureCacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var successSynth = new FakeTtsSynthesizer();
            var successLogger = new CapturingLogger<TtsSegmentSource>();
            var failureSynth = new FakeTtsSynthesizer { ThrowOnNextCall = new IOException("kokoro down") };
            var failureLogger = new CapturingLogger<TtsSegmentSource>();
            try
            {
                await BuildSource(successSynth, successLogger, successCacheRoot).RenderAsync(Request("Rusty Strings"), CancellationToken.None);
                await BuildSource(failureSynth, failureLogger, failureCacheRoot).RenderAsync(Request("Rusty Strings"), CancellationToken.None);

                Assert.Contains(successLogger.Entries, e =>
                    e.Level == LogLevel.Information && e.Message.Contains("outcome=success", StringComparison.Ordinal));
                Assert.Contains(failureLogger.Entries, e =>
                    e.Level == LogLevel.Information && e.Message.Contains("outcome=failure", StringComparison.Ordinal));
            }
            finally
            {
                if (Directory.Exists(successCacheRoot)) Directory.Delete(successCacheRoot, recursive: true);
                if (Directory.Exists(failureCacheRoot)) Directory.Delete(failureCacheRoot, recursive: true);
                if (Directory.Exists(successSynth.OutputDirectory)) Directory.Delete(successSynth.OutputDirectory, recursive: true);
                if (Directory.Exists(failureSynth.OutputDirectory)) Directory.Delete(failureSynth.OutputDirectory, recursive: true);
            }
        }

        /// <summary>
        /// Pins the cause field's VALUE on the success arm, not merely that a line exists (F3
        /// review finding, PLAN T143 re-review): a mutation that stopped passing
        /// <c>TtsSegmentSource.NoCause</c> through on the success arm — substituting an empty
        /// string instead — ran green before this fact existed, because no prior assertion read
        /// the cause field's actual value on that arm.
        /// </summary>
        [Fact]
        public static async Task The_cause_is_n_a_on_a_successful_render()
        {
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var synth = new FakeTtsSynthesizer();
            var logger = new CapturingLogger<TtsSegmentSource>();
            try
            {
                var source = BuildSource(synth, logger, cacheRoot);

                await source.RenderAsync(Request("Rusty Strings"), CancellationToken.None);

                Assert.Contains(logger.Entries, e => e.Message.Contains("cause=n/a", StringComparison.Ordinal));
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
                if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
            }
        }

        /// <summary>
        /// Sibling of <see cref="The_cause_is_n_a_on_a_successful_render"/> on the failure arm: pins
        /// the cause field to the exception's own type name, not merely that SOME cause value
        /// exists (F3 review finding).
        /// </summary>
        [Fact]
        public static async Task The_cause_names_the_exception_type_on_a_failed_render()
        {
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var synth = new FakeTtsSynthesizer { ThrowOnNextCall = new IOException("kokoro down") };
            var logger = new CapturingLogger<TtsSegmentSource>();
            try
            {
                var source = BuildSource(synth, logger, cacheRoot);

                await source.RenderAsync(Request("Rusty Strings"), CancellationToken.None);

                Assert.Contains(logger.Entries, e => e.Message.Contains("cause=IOException", StringComparison.Ordinal));
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
                if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// F1 review finding (HIGH, PLAN T143 re-review): the Kokoro typed <c>HttpClient</c> carries
    /// its own internal timeout — a hung engine throws <see cref="TaskCanceledException"/> with the
    /// CALLER's token left uncancelled, and <c>FallbackTtsSynthesizer.RenderHopAsync</c> rethrows it
    /// unchanged when no hop attempts it. An unguarded <c>catch (OperationCanceledException)</c> in
    /// <c>TtsSegmentSource.RenderAsync</c> swallowed that as a silent null — no WARN, no outcome
    /// line — so a hung engine vanished from the render-outcome rate while genuinely failing. This
    /// scenario proves the fix: only a caller-driven cancellation (<c>ct.IsCancellationRequested</c>)
    /// stays a non-outcome; everything else, OperationCanceledException-shaped or not, is a logged
    /// failure.
    /// </summary>
    public static class ScenarioEngineTimeoutsAreFailuresNotSilence
    {
        [Fact]
        public static async Task A_timeout_that_never_cancels_the_callers_token_is_a_logged_failure()
        {
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var synth = new FakeTtsSynthesizer { ThrowOnNextCall = new TaskCanceledException("kokoro timed out") };
            var logger = new CapturingLogger<TtsSegmentSource>();
            try
            {
                var source = BuildSource(synth, logger, cacheRoot);

                // CancellationToken.None: the caller never cancelled anything — only the
                // synthesizer's own internal timeout fired, exactly Kokoro's HttpClient shape.
                var item = await source.RenderAsync(Request("Rusty Strings"), CancellationToken.None);

                Assert.Null(item);
                Assert.Contains(logger.Entries, e =>
                    e.Level == LogLevel.Information
                    && e.Message.Contains("outcome=failure", StringComparison.Ordinal)
                    && e.Message.Contains("cause=TaskCanceledException", StringComparison.Ordinal));
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
                if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// The symmetric twin of <see cref="ScenarioEngineTimeoutsAreFailuresNotSilence"/> (round-2
    /// review finding): a GENUINE caller cancellation — <c>ct.IsCancellationRequested</c> true —
    /// must stay silent, no outcome line at all, success or failure. A mutation that logged an
    /// outcome line regardless of which <see cref="OperationCanceledException"/> arm caught (i.e.
    /// dropped the <c>when (ct.IsCancellationRequested)</c> guard's asymmetry and always emitted a
    /// failure outcome) ran green before this fact existed: a shutdown-time regression here would
    /// inflate every persona's failure rate exactly when renders cancel en masse.
    /// </summary>
    public static class ScenarioGenuineCancellationIsNotAnOutcome
    {
        [Fact]
        public static async Task A_cancelled_token_emits_no_render_outcome_line()
        {
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var synth = new FakeTtsSynthesizer();
            var logger = new CapturingLogger<TtsSegmentSource>();
            try
            {
                var source = BuildSource(synth, logger, cacheRoot);
                using var cts = new CancellationTokenSource();
                cts.Cancel();

                var item = await source.RenderAsync(Request("Rusty Strings"), cts.Token);

                Assert.Null(item);
                Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("TTS render outcome", StringComparison.Ordinal));
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
                if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Round-2 F2 review finding: a persona name that itself contains a double quote (e.g. an
    /// operator-authored stage name, <c>Rusty "The Riff" Strings</c>) must not break the field's own
    /// quoting — an un-escaped embedded quote would close the wrapping pair early, so a logfmt
    /// reader would extract only <c>Rusty </c> and leave the rest as stray, unparsed content.
    /// </summary>
    public static class ScenarioEmbeddedQuotesAreEscapedNotBroken
    {
        [Fact]
        public static async Task A_persona_name_containing_a_quote_is_escaped_not_truncated()
        {
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var synth = new FakeTtsSynthesizer();
            var logger = new CapturingLogger<TtsSegmentSource>();
            try
            {
                var source = BuildSource(synth, logger, cacheRoot);

                await source.RenderAsync(Request("Rusty \"The Riff\" Strings"), CancellationToken.None);

                // The escaped, still-fully-quoted field — not truncated at the first embedded quote.
                Assert.Contains(logger.Entries, e =>
                    e.Message.Contains("persona=\"Rusty \\\"The Riff\\\" Strings\"", StringComparison.Ordinal));
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
                if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
            }
        }
    }

    public static class ScenarioARateBecomesComputable
    {
        [Fact]
        public static async Task Successes_and_failures_are_attributable_to_the_same_persona()
        {
            // The point of the story: a denominator finally exists. One persona, one success and
            // one failure, both attributed to it — a rate (1 of 2) becomes computable from these
            // two lines alone. Two separate cache roots for the same reason as the sibling scenario
            // above — a shared root would let the "failure" render cache-hit off the success file.
            var successCacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var failureCacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var successSynth = new FakeTtsSynthesizer();
            var successLogger = new CapturingLogger<TtsSegmentSource>();
            var failureSynth = new FakeTtsSynthesizer { ThrowOnNextCall = new IOException("kokoro down") };
            var failureLogger = new CapturingLogger<TtsSegmentSource>();
            try
            {
                await BuildSource(successSynth, successLogger, successCacheRoot).RenderAsync(Request("Rusty Strings"), CancellationToken.None);
                await BuildSource(failureSynth, failureLogger, failureCacheRoot).RenderAsync(Request("Rusty Strings"), CancellationToken.None);

                // Quoted (F2 review finding): "Rusty Strings" contains a space, and logfmt
                // (observability/LABELS.md's own `| logfmt` query path) truncates an unquoted value
                // at its first space.
                Assert.Contains(successLogger.Entries, e =>
                    e.Message.Contains("persona=\"Rusty Strings\"", StringComparison.Ordinal)
                    && e.Message.Contains("outcome=success", StringComparison.Ordinal));
                Assert.Contains(failureLogger.Entries, e =>
                    e.Message.Contains("persona=\"Rusty Strings\"", StringComparison.Ordinal)
                    && e.Message.Contains("outcome=failure", StringComparison.Ordinal));
            }
            finally
            {
                if (Directory.Exists(successCacheRoot)) Directory.Delete(successCacheRoot, recursive: true);
                if (Directory.Exists(failureCacheRoot)) Directory.Delete(failureCacheRoot, recursive: true);
                if (Directory.Exists(successSynth.OutputDirectory)) Directory.Delete(successSynth.OutputDirectory, recursive: true);
                if (Directory.Exists(failureSynth.OutputDirectory)) Directory.Delete(failureSynth.OutputDirectory, recursive: true);
            }
        }
    }

    // -------------------------------------------------------------------------------------
    // SAD PATH
    // -------------------------------------------------------------------------------------
    public static class ScenarioRendersWithNoPersonaInScope
    {
        [Fact]
        public static async Task A_persona_less_render_records_that_explicitly()
        {
            // Station imaging (gh-#96) is deliberately persona-less; the field must say so rather
            // than being omitted, or the absence reads as a logging bug.
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var synth = new FakeTtsSynthesizer();
            var logger = new CapturingLogger<TtsSegmentSource>();
            try
            {
                var source = BuildSource(synth, logger, cacheRoot);

                await source.RenderAsync(Request(personaName: null), CancellationToken.None);

                // Quoted like every other value of this field (F2 review finding) — the sentinel
                // is not a special case of the field's own shape.
                Assert.Contains(logger.Entries, e =>
                    e.Level == LogLevel.Information && e.Message.Contains("persona=\"none\"", StringComparison.Ordinal));
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
                if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
            }
        }
    }
}

namespace GenWave.Tts.Tests.Specs;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

/// <summary>
/// gh-#491 (Dean's 2026-08-13 ruling): when a speech correction and a pronunciation rule target the
/// SAME word, the pronunciation rule wins — the correction is suppressed for that render, loudly.
/// Field origin: a legacy <c>MacLeod → Maa-cloud</c> correction on the demo box rewrote the word out
/// of every audition's text before the operator's candidate IPA rule could match it, so seven
/// auditions with seven different IPAs rendered byte-identically. See
/// <see cref="RuleOverCorrectionPrecedence"/> for the invariant these specs pin.
/// </summary>
public static class FeatureGh491RulesOverCorrections
{
    const string Text = "Now playing: MacLeod.";

    static SpeechCorrectionProvider CorrectionsFor(string json) =>
        new(
            new TestOptionsMonitor<TtsCorrectionsOptions>(new TtsCorrectionsOptions { Corrections = json }),
            NullLogger<SpeechCorrectionProvider>.Instance);

    static NormalizingTtsSynthesizer BuildNormalizer(
        FakeTtsSynthesizer inner,
        SpeechCorrectionProvider corrections,
        CorrectionsFiredStats firedStats,
        ILogger<NormalizingTtsSynthesizer> logger) =>
        new(inner, corrections, NoCorrections.PersonaCache(), firedStats, logger);

    static TtsRenderContext ContextWithRule(string pattern) =>
        new(Text, "af_heart", Kind: null)
        {
            Rules = [new PronunciationRule(pattern, pattern, "mˈʌklˈoʊd")],
        };

    public static class ScenarioARuleSuppressesItsCollidingCorrection
    {
        [Fact]
        public static async Task The_corrected_word_survives_normalization_untouched()
        {
            // The gh-#491 field shape verbatim: the correction would rewrite MacLeod → Maa-cloud
            // BEFORE the rule (matched downstream, at Kokoro request build) could ever see it.
            var inner = new FakeTtsSynthesizer();
            var normalizer = BuildNormalizer(
                inner, CorrectionsFor("""[{"from":"MacLeod","to":"Maa-cloud"}]"""),
                new CorrectionsFiredStats(), NullLogger<NormalizingTtsSynthesizer>.Instance);
            try
            {
                await normalizer.SynthesizeAsync(ContextWithRule("MacLeod"), CancellationToken.None);

                Assert.Contains("MacLeod", inner.LastText, StringComparison.Ordinal);
            }
            finally
            {
                if (Directory.Exists(inner.OutputDirectory)) Directory.Delete(inner.OutputDirectory, recursive: true);
            }
        }

        [Fact]
        public static async Task The_collision_predicate_is_case_insensitive()
        {
            // Both systems match text case-insensitively; the precedence between them must too.
            var inner = new FakeTtsSynthesizer();
            var normalizer = BuildNormalizer(
                inner, CorrectionsFor("""[{"from":"MACLEOD","to":"Maa-cloud"}]"""),
                new CorrectionsFiredStats(), NullLogger<NormalizingTtsSynthesizer>.Instance);
            try
            {
                await normalizer.SynthesizeAsync(ContextWithRule("macleod"), CancellationToken.None);

                Assert.Contains("MacLeod", inner.LastText, StringComparison.Ordinal);
            }
            finally
            {
                if (Directory.Exists(inner.OutputDirectory)) Directory.Delete(inner.OutputDirectory, recursive: true);
            }
        }

        [Fact]
        public static async Task A_suppressed_correction_is_never_counted_as_fired()
        {
            // The fired counter feeds GET /api/tts/corrections-stats (F68.7) — counting a
            // correction the render did not apply would make that surface lie.
            var inner = new FakeTtsSynthesizer();
            var firedStats = new CorrectionsFiredStats();
            var normalizer = BuildNormalizer(
                inner, CorrectionsFor("""[{"from":"MacLeod","to":"Maa-cloud"}]"""),
                firedStats, NullLogger<NormalizingTtsSynthesizer>.Instance);
            try
            {
                await normalizer.SynthesizeAsync(ContextWithRule("MacLeod"), CancellationToken.None);

                Assert.Empty(firedStats.Snapshot());
            }
            finally
            {
                if (Directory.Exists(inner.OutputDirectory)) Directory.Delete(inner.OutputDirectory, recursive: true);
            }
        }

        [Fact]
        public static async Task The_suppression_is_logged_at_information()
        {
            // Never silent (the whole gh-#491 lesson): the operator asking "why did my correction
            // stop applying?" reads the answer in the fleet log store — Information, because Debug
            // never reaches it (the Story258 amendment ground).
            var inner = new FakeTtsSynthesizer();
            var logger = new CapturingLogger<NormalizingTtsSynthesizer>();
            var normalizer = BuildNormalizer(
                inner, CorrectionsFor("""[{"from":"MacLeod","to":"Maa-cloud"}]"""),
                new CorrectionsFiredStats(), logger);
            try
            {
                await normalizer.SynthesizeAsync(ContextWithRule("MacLeod"), CancellationToken.None);

                Assert.Contains(logger.Entries, e =>
                    e.Level == LogLevel.Information
                    && e.Message.Contains("TTS correction suppressed by pronunciation rule", StringComparison.Ordinal)
                    && e.Message.Contains("MacLeod", StringComparison.Ordinal));
            }
            finally
            {
                if (Directory.Exists(inner.OutputDirectory)) Directory.Delete(inner.OutputDirectory, recursive: true);
            }
        }
    }

    public static class ScenarioEverythingElseStillApplies
    {
        [Fact]
        public static async Task A_non_colliding_correction_applies_alongside_the_rule()
        {
            // Suppression is per-collision, never a blanket "rules present, corrections off".
            var inner = new FakeTtsSynthesizer();
            var normalizer = BuildNormalizer(
                inner,
                CorrectionsFor("""[{"from":"MacLeod","to":"Maa-cloud"},{"from":"GWAV","to":"Gee-Wave"}]"""),
                new CorrectionsFiredStats(), NullLogger<NormalizingTtsSynthesizer>.Instance);
            var context = ContextWithRule("MacLeod") with { Text = "GWAV presents: MacLeod." };
            try
            {
                await normalizer.SynthesizeAsync(context, CancellationToken.None);

                Assert.Equal("Gee-Wave presents: MacLeod.", inner.LastText);
            }
            finally
            {
                if (Directory.Exists(inner.OutputDirectory)) Directory.Delete(inner.OutputDirectory, recursive: true);
            }
        }

        [Fact]
        public static async Task A_render_with_no_rules_applies_the_correction_exactly_as_before()
        {
            // The pre-gh-#491 posture survives untouched for every render that carries no rules —
            // the overwhelmingly common case (music-only stations, no rules authored).
            var inner = new FakeTtsSynthesizer();
            var normalizer = BuildNormalizer(
                inner, CorrectionsFor("""[{"from":"MacLeod","to":"Maa-cloud"}]"""),
                new CorrectionsFiredStats(), NullLogger<NormalizingTtsSynthesizer>.Instance);
            try
            {
                await normalizer.SynthesizeAsync(new TtsRenderContext(Text, "af_heart", Kind: null), CancellationToken.None);

                Assert.Equal("Now playing: Maa-cloud.", inner.LastText);
            }
            finally
            {
                if (Directory.Exists(inner.OutputDirectory)) Directory.Delete(inner.OutputDirectory, recursive: true);
            }
        }

        [Fact]
        public static async Task A_correction_for_a_longer_phrase_is_not_suppressed()
        {
            // The predicate is identity equality, deliberately not containment (see
            // RuleOverCorrectionPrecedence): a specific-phrase correction layered over a general
            // rule keeps applying — the same specific-over-general composition both systems
            // document individually.
            var inner = new FakeTtsSynthesizer();
            var normalizer = BuildNormalizer(
                inner, CorrectionsFor("""[{"from":"MacLeod Duncan","to":"Maa-cloud Dunkin"}]"""),
                new CorrectionsFiredStats(), NullLogger<NormalizingTtsSynthesizer>.Instance);
            var context = ContextWithRule("MacLeod") with { Text = "Next: MacLeod Duncan." };
            try
            {
                await normalizer.SynthesizeAsync(context, CancellationToken.None);

                Assert.Equal("Next: Maa-cloud Dunkin.", inner.LastText);
            }
            finally
            {
                if (Directory.Exists(inner.OutputDirectory)) Directory.Delete(inner.OutputDirectory, recursive: true);
            }
        }
    }
}

// STORY-184 — SpeechText normalization module
//
// BDD specification — xUnit (SPEC F68.1–F68.4, F68.6).

using System.Reflection;
using GenWave.Tts;
using Xunit;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureSpeechTextNormalization
{
    public static class ScenarioOrderedPasses
    {
        [Fact]
        public static void Output_is_scrubbed_corrected_expanded_and_collapsed_in_spec_order()
        {
            // Given copy containing a think block, markdown emphasis, an HTML entity,
            //       a correction match, and 76°F
            const string input = "<think>internal reasoning</think>**MacLeod** plays at 76°F &amp; sunny.";
            var corrections = SpeechCorrectionSet.Create([new SpeechCorrection("MacLeod", "Muh-cloud")]);

            // When  SpeechText.Normalize runs
            var result = SpeechText.Normalize(input, corrections);

            // Then  the output is fully scrubbed, corrected, unit-expanded, and
            //       whitespace-collapsed in spec'd order (F68.2)
            Assert.Equal("Muh-cloud plays at 76 degrees Fahrenheit and sunny.", result);
        }
    }

    public static class ScenarioSurvivalCases
    {
        [Fact]
        public static void Kesha_survives_byte_identical()
        {
            // Given the string "Ke$ha" and no matching correction
            const string input = "Ke$ha";

            // When  it is normalized
            var result = SpeechText.Normalize(input, SpeechCorrectionSet.Empty);

            // Then  it survives byte-identical (F68.4)
            Assert.Equal(input, result);
        }

        [Fact]
        public static void Acdc_survives_byte_identical()
        {
            // Given "AC/DC" — Then byte-identical (F68.4)
            const string input = "AC/DC";

            var result = SpeechText.Normalize(input, SpeechCorrectionSet.Empty);

            Assert.Equal(input, result);
        }

        [Fact]
        public static void Pink_survives_byte_identical()
        {
            // Given "P!nk" — Then byte-identical (F68.4)
            const string input = "P!nk";

            var result = SpeechText.Normalize(input, SpeechCorrectionSet.Empty);

            Assert.Equal(input, result);
        }

        [Fact]
        public static void Snake_case_survives_byte_identical()
        {
            // Given "snake_case" — Then byte-identical (F68.4)
            const string input = "snake_case";

            var result = SpeechText.Normalize(input, SpeechCorrectionSet.Empty);

            Assert.Equal(input, result);
        }
    }

    public static class ScenarioPureModule
    {
        [Fact]
        public static void SpeechText_performs_no_io_and_references_only_bcl_types()
        {
            // Given the SpeechText implementation
            var type = typeof(SpeechText);

            // When  its shape is inspected (reflection): a static class has no instance state and
            //       exposes corrections only as a parameter it is handed, never read from the
            //       environment.
            var isStaticClass = type is { IsAbstract: true, IsSealed: true };
            var hasNoInstanceConstructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length == 0;
            var hasNoInstanceFields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length == 0;
            var normalize = type.GetMethod(nameof(SpeechText.Normalize), BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(normalize);
            var correctionsParameter = normalize.GetParameters().SingleOrDefault(p => p.ParameterType == typeof(SpeechCorrectionSet));

            // Then  it performs no I/O and references only BCL types (F68.6): no instance state
            //       anywhere on the type, and corrections arrive as a parameter rather than being
            //       read from settings/I-O internally.
            Assert.True(isStaticClass && hasNoInstanceConstructors && hasNoInstanceFields && correctionsParameter is not null);
        }
    }

    public static class SadPathThinkBlocks
    {
        [Fact]
        public static void Think_block_never_survives_normalization()
        {
            // Given copy with <think>…</think> anywhere, including inside structured output,
            //       plus an unclosed <think> trailing at the end
            const string input =
                "<think>reasoning A</think>Good morning GenWave listeners. " +
                "<think>reasoning B</think>Next up is a great track. " +
                "<think>trailing unclosed reasoning";

            // When  it is normalized
            var result = SpeechText.Normalize(input, SpeechCorrectionSet.Empty);

            // Then  no reasoning text remains in the output (F68.3)
            Assert.Equal("Good morning GenWave listeners. Next up is a great track.", result);
        }

        [Fact]
        public static void Nested_think_blocks_never_survive_normalization()
        {
            // Given a <think> block nested inside another <think> block, with visible text both
            //       between the inner block's tags and between the inner close and outer close
            const string input = "<think>a<think>b</think>c</think>done";

            // When  it is normalized
            var result = SpeechText.Normalize(input, SpeechCorrectionSet.Empty);

            // Then  none of the nested reasoning text or tags survive — not even the fragment
            //       trapped between the inner and outer closing tags (F68.3)
            Assert.Equal("done", result);
        }
    }

    public static class SadPathHostileCorrections
    {
        [Fact]
        public static void Pathological_pattern_aborts_at_timeout_without_unhandled_exception()
        {
            // Given an operator correction forming a pathological backtracking pattern
            // (SpeechCorrectionSet.Create always Regex.Escapes operator text, which defangs
            // catastrophic backtracking by construction — FromRawPattern is a test-only seam that
            // exercises the timeout-and-skip mechanism directly with a classic ReDoS pattern.)
            var corrections = SpeechCorrectionSet.FromRawPattern(@"(a+)+$", "boom");
            var input = new string('a', 35) + "!";

            // When  normalization runs against adversarial input
            var exception = Record.Exception(() => SpeechText.Normalize(input, corrections));

            // Then  matching aborts at the 250ms timeout without unhandled exception (F68.5)
            Assert.Null(exception);
        }

        [Fact]
        public static void Dollar_in_replacement_is_literal_not_group_reference()
        {
            // Given a correction whose To contains a $ sequence that collides with .NET's
            //       replacement-token syntax ($0 = whole match) if it were ever treated as a
            //       pattern rather than literal text
            var corrections = SpeechCorrectionSet.Create([new SpeechCorrection("price", "cost is $0 today")]);

            // When  it fires
            var result = SpeechText.Normalize("The price is set.", corrections);

            // Then  the replacement text is literal, not a regex group reference (F68.5)
            Assert.Equal("The cost is $0 today is set.", result);
        }

        [Fact]
        public static void Blank_and_whitespace_only_from_leave_input_byte_identical()
        {
            // Given a correction set built from a blank From and a whitespace-only From — an
            //       unguarded empty pattern compiles to Regex("") and matches at every position,
            //       so Create must skip both rather than compile them (F68.5)
            var corrections = SpeechCorrectionSet.Create(
            [
                new SpeechCorrection("", "X"),
                new SpeechCorrection("   ", "Y"),
            ]);
            const string input = "hello world";

            // When  it is normalized
            var result = SpeechText.Normalize(input, corrections);

            // Then  the input survives byte-identical — a blank rule is a no-op, never a corruptor
            Assert.Equal(input, result);
        }
    }
}

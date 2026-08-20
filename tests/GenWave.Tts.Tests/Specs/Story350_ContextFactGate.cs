// STORY-350 — Context copy can't invent facts (SPEC F138.1, F138.2, F138.4 · PLAN T329/T331/T335)
//
// BDD specification — xUnit. PENDING: every Specification is skipped until its task builds
// the behavior (the wizard-epic compile-clean-pending convention). /build-loop unskips per
// task; a body still failing after its task is a defect, not a pending.
//
// The gh-#434 aired exhibit is the pinned regression: facts "Edmonton: overcast, 15°C.
// Today's high 21°C, low 12°C." produced copy claiming "6 degrees below", "sunshine",
// and "today is saturday" — three fabrications, all aired. The gate is deterministic
// armor at the LlmCopyWriter seam: prompt asks (F138.5), checker enforces (F138.2),
// ladder degrades re-ask-once → template (F138.4), never silence (F107.6).

using System.Reflection;
using GenWave.Tts;
using Xunit;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureContextFactGate
{
    public static class ScenarioSupportedCopyPassesUntouched
    {
        // Given the gh-#434 fact block
        const string FactBlock = "Edmonton: overcast, 15°C. Today's high 21°C, low 12°C.";

        // When  copy claims only overcast, 15, 21, or 12
        [Fact]
        public static void Copy_with_only_supported_claims_passes_unchanged()
        {
            const string copy = "It's overcast today at 15 degrees, with a high of 21 and a low of 12.";

            var result = CopyClaims.CheckFacts(copy, FactBlock);

            // Then  supported digits/conditions pass the checker with zero violations
            Assert.Empty(result.Violations);
        }

        [Fact]
        public static void A_supported_claim_is_matched_case_insensitively()
        {
            const string copy = "Overcast conditions expected all day.";

            var result = CopyClaims.CheckFacts(copy, FactBlock);

            // Then  'Overcast' in copy matches 'overcast' in facts
            Assert.Empty(result.Violations);
        }
    }

    public static class ScenarioInventedClaimsAreCaught
    {
        // Given the same fact block
        const string FactBlock = "Edmonton: overcast, 15°C. Today's high 21°C, low 12°C.";

        // When  the copy fabricates
        [Fact]
        public static void An_unsupported_digit_run_is_reported()
        {
            const string copy = "It feels like 6 degrees below freezing out there today.";

            var result = CopyClaims.CheckFacts(copy, FactBlock);

            // Then  '6 degrees below' against 15/21/12 facts yields a digit violation naming '6'
            Assert.Contains(result.Violations, v => v.Class == ClaimClass.DigitRun && v.Token == "6");
        }

        [Fact]
        public static void An_unsupported_condition_word_is_reported()
        {
            const string copy = "Expect plenty of sunshine out there this afternoon.";

            var result = CopyClaims.CheckFacts(copy, FactBlock);

            // Then  'sunshine' against overcast facts yields a condition violation
            Assert.Contains(result.Violations, v => v.Class == ClaimClass.ConditionWord && v.Token == "sunshine");
        }

        [Fact]
        public static void An_unsupported_weekday_is_reported()
        {
            const string copy = "Today is saturday here in the studio.";

            var result = CopyClaims.CheckFacts(copy, FactBlock);

            // Then  'today is saturday' with no weekday in facts yields a weekday violation
            Assert.Contains(result.Violations, v => v.Class == ClaimClass.Weekday && v.Token == "saturday");
        }
    }

    // The present-frame narrowing (SPEC F138.3, amended T329 review round 1) governs CheckFacts's own
    // weekday class exactly as it governs CheckClock's: only a weekday ASSERTED as the present frame
    // is a claim at all. A displaced/recall/anticipatory reference is never extracted, so it can never
    // be reported "unsupported" — the F138 when-in-doubt-PASS posture doing its job, not a fact-block
    // leniency of its own.
    public static class ScenarioWeekdayPresentFrameNarrowingAppliesToFacts
    {
        [Fact]
        public static void A_song_title_naming_a_weekday_is_never_a_claim()
        {
            // Given a fact block that names no weekday at all
            const string factBlock = "Edmonton: overcast, 15°C. Today's high 21°C, low 12°C.";
            // When  copy names a weekday only inside a song title, under no present-frame marker
            const string copy = "Next up: Manic Monday.";

            var result = CopyClaims.CheckFacts(copy, factBlock);

            // Then  "Monday" is never extracted (no "this/today is/it's/happy {weekday}" marker
            //       precedes it), so there is nothing to check for support — it passes
            Assert.Empty(result.Violations);
        }
    }

    // T329 review round 3 regression pin: same curly-apostrophe fix as Story351's own pin, exercised
    // through CheckFacts's own weekday class (F138.2) — see Story351's own remarks for why a model
    // reaches this checker with a curly U+2019 apostrophe intact, not the SpeechText-folded straight
    // one.
    public static class ScenarioCurlyApostropheMarksAFactClaimToo
    {
        [Fact]
        public static void A_curly_apostrophe_its_weekday_marker_is_checked_for_support()
        {
            // Given a fact block that names no weekday at all
            const string factBlock = "Edmonton: overcast, 15°C. Today's high 21°C, low 12°C.";
            // When  copy asserts a weekday under the curly-quoted "it's" marker
            const string copy = "It\u2019s saturday here in the studio.";

            var result = CopyClaims.CheckFacts(copy, factBlock);

            // Then  the curly apostrophe still marks "saturday" as a present-frame claim, and with
            //       no weekday anywhere in the facts, it is unsupported
            Assert.Contains(result.Violations, v => v.Class == ClaimClass.Weekday && v.Token == "saturday");
        }
    }

    public static class ScenarioTheLadderDegrades
    {
        // Given a first completion that fails the gate (stub LLM serving poisoned copy
        // through the production LlmCopyWriter seam — the entry-point scenario)
        [Fact(Skip = "pending T331 — gate not wired at the LlmCopyWriter seam yet")]
        public static void Exactly_one_reask_is_issued() =>
            Assert.Fail("pending T331: the writer retries once, never more");

        [Fact(Skip = "pending T331")]
        public static void The_reask_prompt_names_the_violating_claim() =>
            Assert.Fail("pending T331: the retry prompt contains the rejected claim text");

        [Fact(Skip = "pending T331")]
        public static void A_failing_reask_lands_on_the_template() =>
            Assert.Fail("pending T331: second violation airs the deterministic template line (F107.6 — never silence)");

        [Fact(Skip = "pending T331")]
        public static void The_guard_line_rides_the_prompt() =>
            Assert.Fail("pending T331: the system prompt carries the comma-free weekday/daypart guard line (F138.5)");
    }

    public static class SadPathCheckerDiscipline
    {
        [Fact]
        public static void The_checker_is_pure()
        {
            // Given the CopyClaims implementation
            var type = typeof(CopyClaims);

            // When  its shape is inspected (reflection): a static class with no instance state,
            //       exactly the SpeechText purity posture (F68.6) SPEC F138.1 names by name
            var isStaticClass = type is { IsAbstract: true, IsSealed: true };
            var hasNoInstanceConstructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length == 0;
            var hasNoInstanceFields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length == 0;
            var checkFacts = type.GetMethod(nameof(CopyClaims.CheckFacts), BindingFlags.Public | BindingFlags.Static);
            var checkClock = type.GetMethod(nameof(CopyClaims.CheckClock), BindingFlags.Public | BindingFlags.Static);

            // Then  no instance state anywhere on the type, and both entry points are public
            //       static functions of their own parameters only — no I/O, no settings read
            Assert.True(isStaticClass && hasNoInstanceConstructors && hasNoInstanceFields
                && checkFacts is not null && checkClock is not null);
        }

        [Fact(Skip = "pending T331")]
        public static void Budget_exhaustion_degrades_to_template_not_a_longer_hold() =>
            Assert.Fail("pending T331: an exhausted render budget skips the re-ask and airs the template");
    }

    // Further pure-level pins (PLAN T329) — digit-run tokenization the design constraints called
    // out explicitly: decimals, and range endpoints vs. an in-between value. Not gh-#434 exhibit
    // text; a second fact block exercises the shapes the exhibit itself doesn't cover.
    public static class ScenarioDigitRunTokenization
    {
        // Given a fact block with a decimal figure and a hyphenated range
        const string FactBlock = "Ocean depth today: 108.8 meters. Coastal range 12-15°C.";

        [Fact]
        public static void A_decimal_claim_matches_the_full_token()
        {
            const string copy = "Depth reads 108.8 meters this morning.";

            var result = CopyClaims.CheckFacts(copy, FactBlock);

            // Then  "108.8" is one token (never split into "108" and "8") and it is literally
            //       present, so it is supported
            Assert.Empty(result.Violations);
        }

        [Fact]
        public static void A_truncated_digit_run_is_supported_by_the_decimal_prefix_rule()
        {
            const string copy = "Depth reads about 108 meters this morning.";

            var result = CopyClaims.CheckFacts(copy, FactBlock);

            // Then  "108" is not its own token in the fact block, but "108.8" — a fact token — starts
            //       with "108." (amended T329 review round 1: the deliberate decimal-prefix allowance,
            //       kept explicitly; this is NOT the old, removed literal-substring rule)
            Assert.Empty(result.Violations);
        }

        [Fact]
        public static void A_range_endpoint_is_supported()
        {
            const string copy = "Highs near 15 along the coast.";

            var result = CopyClaims.CheckFacts(copy, FactBlock);

            // Then  "15" is the range's own printed endpoint token, equal to a fact token
            Assert.Empty(result.Violations);
        }

        [Fact]
        public static void A_value_strictly_inside_a_stated_range_is_flagged()
        {
            const string copy = "Highs near 13 along the coast.";

            var result = CopyClaims.CheckFacts(copy, FactBlock);

            // Then  "13" is never printed literally in "12-15°C" — the checker does not interpolate
            //       ranges, so this is reported (a documented, accepted conservative gap)
            Assert.Contains(result.Violations, v => v.Class == ClaimClass.DigitRun && v.Token == "13");
        }

        [Fact]
        public static void A_short_digit_claim_is_not_satisfied_by_a_containing_digit_run()
        {
            // Given a fact block whose only "6" lives embedded inside a larger number (gh-#434
            // hardened: the removed literal-substring rule let "6" hide inside "16")
            const string factBlock = "Edmonton: overcast, 16°C. Today's high 21°C, low 12°C.";
            const string copy = "It feels like 6 degrees below freezing out there today.";

            var result = CopyClaims.CheckFacts(copy, factBlock);

            // Then  "6" is never its OWN digit-run token in the fact block (only "16", "21", "12" are),
            //       so whole-token matching still reports it — the robust #434 regression
            Assert.Contains(result.Violations, v => v.Class == ClaimClass.DigitRun && v.Token == "6");
        }

        [Fact]
        public static void A_short_digit_claim_is_not_satisfied_by_a_date_or_timestamp_block()
        {
            // Given a fact block carrying a full date/timestamp
            const string factBlock = "Issued 2026-08-20 14:37 station-local.";
            const string copy = "Just 1 more track before the break.";

            var result = CopyClaims.CheckFacts(copy, factBlock);

            // Then  "1" is never its own digit-run token — it only ever appears embedded inside "14"
            //       — so whole-token matching reports it rather than falsely passing it
            Assert.Contains(result.Violations, v => v.Class == ClaimClass.DigitRun && v.Token == "1");
        }
    }

    public static class ScenarioMultiWordConditionPhrase
    {
        [Fact]
        public static void A_condition_word_inside_a_multiword_phrase_still_matches()
        {
            // Given facts naming only the single word "cloudy" (no compound-phrase entry exists)
            const string factBlock = "Forecast: cloudy, light breeze.";
            // When  copy uses it inside a larger phrase
            const string copy = "Skies look partly cloudy this afternoon.";

            var result = CopyClaims.CheckFacts(copy, factBlock);

            // Then  extraction is word-by-word, so "cloudy" alone still matches
            Assert.Empty(result.Violations);
        }
    }
}

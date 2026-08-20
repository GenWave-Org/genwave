// STORY-351 — Patter can't lie about the clock (SPEC F138.3, F138.5 · PLAN T329/T332)
//
// BDD specification — xUnit. PENDING until built (see Story350's header note).
//
// The gh-#438 aired exhibit is the pinned regression: "We're diving into a neon dusk on
// this Saturday morning... Tonight we flip..." aired at Sunday 11:50 AM while the F117
// clock line named the correct instant in the prompt. The model isn't missing the
// information; it ignores it — so the check is mechanical, on EVERY patter kind.

using GenWave.Tts;
using Xunit;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureClockClaimsGate
{
    public static class ScenarioConsistentClaimsPass
    {
        // Given a clock line of Sunday 11:50 AM
        static readonly DateTimeOffset Clock = new(2026, 8, 16, 11, 0, 0, TimeSpan.Zero);

        [Fact]
        public static void A_matching_weekday_claim_passes()
        {
            const string copy = "Happy Sunday to everyone tuning in.";

            var result = CopyClaims.CheckClock(copy, Clock);

            // Then  copy naming Sunday passes
            Assert.Empty(result.Violations);
        }

        [Fact]
        public static void A_matching_daypart_claim_passes()
        {
            const string copy = "It's morning here in the studio.";

            var result = CopyClaims.CheckClock(copy, Clock);

            // Then  'it's morning' at 11:00 AM (the 05-11 morning window) passes
            Assert.Empty(result.Violations);
        }
    }

    public static class ScenarioClockLiesAreCaught
    {
        [Fact(Skip = "pending T332 — check not wired across patter kinds yet")]
        public static void A_wrong_weekday_in_a_lead_in_is_rejected() =>
            Assert.Fail("pending T332: 'this Saturday morning' against a Sunday clock line rejects with the weekday violation");

        [Fact(Skip = "pending T332")]
        public static void A_wrong_daypart_is_rejected() =>
            Assert.Fail("pending T332: 'Tonight' at 11:50 AM rejects with the daypart violation");

        [Fact(Skip = "pending T332")]
        public static void A_back_announce_is_checked_like_a_lead_in() =>
            Assert.Fail("pending T332: the gate applies to every LLM patter kind, not the context lane alone");
    }

    public static class SadPathExemptionsHold
    {
        [Fact]
        public static void A_track_title_naming_a_day_is_exempt()
        {
            // Given a track title that is itself a present-frame-marked weekday mention
            const string copy = "Coming up next, it's Saturday Night Fever.";

            // When  checked against a Sunday clock, with that title supplied
            var result = CopyClaims.CheckClock(copy, new DateTimeOffset(2026, 8, 16, 11, 0, 0, TimeSpan.Zero), trackTitle: "Saturday Night Fever");

            // Then  "it's Saturday" is a present-frame marker match that would otherwise violate
            //       (Saturday != Sunday), but it falls entirely inside the exempt title span, so
            //       the title mention never becomes a claim
            Assert.Empty(result.Violations);
        }

        [Fact]
        public static void Copy_with_no_clock_claims_records_zero_rejections()
        {
            const string copy = "That was a great track, right off a classic album.";

            var result = CopyClaims.CheckClock(copy, new DateTimeOffset(2026, 8, 19, 15, 0, 0, TimeSpan.Zero));

            // Then  claim-free copy passes with no violations recorded
            Assert.Empty(result.Violations);
        }
    }

    // Further pure-level pins (PLAN T329) — the hour->daypart boundary CheckClock derives its
    // "expected" value from (SPEC F138.3), and a genuine mismatch's own shape (Expected carries the
    // fix), pinned at the pure-checker level ahead of T332's wiring-level equivalents above.
    public static class ScenarioDaypartBoundaries
    {
        [Fact]
        public static void Hour_four_is_still_night()
        {
            // Given a present-frame-marked "night" claim
            const string copy = "It's night out there.";

            // When  checked at Monday 04:00 (the small-hours edge of the night window)
            var result = CopyClaims.CheckClock(copy, new DateTimeOffset(2026, 8, 17, 4, 0, 0, TimeSpan.Zero));

            // Then  04:00 is still inside night's own 21:00-04:59 window, so it passes
            Assert.Empty(result.Violations);
        }

        [Fact]
        public static void Hour_five_becomes_morning()
        {
            // Given a present-frame-marked "morning" claim
            const string copy = "Good morning, early risers.";

            // When  checked at Monday 05:00 (the first hour of the morning window)
            var result = CopyClaims.CheckClock(copy, new DateTimeOffset(2026, 8, 17, 5, 0, 0, TimeSpan.Zero));

            // Then  05:00 is inside morning's own 05:00-11:59 window, so it passes
            Assert.Empty(result.Violations);
        }

        [Fact]
        public static void A_night_claim_at_a_morning_hour_is_rejected_naming_the_correct_daypart()
        {
            const string copy = "Good night, everybody.";

            var result = CopyClaims.CheckClock(copy, new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero));

            // Then  09:00 falls in no window "night" names (21:00-04:59), so this is a genuine
            //       violation naming the hour's own correct daypart
            Assert.Contains(result.Violations,
                v => v.Class == ClaimClass.Daypart && v.Token == "night" && v.Expected == "morning");
        }
    }

    public static class ScenarioWeekdayMismatch
    {
        [Fact]
        public static void A_wrong_weekday_claim_names_the_correct_weekday_as_expected()
        {
            const string copy = "This Saturday has been a wild ride.";

            var result = CopyClaims.CheckClock(copy, new DateTimeOffset(2026, 8, 16, 11, 0, 0, TimeSpan.Zero));

            Assert.Contains(result.Violations,
                v => v.Class == ClaimClass.Weekday && v.Token == "Saturday" && v.Expected == "Sunday");
        }
    }

    // The full T329 review round 1 acceptance set for the amended F138.3 present-frame rule — every
    // line here is realistic DJ patter shape, not a synthetic probe (the finding: bare-token matching
    // rejected 5/10 of exactly these lines, all correct copy). Each PASS below would have wrongly
    // violated under the pre-amendment bare-token rule; each VIOLATE still correctly fires under the
    // narrowed one — both gh-#438 aired exhibits are among the VIOLATEs.
    public static class ScenarioRealisticPatterAcceptanceSet
    {
        static readonly DateTimeOffset Sun11 = new(2026, 8, 16, 11, 0, 0, TimeSpan.Zero);
        static readonly DateTimeOffset Mon9 = new(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);
        static readonly DateTimeOffset Mon21 = new(2026, 8, 17, 21, 0, 0, TimeSpan.Zero);
        static readonly DateTimeOffset Sat21 = new(2026, 8, 15, 21, 0, 0, TimeSpan.Zero);

        [Fact]
        public static void Anticipation_of_a_future_weekday_passes()
        {
            // "next {weekday}" is anticipation, never present-frame
            var result = CopyClaims.CheckClock("Join us next Friday for the countdown.", Sun11);

            Assert.Empty(result.Violations);
        }

        [Fact]
        public static void Recall_of_a_past_weekday_with_a_possessive_passes()
        {
            // "last {weekday}'s" is recall, and the possessive besides
            var result = CopyClaims.CheckClock("Last Saturday's show was a wild ride.", Sun11);

            Assert.Empty(result.Violations);
        }

        [Fact]
        public static void A_bare_daypart_mention_with_no_greeting_marker_passes()
        {
            // "coming up tonight" — no greeting/copula marker precedes "tonight"
            var result = CopyClaims.CheckClock("Coming up tonight: two hours of soul.", Mon9);

            Assert.Empty(result.Violations);
        }

        [Fact]
        public static void Recall_using_this_daypart_is_not_a_present_frame_claim()
        {
            // "this morning" is recall of an earlier hour, not a claim about the current one — the
            // whole reason "this {daypart}" was deliberately excluded from the daypart marker set
            var result = CopyClaims.CheckClock("We opened this morning with a classic.", Mon21);

            Assert.Empty(result.Violations);
        }

        [Fact]
        public static void A_greeting_daypart_within_its_overlapping_window_passes()
        {
            // "good evening" at 21:00 — inside evening's own 17:00-22:59 window, not a lie just
            // because 21:00 also falls in night's window
            var result = CopyClaims.CheckClock("Good evening and welcome in.", Sat21);

            Assert.Empty(result.Violations);
        }

        [Fact]
        public static void Tomorrow_prefixed_daypart_is_not_a_present_frame_claim()
        {
            // "tomorrow morning" — no greeting/copula marker precedes "morning"
            var result = CopyClaims.CheckClock("Tomorrow morning we do it all again.", Sat21);

            Assert.Empty(result.Violations);
        }

        [Fact]
        public static void Every_prefixed_weekday_is_never_a_claim()
        {
            // "every Saturday" — a recurring reference, explicitly outside the marker set
            var result = CopyClaims.CheckClock("That track owned every Saturday night in 1978.", Sun11);

            Assert.Empty(result.Violations);
        }

        [Fact]
        public static void On_a_prefixed_weekday_is_never_a_claim()
        {
            // "on a Tuesday" — a generic, non-present reference, explicitly outside the marker set
            var result = CopyClaims.CheckClock("This one topped the charts on a Tuesday back in 1983.", Sun11);

            Assert.Empty(result.Violations);
        }

        [Fact]
        public static void This_weekday_still_violates_under_the_narrowed_rule()
        {
            // "this Saturday" IS in the marker set — a gh-#438 aired exhibit, still caught
            var result = CopyClaims.CheckClock("We're diving into a neon dusk on this Saturday morning", Sun11);

            Assert.Contains(result.Violations, v => v.Class == ClaimClass.Weekday && v.Token == "Saturday");
        }

        [Fact]
        public static void Today_is_weekday_still_violates_under_the_narrowed_rule()
        {
            // "today is {weekday}" IS in the marker set — the other gh-#438-family exhibit shape
            var result = CopyClaims.CheckClock("Today is saturday", Sun11);

            Assert.Contains(result.Violations, v => v.Class == ClaimClass.Weekday && v.Token == "saturday");
        }
    }

    // T329 review round 3 regression pin: a curly apostrophe (U+2019, RIGHT SINGLE QUOTATION MARK)
    // must mark a present-frame "it's" claim exactly like the straight one (U+0027) does.
    // SpeechText's own curly->straight fold runs AFTER this checker by design (this checker sees
    // LlmCopyWriter's POST-hygiene, PRE-Normalize text), and LlmCopyWriter already treats U+2019 as
    // an apostrophe elsewhere, so a model emitting "It’s Saturday" reaches this checker with the
    // curly form intact — the marker regex must recognize it, not silently wave the claim through.
    public static class ScenarioCurlyApostropheMarksAClaimToo
    {
        [Fact]
        public static void A_curly_apostrophe_its_weekday_marker_still_violates()
        {
            const string copy = "It\u2019s Saturday, folks.";

            var result = CopyClaims.CheckClock(copy, new DateTimeOffset(2026, 8, 16, 11, 0, 0, TimeSpan.Zero));

            // Then  the curly-quoted "It\u2019s Saturday" marks a present-frame weekday claim exactly
            //       like "It's Saturday" would, and Saturday != Sunday still violates
            Assert.Contains(result.Violations, v => v.Class == ClaimClass.Weekday && v.Token == "Saturday");
        }
    }

    // T329 review round 3 advisory: ClaimVocabulary encodes the hour->daypart boundaries twice —
    // CategoryForHour's own non-overlapping partition (used only to fill ClaimViolation.Expected)
    // and HourIsInCategory's overlapping windows (used for the actual pass/fail decision). This pin
    // holds the two in agreement across every hour of the day, so an edit to one boundary set and
    // not the other fails loudly here rather than drifting silently apart.
    public static class ScenarioHourCategoryAgreement
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(9)]
        [InlineData(10)]
        [InlineData(11)]
        [InlineData(12)]
        [InlineData(13)]
        [InlineData(14)]
        [InlineData(15)]
        [InlineData(16)]
        [InlineData(17)]
        [InlineData(18)]
        [InlineData(19)]
        [InlineData(20)]
        [InlineData(21)]
        [InlineData(22)]
        [InlineData(23)]
        public static void Every_hours_own_canonical_category_is_inside_its_own_overlapping_window(int hour)
        {
            // Given  the hour's single canonical category (the one Expected would carry on a mismatch)
            var category = ClaimVocabulary.CategoryForHour(hour);

            // Then  that same category's own overlapping window always includes the hour it was
            //       derived from — the two structures can never disagree about this hour
            Assert.True(ClaimVocabulary.HourIsInCategory(category, hour));
        }
    }
}

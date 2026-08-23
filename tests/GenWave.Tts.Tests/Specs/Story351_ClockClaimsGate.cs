// STORY-351 — Patter can't lie about the clock (SPEC F138.3, F138.5 · PLAN T329/T332)
//
// BDD specification — xUnit. The pure-checker-level pins below (PLAN T329) were built first;
// PLAN T332 wires CopyClaims.CheckClock into the real LlmCopyWriter seam for every LLM patter
// kind, so ScenarioClockLiesAreCaught and everything below it drive the REAL writer, not the
// pure checker in isolation — see Story350's own BuildWriter idiom, mirrored here.
//
// The gh-#438 aired exhibit is the pinned regression: "We're diving into a neon dusk on
// this Saturday morning... Tonight we flip..." aired at Sunday 11:50 AM while the F117
// clock line named the correct instant in the prompt. The model isn't missing the
// information; it ignores it — so the check is mechanical, on EVERY patter kind.

using System.Net;
using System.Text;
using System.Text.Json;
using GenWave.Core.Domain;
using GenWave.Tts;
using GenWave.Tts.Tests.Fakes;
using Xunit;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureClockClaimsGate
{
    // ── Shared fixture for the HTTP-driven wiring scenarios below (PLAN T332) — mirrors
    // Story350_ContextFactGate's own BuildWriter idiom: drives the REAL LlmCopyWriter through a
    // scripted FakeHttpMessageHandler rather than asserting on CopyClaims in isolation, so the
    // T332 wiring at the LlmCopyWriter seam itself is what is under test, for kinds beyond
    // ContextSegment. Every wiring scenario shares ONE clock — Sunday, 11:50 AM station-local —
    // the exact instant the gh-#438 aired exhibit was rejected against, so "Saturday" and
    // "tonight" are both known, assertable violations rather than whatever day the machine
    // running the test happens to land on.

    static readonly DateTimeOffset FixedStationLocalNow = new(2026, 8, 16, 11, 50, 0, TimeSpan.Zero);

    static SegmentRequest LeadInRequest(string trackTitle = "Astral Plane") =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", trackTitle, default, "Valerie June"),
            FixedStationLocalNow, "test-station");

    static SegmentRequest BackAnnounceRequest() =>
        new(SegmentKind.BackAnnounce, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            FixedStationLocalNow, "test-station");

    /// <param name="facts">
    /// SPEC F107.3 fact block, or <see langword="null"/> for a factless ContextSegment request — the
    /// ONLY shape that reaches this method from PersonaController.Preview (SPEC F138.2's structural
    /// exemption is for the FACTS half alone, review round-2 finding F1 — see
    /// LlmCopyWriter.RequestCleanedCompletionAsync's own remarks; the clock half still applies).
    /// </param>
    static SegmentRequest ContextRequest(string? facts) =>
        new(SegmentKind.ContextSegment, "af_heart", "GenWave", Track: null, FixedStationLocalNow, "test-station",
            PersonaName: null, CounterpartName: null, ContextFacts: facts);

    /// <summary>Builds a REAL <see cref="LlmCopyWriter"/> against a fake completions handler that
    /// scripts its reply BY CALL NUMBER (1-based) — see Story350_ContextFactGate's own BuildWriter
    /// for the full idiom this mirrors. <see cref="CapturingLogger{T}"/> rides along (review round-2
    /// finding F4) so a fact can pin the exact WARN wording <see cref="LlmCopyWriter"/> produces on an
    /// exhausted ladder, not just the airable outcome.</summary>
    static (LlmCopyWriter Writer, List<string> RequestBodies, CapturingLogger<LlmCopyWriter> Logger) BuildWriter(
        Func<int, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        var bodies = new List<string>();
        var handler = new FakeHttpMessageHandler(async (request, ct) =>
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            bodies.Add(body);
            return await respond(bodies.Count, ct);
        });
        var ring = new LlmCallRing(new TestOptionsMonitor<LlmOptions>(new LlmOptions()));
        var logger = new CapturingLogger<LlmCopyWriter>();
        var writer = new LlmCopyWriter(
            new TemplateCopyWriter(new PatterTemplateRenderer()),
            new SingleHandlerHttpClientFactory(handler),
            new TestOptionsMonitor<LlmOptions>(new LlmOptions
            {
                Endpoint = "http://fake-llm.local", Model = "test-model", TimeoutSeconds = 5, MaxCopyChars = 450,
            }),
            new LlmCopyStatusHolder(),
            new FakeActivePersonaAccessor(),
            logger,
            TimeProvider.System,
            new LlmCallRecorder(ring, new LlmCallCauseCounters(TimeProvider.System)),
            new FakeDegradationModeReader(),
            new FakeStationClockProvider(FixedStationLocalNow));
        return (writer, bodies, logger);
    }

    static Task<HttpResponseMessage> Ok(string content) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = CompletionsBody(content),
    });

    static StringContent CompletionsBody(string content) => new(
        JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } }),
        Encoding.UTF8, "application/json");

    static string ExtractUserContent(string requestBodyJson)
    {
        using var doc = JsonDocument.Parse(requestBodyJson);
        foreach (var message in doc.RootElement.GetProperty("messages").EnumerateArray())
        {
            if (message.GetProperty("role").GetString() == "user")
                return message.GetProperty("content").GetString() ?? "";
        }

        return "";
    }

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
        // Wrong-weekday and wrong-daypart replies, both under a present-frame marker (SPEC
        // F138.3's own closed marker set) so they are genuine claims, not displaced/recall
        // mentions the checker deliberately lets pass. Sunday 11:50 AM (FixedStationLocalNow)
        // makes "this Saturday" a weekday violation and "it's tonight" a daypart violation
        // (tonight's own "night" category window is 21:00-04:59, nowhere near 11:00).
        const string WrongWeekdayCopy = "This Saturday has been one for the books so let's keep it going.";
        const string WrongDaypartCopy = "It's tonight and this one is going to hit just right.";
        const string CleanReply = "Coming up next a classic from the vault.";

        [Fact]
        public static async Task A_wrong_weekday_in_a_lead_in_is_rejected()
        {
            // Given a first lead-in reply asserting the wrong weekday and a clean second reply,
            // driven through the real production LlmCopyWriter seam
            var (writer, bodies, _) = BuildWriter((call, _) => Ok(call == 1 ? WrongWeekdayCopy : CleanReply));

            // When the render goes through WriteAsync -> RequestCleanedCompletionAsync
            var result = await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            // Then the clean re-ask airs, and exactly one re-ask fired — never the wrong-weekday text
            Assert.Equal(CleanReply, result.Text);
            Assert.True(result.FreshPerAiring);
            Assert.Equal(2, bodies.Count);
        }

        [Fact]
        public static async Task A_wrong_daypart_is_rejected()
        {
            // Given a first lead-in reply asserting the wrong daypart and a clean second reply
            var (writer, bodies, _) = BuildWriter((call, _) => Ok(call == 1 ? WrongDaypartCopy : CleanReply));

            var result = await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            Assert.Equal(CleanReply, result.Text);
            Assert.True(result.FreshPerAiring);
            Assert.Equal(2, bodies.Count);
        }

        [Fact]
        public static async Task A_back_announce_is_checked_like_a_lead_in()
        {
            // Given the SAME wrong-weekday shape, but for BackAnnounce instead of LeadIn — the
            // gate applies to every LLM patter kind (F138.3), not the context lane or LeadIn alone
            var (writer, bodies, _) = BuildWriter((call, _) => Ok(call == 1 ? WrongWeekdayCopy : CleanReply));

            var result = await writer.WriteAsync(BackAnnounceRequest(), CancellationToken.None);

            Assert.Equal(CleanReply, result.Text);
            Assert.True(result.FreshPerAiring);
            Assert.Equal(2, bodies.Count);
        }
    }

    // The truth-gate ladder is reachable from WritePreviewAsync too (review round-2 findings F1-F3,
    // PLAN T332) — RequestCleanedCompletionAsync is the ONE seam both WriteAsync and
    // WritePreviewAsync call, and CheckTruthGate/RunTruthGateLadderAsync gate on request.Kind alone,
    // never on which caller reached them. These facts pin that reachability directly rather than
    // leaving it as an inference from the production seam's own doc comments. They live HERE, not in
    // Story123_PersonaPreviewWriter, because they are specifically about the T332 ladder's OWN
    // preview reachability (a brand-new code path as of this task) — Story123 already owns the
    // broader, pre-existing "preview never templates" contract (SPEC F35.6) and has no reason to grow
    // clock/fact-claim fixtures of its own. Reuses this file's own call-scripted BuildWriter fixture
    // rather than Story123's MockCompletionsServer idiom, since a poisoned-then-clean re-ask needs a
    // reply that differs BY CALL NUMBER — exactly what BuildWriter already scripts and
    // MockCompletionsServer's single mutable ReplyContent field does not.
    public static class ScenarioPreviewReachesTheLadderToo
    {
        const string WrongWeekdayCopy = "This Saturday has been one for the books so let's keep it going.";
        const string CleanReply = "Coming up next a classic from the vault.";

        [Fact]
        public static async Task A_poisoned_lead_in_preview_reasks_once_and_returns_the_clean_text()
        {
            // Given a first preview reply asserting the wrong weekday and a clean second reply
            var (writer, bodies, _) = BuildWriter((call, _) => Ok(call == 1 ? WrongWeekdayCopy : CleanReply));

            // When the preview goes through WritePreviewAsync -> RequestCleanedCompletionAsync
            var result = await writer.WritePreviewAsync(LeadInRequest(), personaOverride: null, CancellationToken.None);

            // Then the clean re-ask airs as a Success, exactly one re-ask fired — the SAME ladder
            // WriteAsync exercises, reachable from the preview seam too
            var success = Assert.IsType<PersonaPreviewResult.Success>(result);
            Assert.Equal(CleanReply, success.Text);
            Assert.Equal(2, bodies.Count);
        }

        [Fact]
        public static async Task An_exhausted_ladder_preview_names_the_truth_gate_not_empty_or_over_length()
        {
            // Given BOTH the first reply AND the re-ask asserting the wrong weekday
            var (writer, bodies, _) = BuildWriter((_, _) => Ok(WrongWeekdayCopy));

            // When the preview exhausts the ladder
            var result = await writer.WritePreviewAsync(LeadInRequest(), personaOverride: null, CancellationToken.None);

            // Then Failed.Detail names the truth gate (review round-2 finding F2 — DescribeNullTextReason
            // reused here), never the wrong-lever hygiene wording a preview used to report
            // unconditionally for ANY null TextOf result before this fix
            var failed = Assert.IsType<PersonaPreviewResult.Failed>(result);
            Assert.Contains("truth gate", failed.Detail, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("empty or over-length", failed.Detail, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, bodies.Count);
        }

        [Fact]
        public static async Task A_factless_context_segment_preview_is_still_clock_checked()
        {
            // Given a ContextSegment preview with NO fact block at all — the ONLY shape that ever
            // reaches this seam with ContextFacts null (PersonaController.Preview never supplies
            // one; the AIR path never builds a factless ContextSegment request at all —
            // Orchestrator.BuildContextSegmentRequestAsync's own blank-facts guard) — and a first
            // reply asserting the wrong weekday, which a Kind-based whole-gate exemption (deleted,
            // review round-2 finding F1) used to let straight through unchecked
            var (writer, bodies, _) = BuildWriter((call, _) => Ok(call == 1 ? WrongWeekdayCopy : CleanReply));

            // When the preview renders
            var result = await writer.WritePreviewAsync(
                ContextRequest(facts: null), personaOverride: null, CancellationToken.None);

            // Then the clock half still gates it — one re-ask, clean text airs — proving the FACTS
            // half's own "never even ask" (CheckTruthGate's factBlock-is-null guard) is scoped to the
            // facts half alone, never the whole gate
            var success = Assert.IsType<PersonaPreviewResult.Success>(result);
            Assert.Equal(CleanReply, success.Text);
            Assert.Equal(2, bodies.Count);
        }
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

        [Fact]
        public static void An_owner_message_naming_a_day_is_exempt()
        {
            // Given the owner's own announcement message names a present-frame weekday
            const string message = "Bake sale this Saturday at the community hall.";
            const string copy = "Hey neighbors, quick note: Bake sale this Saturday at the community hall. See you there!";

            // When  checked against a Wednesday clock, with that message supplied as the
            //       owner-trusted core (HIGH-2, PLAN T342 round 2)
            var result = CopyClaims.CheckClock(
                copy, new DateTimeOffset(2026, 8, 19, 15, 0, 0, TimeSpan.Zero), ownerMessage: message);

            // Then  "this Saturday" falls entirely inside the owner's own literal message text, so
            //       it never becomes a claim — the owner wrote those words, not the model
            Assert.Empty(result.Violations);
        }

        [Fact]
        public static void An_llm_added_day_claim_outside_the_message_still_rejects()
        {
            // Given the owner's message names no day at all
            const string message = "The garage sale starts at nine.";
            // And the model adds a weekday claim OUTSIDE any quote of that message
            const string copy = "Quick note from the station: The garage sale starts at nine. It's Saturday, folks!";

            // When  checked against a Wednesday clock, with the message supplied as the owner-trusted core
            var result = CopyClaims.CheckClock(
                copy, new DateTimeOffset(2026, 8, 19, 15, 0, 0, TimeSpan.Zero), ownerMessage: message);

            // Then  the LLM-added "It's Saturday" falls outside the message's own literal span, so
            //       the owner-trust exemption never reaches it — it still violates
            var violation = Assert.Single(result.Violations);
            Assert.Equal(ClaimClass.Weekday, violation.Class);
            Assert.Equal("Saturday", violation.Token);
        }

        [Fact]
        public static void An_echoed_message_plus_the_models_own_same_weekday_claim_still_rejects()
        {
            // HIGH-A review finding (the dedupe-slot leak): the owner's message itself names a
            // present-frame weekday, AND the model appends its own separate present-frame claim of
            // the identical weekday elsewhere in the copy, OUTSIDE any quote of the message.
            const string message = "Bake sale this Saturday at the community hall.";
            const string copy = message + " And by the way it's Saturday today, folks!";

            // When  checked against a Wednesday clock, with the message supplied as the owner-trusted core
            var result = CopyClaims.CheckClock(
                copy, new DateTimeOffset(2026, 8, 19, 15, 0, 0, TimeSpan.Zero), ownerMessage: message);

            // Then  the message's own EXEMPT "this Saturday" must never consume the dedupe slot for
            //       "Saturday" and so suppress the model's LATER, genuine, non-exempt "it's Saturday"
            //       — exactly one violation rides the ladder, not zero
            var violation = Assert.Single(result.Violations);
            Assert.Equal(ClaimClass.Weekday, violation.Class);
            Assert.Equal("Saturday", violation.Token);
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

    // The gh-#438 aired exhibit, end to end, through the real production LlmCopyWriter seam (PLAN
    // T332) — not the pure-checker-level pin ScenarioRealisticPatterAcceptanceSet already holds.
    public static class ScenarioGh438ExhibitEndToEnd
    {
        // The exhibit's own two lines, unchanged: "this Saturday morning" still violates under the
        // amended present-frame rule (a weekday claim); the bare "Tonight we flip" mention does not
        // (no greeting/copula marker precedes it — ScenarioRealisticPatterAcceptanceSet's own
        // A_bare_daypart_mention_with_no_greeting_marker_passes pins that half separately), so this
        // exhibit's ladder trip is the weekday claim alone.
        const string PoisonedExhibit =
            "We're diving into a neon dusk on this Saturday morning. Tonight we flip the switch and keep it going.";
        const string CleanReply =
            "We're diving into a neon dusk this evening. Let's flip the switch and keep it going.";

        [Fact]
        public static async Task The_pinned_exhibit_recovers_through_the_reask()
        {
            // Given the gh-#438 exhibit's own poisoned first reply, aired at Sunday 11:50 AM while
            // the F117 clock line named the correct instant, and a clean second reply
            var (writer, bodies, _) = BuildWriter((call, _) => Ok(call == 1 ? PoisonedExhibit : CleanReply));

            // When the render goes through the real ladder end to end
            var result = await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            // Then the clean re-ask airs — genuinely LLM-authored — never the exhibit's own
            // invented "this Saturday morning"
            Assert.Equal(CleanReply, result.Text);
            Assert.True(result.FreshPerAiring);
            Assert.Equal(2, bodies.Count);
        }
    }

    // The composite check (SPEC F138.2 + F138.3, PLAN T332): a ContextSegment reply violating BOTH
    // claim families gets exactly ONE re-ask naming both — never two chained ladder runs (the T331
    // reviewer ruling; see LlmCopyWriter.CheckTruthGate's own remarks).
    public static class ScenarioCompositeContextChecksBothFamilies
    {
        const string FactBlock = "Edmonton: overcast, 15°C. Today's high 21°C, low 12°C.";

        // gh-#434's own exhibit shape, unchanged: a digit run ("6") and a condition word
        // ("sunshine") the fact block never supports, PLUS — at this file's Sunday clock — "today
        // is saturday" is now ALSO a clock violation, not merely a fact-block one: the SAME token
        // trips BOTH CheckFacts (no weekday anywhere in the fact block) and CheckClock (the actual
        // day is Sunday, not Saturday).
        const string PoisonedCopy =
            "It feels like 6 degrees below freezing with plenty of sunshine and today is saturday here in the studio.";
        const string CleanReply = "It's overcast today at 15 degrees with a high of 21 and a low of 12.";

        [Fact]
        public static async Task A_context_reply_violating_both_families_gets_one_reask_naming_both()
        {
            // Given the composite poisoned reply and a clean second reply
            var (writer, bodies, _) = BuildWriter((call, _) => Ok(call == 1 ? PoisonedCopy : CleanReply));

            // When the render goes through the real ladder end to end
            var result = await writer.WriteAsync(ContextRequest(FactBlock), CancellationToken.None);

            // Then the clean re-ask airs, and exactly ONE re-ask fired for BOTH claim families —
            // never a second, chained ladder run
            Assert.Equal(CleanReply, result.Text);
            Assert.True(result.FreshPerAiring);
            Assert.Equal(2, bodies.Count);

            // And that single re-ask's own prompt names a violation from EACH family: the facts
            // half ("sunshine", never in the fact block) and the clock half (the correct weekday
            // named as the FIX, "actually Sunday" — the clock-violation clause shape,
            // LlmPromptBuilder.DescribeViolationForReask's own Expected-is-set branch, distinct
            // from a bare "Sunday" mention, which the ambient F71.8 clock line
            // (LlmPromptBuilder.BuildStationClockLine) would already put in every prompt regardless
            // of any violation at all) — proof the two checks composed into one gate/re-ask cycle
            // rather than needing two.
            var reaskPrompt = ExtractUserContent(bodies[1]);
            Assert.Contains("sunshine", reaskPrompt, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("actually Sunday", reaskPrompt, StringComparison.Ordinal);
        }
    }

    // The track-title exemption (SPEC F138.3), through the real writer rather than CopyClaims in
    // isolation (SadPathExemptionsHold above already pins the pure-checker level).
    public static class SadPathTrackTitleExemptionThroughTheRealWriter
    {
        const string TrackTitleMention = "Coming up next, it's Saturday Night Fever.";

        [Fact]
        public static async Task A_lead_in_naming_its_own_track_title_is_not_rejected()
        {
            // Given a lead-in whose track IS "Saturday Night Fever", and a reply that names it
            // under a present-frame marker ("it's Saturday Night Fever") — a claim that would
            // otherwise violate this file's Sunday clock
            var (writer, bodies, _) = BuildWriter((_, _) => Ok(TrackTitleMention));

            // When it renders through the real writer
            var result = await writer.WriteAsync(LeadInRequest(trackTitle: "Saturday Night Fever"), CancellationToken.None);

            // Then the title mention is exempt — the copy airs on the FIRST call, no re-ask ever fired
            Assert.Equal(TrackTitleMention, result.Text);
            Assert.True(result.FreshPerAiring);
            Assert.Single(bodies);
        }
    }

    // The per-kind floor (PLAN T332 investigation): LeadIn/BackAnnounce have no F107.6-style
    // skip-never-silence guard the way ContextSegment/SignOff/SignOn do (TtsSegmentSource's own
    // non-fresh-copy guard names only those three) — a still-violating re-ask degrades to
    // PatterTemplateRenderer's deterministic template instead, and that template DOES reach air.
    public static class SadPathReaskStillViolatingLandsOnTheTemplate
    {
        const string WrongWeekdayCopy = "This Saturday has been one for the books so let's keep it going.";

        [Fact]
        public static async Task A_lead_in_whose_reask_still_violates_lands_on_the_template()
        {
            // Given BOTH the first reply AND the re-ask asserting the wrong weekday
            var (writer, bodies, logger) = BuildWriter((_, _) => Ok(WrongWeekdayCopy));

            // When the render exhausts the ladder
            var result = await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            // Then it degrades to the LeadIn template floor (PatterTemplateRenderer.Expand's own
            // arm, which renders fixed prose with no weekday/daypart word in it for THIS request —
            // it interpolates the track's own title/artist verbatim, so it is the template's fixed
            // wording, not a guarantee about arbitrary track metadata, that the floor actually relies
            // on) — never the still-violating LLM text, and never silence either: unlike
            // ContextSegment/SignOff/SignOn, this template DOES reach air for LeadIn. Still exactly
            // one re-ask, never a retry storm.
            Assert.Equal("Coming up: Astral Plane by Valerie June.", result.Text);
            Assert.False(result.FreshPerAiring);
            Assert.Equal(2, bodies.Count);

            // And the failure WARN names this as a WRONG-DAY claim, never an "unsupported claim"
            // (review round-2 finding F4 — LlmCopyWriter.DescribeViolationForLog's three-way split
            // was unpinned: a clock violation carries ClaimViolation.Expected, a fact-block violation
            // never does, and only THIS fact proves the Expected-set branch actually fires rather
            // than every violation reading as a generic "unsupported claim").
            Assert.Contains(
                logger.Warnings, warning => warning.Contains("wrong-day claim", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                logger.Warnings, warning => warning.Contains("unsupported claim", StringComparison.OrdinalIgnoreCase));
        }
    }
}

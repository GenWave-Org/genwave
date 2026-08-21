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

using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using GenWave.Core.Domain;
using GenWave.Tts;
using GenWave.Tts.Tests.Fakes;
using Xunit;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureContextFactGate
{
    // ── Shared fixture for the HTTP-driven ladder scenarios below (mirrors GenWave.Host.Tests'
    // own Story353 BuildWriter idiom — the ONE constructor arg list every fact in
    // ScenarioTheLadderDegrades/ScenarioGh434ExhibitEndToEnd/ScenarioNonContextKindIsNeverGated/
    // ScenarioEmptyFactBlockNeverGates/SadPathCheckerDiscipline shares) — drives the REAL
    // LlmCopyWriter through a scripted FakeHttpMessageHandler rather than asserting on CopyClaims
    // in isolation, so the ladder wiring at the LlmCopyWriter seam itself is what is under test.

    const string GhFactBlock = "Edmonton: overcast, 15°C. Today's high 21°C, low 12°C.";

    // gh-#434's own aired exhibit, unchanged: three fabrications in one line, all three F138.1
    // claim classes at once — a digit run ("6"), a condition word ("sunshine"), and a weekday
    // ("saturday") — none of them supported by GhFactBlock.
    const string PoisonedCopy =
        "It feels like 6 degrees below freezing with plenty of sunshine and today is saturday here in the studio.";

    const string CleanCopy = "It's overcast today at 15 degrees with a high of 21 and a low of 12.";

    // 2026-08-15, station-local — a Saturday morning, so LlmPromptBuilder.BuildClockGuardLine's own
    // output is a known, assertable literal ("It is Saturday morning...") rather than whatever day
    // the machine running the test happens to land on.
    static readonly DateTimeOffset FixedStationLocalNow = new(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);

    static SegmentRequest ContextRequest(string? facts) =>
        new(SegmentKind.ContextSegment, "af_heart", "GenWave", Track: null, FixedStationLocalNow, "test-station",
            PersonaName: null, CounterpartName: null, ContextFacts: facts);

    static SegmentRequest LeadInRequest() =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            FixedStationLocalNow, "test-station");

    /// <summary>Builds a REAL <see cref="LlmCopyWriter"/> against a fake completions handler that
    /// scripts its reply BY CALL NUMBER (1-based) — <paramref name="respond"/> also sees each call's
    /// raw request body via the returned <c>RequestBodies</c> list, so a fact can inspect exactly
    /// what the re-ask's own prompt said. <see cref="FakeStationClockProvider"/> pins the station
    /// clock to <see cref="FixedStationLocalNow"/> so the F138.5 guard line is a known literal.
    /// <c>Logger</c> is a real <see cref="CapturingLogger{T}"/> a fact can inspect for the T331
    /// review finding F3 WARN pin, rather than a value every caller must construct and discard.</summary>
    static (LlmCopyWriter Writer, LlmCallRing Ring, List<string> RequestBodies, CapturingLogger<LlmCopyWriter> Logger) BuildWriter(
        Func<int, CancellationToken, Task<HttpResponseMessage>> respond, int timeoutSeconds = 5)
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
                Endpoint = "http://fake-llm.local", Model = "test-model", TimeoutSeconds = timeoutSeconds,
                MaxCopyChars = 450,
            }),
            new LlmCopyStatusHolder(),
            new FakeActivePersonaAccessor(),
            logger,
            TimeProvider.System,
            new LlmCallRecorder(ring, new LlmCallCauseCounters(TimeProvider.System)),
            new FakeDegradationModeReader(),
            new FakeStationClockProvider(FixedStationLocalNow));
        return (writer, ring, bodies, logger);
    }

    static Task<HttpResponseMessage> Ok(string content) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = CompletionsBody(content),
    });

    static async Task<HttpResponseMessage> DelayThenOk(TimeSpan delay, CancellationToken ct, string content = CleanCopy)
    {
        await Task.Delay(delay, ct);
        return await Ok(content);
    }

    static StringContent CompletionsBody(string content) => new(
        JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } }),
        Encoding.UTF8, "application/json");

    // Role-keyed, not positional (T331 review finding F6 — the Story119/121/123 precedent): looks
    // up the message BY its own "role" field rather than trusting messages[0]/messages[1] to stay
    // system-then-user forever.
    static string ExtractSystemContent(string requestBodyJson) => ExtractMessageContent(requestBodyJson, "system");

    static string ExtractUserContent(string requestBodyJson) => ExtractMessageContent(requestBodyJson, "user");

    static string ExtractMessageContent(string requestBodyJson, string role)
    {
        using var doc = JsonDocument.Parse(requestBodyJson);
        foreach (var message in doc.RootElement.GetProperty("messages").EnumerateArray())
        {
            if (message.GetProperty("role").GetString() == role)
                return message.GetProperty("content").GetString() ?? "";
        }

        return "";
    }

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
        [Fact]
        public static async Task Exactly_one_reask_is_issued()
        {
            // Given a first completion that fails the gate (the gh-#434 exhibit) and a second that
            // finally supports the facts — driven through the real production LlmCopyWriter seam
            var (writer, _, bodies, _) = BuildWriter((call, _) => Ok(call == 1 ? PoisonedCopy : CleanCopy));

            // When the render goes through WriteAsync -> RequestCleanedCompletionAsync
            await writer.WriteAsync(ContextRequest(GhFactBlock), CancellationToken.None);

            // Then exactly two completion calls were made — the rejected first, and ONE re-ask, never more
            Assert.Equal(2, bodies.Count);
        }

        [Fact]
        public static async Task The_reask_prompt_names_the_violating_claim()
        {
            // Given the same poisoned-then-clean pair
            var (writer, ring, bodies, _) = BuildWriter((call, _) => Ok(call == 1 ? PoisonedCopy : CleanCopy));

            // When the render resolves
            await writer.WriteAsync(ContextRequest(GhFactBlock), CancellationToken.None);

            // Then the SECOND call's own user prompt names one of the rejected claims — the retry
            // prompt contains the rejected claim text, not a bare "try again" — and it opens with
            // plain declarative English, never a machine-looking "Re-ask:" label a model could echo
            // straight back into its own reply (T331 review advisory F5).
            var reaskPrompt = ExtractUserContent(bodies[1]);
            Assert.Contains("sunshine", reaskPrompt, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Re-ask:", reaskPrompt, StringComparison.Ordinal);

            // And the RING's own re-ask entry (T331 review finding F4a) — not just the wire — carries
            // that same re-ask prompt: the newest ring record is the re-ask's own honest entry.
            var newest = ring.Snapshot()[0];
            Assert.Contains("sunshine", newest.PromptUser ?? "", StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public static async Task A_failing_reask_lands_on_the_f107_floor()
        {
            // Given a first AND second completion that both violate the facts
            var (writer, _, bodies, logger) = BuildWriter((_, _) => Ok(PoisonedCopy));

            // When the render exhausts the ladder
            var result = await writer.WriteAsync(ContextRequest(GhFactBlock), CancellationToken.None);

            // Then it degrades to the EXISTING context-lane floor (SPEC F107.6's skip-never-silence
            // posture) — the same template PatterTemplateRenderer already produces for a
            // ContextSegment writer that degraded for any other reason, never a new floor invented
            // for the gate — and never the still-violating LLM text. Still exactly one re-ask, never
            // a retry storm.
            Assert.Equal("Here's something worth knowing.", result.Text);
            Assert.False(result.FreshPerAiring);
            Assert.Equal(2, bodies.Count);

            // And the failure WARN names the REAL cause (T331 review finding F3, generalized wording
            // PLAN T332) — the truth gate, and the still-unsupported claim — never the wrong-lever
            // "empty or exceeded Llm:MaxCopyChars" wording a hygiene reject carries (that message
            // sends an operator at settings this failure has nothing to do with).
            Assert.Contains(
                logger.Warnings,
                warning => warning.Contains("truth gate", StringComparison.OrdinalIgnoreCase)
                    && warning.Contains("sunshine", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                logger.Warnings, warning => warning.Contains("empty or exceeded", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public static async Task The_guard_line_rides_the_prompt()
        {
            // Given an ordinary completion that never trips the gate at all
            var (writer, _, bodies, _) = BuildWriter((_, _) => Ok(CleanCopy));

            // When any patter prompt renders
            await writer.WriteAsync(ContextRequest(GhFactBlock), CancellationToken.None);

            // Then the system prompt carries the F138.5 guard line verbatim (weekday/daypart
            // substituted for the pinned station clock) — comma-free prompt hardening on every
            // render, gate or not.
            var systemPrompt = ExtractSystemContent(bodies[0]);
            Assert.Contains(LlmPromptBuilder.BuildClockGuardLine(FixedStationLocalNow), systemPrompt);
        }
    }

    public static class ScenarioGh434ExhibitEndToEnd
    {
        [Fact]
        public static async Task The_pinned_exhibit_recovers_through_the_reask()
        {
            // Given the gh-#434 aired exhibit's own poisoned first reply against the real fact
            // block, and a clean second reply
            var (writer, _, bodies, _) = BuildWriter((call, _) => Ok(call == 1 ? PoisonedCopy : CleanCopy));

            // When the render goes through the real ladder end to end
            var result = await writer.WriteAsync(ContextRequest(GhFactBlock), CancellationToken.None);

            // Then the clean re-ask airs — genuinely LLM-authored, never the invented first reply
            Assert.Equal(CleanCopy, result.Text);
            Assert.True(result.FreshPerAiring);
            Assert.Equal(2, bodies.Count);
        }

        [Fact]
        public static async Task Both_calls_failing_the_gate_still_lands_on_the_floor()
        {
            // Given the SAME exhibit poisoning both the first reply and the re-ask
            var (writer, ring, bodies, _) = BuildWriter((_, _) => Ok(PoisonedCopy));

            // When the render exhausts the ladder
            var result = await writer.WriteAsync(ContextRequest(GhFactBlock), CancellationToken.None);

            // Then the fabricated copy never airs (the F107.6 floor), and BOTH calls left their own
            // honest ring entry — the rejected first, and the re-ask that violated again — never one
            // entry standing in for two calls.
            Assert.False(result.FreshPerAiring);
            Assert.Equal(2, bodies.Count);
            Assert.Equal(2, ring.Snapshot().Count);
            Assert.All(ring.Snapshot(), record => Assert.Equal(LlmCallCause.TruthGateReject, record.Cause));
        }
    }

    public static class ScenarioNonContextKindIsNeverGated
    {
        [Fact]
        public static async Task A_lead_in_with_fabricated_claims_is_never_fact_checked()
        {
            // Given a LeadIn request (not a context segment) whose only reply fabricates a claim
            // that would trip CheckFacts if this kind were ever gated
            var (writer, _, bodies, _) = BuildWriter((_, _) => Ok(PoisonedCopy));

            // When it renders
            var result = await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            // Then the copy airs exactly as the model wrote it — F138.2 gates ContextSegment only,
            // so no re-ask is even attempted for any other kind (the scope pin).
            Assert.Equal(PoisonedCopy, result.Text);
            Assert.Single(bodies);

            // And the narrowing to ContextSegment-only is pinned on the REAL LeadIn call's own
            // system prompt (T331 review finding F2 — the reviewer's own mutation: narrowing
            // production to context-only survived every fact here because none of them ever looked
            // at what a non-gated kind's prompt actually carries) — the F138.5 guard line still rides
            // it regardless, since that line is unconditional across every LLM-authored kind.
            var systemPrompt = ExtractSystemContent(bodies[0]);
            Assert.Contains(LlmPromptBuilder.BuildClockGuardLine(FixedStationLocalNow), systemPrompt);
        }
    }

    public static class ScenarioEmptyFactBlockNeverGates
    {
        [Fact]
        public static async Task A_context_segment_with_no_fact_block_is_never_fact_checked()
        {
            // Given a ContextSegment request whose own ContextFacts is blank (an admin preview's
            // typical case, per LlmPromptBuilder.BuildContextFactsLine's own remarks) and a reply
            // fabricating a claim
            var (writer, _, bodies, _) = BuildWriter((_, _) => Ok(PoisonedCopy));

            // When it renders
            var result = await writer.WriteAsync(ContextRequest(facts: null), CancellationToken.None);

            // Then CheckFacts is never even invoked — an empty fact block skips the gate entirely,
            // so the copy airs unchecked with no re-ask.
            Assert.Equal(PoisonedCopy, result.Text);
            Assert.Single(bodies);
        }
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

        [Fact]
        public static async Task Budget_exhaustion_degrades_to_template_not_a_longer_hold()
        {
            // Given a first reply that BURNS MOST of this render's Llm:TimeoutSeconds budget before
            // violating the facts (T331 review finding F1 — an instantly-answering first call left
            // the shared budget entirely unconsumed, so this fact previously could not tell a
            // correctly-SHARED clock apart from a re-ask that wrongly got its own fresh one: both
            // shapes finish in about the same wall-clock time when call 1 is instant), and a re-ask
            // endpoint that would take 10s regardless of which clock ends up bounding it.
            var (writer, ring, bodies, _) = BuildWriter(
                (call, ct) => call == 1
                    ? DelayThenOk(TimeSpan.FromMilliseconds(1500), ct, PoisonedCopy)
                    : DelayThenOk(TimeSpan.FromSeconds(10), ct),
                timeoutSeconds: 2);
            var stopwatch = Stopwatch.StartNew();

            // When the render's own timeout budget elapses mid-reask — RequestCleanedCompletionAsync's
            // own timeoutCts, shared by BOTH calls, never a fresh clock for the re-ask
            var result = await writer.WriteAsync(ContextRequest(GhFactBlock), CancellationToken.None);
            stopwatch.Stop();

            // Then the render degrades to the template rung — never a longer feeder hold than this
            // render's own single 2s budget, ~1.5s of which the first call already spent, leaving
            // only ~0.5s for the re-ask before the SHARED clock fires. Sharing correctly lands at
            // ~2s total; the reviewer's own mutation (a fresh CreateLinkedTokenSource + CancelAfter
            // for the re-ask, starting its OWN 2s from ~1.5s in) would run to ~3.5s instead — the
            // bound is pinned at the MIDPOINT of the two (T331 pickup, PLAN T332: do NOT widen this
            // toward 3.5s, and do NOT change either delay above — both would weaken the discriminant),
            // so it reds under that mutation with room to spare on either side.
            Assert.Equal("Here's something worth knowing.", result.Text);
            Assert.False(result.FreshPerAiring);
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(2.75),
                $"took {stopwatch.Elapsed} - a re-ask given its OWN fresh timeout clock (not this " +
                "render's one shared budget) would run past 2.75s (the midpoint of the correctly-shared " +
                "~2s and the wrongly-fresh ~3.5s)");

            // And the ring shows exactly what happened: the rejected first call, then the re-ask's
            // own honest Timeout — TWO entries with TWO distinct dispatch times (T331 review finding
            // F4b: the re-ask's own ring entry must carry its OWN StartedAt, never the first call's —
            // a catch-all that reused the first call's timing would leave both entries stamped alike).
            Assert.Equal(2, ring.Snapshot().Count);
            var rejected = Assert.Single(ring.Snapshot(), record => record.Cause == LlmCallCause.TruthGateReject);
            var timedOut = Assert.Single(ring.Snapshot(), record => record.Cause == LlmCallCause.Timeout);
            Assert.NotEqual(rejected.StartedAt, timedOut.StartedAt);
        }
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

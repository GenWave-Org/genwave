// STORY-352 — Banter stays fictional, never false (SPEC F138.6, F127.4 as amended · PLAN T333)
//
// BDD specification — xUnit, LIVE as of T333. Drives the REAL CrosstalkScriptWriter through a
// scripted completions server (Story326_BoothWritesForTwo's own writer-harness idiom), never
// CopyClaims/CrosstalkScriptParser in isolation, so the actual T333 wiring is what is under test.
//
// The ruling (Dean, 2026-08-20): real-world verifiables are forbidden — frequency/call-sign
// shapes, dates, weather words, clock lies — and mechanically enforced through F127.4's
// fail-closed discard. Fictional lore (recurring characters, running gags, station
// mythology) is explicitly ALLOWED: invented characters are good radio. Real geography is
// prompt-clause-only — a checker cannot know real places from invented ones (F138.6 says
// so honestly instead of pretending).

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureBanterTruth
{
    // ── Shared fixtures ─────────────────────────────────────────────────────

    static readonly PersonaCard HostCard = MakeCard(
        "Neon Nightowl", "Neon Nightowl spins moody late-night sets deep into the small hours.");

    static readonly PersonaCard NeighborCard = MakeCard(
        "Daybreak Dana", "Daybreak Dana brings bright upbeat energy straight off the morning show.");

    // A fixed Monday noon (verified: 2026-08-17 is a Monday) — every clock-claim fixture below is
    // written against this ONE known instant, mirroring Story351_ClockClaimsGate's own
    // FixedStationLocalNow idiom, so "Friday" is a known, assertable violation rather than
    // whatever day the machine running the test happens to land on.
    static readonly DateTimeOffset FixedStationLocalNow = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    static readonly string FrequencyShapeReply = string.Join('\n', new[]
    {
        $"{CrosstalkScriptParser.HostTag}: Did you catch us at 98.7 FM last night.",
        $"{CrosstalkScriptParser.NeighborTag}: Every single time I tune in.",
        $"{CrosstalkScriptParser.HostTag}: That is the spirit right there.",
    });

    // The task-pinned edge (PLAN T333 review round 1, probe-proven F1): a real FM frequency spoken
    // as a bare INTEGER, no decimal point — real FM frequencies are commonly said this way, and a
    // decimal-required rule let this class of claim through as a false PASS (the exact F138.6 harm:
    // an airable fabricated broadcast fact).
    static readonly string IntegerFrequencyShapeReply = string.Join('\n', new[]
    {
        $"{CrosstalkScriptParser.HostTag}: Radio 101 FM keeps us on the air every day.",
        $"{CrosstalkScriptParser.NeighborTag}: That is the frequency that never lets me down.",
        $"{CrosstalkScriptParser.HostTag}: Long may it broadcast.",
    });

    static readonly string CallSignShapeReply = string.Join('\n', new[]
    {
        $"{CrosstalkScriptParser.HostTag}: Tune into KXRT for more of this energy.",
        $"{CrosstalkScriptParser.NeighborTag}: That station never lets me down.",
        $"{CrosstalkScriptParser.HostTag}: KXRT is the name everyone remembers.",
    });

    static readonly string WeatherShapeReply = string.Join('\n', new[]
    {
        $"{CrosstalkScriptParser.HostTag}: It is so sunny outside for the drive today.",
        $"{CrosstalkScriptParser.NeighborTag}: Perfect for a road trip playlist.",
        $"{CrosstalkScriptParser.HostTag}: Let us keep the good vibes rolling.",
    });

    static readonly string DateShapeReply = string.Join('\n', new[]
    {
        $"{CrosstalkScriptParser.HostTag}: Mark your calendars for August 20 because it is a big one.",
        $"{CrosstalkScriptParser.NeighborTag}: I already circled it in red.",
        $"{CrosstalkScriptParser.HostTag}: See you all right here when it comes around.",
    });

    static readonly string ClockLieReply = string.Join('\n', new[]
    {
        $"{CrosstalkScriptParser.HostTag}: Happy Friday to everyone listening in.",
        $"{CrosstalkScriptParser.NeighborTag}: Best day of the week hands down.",
        $"{CrosstalkScriptParser.HostTag}: Let us make it count together.",
    });

    // A recurring invented character plus a running gag — no real frequency, call sign, place,
    // weather, or date anywhere, and no clock claim either (SPEC F138.6's "fictional lore passes
    // untouched" half).
    static readonly string FictionalLoreReply = string.Join('\n', new[]
    {
        $"{CrosstalkScriptParser.HostTag}: Is Gary the Ghost DJ still haunting the night shift again.",
        $"{CrosstalkScriptParser.NeighborTag}: Gary never skips his overnight howl before sign off.",
        $"{CrosstalkScriptParser.HostTag}: Somewhere out there Gary is smiling right now.",
    });

    // The task-pinned edge: a TIME reference shaped just like a frequency claim ("digit space AM")
    // must never trip the frequency shape — only a 3-4 digit AM frequency does (see
    // CrosstalkScriptParser.FrequencyRx's own remarks).
    static readonly string NineAmReply = string.Join('\n', new[]
    {
        $"{CrosstalkScriptParser.HostTag}: We're back on the air again at 9 AM sharp.",
        $"{CrosstalkScriptParser.NeighborTag}: Set an alarm because I will be listening.",
        $"{CrosstalkScriptParser.HostTag}: That is exactly the plan for both of us.",
    });

    static PersonaCard MakeCard(string name, string soul) =>
        new(PersonaCard.CurrentSchemaVersion, name, Tagline: "", soul, Quirks: [],
            new VoiceSpec("kokoro", "af_heart", 1.0, "en"), EnergyDisposition: 0, Lore: [], Corrections: []);

    static CrosstalkExchangeRequest Request() =>
        new(HostCard, NeighborCard, "GenWave", ShowName: "Night Shift", Daypart: "late night",
            StationLocalNow: FixedStationLocalNow);

    /// <summary>Mirrors Story326_BoothWritesForTwo's own BuildWriterWithRingAndLogger idiom — the
    /// house crosstalk-writer spec harness, driving the REAL CrosstalkScriptWriter end to end.</summary>
    static CrosstalkScriptWriter BuildWriter(string endpoint)
    {
        var ring = new LlmCallRing(new TestOptionsMonitor<LlmOptions>(new LlmOptions()));
        return new CrosstalkScriptWriter(
            new FakeHttpClientFactory(),
            new TestOptionsMonitor<LlmOptions>(new LlmOptions
            {
                Endpoint = endpoint,
                Model = "test-model",
                TimeoutSeconds = 5,
                MaxCopyChars = 300,
            }),
            new TestOptionsMonitor<CrosstalkOptions>(new CrosstalkOptions()),
            new LlmCallRecorder(ring, new LlmCallCauseCounters(TimeProvider.System)),
            new FakeDegradationModeReader(),
            new CapturingLogger<CrosstalkScriptWriter>(),
            TimeProvider.System);
    }

    static string ExtractSystemPrompt(string body)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        foreach (var message in doc.RootElement.GetProperty("messages").EnumerateArray())
        {
            if (message.GetProperty("role").GetString() == "system")
                return message.GetProperty("content").GetString() ?? "";
        }

        return "";
    }

    // ── Verifiables discard the script ─────────────────────────────────────

    public static class ScenarioVerifiablesDiscardTheScript
    {
        [Fact]
        public static async Task A_frequency_shape_discards_with_the_verifiable_reason()
        {
            // Given a script that names a real-shaped FM frequency ("98.7 FM")...
            await using var mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = FrequencyShapeReply;
            var writer = BuildWriter(mock.BaseUri.ToString());

            // When the writer validates it...
            var result = await writer.WriteExchangeAsync(Request(), CancellationToken.None);

            // Then the whole exchange discards (fail-closed skip, no salvage), stamped as a truth
            // rejection — never a shape/malformed one, since the script parsed cleanly.
            var discarded = Assert.IsType<CrosstalkWriteResult.Discarded>(result);
            Assert.Equal(LlmCallCause.TruthGateReject, discarded.Cause);
            Assert.Contains("98.7 FM", discarded.Reason, StringComparison.Ordinal);
        }

        [Fact]
        public static async Task An_integer_FM_frequency_shape_also_discards()
        {
            // Given a script naming a real FM frequency spoken WITHOUT a decimal ("Radio 101 FM") —
            // requiring a decimal would let this class of real, commonly-spoken frequency claim
            // through as a false PASS (SPEC F138.6's own harm: an airable fabricated broadcast fact).
            await using var mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = IntegerFrequencyShapeReply;
            var writer = BuildWriter(mock.BaseUri.ToString());

            var result = await writer.WriteExchangeAsync(Request(), CancellationToken.None);

            var discarded = Assert.IsType<CrosstalkWriteResult.Discarded>(result);
            Assert.Equal(LlmCallCause.TruthGateReject, discarded.Cause);
            Assert.Contains("101 FM", discarded.Reason, StringComparison.Ordinal);
        }

        [Fact]
        public static async Task A_call_sign_shape_discards()
        {
            // Given a script that names a K/W-prefixed call sign shape...
            await using var mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = CallSignShapeReply;
            var writer = BuildWriter(mock.BaseUri.ToString());

            var result = await writer.WriteExchangeAsync(Request(), CancellationToken.None);

            var discarded = Assert.IsType<CrosstalkWriteResult.Discarded>(result);
            Assert.Equal(LlmCallCause.TruthGateReject, discarded.Cause);
            Assert.Contains("KXRT", discarded.Reason, StringComparison.Ordinal);
        }

        [Fact]
        public static async Task A_weather_claim_discards()
        {
            // Given a script that names a real weather-condition word...
            await using var mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = WeatherShapeReply;
            var writer = BuildWriter(mock.BaseUri.ToString());

            var result = await writer.WriteExchangeAsync(Request(), CancellationToken.None);

            var discarded = Assert.IsType<CrosstalkWriteResult.Discarded>(result);
            Assert.Equal(LlmCallCause.TruthGateReject, discarded.Cause);
            Assert.Contains("sunny", discarded.Reason, StringComparison.Ordinal);
        }

        [Fact]
        public static async Task A_date_claim_discards()
        {
            // Given a script that names a real-shaped date ("August 20")...
            await using var mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = DateShapeReply;
            var writer = BuildWriter(mock.BaseUri.ToString());

            var result = await writer.WriteExchangeAsync(Request(), CancellationToken.None);

            var discarded = Assert.IsType<CrosstalkWriteResult.Discarded>(result);
            Assert.Equal(LlmCallCause.TruthGateReject, discarded.Cause);
            Assert.Contains("August 20", discarded.Reason, StringComparison.Ordinal);
        }

        [Fact]
        public static async Task A_clock_lie_discards()
        {
            // Given a wrong-weekday line against the clock context (station-local is a Monday, the
            // script claims "Happy Friday")...
            await using var mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = ClockLieReply;
            var writer = BuildWriter(mock.BaseUri.ToString());

            var result = await writer.WriteExchangeAsync(Request(), CancellationToken.None);

            // Then it discards with the clock reason — CopyClaims.CheckClock, the SAME T329
            // predicate every other patter kind shares, judged against the SAME generation-time
            // instant the prompt's own clock line stated.
            var discarded = Assert.IsType<CrosstalkWriteResult.Discarded>(result);
            Assert.Equal(LlmCallCause.TruthGateReject, discarded.Cause);
            Assert.Contains("Friday", discarded.Reason, StringComparison.Ordinal);
            Assert.Contains("Monday", discarded.Reason, StringComparison.Ordinal);
        }
    }

    // ── Fictional lore passes ───────────────────────────────────────────────

    public static class ScenarioFictionalLorePasses
    {
        [Fact]
        public static async Task An_invented_recurring_character_passes()
        {
            // Given a script built entirely from invented lore — a recurring character and a
            // running gag, no real-world verifiable of any kind...
            await using var mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = FictionalLoreReply;
            var writer = BuildWriter(mock.BaseUri.ToString());

            // When the writer validates it...
            var result = await writer.WriteExchangeAsync(Request(), CancellationToken.None);

            // Then it validates clean — no lore-shaped rejection exists.
            Assert.IsType<CrosstalkWriteResult.Accepted>(result);
        }

        [Fact]
        public static async Task The_narrow_clause_rides_the_banter_prompt()
        {
            // Given any generation attempt...
            await using var mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = FictionalLoreReply;
            var writer = BuildWriter(mock.BaseUri.ToString());

            await writer.WriteExchangeAsync(Request(), CancellationToken.None);

            // Then the system prompt forbids real-world verifiables and explicitly allows
            // fictional lore, beside the banter prompt's other style rules.
            var prompt = ExtractSystemPrompt(mock.Requests[0].Body);
            Assert.Contains("Never mention a real radio frequency.", prompt, StringComparison.Ordinal);
            Assert.Contains("Never mention a real call sign.", prompt, StringComparison.Ordinal);
            Assert.Contains("Never mention a real place name.", prompt, StringComparison.Ordinal);
            Assert.Contains("Never mention a real weather condition.", prompt, StringComparison.Ordinal);
            Assert.Contains("Never mention a real date.", prompt, StringComparison.Ordinal);
            Assert.Contains(
                "Invented recurring characters running gags and station mythology are welcome.",
                prompt, StringComparison.Ordinal);
        }

        [Fact]
        public static async Task The_f138_5_clock_guard_line_also_rides_the_banter_prompt()
        {
            // Given any generation attempt (PLAN T333 review round 1, probe-proven F2) — crosstalk
            // was the only patter kind whose F138.3 clock check ran with no prompt-side guard at
            // all, silently discarding a clock lie the model was never told not to make...
            await using var mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = FictionalLoreReply;
            var writer = BuildWriter(mock.BaseUri.ToString());

            await writer.WriteExchangeAsync(Request(), CancellationToken.None);

            // Then the SAME F138.5 guard line every other patter prompt already carries rides the
            // crosstalk prompt too, stated against the SAME station-local instant this request
            // carries (FixedStationLocalNow: Monday, 12:00 -> "afternoon").
            var prompt = ExtractSystemPrompt(mock.Requests[0].Body);
            Assert.Contains(
                "It is Monday afternoon. Never name another day or time of day.",
                prompt, StringComparison.Ordinal);
        }
    }

    // ── The ratified target ─────────────────────────────────────────────────

    public static class ScenarioTheRatifiedTarget
    {
        [Fact]
        public static void The_duration_default_is_fifty_seconds() =>
            // With no override, Crosstalk:DurationTargetSeconds reads 50 (F127.4 as amended,
            // ratified from two days of live convergence — the 25s paper-audition posture retires).
            Assert.Equal(50, new CrosstalkOptions().DurationTargetSeconds);
    }

    // ── Edge pins (PLAN T333 review guidance) ───────────────────────────────

    public static class ScenarioEdgeCasesArePinned
    {
        [Fact]
        public static async Task A_time_like_9_AM_does_not_trip_the_frequency_shape()
        {
            // Given a script that mentions a CLOCK TIME shaped like a frequency claim ("9 AM") —
            // the exact edge the frequency shape must NOT catch, since a time-of-day mention is
            // not a station's dial position.
            await using var mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = NineAmReply;
            var writer = BuildWriter(mock.BaseUri.ToString());

            var result = await writer.WriteExchangeAsync(Request(), CancellationToken.None);

            // Then it validates clean — "9 AM" is never mistaken for "98.7 FM".
            Assert.IsType<CrosstalkWriteResult.Accepted>(result);
        }

        [Fact]
        public static async Task The_discard_reason_names_what_was_wrong_not_an_internal_code_path()
        {
            // Given any truth-gate discard...
            await using var mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = FrequencyShapeReply;
            var writer = BuildWriter(mock.BaseUri.ToString());

            var result = await writer.WriteExchangeAsync(Request(), CancellationToken.None);

            // Then the reason is operator-honest: it names the offending text in plain words, never
            // an internal enum or type name an operator reading /api/llm-calls would not recognize.
            var discarded = Assert.IsType<CrosstalkWriteResult.Discarded>(result);
            Assert.Contains("real-world radio frequency", discarded.Reason, StringComparison.Ordinal);
            Assert.DoesNotContain("TruthGateReject", discarded.Reason, StringComparison.Ordinal);
            Assert.DoesNotContain(nameof(CrosstalkScriptParser), discarded.Reason, StringComparison.Ordinal);
        }
    }
}

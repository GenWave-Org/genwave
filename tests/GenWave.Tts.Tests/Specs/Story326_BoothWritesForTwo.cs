// STORY-326 — The booth writes for two (gh-#385 · SPEC F127.3/.4 · PLAN VQ-i, T282)
//
// BDD specification — xUnit, LIVE as of T282. The design's named risk (2026-08-14): a 3B model
// writing two coherent voices — these facts pin the contract that makes bad output unairable, not
// the output good. One completion produces the WHOLE exchange (reactions must react to what was
// actually said); validation is fail-closed and the failure mode is silent skip — no template rung,
// no salvage. One assertion per Fact; happy first; sad path segregated. The T288 wire acceptance (an
// exchange airs once on a running stack) is a production check, not represented here. T283, the
// paper-audition checkpoint, gates everything after T282 — these facts green first.

namespace GenWave.Tts.Tests.Specs;

using Microsoft.Extensions.Logging;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureBoothWritesForTwo
{
    // ── Shared fixtures ─────────────────────────────────────────────────────

    static readonly PersonaCard HostCard = MakeCard(
        "Neon Nightowl", "Neon Nightowl spins moody late-night sets deep into the small hours.");

    static readonly PersonaCard NeighborCard = MakeCard(
        "Daybreak Dana", "Daybreak Dana brings bright upbeat energy straight off the morning show.");

    static readonly string WellFormedReply = string.Join('\n', new[]
    {
        $"{CrosstalkScriptParser.HostTag}: Hey, welcome back to the show.",
        $"{CrosstalkScriptParser.NeighborTag}: Great to drop in tonight.",
        $"{CrosstalkScriptParser.HostTag}: Always good to have you around.",
    });

    static PersonaCard MakeCard(string name, string soul) =>
        new(PersonaCard.CurrentSchemaVersion, name, Tagline: "", soul, Quirks: [],
            new VoiceSpec("kokoro", "af_heart", 1.0, "en"), EnergyDisposition: 0, Lore: [], Corrections: []);

    static CrosstalkExchangeRequest Request() =>
        new(HostCard, NeighborCard, "GenWave", ShowName: "Night Shift", Daypart: "late night",
            StationLocalNow: DateTimeOffset.UtcNow);

    /// <summary>
    /// The one constructor arg list in this file (mirrors Story319_CopyFitsItsBreak's own
    /// BuildWriterWithRingAndLogger idiom) — every other builder below is expressed in terms of
    /// this, not a second copy of it.
    /// </summary>
    static (CrosstalkScriptWriter Writer, LlmCallRing Ring, CapturingLogger<CrosstalkScriptWriter> Logger,
        TestOptionsMonitor<CrosstalkOptions> CrosstalkMonitor) BuildWriterWithRingAndLogger(
            string endpoint, int maxCopyChars = 450, int durationTargetSeconds = 25)
    {
        var ring = new LlmCallRing(new TestOptionsMonitor<LlmOptions>(new LlmOptions()));
        var logger = new CapturingLogger<CrosstalkScriptWriter>();
        var crosstalkMonitor = new TestOptionsMonitor<CrosstalkOptions>(
            new CrosstalkOptions { DurationTargetSeconds = durationTargetSeconds });
        var writer = new CrosstalkScriptWriter(
            new FakeHttpClientFactory(),
            new TestOptionsMonitor<LlmOptions>(new LlmOptions
            {
                Endpoint = endpoint,
                Model = "test-model",
                TimeoutSeconds = 5,
                MaxCopyChars = maxCopyChars,
            }),
            crosstalkMonitor,
            new LlmCallRecorder(ring, new LlmCallCauseCounters(TimeProvider.System)),
            new FakeDegradationModeReader(),
            logger,
            TimeProvider.System);
        return (writer, ring, logger, crosstalkMonitor);
    }

    static CrosstalkScriptWriter BuildWriter(string endpoint, int maxCopyChars = 450, int durationTargetSeconds = 25) =>
        BuildWriterWithRingAndLogger(endpoint, maxCopyChars, durationTargetSeconds).Writer;

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

    static int ExtractMaxTokens(string body)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("max_tokens").GetInt32();
    }

    static string ExtractUserContent(string body)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        foreach (var message in doc.RootElement.GetProperty("messages").EnumerateArray())
        {
            if (message.GetProperty("role").GetString() == "user")
                return message.GetProperty("content").GetString() ?? "";
        }

        return "";
    }

    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioOneCallWholeExchange : IAsyncLifetime
    {
        const int MaxCopyChars = 300;

        MockCompletionsServer mock = null!;
        string wireSystemPrompt = "";
        int wireMaxTokens;

        public async Task InitializeAsync()
        {
            // Given the host and neighbor persona cards plus show/daypart/time hooks...
            mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = WellFormedReply;
            var writer = BuildWriter(mock.BaseUri.ToString(), MaxCopyChars);

            // When the writer requests an exchange...
            await writer.WriteExchangeAsync(Request(), CancellationToken.None);

            wireSystemPrompt = ExtractSystemPrompt(mock.Requests[0].Body);
            wireMaxTokens = ExtractMaxTokens(mock.Requests[0].Body);
        }

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public void Exactly_one_completion_is_issued_per_exchange() =>
            // Then ONE completion request leaves — never per-turn calls.
            Assert.Equal(1, mock.RequestCount);

        [Fact]
        public void The_request_carries_both_persona_cards()
        {
            Assert.Contains(HostCard.Soul, wireSystemPrompt, StringComparison.Ordinal);
            Assert.Contains(NeighborCard.Soul, wireSystemPrompt, StringComparison.Ordinal);
        }

        [Fact]
        public void The_request_carries_the_duration_derived_generation_cap() =>
            // SPEC F127.3 (T283 paper-audition reconciliation, gh-#385): the cap derives from
            // Crosstalk:DurationTargetSeconds (default 25s here — BuildWriter's own default), NOT
            // from Llm:MaxCopyChars (300 in this fixture) — 25s x 15 chars/sec (CrosstalkScriptParser
            // .CharsPerSecond) x 2x headroom = 750 chars -> DeriveMaxTokens(750) = 250 tokens.
            // ScenarioTheGenerationCapTracksDuration below pins that this tracks DURATION, not chars.
            Assert.Equal(250, wireMaxTokens);
    }

    // T283 paper-audition reconciliation (SPEC F127.3, gh-#385): the first live run against
    // llama3.2:3b proved the PRIOR blurb-scaled cap (derived from Llm:MaxCopyChars, sized for one
    // short line) starves a 3-8 line script — 4 of 8 attempts died to finish_reason=length. These
    // facts pin that the cap now tracks Crosstalk:DurationTargetSeconds instead, and that
    // Llm:MaxCopyChars alone can no longer move it.
    public sealed class ScenarioTheGenerationCapTracksDuration : IAsyncLifetime
    {
        const int MaxCopyChars = 300;

        MockCompletionsServer shortTargetMock = null!;
        MockCompletionsServer longTargetMock = null!;
        int shortTargetMaxTokens;
        int longTargetMaxTokens;

        public async Task InitializeAsync()
        {
            // Given two writers differing ONLY in Crosstalk:DurationTargetSeconds (both share the
            // SAME Llm:MaxCopyChars)...
            shortTargetMock = await MockCompletionsServer.StartAsync();
            shortTargetMock.ReplyContent = WellFormedReply;
            var shortWriter = BuildWriter(shortTargetMock.BaseUri.ToString(), MaxCopyChars, durationTargetSeconds: 25);
            await shortWriter.WriteExchangeAsync(Request(), CancellationToken.None);
            shortTargetMaxTokens = ExtractMaxTokens(shortTargetMock.Requests[0].Body);

            longTargetMock = await MockCompletionsServer.StartAsync();
            longTargetMock.ReplyContent = WellFormedReply;
            var longWriter = BuildWriter(longTargetMock.BaseUri.ToString(), MaxCopyChars, durationTargetSeconds: 50);
            await longWriter.WriteExchangeAsync(Request(), CancellationToken.None);
            longTargetMaxTokens = ExtractMaxTokens(longTargetMock.Requests[0].Body);
        }

        public async Task DisposeAsync()
        {
            await shortTargetMock.DisposeAsync();
            await longTargetMock.DisposeAsync();
        }

        [Fact]
        public void A_longer_duration_target_yields_a_larger_generation_cap() =>
            // Then the cap MOVED with the duration target alone.
            Assert.True(longTargetMaxTokens > shortTargetMaxTokens);

        [Fact]
        public void Doubling_the_duration_target_doubles_the_derived_generation_cap() =>
            // 50s x 15 chars/sec x 2x headroom = 1500 chars -> DeriveMaxTokens(1500) = 500 tokens.
            Assert.Equal(500, longTargetMaxTokens);
    }

    public static class ScenarioMaxCopyCharsAloneDoesNotMoveTheCap
    {
        [Fact]
        public static async Task Changing_MaxCopyChars_alone_leaves_the_generation_cap_unchanged()
        {
            // Given two writers differing ONLY in Llm:MaxCopyChars (both share the SAME
            // Crosstalk:DurationTargetSeconds default)...
            await using var smallMaxCopyMock = await MockCompletionsServer.StartAsync();
            smallMaxCopyMock.ReplyContent = WellFormedReply;
            var smallMaxCopyWriter = BuildWriter(smallMaxCopyMock.BaseUri.ToString(), maxCopyChars: 150);
            await smallMaxCopyWriter.WriteExchangeAsync(Request(), CancellationToken.None);

            await using var largeMaxCopyMock = await MockCompletionsServer.StartAsync();
            largeMaxCopyMock.ReplyContent = WellFormedReply;
            var largeMaxCopyWriter = BuildWriter(largeMaxCopyMock.BaseUri.ToString(), maxCopyChars: 900);
            await largeMaxCopyWriter.WriteExchangeAsync(Request(), CancellationToken.None);

            // Then the generation cap is IDENTICAL — MaxCopyChars alone never moves it.
            Assert.Equal(
                ExtractMaxTokens(smallMaxCopyMock.Requests[0].Body),
                ExtractMaxTokens(largeMaxCopyMock.Requests[0].Body));
        }
    }

    // T283 paper-audition reconciliation (SPEC F127.3, gh-#385): the prompt's stated word budget
    // now derives from the SAME Crosstalk:DurationTargetSeconds figure as the generation cap (never
    // from Llm:MaxCopyChars) — the model is asked for what the duration gate will actually accept.
    public static class ScenarioTheStatedWordBudgetTracksDuration
    {
        [Fact]
        public static async Task The_default_duration_target_states_the_duration_derived_word_budget()
        {
            // 25s x 15 chars/sec / 6 chars-per-word = 62 words (int division: 375 / 6 = 62).
            await using var mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = WellFormedReply;
            var writer = BuildWriter(mock.BaseUri.ToString());

            await writer.WriteExchangeAsync(Request(), CancellationToken.None);

            var prompt = ExtractSystemPrompt(mock.Requests[0].Body);
            Assert.Contains("approximately 62 words total", prompt, StringComparison.Ordinal);
        }

        [Fact]
        public static async Task A_longer_duration_target_states_a_larger_word_budget()
        {
            // 50s x 15 chars/sec / 6 chars-per-word = 125 words — double the duration target moves
            // the stated budget too, proving it is not a fixed/hardcoded figure.
            await using var mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = WellFormedReply;
            var writer = BuildWriter(mock.BaseUri.ToString(), durationTargetSeconds: 50);

            await writer.WriteExchangeAsync(Request(), CancellationToken.None);

            var prompt = ExtractSystemPrompt(mock.Requests[0].Body);
            Assert.Contains("approximately 125 words total", prompt, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioTheScriptParsesStrictly : IAsyncLifetime
    {
        // A NEIGHBOR interjection immediately after another NEIGHBOR line — plain alternation would
        // reject this, but the interjection marker is the one sanctioned exception (SPEC F127.4).
        static readonly string Reply = string.Join('\n', new[]
        {
            $"{CrosstalkScriptParser.HostTag}: Hey, welcome back to the show.",
            $"{CrosstalkScriptParser.NeighborTag}: Great to drop in tonight.",
            $"{CrosstalkScriptParser.NeighborTag} {CrosstalkScriptParser.InterjectionMarker}: Wait, I have to say —",
            $"{CrosstalkScriptParser.HostTag}: Go right ahead then.",
        });

        MockCompletionsServer mock = null!;
        CrosstalkWriteResult result = null!;

        public async Task InitializeAsync()
        {
            mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = Reply;
            var writer = BuildWriter(mock.BaseUri.ToString());

            // When the script is parsed...
            result = await writer.WriteExchangeAsync(Request(), CancellationToken.None);
        }

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public void A_well_formed_response_yields_three_to_eight_speaker_tagged_lines()
        {
            var accepted = Assert.IsType<CrosstalkWriteResult.Accepted>(result);
            Assert.InRange(accepted.Script.Lines.Count, CrosstalkScriptParser.MinLines, CrosstalkScriptParser.MaxLines);
        }

        [Fact]
        public void Both_speakers_are_present_in_an_accepted_script()
        {
            var accepted = Assert.IsType<CrosstalkWriteResult.Accepted>(result);
            Assert.Contains(accepted.Script.Lines, line => line.Speaker == CrosstalkSpeaker.Host);
            Assert.Contains(accepted.Script.Lines, line => line.Speaker == CrosstalkSpeaker.Neighbor);
        }

        [Fact]
        public void Alternation_holds_outside_interjection_marked_lines()
        {
            // The reply's third line breaks strict adjacent alternation (NEIGHBOR follows NEIGHBOR)
            // ONLY because it is marked as an interjection — proving the parser's one sanctioned
            // exception is what let it through, not a broken alternation check.
            var accepted = Assert.IsType<CrosstalkWriteResult.Accepted>(result);
            Assert.True(accepted.Script.Lines[2].IsInterjection);
            Assert.Equal(accepted.Script.Lines[1].Speaker, accepted.Script.Lines[2].Speaker);
        }
    }

    public sealed class ScenarioPerLineHygieneWithoutTrimming : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();
        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task Every_accepted_line_has_cleared_the_standing_copy_cleanup()
        {
            // Given a parsed script whose lines carry the same hygiene hazards an ordinary blurb
            // does (wrapping quotes, a bracketed stage direction)...
            mock.ReplyContent = string.Join('\n', new[]
            {
                $"{CrosstalkScriptParser.HostTag}: \"Welcome back to the show.\"",
                $"{CrosstalkScriptParser.NeighborTag}: *laughs* Great to be here tonight.",
                $"{CrosstalkScriptParser.HostTag}: Always a pleasure to have you.",
            });
            var writer = BuildWriter(mock.BaseUri.ToString());

            // When validation runs...
            var result = await writer.WriteExchangeAsync(Request(), CancellationToken.None);

            // Then every accepted line has cleared the standing copy cleanup.
            var accepted = Assert.IsType<CrosstalkWriteResult.Accepted>(result);
            Assert.Equal("Welcome back to the show.", accepted.Script.Lines[0].Text);
            Assert.Equal("Great to be here tonight.", accepted.Script.Lines[1].Text);
        }

        [Fact]
        public async Task No_line_is_ever_trimmed()
        {
            // A cut dialogue line breaks the reaction to it — over-budget rejects the WHOLE exchange
            // (sad path), it never salvages a line (F123.2's trim deliberately does NOT extend here).
            mock.ReplyContent = string.Join('\n', new[]
            {
                $"{CrosstalkScriptParser.HostTag}: This line is written to run well past the tiny " +
                    "configured per-line character budget for this fact.",
                $"{CrosstalkScriptParser.NeighborTag}: Short reply.",
                $"{CrosstalkScriptParser.HostTag}: Another short one.",
            });
            var writer = BuildWriter(mock.BaseUri.ToString(), maxCopyChars: 20);

            var result = await writer.WriteExchangeAsync(Request(), CancellationToken.None);

            // Never a truncated Accepted — the whole exchange is discarded instead.
            Assert.IsType<CrosstalkWriteResult.Discarded>(result);
        }
    }

    public sealed class ScenarioTheExchangeFitsItsMoment : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();
        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task A_script_under_the_duration_target_is_accepted()
        {
            // Given a validated script well under the shipped 50s default (three short lines) —
            // built EXPLICITLY off CrosstalkOptions()'s own default (T333 review advisory A5), never
            // this file's own BuildWriter convenience parameter default (25, an unrelated fixed test
            // value several OTHER facts in this file use purely to prove the cap/word-budget SCALE
            // with whatever target is configured) — so "the default" means one thing in this scenario.
            mock.ReplyContent = WellFormedReply;
            var writer = BuildWriter(mock.BaseUri.ToString(), durationTargetSeconds: new CrosstalkOptions().DurationTargetSeconds);

            // When the spoken-duration estimate is computed...
            var result = await writer.WriteExchangeAsync(Request(), CancellationToken.None);

            Assert.IsType<CrosstalkWriteResult.Accepted>(result);
        }

        [Fact]
        public async Task The_duration_target_is_live_editable_with_the_shipped_default()
        {
            // Given the shipped default (SPEC F127.4 as amended, PLAN T333) — 200 chars / 15
            // chars-per-sec ~= 13.3s, which fits comfortably under it.
            var shippedDefault = new CrosstalkOptions().DurationTargetSeconds;
            Assert.Equal(50, shippedDefault);

            mock.ReplyContent = string.Join('\n', new[]
            {
                $"{CrosstalkScriptParser.HostTag}: {new string('a', 70)}",
                $"{CrosstalkScriptParser.NeighborTag}: {new string('b', 70)}",
                $"{CrosstalkScriptParser.HostTag}: {new string('c', 60)}",
            });
            // Threads the SAME shippedDefault value read above (T333 review advisory A5) — never
            // this file's own BuildWriter convenience default (still 25 elsewhere in this file), so
            // the fact's own "shipped default" assertion and the writer it builds provably agree on
            // what "the default" means.
            var (writer, _, _, crosstalkMonitor) = BuildWriterWithRingAndLogger(
                mock.BaseUri.ToString(), durationTargetSeconds: shippedDefault);

            var underDefault = await writer.WriteExchangeAsync(Request(), CancellationToken.None);
            Assert.IsType<CrosstalkWriteResult.Accepted>(underDefault);

            // When the live setting drops below that same script's estimate, with NO writer
            // rebuild...
            crosstalkMonitor.CurrentValue = new CrosstalkOptions { DurationTargetSeconds = 10 };
            var overLoweredTarget = await writer.WriteExchangeAsync(Request(), CancellationToken.None);

            // Then the very next attempt reflects the live edit.
            Assert.IsType<CrosstalkWriteResult.Discarded>(overLoweredTarget);
        }
    }

    public static class ScenarioGenerationIsVisible
    {
        [Fact]
        public static async Task The_call_appears_in_the_llm_ring_under_its_own_kind()
        {
            // Given any generation attempt...
            await using var mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = WellFormedReply;
            var (writer, ring, _, _) = BuildWriterWithRingAndLogger(mock.BaseUri.ToString());

            await writer.WriteExchangeAsync(Request(), CancellationToken.None);

            // When /api/llm-calls is read (the ring it's built from)...
            var record = Assert.Single(ring.Snapshot());

            // Then the call appears under its own kind.
            Assert.Equal(LlmCallKind.Crosstalk, record.Kind);
        }

        // T282 review finding (F2b): mutation-proven — deleting the discard path's own
        // callRing.Record(...) call left the suite green, since nothing previously asserted a
        // DISCARDED attempt is visible to the ring at all. A discard must be just as visible as an
        // accept (SPEC F127.11 — "why was there no banter" has to be answerable from the ring for
        // a reject, not only for a success).
        [Fact]
        public static async Task A_discarded_attempt_also_appears_in_the_ring()
        {
            // Given a response that fails validation (no recognizable speaker tags at all)...
            await using var mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = "Hey there, welcome back!\nGreat to see you too!\nLet's get into it.";
            var (writer, ring, _, _) = BuildWriterWithRingAndLogger(mock.BaseUri.ToString());

            var result = await writer.WriteExchangeAsync(Request(), CancellationToken.None);

            // Then the ring records the discard — Rejected outcome, the Crosstalk kind, and the
            // SAME reason string returned to the caller carried as StatusDetail — never silence.
            var discarded = Assert.IsType<CrosstalkWriteResult.Discarded>(result);
            var record = Assert.Single(ring.Snapshot());
            Assert.Equal(LlmCallOutcome.Rejected, record.Outcome);
            Assert.Equal(LlmCallKind.Crosstalk, record.Kind);
            Assert.Equal(discarded.Reason, record.StatusDetail);
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public sealed class ScenarioAnyValidationFailureDiscardsSilently : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();
        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task A_malformed_response_produces_no_exchange_and_one_reason_line()
        {
            // Given a response failing parse (no recognizable speaker tags at all)...
            mock.ReplyContent = "Hey there, welcome back!\nGreat to see you too!\nLet's get into it.";
            var (writer, _, logger, _) = BuildWriterWithRingAndLogger(mock.BaseUri.ToString());

            // When the writer completes...
            var result = await writer.WriteExchangeAsync(Request(), CancellationToken.None);

            // Then no exchange is produced, and exactly one Information line names the reason — no
            // template rung, no salvage, and never a WARN (banter is optional color, a miss is not
            // an outage).
            Assert.IsType<CrosstalkWriteResult.Discarded>(result);
            Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Information);
            Assert.Empty(logger.Warnings);
        }

        [Fact]
        public async Task An_over_budget_line_rejects_the_whole_exchange()
        {
            mock.ReplyContent = string.Join('\n', new[]
            {
                $"{CrosstalkScriptParser.HostTag}: A line intentionally longer than the tiny " +
                    "per-line budget configured for this fact.",
                $"{CrosstalkScriptParser.NeighborTag}: Short.",
                $"{CrosstalkScriptParser.HostTag}: Also short.",
            });
            var writer = BuildWriter(mock.BaseUri.ToString(), maxCopyChars: 15);

            var result = await writer.WriteExchangeAsync(Request(), CancellationToken.None);

            var discarded = Assert.IsType<CrosstalkWriteResult.Discarded>(result);
            Assert.Contains("per-line budget", discarded.Reason, StringComparison.Ordinal);
        }

        [Fact]
        public async Task An_over_duration_script_rejects_whole()
        {
            mock.ReplyContent = string.Join('\n', new[]
            {
                $"{CrosstalkScriptParser.HostTag}: {new string('a', 70)}",
                $"{CrosstalkScriptParser.NeighborTag}: {new string('b', 70)}",
                $"{CrosstalkScriptParser.HostTag}: {new string('c', 60)}",
            });
            var writer = BuildWriter(mock.BaseUri.ToString(), durationTargetSeconds: 1);

            var result = await writer.WriteExchangeAsync(Request(), CancellationToken.None);

            var discarded = Assert.IsType<CrosstalkWriteResult.Discarded>(result);
            Assert.Contains("exceeds", discarded.Reason, StringComparison.Ordinal);
        }

        // T282 review finding (F2a): mutation-proven — deleting the both-speakers-present guards
        // left the suite green. The reachable hole: a reply where every line AFTER the first is
        // marked as an interjection (SPEC F127.4's one sanctioned exception to strict alternation)
        // never trips the alternation loop below either, since IsInterjection short-circuits every
        // adjacent pair — so with the guards gone, a one-voice "HOST:"/all-"HOST (interjects):"
        // script would validate cleanly. Only "both speakers must be present" catches it.
        [Fact]
        public async Task A_single_speaker_all_interjection_reply_is_discarded()
        {
            mock.ReplyContent = string.Join('\n', new[]
            {
                $"{CrosstalkScriptParser.HostTag}: Hey there, just me tonight.",
                $"{CrosstalkScriptParser.HostTag} {CrosstalkScriptParser.InterjectionMarker}: Still me.",
                $"{CrosstalkScriptParser.HostTag} {CrosstalkScriptParser.InterjectionMarker}: Also me.",
            });
            var writer = BuildWriter(mock.BaseUri.ToString());

            var result = await writer.WriteExchangeAsync(Request(), CancellationToken.None);

            var discarded = Assert.IsType<CrosstalkWriteResult.Discarded>(result);
            Assert.Contains(CrosstalkScriptParser.NeighborTag, discarded.Reason, StringComparison.Ordinal);
        }

        // SPEC F139.1 amendment (T330 review round 1, 2026-08-20 — the F135.5 precedent): the
        // reviewer's own exhibit — a reply carrying MORE than MaxLines is not "empty" by any honest
        // reading, so the whole parser-shape family (this branch included) moved off EmptyCompletion
        // onto its own MalformedResponse bucket.
        [Fact]
        public async Task A_twelve_line_reply_is_a_malformed_response_not_an_empty_one()
        {
            mock.ReplyContent = string.Join('\n', Enumerable.Range(1, 12).Select(i =>
                $"{(i % 2 == 1 ? CrosstalkScriptParser.HostTag : CrosstalkScriptParser.NeighborTag)}: Line {i}."));
            var writer = BuildWriter(mock.BaseUri.ToString());

            var result = await writer.WriteExchangeAsync(Request(), CancellationToken.None);

            Assert.Equal(LlmCallCause.MalformedResponse, Assert.IsType<CrosstalkWriteResult.Discarded>(result).Cause);
        }
    }

    // T282 review finding (F3, gh-#424 class one seam over): a completion the backend cuts short
    // at its own max_tokens cap leaves a truncated last line that can still PARSE cleanly (a
    // chopped sentence still matches the HOST:/NEIGHBOR: line shape) and would otherwise air
    // mid-word — finish_reason is the OpenAI/ollama-compatible signal that catches it BEFORE Parse
    // ever runs.
    public sealed class ScenarioATruncatedCompletionNeverAirs : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();
        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task A_completion_capped_by_max_tokens_is_discarded_even_though_it_would_otherwise_parse()
        {
            // Given a well-formed-LOOKING reply the backend flags as cut short by its own token cap...
            mock.ReplyContent = WellFormedReply;
            mock.ReplyFinishReason = "length";
            var writer = BuildWriter(mock.BaseUri.ToString());

            // When the writer completes...
            var result = await writer.WriteExchangeAsync(Request(), CancellationToken.None);

            // Then the whole exchange is discarded — never aired truncated.
            var discarded = Assert.IsType<CrosstalkWriteResult.Discarded>(result);
            Assert.Contains("length", discarded.Reason, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_completion_that_finished_naturally_is_not_discarded_for_that_reason()
        {
            // Given the SAME well-formed reply, this time flagged as having finished naturally
            // (the mock's own default) — a "stop" finish_reason must never trip this check.
            mock.ReplyContent = WellFormedReply;
            mock.ReplyFinishReason = "stop";
            var writer = BuildWriter(mock.BaseUri.ToString());

            var result = await writer.WriteExchangeAsync(Request(), CancellationToken.None);

            Assert.IsType<CrosstalkWriteResult.Accepted>(result);
        }
    }

    public static class ScenarioTheCurrentTrackIsStructurallyUnknowable
    {
        [Fact]
        public static void The_prompt_contains_no_current_track_reference()
        {
            // Given prompt assembly for an exchange — CrosstalkExchangeRequest is the ONLY input
            // CrosstalkPromptBuilder ever reads from, and it carries no MediaItem/track-shaped member
            // at all, by construction (mirrors Story228_RequestShoutOut's own reflection proof one
            // seam over).
            var properties = typeof(CrosstalkExchangeRequest).GetProperties();

            Assert.DoesNotContain(properties, p => p.PropertyType == typeof(MediaItem));
            Assert.DoesNotContain(properties, p => p.Name.Contains("Track", StringComparison.OrdinalIgnoreCase));
        }
    }

    // T282 review finding (F4): CrosstalkExchangeRequest's ShowName/Daypart are operator-editable
    // hooks with no length constraint of their own (the db show name column is unbounded text) —
    // every LlmPromptBuilder counterpart truncates text like this before it reaches a prompt (e.g.
    // BuildShowLine's own showName truncation), and this builder must do the same.
    public sealed class ScenarioOperatorHooksAreCapped : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();
        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task An_oversized_show_name_reaches_the_prompt_truncated()
        {
            // Given a ShowName far past the house 4000-char cap...
            mock.ReplyContent = WellFormedReply;
            var writer = BuildWriter(mock.BaseUri.ToString());
            var oversizedShowName = new string('a', 5000);
            var request = Request() with { ShowName = oversizedShowName };

            // When the writer requests an exchange...
            await writer.WriteExchangeAsync(request, CancellationToken.None);

            // Then the prompt carries the truncated form, never the full 5000 chars.
            var userContent = ExtractUserContent(mock.Requests[0].Body);
            Assert.DoesNotContain(oversizedShowName, userContent, StringComparison.Ordinal);
            Assert.Contains(new string('a', 4000), userContent, StringComparison.Ordinal);
        }
    }
}

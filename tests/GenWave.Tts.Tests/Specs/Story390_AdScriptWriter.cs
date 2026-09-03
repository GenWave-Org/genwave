// STORY-390 — The station writes its own ads (writer half: AC2/AC3 · F160.1/.2 · pending T400)

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureAdScriptWriter
{
    // ── Shared fixtures ─────────────────────────────────────────────────────

    static AdScriptWriteRequest Request(
        int spotSeconds = 30, string brand = "Cravin's Diner", string? premise = "A retro diner with a twist",
        string? tone = "warm and playful", int maxLineChars = 200, double toleranceRatio = 0.4,
        AudiencePosture posture = AudiencePosture.Everyone) =>
        new(brand, premise, tone, spotSeconds, posture, maxLineChars, toleranceRatio);

    static Func<string, AdScriptValidationOutcome> AlwaysAccepts() =>
        static _ => new AdScriptValidationOutcome.Accepted();

    static Func<string, AdScriptValidationOutcome> AlwaysRefuses(string ruleId, string reason) =>
        _ => new AdScriptValidationOutcome.Refused(ruleId, reason);

    /// <summary>Accepts unconditionally, capturing the exact raw text the writer handed it — the spy
    /// PLAN T400 review F2(a) needs to prove line breaks survive hygiene, without needing the real
    /// GenWave.Ads.AdScriptValidator (that half lives in GenWave.Ads.Tests' own
    /// FeatureAdScriptWriterMeetsTheRealValidator, PLAN T400 review F2(b)).</summary>
    static (Func<string, AdScriptValidationOutcome> Validate, Func<string> Captured) CapturingAccept()
    {
        string? captured = null;
        Func<string, AdScriptValidationOutcome> validate = raw =>
        {
            captured = raw;
            return new AdScriptValidationOutcome.Accepted();
        };
        return (validate, () => captured ?? throw new InvalidOperationException("validate was never called"));
    }

    /// <summary>Refuses the FIRST call, accepts every call after — the re-ask ladder's own shape,
    /// without needing the underlying completion text to differ between attempts (this writer hands
    /// whatever the mock server replies with to the SAME delegate both times; only the delegate's own
    /// state needs to change).</summary>
    static Func<string, AdScriptValidationOutcome> RefusesOnceThenAccepts(string ruleId, string reason)
    {
        var calls = 0;
        return _ => Interlocked.Increment(ref calls) == 1
            ? new AdScriptValidationOutcome.Refused(ruleId, reason)
            : new AdScriptValidationOutcome.Accepted();
    }

    /// <summary>The one constructor arg list in this file (mirrors Story326_BoothWritesForTwo's own
    /// BuildWriterWithRingAndLogger idiom) — every other builder below is expressed in terms of this.</summary>
    static (AdScriptWriter Writer, LlmCallRing Ring, CapturingLogger<AdScriptWriter> Logger) BuildWriterWithRingAndLogger(
        string endpoint, int timeoutSeconds = 5)
    {
        var ring = new LlmCallRing(new TestOptionsMonitor<LlmOptions>(new LlmOptions()));
        var logger = new CapturingLogger<AdScriptWriter>();
        var writer = new AdScriptWriter(
            new FakeHttpClientFactory(),
            new TestOptionsMonitor<LlmOptions>(new LlmOptions
            {
                Endpoint = endpoint,
                Model = "test-model",
                TimeoutSeconds = timeoutSeconds,
            }),
            new LlmCallRecorder(ring, new LlmCallCauseCounters(TimeProvider.System)),
            new FakeDegradationModeReader(),
            logger,
            TimeProvider.System);
        return (writer, ring, logger);
    }

    static AdScriptWriter BuildWriter(string endpoint) => BuildWriterWithRingAndLogger(endpoint).Writer;

    static string ExtractSystemPrompt(string body) => ExtractMessageContent(body, "system");

    static string ExtractUserContent(string body) => ExtractMessageContent(body, "user");

    static string ExtractMessageContent(string body, string role)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        foreach (var message in doc.RootElement.GetProperty("messages").EnumerateArray())
        {
            if (message.GetProperty("role").GetString() == role)
                return message.GetProperty("content").GetString() ?? "";
        }

        return "";
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioOneCompletionOneRecordedCall
    {
        [Fact]
        public async Task AGenerationRecordsOneAdScriptCallInTheRing()
        {
            // Given a draft that clears validation on the first attempt...
            await using var mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = "ANNOUNCER: Cravin's Diner, open late. Call 555-0142.";
            var (writer, ring, _) = BuildWriterWithRingAndLogger(mock.BaseUri.ToString());

            // When the writer generates one spot...
            await writer.WriteAsync(Request(), AlwaysAccepts(), CancellationToken.None);

            // Then LlmCallRing gains exactly one LlmCallKind.AdScript entry (F160.1).
            var record = Assert.Single(ring.Snapshot());
            Assert.Equal(LlmCallKind.AdScript, record.Kind);
        }

        [Fact]
        public async Task ThePromptCarriesTheSpotStructure()
        {
            // Given a 30s spot request...
            await using var mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = "ANNOUNCER: Cravin's Diner, open late. Call 555-0142.";
            var writer = BuildWriter(mock.BaseUri.ToString());

            // When the writer generates one spot...
            await writer.WriteAsync(Request(spotSeconds: 30), AlwaysAccepts(), CancellationToken.None);

            // Then the prompt carries the 30s template's own structure beats (SPEC F160.2).
            var systemPrompt = ExtractSystemPrompt(mock.Requests[0].Body);
            Assert.Contains("hook", systemPrompt, StringComparison.Ordinal);
            Assert.Contains("pitch", systemPrompt, StringComparison.Ordinal);
            Assert.Contains("tagline", systemPrompt, StringComparison.Ordinal);
            Assert.Contains("call-to-action", systemPrompt, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ThePromptCarriesTheBrief()
        {
            // Given a spot request naming a brand, premise, and tone...
            await using var mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = "ANNOUNCER: Cravin's Diner, open late. Call 555-0142.";
            var writer = BuildWriter(mock.BaseUri.ToString());
            var request = Request(brand: "Cravin's Diner", premise: "A retro diner with a twist", tone: "warm and playful");

            // When the writer generates one spot...
            await writer.WriteAsync(request, AlwaysAccepts(), CancellationToken.None);

            // Then the prompt carries the sampled brief's own brand/premise/tone (SPEC F160.2).
            var userContent = ExtractUserContent(mock.Requests[0].Body);
            Assert.Contains("Cravin's Diner", userContent, StringComparison.Ordinal);
            Assert.Contains("A retro diner with a twist", userContent, StringComparison.Ordinal);
            Assert.Contains("warm and playful", userContent, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioMultiVoiceLineStructureSurvivesHygiene
    {
        [Fact]
        public async Task TheDelegateReceivesEveryVoiceOnItsOwnLineWithTagsIntact()
        {
            // Given a real 4-beat multi-voice reply — hook/pitch/tagline/CTA, alternating voices...
            var wellFormedReply = string.Join('\n', new[]
            {
                "ANNOUNCER: Cravin's Diner has a deal so good it's almost illegal.",
                "VOICE1: Almost. Stop by and taste the difference tonight.",
                "ANNOUNCER: Cravin's Diner. Taste the difference.",
                "VOICE1: Call 555-0142 today.",
            });
            await using var mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = wellFormedReply;
            var (validate, captured) = CapturingAccept();
            var writer = BuildWriter(mock.BaseUri.ToString());

            // When the writer generates one spot...
            await writer.WriteAsync(Request(), validate, CancellationToken.None);

            // Then the delegate receives the reply with its own line structure intact — NOT
            // LlmCopyWriter.ApplyCopyHygiene's whole-blob newline-collapse (PLAN T400 review F1
            // BLOCKER), which would merge every voice after the first into ANNOUNCER's own line.
            var lines = captured().Split('\n');
            Assert.Equal(4, lines.Length);
            Assert.StartsWith("ANNOUNCER:", lines[0], StringComparison.Ordinal);
            Assert.StartsWith("VOICE1:", lines[1], StringComparison.Ordinal);
            Assert.StartsWith("ANNOUNCER:", lines[2], StringComparison.Ordinal);
            Assert.StartsWith("VOICE1:", lines[3], StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioTheReAskLadder : IAsyncLifetime
    {
        const string RuleId = "format";
        const string Reason = "no ANNOUNCER line appeared";

        MockCompletionsServer mock = null!;
        LlmCallRing ring = null!;
        AdScriptWriteResult result = null!;

        public async Task InitializeAsync()
        {
            // Given a draft the validator refuses ONCE, naming a rule, then accepts...
            mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = "VOICE1: Cravin's Diner, open late.";
            var (writer, builtRing, _) = BuildWriterWithRingAndLogger(mock.BaseUri.ToString());
            ring = builtRing;

            // When the writer generates one spot...
            result = await writer.WriteAsync(Request(), RefusesOnceThenAccepts(RuleId, Reason), CancellationToken.None);
        }

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public void OneViolationTriggersExactlyOneReAskNamingTheRule()
        {
            // Then exactly TWO completions were issued — the original draft plus one re-ask, never more.
            Assert.Equal(2, mock.RequestCount);

            // And the re-ask's own user prompt names the violated rule and its reason.
            var reaskUserContent = ExtractUserContent(mock.Requests[1].Body);
            Assert.Contains($"violated the '{RuleId}' rule", reaskUserContent, StringComparison.Ordinal);
            Assert.Contains(Reason, reaskUserContent, StringComparison.Ordinal);
        }

        [Fact]
        public void ACleanSecondDraftPasses() =>
            // Then the spot is written — a clean second draft is not wasted just because the first
            // one broke a rule.
            Assert.IsType<AdScriptWriteResult.Success>(result);

        [Fact]
        public void BothAttemptsAreEachTheirOwnAdScriptRingRow()
        {
            // Then the ring holds TWO LlmCallKind.AdScript rows — the rejected first draft AND the
            // re-ask (PLAN T400 review F3) — never silently folded into one entry (SPEC F127.11's own
            // "why was there no spot" answerability, the same discipline CrosstalkScriptWriter's own
            // ladder-adjacent rejects already carry).
            var records = ring.Snapshot();
            Assert.Equal(2, records.Count);
            Assert.All(records, record => Assert.Equal(LlmCallKind.AdScript, record.Kind));
        }
    }

    public sealed class ScenarioRuleIdMapsHonestlyToItsOwnCause
    {
        // PLAN T400 review F4: a validator refusal is mapped per-rule, never flattened to one bucket —
        // the CrosstalkScriptParser precedent (a shape mistake is MalformedResponse, a length/duration
        // miss is OverLength, a content-truth-shaped miss is TruthGateReject).
        [Theory]
        [InlineData("format", LlmCallCause.MalformedResponse)]
        [InlineData("duration", LlmCallCause.OverLength)]
        [InlineData("brand_collision", LlmCallCause.TruthGateReject)]
        [InlineData("phone_shape", LlmCallCause.TruthGateReject)]
        [InlineData("audience_posture", LlmCallCause.TruthGateReject)]
        public async Task ARuleIdStampsItsOwnMappedCause(string ruleId, LlmCallCause expectedCause)
        {
            // Given a draft the validator refuses on EVERY attempt under the SAME rule id...
            await using var mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = "VOICE1: Cravin's Diner, open late.";
            var (writer, ring, _) = BuildWriterWithRingAndLogger(mock.BaseUri.ToString());

            // When the writer generates one spot...
            await writer.WriteAsync(Request(), AlwaysRefuses(ruleId, "a reason"), CancellationToken.None);

            // Then every ring row this rule id produced (the first draft AND its one re-ask) stamps
            // the rule's own honestly-mapped F139 cause.
            Assert.All(ring.Snapshot(), record => Assert.Equal(expectedCause, record.Cause));
        }
    }

    public sealed class ScenarioAnUntrustedReasonIsBounded
    {
        [Fact]
        public async Task AnOverLongControlCharLadenReasonIsTruncatedBeforeTheReAskPrompt()
        {
            // PLAN T400 review F7: Reason arrives from an ARBITRARY caller-supplied delegate — this
            // writer never trusts it to already be bounded.
            var hugeReason = "bad" + '\r' + new string('x', 500) + '\a';
            await using var mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = "VOICE1: Cravin's Diner, open late.";
            var writer = BuildWriter(mock.BaseUri.ToString());

            await writer.WriteAsync(Request(), RefusesOnceThenAccepts("format", hugeReason), CancellationToken.None);

            var reaskUserContent = ExtractUserContent(mock.Requests[1].Body);
            Assert.DoesNotContain(hugeReason, reaskUserContent, StringComparison.Ordinal);
            Assert.DoesNotContain('\r', reaskUserContent);
            Assert.DoesNotContain('\a', reaskUserContent);
        }

        [Fact]
        public async Task AnOverLongReasonIsAlsoBoundedOnTheFinalFailure()
        {
            var hugeReason = new string('x', 500);
            await using var mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = "VOICE1: Cravin's Diner, open late.";
            var writer = BuildWriter(mock.BaseUri.ToString());

            var result = await writer.WriteAsync(Request(), AlwaysRefuses("format", hugeReason), CancellationToken.None);

            var failed = Assert.IsType<AdScriptWriteResult.Failed>(result);
            Assert.True(failed.Reason.Length < 200, $"Reason was {failed.Reason.Length} chars");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — skip-only, no floor
    // ---------------------------------------------------------------------

    public sealed class ScenarioSkipOnlyNoTemplateFloor
    {
        [Fact]
        public async Task AFailedLlmProducesNoSpotAndNoCannedAd()
        {
            // Given a completions endpoint that refuses every request...
            await using var mock = await MockCompletionsServer.StartAsync(MockCompletionsMode.Fail);
            var writer = BuildWriter(mock.BaseUri.ToString());

            // When the writer generates one spot...
            var result = await writer.WriteAsync(Request(), AlwaysAccepts(), CancellationToken.None);

            // Then nothing advances — no spot, and no canned/template fallback (F160.1): a transport
            // fault never carries a rule id, and is never re-asked.
            var failed = Assert.IsType<AdScriptWriteResult.Failed>(result);
            Assert.Null(failed.RuleId);
            Assert.Equal(1, mock.RequestCount);
        }

        [Fact]
        public async Task ASecondViolationFailsTheSpotWithTheRuleId()
        {
            // Given a draft the validator refuses on EVERY attempt, first draft and re-ask alike...
            await using var mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = "VOICE1: Cravin's Diner, open late.";
            var writer = BuildWriter(mock.BaseUri.ToString());

            // When the writer generates one spot...
            var result = await writer.WriteAsync(
                Request(), AlwaysRefuses("format", "no ANNOUNCER line appeared"), CancellationToken.None);

            // Then the spot fails, fail_reason = the rule id (the F138 ladder shape) — and the ladder
            // spent exactly its one re-ask, never a third attempt.
            var failed = Assert.IsType<AdScriptWriteResult.Failed>(result);
            Assert.Equal("format", failed.RuleId);
            Assert.Equal(2, mock.RequestCount);
        }
    }
}

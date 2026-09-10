// STORY-426 — Plain-sentence scripts are accepted (SPEC F174.6 · PLAN T444)

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GenWave.Ads.Tests.Fakes;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts;

namespace GenWave.Ads.Tests.Specs;

public static partial class FeaturePlainSentenceScriptsAreAccepted
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static readonly AdScriptValidationRequest DefaultRequest = new(
        Posture: AudiencePosture.Everyone, MaxLineChars: 200, SpotSeconds: 30, ToleranceRatio: 0.4);

    static AdScriptValidationResult Validate(string script, AdScriptValidationRequest? request = null) =>
        AdScriptValidator.Validate(script, request ?? DefaultRequest, new FakePatterDurationEstimator());

    // The writer's own grammar (SPEC F160.2): TAG: line, tag uppercase-alphanumeric starting with a
    // letter. Used only to assert AC4's claim about the WRITER's own output shape — never to relax
    // what AdScriptParser itself enforces.
    [GeneratedRegex(@"^[A-Z][A-Z0-9]*: .+$", RegexOptions.CultureInvariant)]
    private static partial Regex WriterLineGrammar();

    static AdScriptWriteRequest WriterRequest(int spotSeconds, int maxLineChars, double toleranceRatio = 0.4) =>
        new(
            SponsorName: "Cravin's Diner", Premise: "A retro diner with a twist", Tone: "warm and playful",
            spotSeconds, AudiencePosture.Everyone, maxLineChars, toleranceRatio);

    /// <summary>The exact adapter PLAN T402's own AdSpotWorker builds (the same shape
    /// Story390_AdScriptWriterMeetsTheRealValidator.cs uses): closes over the REAL
    /// AdScriptValidator.Validate, translating its result into GenWave.Tts's own minimal
    /// AdScriptValidationOutcome contract.</summary>
    static Func<string, AdScriptValidationOutcome> RealValidatorDelegate(
        AdScriptValidationRequest validationRequest, IPatterDurationEstimator estimator) =>
        rawScript => AdScriptValidator.Validate(rawScript, validationRequest, estimator) switch
        {
            AdScriptValidationResult.Accepted => new AdScriptValidationOutcome.Accepted(),
            AdScriptValidationResult.Refused refused =>
                new AdScriptValidationOutcome.Refused(refused.Violation.RuleId, refused.Violation.Reason),
            _ => throw new InvalidOperationException("Unhandled AdScriptValidationResult case."),
        };

    static AdScriptWriter BuildWriter(HttpMessageHandler handler) =>
        new(
            new SingleHandlerHttpClientFactory(handler),
            new FakeOptionsMonitor<LlmOptions>(new LlmOptions
            {
                Endpoint = "http://fake-llm.local", Model = "test-model", TimeoutSeconds = 5,
            }),
            new LlmCallRecorder(
                new LlmCallRing(new FakeOptionsMonitor<LlmOptions>(new LlmOptions())),
                new LlmCallCauseCounters(TimeProvider.System)),
            new FakeDegradationModeReader(),
            new NoOpLogger<AdScriptWriter>(),
            TimeProvider.System);

    /// <summary>Serves the SAME completion reply for every request the writer sends (a re-ask,
    /// should one fire, gets the identical reply back).</summary>
    static HttpMessageHandler ServeSameReplyEveryTime(string content) => new FakeHttpMessageHandler((_, _) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } }),
                Encoding.UTF8, "application/json"),
        }));

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioUntaggedLinesBecomeAnnouncer
    {
        [Fact]
        public void ThreeUntaggedLinesBecomeThreeAnnouncerLines()
        {
            var result = AdScriptParser.Parse(
                "Corner Coffee.\nOpen at six.\nWorth waking up for.", maxLineChars: 200);

            var accepted = Assert.IsType<AdScriptValidationResult.Accepted>(result);
            Assert.Equal(["ANNOUNCER", "ANNOUNCER", "ANNOUNCER"], accepted.Script.Lines.Select(line => line.Tag));
        }

        [Fact]
        public void TheTextsAndOrderArePreserved()
        {
            var result = AdScriptParser.Parse(
                "Corner Coffee.\nOpen at six.\nWorth waking up for.", maxLineChars: 200);

            var accepted = Assert.IsType<AdScriptValidationResult.Accepted>(result);
            Assert.Equal(
                ["Corner Coffee.", "Open at six.", "Worth waking up for."],
                accepted.Script.Lines.Select(line => line.Text));
        }

        [Fact]
        public void SingleUntaggedLineBecomesOneAnnouncerLine()
        {
            var result = AdScriptParser.Parse("Just one line.", maxLineChars: 200);

            var accepted = Assert.IsType<AdScriptValidationResult.Accepted>(result);
            var line = Assert.Single(accepted.Script.Lines);
            Assert.Equal("ANNOUNCER", line.Tag);
            Assert.Equal("Just one line.", line.Text);
        }

        [Fact]
        public void APlainSentenceWithAnInteriorColonStaysUntagged()
        {
            // PLAN T444 ruling: an untagged line is never split — "The deal" has a lowercase-led
            // colon prefix (untagged per IsTaggedLine), so the whole sentence becomes one ANNOUNCER
            // line's text, verbatim, not truncated at the interior colon.
            var result = AdScriptParser.Parse(
                "Corner Coffee opens at six.\nThe deal: two for one until nine.\nSee you there.",
                maxLineChars: 200);

            var accepted = Assert.IsType<AdScriptValidationResult.Accepted>(result);
            Assert.Equal(["ANNOUNCER", "ANNOUNCER", "ANNOUNCER"], accepted.Script.Lines.Select(line => line.Tag));
            Assert.Equal("The deal: two for one until nine.", accepted.Script.Lines[1].Text);
        }
    }

    public sealed class ScenarioTheWritersOwnOutputStaysFullyTagged
    {
        [Fact]
        public async Task EveryWriterLineStartsWithAValidTag()
        {
            var reply = string.Join('\n', new[]
            {
                "ANNOUNCER: Cravin's Diner has a deal so good it's almost illegal.",
                "VOICE1: Almost. Stop by and taste the difference tonight.",
                "ANNOUNCER: Call 555-0142 - that's 555-0142 - Cravin's Diner.",
            });
            var writer = BuildWriter(ServeSameReplyEveryTime(reply));
            var validationRequest = new AdScriptValidationRequest(
                Posture: AudiencePosture.Everyone, MaxLineChars: 200, SpotSeconds: 30, ToleranceRatio: 0.4);

            var result = await writer.WriteAsync(
                WriterRequest(30, 200), RealValidatorDelegate(validationRequest, new FakePatterDurationEstimator()),
                CancellationToken.None);

            var success = Assert.IsType<AdScriptWriteResult.Success>(result);
            var nonEmptyLines = success.Script
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0);
            Assert.All(nonEmptyLines, line => Assert.Matches(WriterLineGrammar(), line));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    public sealed class ScenarioRefusingMixedAndStillValidating
    {
        [Fact]
        public void MixedTaggedAndUntaggedRefusesWithFormat()
        {
            var result = AdScriptParser.Parse(
                "ANNOUNCER: Corner Coffee.\nOpen at six.\nWorth waking up for.", maxLineChars: 200);

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.Format, refused.Violation.RuleId);
            Assert.Equal("line 2 has no voice tag while others do", refused.Violation.Reason);
        }

        [Fact]
        public void AMixedScriptWhereTheUntaggedLineIsFirstNamesLineOne()
        {
            var result = AdScriptParser.Parse(
                "Open at six.\nANNOUNCER: Corner Coffee.", maxLineChars: 200);

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.Format, refused.Violation.RuleId);
            Assert.Equal("line 1 has no voice tag while others do", refused.Violation.Reason);
        }

        [Fact]
        public void ABlankInteriorLineDoesNotShiftTheNamedLineNumber()
        {
            // PLAN T444 ruling: blanks are dropped before the 1-based line count that names a
            // violation runs, so the blank line 2 in the raw script never makes "Open at six." read
            // as line 3.
            var result = AdScriptParser.Parse("ANNOUNCER: Corner Coffee.\n\nOpen at six.", maxLineChars: 200);

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.Format, refused.Violation.RuleId);
            Assert.Equal("line 2 has no voice tag while others do", refused.Violation.Reason);
        }

        [Fact]
        public void AnUntaggedLineOverTheBudgetStillRefusesOnTheBudget()
        {
            var overBudgetLine = new string('x', 201);

            var result = AdScriptParser.Parse(overBudgetLine, maxLineChars: 200);

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.Format, refused.Violation.RuleId);
            Assert.Contains("per-line budget", refused.Violation.Reason, StringComparison.Ordinal);
        }

        [Fact]
        public void APlainSentenceScriptWithANon555NumberRefusesOnThe555Rule()
        {
            // AdScriptRuleIds.PhoneShape is STORY-426's "the 555 rule" — the SAME shipped check
            // Story390 pinned, unchanged by the plain-sentence pre-pass (SPEC F160.3).
            var result = Validate("Corner Coffee.\nCall us at (406) 222-0100 today.");

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.PhoneShape, refused.Violation.RuleId);
        }
    }
}

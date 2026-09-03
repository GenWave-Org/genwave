// STORY-390 — The station writes its own ads (the real crossing: GenWave.Tts.AdScriptWriter meets the
// REAL GenWave.Ads.AdScriptValidator through the delegate · F160.1/.2/.3 · PLAN T400 review F2)
//
// Every OTHER Story390 fact fakes one side or the other: GenWave.Tts.Tests' own AdScriptWriter specs
// script the validate delegate directly (never touching the real validator), and
// Story390_AdScriptValidator.cs exercises AdScriptValidator.Validate directly (never touching the real
// writer). This file is the one place both sides are REAL at once — the exact adapter shape PLAN T402's
// own AdSpotWorker (this project) will build once it exists: a delegate that closes over
// AdScriptValidator.Validate and hands GenWave.Tts back its own minimal AdScriptValidationOutcome.
//
// PLAN T400 review F1's own regression (a legitimate multi-line spot corrupted by whole-blob hygiene)
// is proven fixed HERE, at the real cross-project boundary, not only via a Tts-side capturing delegate
// — see ScenarioTheF1RegressionIsFixedForReal below.

using System.Net;
using System.Text;
using System.Text.Json;
using GenWave.Ads.Tests.Fakes;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts;

namespace GenWave.Ads.Tests.Specs;

public static class FeatureAdScriptWriterMeetsTheRealValidator
{
    static AdScriptWriteRequest Request(int spotSeconds, int maxLineChars, double toleranceRatio = 0.4) =>
        new(
            Brand: "Cravin's Diner", Premise: "A retro diner with a twist", Tone: "warm and playful",
            spotSeconds, AudiencePosture.Everyone, maxLineChars, toleranceRatio);

    /// <summary>The exact adapter PLAN T402's own AdSpotWorker will build: closes over the REAL
    /// AdScriptValidator.Validate, translating its GenWave.Ads-owned result into the minimal
    /// GenWave.Tts.AdScriptValidationOutcome contract that crosses the L10 boundary.</summary>
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

    public sealed class ScenarioAWellFormedSpotClearsTheRealValidator
    {
        [Fact]
        public async Task AWellFormedThirtySecondSpotIsAcceptedEndToEnd()
        {
            // Given a well-formed 30s reply (ANNOUNCER-led, two voices, a 555 number)...
            var reply = string.Join('\n', new[]
            {
                "ANNOUNCER: Cravin's Diner has a deal so good it's almost illegal.",
                "VOICE1: Almost. Stop by and taste the difference tonight.",
                "ANNOUNCER: Call 555-0142 - that's 555-0142 - Cravin's Diner.",
            });
            var writer = BuildWriter(ServeSameReplyEveryTime(reply));
            var validationRequest = new AdScriptValidationRequest(
                Posture: AudiencePosture.Everyone, MaxLineChars: 200, SpotSeconds: 30, ToleranceRatio: 0.4);

            // When the writer generates one spot, validated by the REAL AdScriptValidator...
            var result = await writer.WriteAsync(
                Request(30, 200), RealValidatorDelegate(validationRequest, new FakePatterDurationEstimator()),
                CancellationToken.None);

            // Then the spot is accepted end to end.
            Assert.IsType<AdScriptWriteResult.Success>(result);
        }
    }

    // PLAN T400 review F1's own repro, proven at the real cross-project boundary: a legitimate 4-line,
    // 220-char-per-line, 60s spot collapsed to one ~913-char blob under the OLD whole-blob hygiene and
    // failed the 450-char per-line budget it never actually broke (913 > 450). Line-aware hygiene fixes
    // this for real, through the ACTUAL GenWave.Ads.AdScriptValidator — not only a Tts-side capturing
    // delegate (see GenWave.Tts.Tests' own FeatureAdScriptWriter facts for that half).
    public sealed class ScenarioTheF1RegressionIsFixedForReal
    {
        [Fact]
        public async Task AFourLineTwoHundredTwentyCharSixtySecondSpotIsAcceptedEndToEnd()
        {
            var filler = new string('x', 220);
            var reply = string.Join('\n', new[]
            {
                $"ANNOUNCER: {filler}",
                $"VOICE1: {filler}",
                $"ANNOUNCER: {filler}",
                $"VOICE1: {filler}",
            });
            var writer = BuildWriter(ServeSameReplyEveryTime(reply));
            // Llm:MaxCopyChars default (450) — each line (220 chars) is comfortably under it; the OLD
            // whole-blob bug merged all four lines into one ~913-char "line", which is what actually
            // failed against this SAME 450-char ceiling.
            var validationRequest = new AdScriptValidationRequest(
                Posture: AudiencePosture.Everyone, MaxLineChars: 450, SpotSeconds: 60, ToleranceRatio: 0.4);

            var result = await writer.WriteAsync(
                Request(60, 450), RealValidatorDelegate(validationRequest, new FakePatterDurationEstimator()),
                CancellationToken.None);

            Assert.IsType<AdScriptWriteResult.Success>(result);
        }
    }
}

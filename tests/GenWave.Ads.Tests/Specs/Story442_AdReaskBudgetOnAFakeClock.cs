// STORY-442 — Time-budget facts run on a fake clock (gh-#723 · SPEC F181.3 · PLAN T484)
//
// BDD specification — xUnit. AC5/AC6: AdScriptWriter's per-attempt Llm:TimeoutSeconds budget is a
// fake-clock cancellation (CancellationTokenSource(delay, timeProvider)), so a completion that never
// arrives resolves to Timeout when FAKE time passes the budget — the fact finishes in milliseconds of
// wall clock. The LLM double parks every request until its token cancels (never a real wait).
// The writer is built the Story390 way (real recorder/ring/counters, fakes at the HTTP/options/mode
// seams) with a FakeTimeProvider in place of TimeProvider.System.
//
// Went green at T484: AdScriptWriter.WriteAsync now builds each attempt's budget as
// new CancellationTokenSource(delay, timeProvider), so fake time advances it instead of a real wait.

using System.Diagnostics;
using GenWave.Ads.Tests.Fakes;
using GenWave.Core.Domain;
using GenWave.Tts;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Ads.Tests.Specs;

public static class FeatureAdReaskBudgetOnAFakeClock
{
    const int BudgetSeconds = 5;
    static readonly TimeSpan WallClockCeiling = TimeSpan.FromMilliseconds(500);

    static AdScriptWriter BuildWriter(FakeTimeProvider time, HttpMessageHandler handler)
    {
        var options = new FakeOptionsMonitor<LlmOptions>(new LlmOptions
        {
            Endpoint = "http://fake-llm.local", Model = "test-model", TimeoutSeconds = BudgetSeconds,
        });
        return new AdScriptWriter(
            new SingleHandlerHttpClientFactory(handler),
            options,
            new LlmCallRecorder(new LlmCallRing(options), new LlmCallCauseCounters(time)),
            new FakeDegradationModeReader(),
            new NoOpLogger<AdScriptWriter>(),
            time);
    }

    /// <summary>Never answers: parks until the writer's own attempt budget cancels the request.</summary>
    static FakeHttpMessageHandler NeverCompletes() => new(async (_, ct) =>
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        throw new InvalidOperationException("unreachable — the attempt budget cancels first");
    });

    static AdScriptWriteRequest Request() => new(
        SponsorName: "Cravin's Diner", Premise: "A retro diner with a twist", Tone: "warm and playful",
        SpotSeconds: 30, AudiencePosture.Everyone, MaxLineChars: 200, ToleranceRatio: 0.4);

    // ---------------------------------------------------------------------
    // HAPPY PATH — the budget is decided by fake time
    // ---------------------------------------------------------------------

    public sealed class ScenarioFakeTimePassesTheBudgetWhileTheLlmNeverAnswers : IAsyncLifetime
    {
        AdScriptWriteResult? result;
        TimeSpan elapsed;

        public async Task InitializeAsync()
        {
            var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero));
            var writer = BuildWriter(time, NeverCompletes());

            var clock = Stopwatch.StartNew();
            var pending = writer.WriteAsync(Request(), _ => new AdScriptValidationOutcome.Accepted(), CancellationToken.None);
            time.Advance(TimeSpan.FromSeconds(BudgetSeconds + 1));
            result = await pending;
            elapsed = clock.Elapsed;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheAttemptResolvesToTimeout() =>
            Assert.Equal(LlmCallCause.Timeout, (result as AdScriptWriteResult.Failed)?.Cause);

        [Fact]
        public void TheFactNeverWaitedOnTheWall() => Assert.True(elapsed < WallClockCeiling, $"took {elapsed}");
    }
}

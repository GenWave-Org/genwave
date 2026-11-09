// STORY-534 AC6/AC7 wire proof — a repeating LLM through the real container (SPEC F218.11-F218.13 · PLAN T693)
//
// Boots the real composition root (Program.cs, no DB) against a stub completions endpoint that
// repeats a stored line, resolves the production ISegmentCopyWriter, and reads the booth-log row
// back off the real IStationEventSink -> BoothLogWriter -> BoothLogDrainService chain. Only the
// utterance store and the booth-log appender (the two Postgres edges) are faked.

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace GenWave.Host.Tests.Specs;

sealed class RepeatingDjFactory(string llmEndpoint, IUtteranceStore store, IBoothLogAppender appender)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", LlmCompletionsWebFactory.Password);
        builder.UseSetting("Llm:Endpoint", llmEndpoint);
        builder.UseSetting("Llm:Model", LlmCompletionsWebFactory.Model);
        builder.ConfigureTestServices(services =>
        {
            // Keep only the booth-log drain loop; no engine, feeder or other background reach.
            var drain = services.First(d => d.ServiceType == typeof(IHostedService)
                && d.ImplementationType?.Name == "BoothLogDrainService");
            services.RemoveAll<IHostedService>();
            services.Add(drain);
            services.RemoveAll<IUtteranceStore>();
            services.AddSingleton(store);
            services.RemoveAll<IBoothLogAppender>();
            services.AddSingleton(appender);
        });
    }
}

file sealed class SeededUtteranceStore(string recentLine) : IUtteranceStore
{
    public List<NewUtterance> Recorded { get; } = [];

    public Task<long> RecordAsync(NewUtterance utterance, CancellationToken ct)
    {
        lock (Recorded) Recorded.Add(utterance);
        return Task.FromResult((long)Recorded.Count);
    }

    public Task MarkAiredAsync(long id, CancellationToken ct) => Task.CompletedTask;

    public Task<IReadOnlyList<UtteranceEntry>> RecentAsync(long personaId, int take, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<UtteranceEntry>>(
        [
            new UtteranceEntry(
                1, personaId, null, UtteranceKind.Patter, "LeadIn", ShiftKey.Unscheduled(personaId, new DateOnly(2026, 11, 9)),
                recentLine, false, DateTimeOffset.UnixEpoch, null),
        ]);

    public Task<int> SweepAsync(CancellationToken ct) => Task.FromResult(0);
}

public static class FeatureRepeatCaughtWire
{
    const long PersonaId = 7;
    const string Recent = "Here comes a golden hour groove from the vault tonight.";
    const string Repeating = "Settle in for a golden hour groove from the vault, folks.";

    public sealed record RenderOutcome(
        string CopyText, int LlmCalls, bool StoredFallback, IReadOnlyList<BoothLogAppendRequest> RepeatCaughtRows);

    /// <summary>Boots the container and renders once, so every fact reads the same run.</summary>
    public sealed class RepeatingRender : IAsyncLifetime
    {
        LlmCompletionsStub? stub;
        RepeatingDjFactory? factory;

        RenderOutcome? outcome;

        public RenderOutcome Outcome =>
            outcome ?? throw new InvalidOperationException("The fixture has not rendered yet.");

        public async Task InitializeAsync()
        {
            stub = await LlmCompletionsStub.StartAsync();
            stub.ReplyContent = Repeating;
            var store = new SeededUtteranceStore(Recent);
            var appender = new FakeBoothLogAppender();
            factory = new RepeatingDjFactory(stub.BaseUri.ToString(), store, appender);

            var request = new SegmentRequest(
                SegmentKind.LeadIn, "af_heart", "GenWave",
                new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
                DateTimeOffset.UtcNow, "test-station")
            {
                PersonaId = PersonaId,
                ShiftKeyText = ShiftKey.Unscheduled(PersonaId, new DateOnly(2026, 11, 9)).ToString(),
            };

            var copy = await factory.Services.GetRequiredService<ISegmentCopyWriter>()
                .WriteAsync(request, CancellationToken.None);
            // The host owns the drain loop (started on boot, stopped on factory dispose); wait for its rows.
            await appender.WaitForCallsAsync(2, TimeSpan.FromSeconds(5));

            outcome = new RenderOutcome(
                copy.Text,
                stub.Requests.Count,
                store.Recorded.Single().Fallback,
                appender.Calls.Where(c => c.Kind == "repeat-caught").ToList());
        }

        public async Task DisposeAsync()
        {
            if (factory is not null) await factory.DisposeAsync();
            if (stub is not null) await stub.DisposeAsync();
        }
    }

    public sealed class ScenarioARepeatingLlmAirsTheTemplateAndLogsTheCatch(RepeatingRender run)
        : IClassFixture<RepeatingRender>
    {
        [Fact]
        public void TheTemplateAirs() =>
            Assert.Equal("Coming up: Astral Plane by Valerie June.", run.Outcome.CopyText);

        [Fact]
        public void TwoLlmCallsWereMade() => Assert.Equal(2, run.Outcome.LlmCalls);

        [Fact]
        public void TheStoredUtteranceIsAFallback() => Assert.True(run.Outcome.StoredFallback);

        [Fact]
        public void TheBoothLogHasARepeatCaughtRowPerCaughtAttempt() => Assert.Equal(2, run.Outcome.RepeatCaughtRows.Count);

        [Fact]
        public void EveryRowNamesThePersona() =>
            Assert.All(run.Outcome.RepeatCaughtRows, r => Assert.Equal(PersonaId, r.PersonaId));

        [Fact]
        public void EveryRowNamesThePhrase() =>
            Assert.All(run.Outcome.RepeatCaughtRows, r => Assert.Contains("golden hour groove from the vault", r.Summary));
    }
}

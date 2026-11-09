// STORY-532 — Every DJ line is on the record (gh-#293 gh-#920 · SPEC F218 · PLAN T684 T685 T682)
//
// BDD specification — xUnit. AC1 (LeadIn accepted, LLM-less station) and AC2 (truth-gate double
// rejection, degraded mode) are active (PLAN T684).
//
// Spans three files by seam: AC1/AC2 (T684, LlmCopyWriter) here; AC3/AC4/AC6 (T684/T685,
// BoothLogWriter + engine callback) in tests/GenWave.Host.Tests/Specs/Story532_EveryDjLineIsOnTheRecord_Booth.cs;
// AC5/AC7 (T682, IUtteranceStore) in tests/GenWave.MediaLibrary.Tests/Specs/Story532_EveryDjLineIsOnTheRecord_Store.cs (real Postgres, so it lives beside the DatabaseFixture; GenWave.Memory stays Core + Abstractions only).

namespace GenWave.Tts.Tests.Specs;

using System.Net;
using System.Text;
using System.Text.Json;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

public static class FeatureEveryDjLineIsOnTheRecordCopy
{
    const long PersonaId = 7;
    const long ShowId = 3;

    // Sunday 11:50 station-local: "this Saturday" is a mechanical truth-gate violation (Story351).
    static readonly DateTimeOffset FixedStationLocalNow = new(2026, 8, 16, 11, 50, 0, TimeSpan.Zero);
    const string CleanReply = "Coming up next a classic from the vault.";
    const string WrongWeekdayCopy = "This Saturday has been one for the books so let's keep it going.";

    static SegmentRequest LeadInRequest() =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            FixedStationLocalNow, "test-station")
        {
            PersonaId = PersonaId,
            ShowId = ShowId,
            ShiftKeyText = ShiftKey.Scheduled(PersonaId, DayOfWeek.Tuesday, 360, new DateOnly(2026, 11, 10)).ToString(),
        };

    static IHttpClientFactory RepliesWith(Func<int, string> reply)
    {
        var calls = 0;
        return new SingleHandlerHttpClientFactory(new FakeHttpMessageHandler((_, _) =>
        {
            var content = reply(++calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } }),
                    Encoding.UTF8, "application/json"),
            });
        }));
    }

    static LlmCopyWriter BuildWriter(
        Func<int, string> reply, RecordingUtteranceStore store, IDegradationModeReader? degradation = null,
        string endpoint = "http://fake-llm.local") =>
        new(
            new TemplateCopyWriter(new PatterTemplateRenderer()),
            RepliesWith(reply),
            new TestOptionsMonitor<LlmOptions>(new LlmOptions
            {
                Endpoint = endpoint,
                Model = "test-model",
                TimeoutSeconds = 5,
                MaxCopyChars = 450,
            }),
            new LlmCopyStatusHolder(),
            new FakeActivePersonaAccessor(),
            NullLogger<LlmCopyWriter>.Instance,
            TimeProvider.System,
            new LlmCallRecorder(
                new LlmCallRing(new TestOptionsMonitor<LlmOptions>(new LlmOptions())),
                new LlmCallCauseCounters(TimeProvider.System)),
            degradation ?? new FakeDegradationModeReader(),
            new FakeStationClockProvider(FixedStationLocalNow),
            utteranceStore: store);

    static string TemplateLine() =>
        new PatterTemplateRenderer().Expand(LeadInRequest());

    // AC1 — one break, one row
    // entry point: LlmCopyWriter.WriteAsync
    public sealed class ScenarioOneBreakOneRow : IAsyncLifetime
    {
        readonly RecordingUtteranceStore store = new();
        SegmentCopy? copy;

        public async Task InitializeAsync() =>
            // Given an active persona and a LeadIn request
            // When  LlmCopyWriter.WriteAsync accepts a copy
            copy = await BuildWriter(_ => CleanReply, store).WriteAsync(LeadInRequest(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void StationUtteranceHoldsExactlyOneNewRowForThatPersona() =>
            Assert.Equal(PersonaId, Assert.Single(store.Recorded).PersonaId);

        [Fact]
        public void ItsTextIsByteEqualToTheCopyHandedToTheRenderer() =>
            Assert.Equal(copy?.Text, Assert.Single(store.Recorded).Text, StringComparer.Ordinal);

        [Fact]
        public void ItsKindIsPatter() => Assert.Equal(UtteranceKind.Patter, Assert.Single(store.Recorded).Kind);

        [Fact]
        public void ItsSegmentKindIsLeadIn() => Assert.Equal("LeadIn", Assert.Single(store.Recorded).SegmentKind);

        [Fact]
        public void FallbackIsFalse() => Assert.False(Assert.Single(store.Recorded).Fallback);

        [Fact]
        public void ItCarriesTheShowId() => Assert.Equal(ShowId, Assert.Single(store.Recorded).ShowId);

        [Fact]
        public void ItCarriesTheRequestsShiftKey() =>
            Assert.Equal("7|Tuesday|360|2026-11-10", Assert.Single(store.Recorded).ShiftKey.ToString());

        [Fact]
        public void TheReturnedCopyNamesTheStoredRow() => Assert.Equal(1L, copy?.UtteranceId);
    }

    // AC2 — the fallback is on the record too
    public sealed class ScenarioTheFallbackIsOnTheRecordToo : IAsyncLifetime
    {
        readonly RecordingUtteranceStore store = new();

        public async Task InitializeAsync() =>
            // Given the truth gate rejects the copy twice (the wrong weekday, asked and re-asked)
            // When  the template line is used instead
            await BuildWriter(_ => WrongWeekdayCopy, store).WriteAsync(LeadInRequest(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheStoredRowHasFallbackTrue() => Assert.True(Assert.Single(store.Recorded).Fallback);

        [Fact]
        public void TheStoredRowHasTheTemplateText() =>
            Assert.Equal(TemplateLine(), Assert.Single(store.Recorded).Text, StringComparer.Ordinal);
    }

    // An LLM-less station (no endpoint) airs template copy; that line is on the record too.
    public sealed class ScenarioAnLlmLessStationRecordsItsTemplateCopy : IAsyncLifetime
    {
        readonly RecordingUtteranceStore store = new();

        public async Task InitializeAsync() =>
            // Given Llm:Endpoint is empty and a LeadIn request
            // When  LlmCopyWriter.WriteAsync serves the template line
            await BuildWriter(_ => CleanReply, store, endpoint: "").WriteAsync(LeadInRequest(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheStoredRowHasFallbackTrue() => Assert.True(Assert.Single(store.Recorded).Fallback);

        [Fact]
        public void TheStoredRowHasTheTemplateText() =>
            Assert.Equal(TemplateLine(), Assert.Single(store.Recorded).Text, StringComparer.Ordinal);
    }

    // Degradation routes around the LLM entirely; the template line that airs is still on the record.
    public sealed class ScenarioDegradedTemplateCopyIsOnTheRecord : IAsyncLifetime
    {
        readonly RecordingUtteranceStore store = new();

        public async Task InitializeAsync()
        {
            // Given degradation mode is Hard, so the gated writer never calls the LLM
            var template = new TemplateCopyWriter(new PatterTemplateRenderer());
            var llmOptions = new TestOptionsMonitor<LlmOptions>(new LlmOptions
            {
                Endpoint = "https://llm.example/v1",
                DegradationPin = "hard",
            });
            var controller = new DegradationController(
                new FakeDependencyHealth(),
                new LlmCopyStatusHolder(),
                llmOptions,
                new TestOptionsMonitor<DegradationOptions>(new DegradationOptions()),
                TimeProvider.System,
                new CapturingLogger<DegradationController>());
            var llmWriter = new LlmCopyWriter(
                template,
                new FakeHttpClientFactory(),
                llmOptions,
                new LlmCopyStatusHolder(),
                new FakeActivePersonaAccessor(),
                NullLogger<LlmCopyWriter>.Instance,
                TimeProvider.System,
                new LlmCallRecorder(
                    new LlmCallRing(new TestOptionsMonitor<LlmOptions>(new LlmOptions())),
                    new LlmCallCauseCounters(TimeProvider.System)),
                controller,
                utteranceStore: store);
            var gated = new DegradationGatedCopyWriter(
                controller, llmWriter, template, new TestOptionsMonitor<DegradationOptions>(new DegradationOptions()), TimeProvider.System);

            // When  the lead-in is written
            await gated.WriteAsync(LeadInRequest(), CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheStoredRowHasFallbackTrue() => Assert.True(Assert.Single(store.Recorded).Fallback);

        [Fact]
        public void TheStoredRowHasTheTemplateText() =>
            Assert.Equal(TemplateLine(), Assert.Single(store.Recorded).Text, StringComparer.Ordinal);
    }

    // The planner leaves a request unstamped when no persona is active — nothing to record.
    public sealed class ScenarioNoActivePersonaRecordsNothing : IAsyncLifetime
    {
        readonly RecordingUtteranceStore store = new();

        public async Task InitializeAsync() =>
            await BuildWriter(_ => CleanReply, store)
                .WriteAsync(LeadInRequest() with { PersonaId = null, ShiftKeyText = null }, CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoRowIsStored() => Assert.Empty(store.Recorded);
    }
}

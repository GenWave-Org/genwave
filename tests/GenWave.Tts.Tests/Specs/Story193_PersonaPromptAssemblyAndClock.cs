// STORY-193 — Prompt assembly on personas with a real clock
//
// BDD specification — xUnit (SPEC F71.3, F71.7, F71.8). Relocated from the PLAN's suggested
// GenWave.Orchestration.Tests home to GenWave.Tts.Tests (T37 build note): the seam under test —
// LlmCopyWriter's prompt composition and SpeechCorrectionProvider's merge — lives in GenWave.Tts,
// and GenWave.Orchestration.Tests only references GenWave.Orchestration (no GenWave.Tts project
// reference at all). This file follows the established STORY-119/121/123/188/189 idiom already
// living here — a real Kestrel-backed MockCompletionsServer plus the real, unmodified
// LlmCopyWriter — rather than adding a cross-project reference for one spec file.
//
// AC1/AC2 exercise the real production LlmCopyWriter (the class TtsServiceCollectionExtensions
// wires behind ISegmentCopyWriter) against MockCompletionsServer, asserting on the actual outbound
// prompt content — the "wire acceptance" half of T37. AC3 pins SpeechCorrectionProvider.BuildMerged
// directly: the merge precedence itself needs no HTTP round trip to prove.
//
// The /spec-generated pending scaffold this implements/replaces previously lived at
// tests/GenWave.Orchestration.Tests/Specs/Story193_PersonaPromptAssemblyAndClock.cs (three
// [Fact(Skip = "pending — PLAN T37 (/build-loop)")] stubs) — deleted, not left behind, once this
// file took over that filename one project over.
// See docs/PLAN.md Epic T, docs/STORIES.md STORY-193.

namespace GenWave.Tts.Tests.Specs;

using System.Text.Json;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeaturePersonaPromptAssemblyAndClock
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    static SegmentRequest LeadInRequest() =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            DateTimeOffset.UtcNow, "test-station");

    static Persona BuildPersona() => new(1, "DJ Nova", "", "", "", DateTime.UtcNow, DateTime.UtcNow);

    static PersonaCard BuildCard(IReadOnlyList<string> quirks, IReadOnlyList<PersonaCorrection>? corrections = null) =>
        new(
            SchemaVersion: 1,
            Name: "DJ Nova",
            Tagline: "",
            Soul: "A washed-up 90s radio jock chasing one more big break.",
            Quirks: quirks,
            Voice: new VoiceSpec(Engine: "", VoiceId: "", Pace: 1.0, Language: "en"),
            EnergyDisposition: 0,
            Lore: [],
            Corrections: corrections ?? []);

    static LlmCopyWriter BuildWriter(string endpoint, FakeActivePersonaAccessor accessor, TimeProvider? timeProvider = null) =>
        new(
            new TemplateCopyWriter(new PatterTemplateRenderer()),
            new FakeHttpClientFactory(),
            new TestOptionsMonitor<LlmOptions>(new LlmOptions
            {
                Endpoint = endpoint,
                Model = "test-model",
                TimeoutSeconds = 5,
                MaxCopyChars = 450,
            }),
            new LlmCopyStatusHolder(),
            accessor,
            new CapturingLogger<LlmCopyWriter>(),
            timeProvider ?? TimeProvider.System);

    static string ExtractMessageContent(string body, string role)
    {
        using var doc = JsonDocument.Parse(body);
        foreach (var message in doc.RootElement.GetProperty("messages").EnumerateArray())
        {
            if (message.GetProperty("role").GetString() == role)
                return message.GetProperty("content").GetString() ?? "";
        }

        return "";
    }

    // ---------------------------------------------------------------------
    // AC1 — quirks sampled (F71.3)
    // ---------------------------------------------------------------------

    public sealed class ScenarioQuirksAreSampled : IAsyncLifetime
    {
        static readonly string[] AllQuirks =
            ["QuirkAlpha", "QuirkBravo", "QuirkCharlie", "QuirkDelta", "QuirkEcho"];

        MockCompletionsServer mock = null!;
        FakeActivePersonaAccessor accessor = null!;

        public async Task InitializeAsync()
        {
            mock = await MockCompletionsServer.StartAsync();
            accessor = new FakeActivePersonaAccessor { Persona = BuildPersona(), Card = BuildCard(AllQuirks) };
        }

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task EveryPromptShowsTwoOrThreeQuirksNeverAllFive()
        {
            // Given a persona with five quirks
            var writer = BuildWriter(mock.BaseUri.ToString(), accessor);

            // When many prompts are assembled
            const int Iterations = 40;
            for (var i = 0; i < Iterations; i++)
                await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            Assert.Equal(Iterations, mock.RequestCount);

            // Then each prompt contains 2-3 quirks and never all five (F71.3, AC1)
            foreach (var captured in mock.Requests)
            {
                var systemContent = ExtractMessageContent(captured.Body, "system");
                var shownCount = AllQuirks.Count(quirk => systemContent.Contains(quirk));

                Assert.InRange(shownCount, 2, 3);
            }
        }
    }

    // ---------------------------------------------------------------------
    // AC2 — the DJ's clock (F71.8)
    // ---------------------------------------------------------------------

    public sealed class ScenarioStationClock : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task PromptCarriesTheInjectedStationLocalDateWeekdayAndTime()
        {
            // Given an injected station-local clock (2026-07-20 09:41 — a Monday, UTC station-local)
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 20, 9, 41, 0, TimeSpan.Zero));
            var writer = BuildWriter(mock.BaseUri.ToString(), new FakeActivePersonaAccessor(), clock);

            // When any copywriter prompt is assembled
            await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            var userContent = ExtractMessageContent(mock.Requests[0].Body, "user");

            // Then it contains the injected current date, weekday, and time (F71.8, AC2)
            Assert.Contains("Monday", userContent);
            Assert.Contains("July 20, 2026", userContent);
            Assert.Contains("9:41 AM", userContent);
        }
    }

    // ---------------------------------------------------------------------
    // AC3 — correction precedence seam (F71.7)
    // ---------------------------------------------------------------------

    public sealed class ScenarioStationOverCardCorrectionPrecedence
    {
        [Fact]
        public void StationRuleWinsOverAnIdenticalCaseInsensitiveCardFrom()
        {
            // Given a station correction and a card correction with the same From
            var station = SpeechCorrectionSet.Create([new SpeechCorrection("Kokoro", "koh-KOH-roh")]);
            var cardCorrections = new List<SpeechCorrection> { new("KOKORO", "kaw-KOR-oh") };

            // When the merged set is built
            var merged = SpeechCorrectionProvider.BuildMerged(station, cardCorrections);
            var result = merged.Apply("Kokoro is live.", out var firedFroms);

            // Then the station rule wins (F71.7, AC3)
            Assert.Equal("koh-KOH-roh is live.", result);
            Assert.Contains("Kokoro", firedFroms);
        }
    }
}

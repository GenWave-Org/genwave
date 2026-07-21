// STORY-121 — The active persona flavors copy and voice (prompt half)
//
// BDD specification — xUnit. The LlmCopyWriter prompt gains backstory + style sections
// when a persona is active; neutral house scaffold otherwise (blurbs work persona-less,
// F35.2 — gitea-#175 ships independently of gitea-#176). Voice resolution is the Orchestration half
// (Story121_PersonaVoiceResolution). Landed T6.
// See docs/PLAN.md Epic T.

namespace GenWave.Tts.Tests.Specs;

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeaturePersonaPromptSections
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    static SegmentRequest LeadInRequest() =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            DateTimeOffset.UtcNow, "test-station");

    static Persona BuildPersona(string backstory = "", string style = "") =>
        new(1, "DJ Nova", backstory, style, "", DateTime.UtcNow, DateTime.UtcNow);

    static LlmCopyWriter BuildWriter(string endpoint, FakeActivePersonaAccessor accessor) =>
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
            TimeProvider.System);

    static string ExtractSystemContent(string body)
    {
        using var doc = JsonDocument.Parse(body);
        foreach (var message in doc.RootElement.GetProperty("messages").EnumerateArray())
        {
            if (message.GetProperty("role").GetString() == "system")
                return message.GetProperty("content").GetString() ?? "";
        }

        return "";
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — persona reaches the prompt; absence degrades to neutral
    // ---------------------------------------------------------------------

    public sealed class ScenarioActivePersonaShapesThePrompt : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;
        FakeActivePersonaAccessor accessor = null!;

        public async Task InitializeAsync()
        {
            mock = await MockCompletionsServer.StartAsync();
            accessor = new FakeActivePersonaAccessor
            {
                Persona = BuildPersona(
                    backstory: "A washed-up 90s radio jock chasing one more big break.",
                    style: "Fast-talking, wisecracking, name-drops constantly."),
            };
        }

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task RequestBodyCarriesTheBackstorySection()
        {
            var writer = BuildWriter(mock.BaseUri.ToString(), accessor);

            await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            var systemContent = ExtractSystemContent(mock.Requests[0].Body);

            // (F34.3, F35.3, AC1).
            Assert.Contains("A washed-up 90s radio jock chasing one more big break.", systemContent);
        }

        [Fact]
        public async Task RequestBodyCarriesTheStyleSection()
        {
            var writer = BuildWriter(mock.BaseUri.ToString(), accessor);

            await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            var systemContent = ExtractSystemContent(mock.Requests[0].Body);

            // (F34.3, F35.3, AC1).
            Assert.Contains("Fast-talking, wisecracking, name-drops constantly.", systemContent);
        }
    }

    public sealed class ScenarioNoPersonaMeansNeutralHouseStyle : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task PromptCarriesTheNeutralScaffoldWithNoPersonaSection()
        {
            // ActiveId 0/absent → neutral scaffold; blurbs still generate (F35.2, AC4).
            var accessor = new FakeActivePersonaAccessor(); // Persona stays null
            var writer = BuildWriter(mock.BaseUri.ToString(), accessor);

            var result = await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            var systemContent = ExtractSystemContent(mock.Requests[0].Body);
            var neutralScaffoldOnly =
                systemContent.Contains("radio DJ") &&
                !systemContent.Contains("Backstory:") &&
                !systemContent.Contains("Style:");

            Assert.True(neutralScaffoldOnly, systemContent);
            Assert.Equal(mock.ReplyContent, result.Text);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the persona's reach is exactly two things (prompt + voice)
    // ---------------------------------------------------------------------

    public sealed class ScenarioSafeAuthoringIsUntouched
    {
        [Fact]
        public async Task SafeSegmentAuthorUsesTheExplicitVoiceRegardlessOfActivePersona()
        {
            // POST /api/safe-segments' pipeline: explicit voice wins; persona plays no part
            // (F35.3, AC5).
            //
            // Strongest honest form given SafeSegmentAuthor's own dependency list: it never takes an
            // IActivePersonaAccessor (asserted below by construction — the only ctor is the one
            // exercised here, with no accessor parameter to satisfy), so an "active persona" set up
            // alongside it (accessor.Persona below) has no seam through which it could ever reach
            // this pipeline. The functional half proves the explicit request Voice — never
            // DefaultVoice, never any persona voice — is what actually reaches synthesis.
            var accessor = new FakeActivePersonaAccessor
            {
                Persona = BuildPersona() with { Voice = "am_onyx" },
            };
            Assert.NotNull(accessor.Persona); // an active persona genuinely exists in this test

            var authoredRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var synth = new FakeTtsSynthesizer();
            var mixer = new FakeAudioMixer();
            var loudness = new FakeLoudnessAnalyzer();
            var cue = new FakeCueAnalyzer();
            var energy = new FakeEnergyAnalyzer();
            var writer = new FakeAuthoredCatalogWriter();
            var opts = Microsoft.Extensions.Options.Options.Create(new TtsOptions { Format = "wav" });
            var author = new SafeSegmentAuthor(
                synth, mixer, loudness, cue, energy, writer, opts, NullLogger<SafeSegmentAuthor>.Instance);

            try
            {
                var request = new SafeSegmentRequest(
                    Text: "Please stand by.",
                    LibraryId: 1,
                    StationName: "GenWave",
                    DefaultVoice: "af_heart",
                    AuthoredRoot: authoredRoot,
                    BedDuckDb: -12.0,
                    BedPadSeconds: 1.5,
                    Voice: "explicit_voice");

                await author.AuthorAsync(request, CancellationToken.None);

                // Neither DefaultVoice ("af_heart") nor the active persona's voice ("am_onyx") —
                // the explicit request voice is what synthesis actually receives.
                Assert.Equal("explicit_voice", synth.LastVoice);
            }
            finally
            {
                if (Directory.Exists(authoredRoot)) Directory.Delete(authoredRoot, recursive: true);
                if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
            }
        }
    }
}

// gh-#291 — Dislike rules never read as taste matches
//
// BDD specification — xUnit. Found by the gh-#270 spike: PersonaRanker keeps every matched rule in
// FiredRules regardless of weight sign (deliberate — the booth-log pick stamp persists the SIGNED
// weight, SPEC F86.1, and the admin UI renders it), but the prompt side ignored the sign, so a
// disliked-but-winning track was described to the DJ as "matches the persona's taste for
// {dislike-label}" — the DJ warmly crediting a rule that voted AGAINST the track. Chosen shape:
// diagnostics stay complete; LlmPromptBuilder.DescribeFiredRules (the one function shared by the
// prompt's taste line AND the writer's taste memory) excludes non-positive weights, and nothing —
// no "despite..." line, the gh-#270 complaint-class family — ever takes a dislike's place.
// Prompt-content assertions follow the F71.8 idiom (assert on the assembled prompt, never on model
// output) — the pure-builder and MockCompletionsServer shapes Story214 already established.

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureDislikeRulesNeverSpokenAsTaste
{
    // -------------------------------------------------------------------------
    // Helpers (Story214's shapes)
    // -------------------------------------------------------------------------

    static readonly DateTimeOffset FixedLocalNow = new(2026, 7, 20, 9, 41, 0, TimeSpan.Zero);

    const string ClockLine = "Current date/time (station-local): irrelevant";

    static readonly TasteContext AnyTime = new([], null, null);

    static TasteRule ArtistRule(string artist, double weight) =>
        new(new TastePredicate(artist, Genre: null, Tag: null), AnyTime, weight);

    static TasteRule GenreRule(string genre, double weight) =>
        new(new TastePredicate(Artist: null, genre, Tag: null), AnyTime, weight);

    static MediaItem PlainTrack(PersonaPickDiagnostics? personaPick) =>
        new(
            "m1", "/media/x.mp3", "Astral Plane", default, "Valerie June",
            Album: "The Order of Time", Genre: "Folk", Year: 2017, PersonaPick: personaPick);

    static SegmentRequest LeadInRequest(PersonaPickDiagnostics? personaPick) =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave", PlainTrack(personaPick), FixedLocalNow, "test-station");

    static PersonaPickDiagnostics Pick(params TasteRule[] firedRules) =>
        new(PoolSize: 12, TopScores: [0.8, 0.6, 0.4], FiredRules: firedRules, IsExploration: false);

    static LlmCopyWriter BuildWriter(string endpoint) =>
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
            new FakeActivePersonaAccessor(),
            new CapturingLogger<LlmCopyWriter>(),
            new FakeTimeProvider(FixedLocalNow),
            new LlmCallRing(new TestOptionsMonitor<LlmOptions>(new LlmOptions())),
            new FakeDegradationModeReader());

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
    // The disliked-but-winning track: a like and a dislike both fired
    // ---------------------------------------------------------------------

    public static class ScenarioDislikedButWinningTrack
    {
        // Arrange: the pick won on the like rule; the dislike matched too and rides FiredRules
        // (the complete-diagnostics shape — see Gh291_FiredRulesKeepDislikes on the ranker side).
        // "Synthwave" appears NOWHERE in the track's own metadata, so its only possible route into
        // the prompt is the taste line under test.
        static string AssembledPrompt() =>
            LlmPromptBuilder.BuildUserContent(
                LeadInRequest(Pick(ArtistRule("Valerie June", 0.5), GenreRule("Synthwave", -0.6))),
                ClockLine, []);

        [Fact]
        public static void TheTasteLineNeverNamesTheDislike()
        {
            Assert.DoesNotContain("Synthwave", AssembledPrompt());
        }

        [Fact]
        public static void TheGenuinelyMatchingLikeRuleStillReads()
        {
            Assert.Contains("matches the persona's taste for Valerie June", AssembledPrompt());
        }

        [Fact]
        public static void NoDespiteLineTakesTheDislikesPlace()
        {
            // gh-#291 decision pin: the dislike is EXCLUDED, never rephrased — "despite not being
            // their usual X" is the same complaint-class family gh-#270 eliminated, and a 3B model
            // would overuse it.
            Assert.DoesNotContain("despite", AssembledPrompt(), StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---------------------------------------------------------------------
    // Every fired rule is a dislike: no taste line at all
    // ---------------------------------------------------------------------

    public static class ScenarioOnlyDislikesFired
    {
        [Fact]
        public static void NoTasteLineAppears()
        {
            var prompt = LlmPromptBuilder.BuildUserContent(
                LeadInRequest(Pick(GenreRule("Synthwave", -0.6))), ClockLine, []);

            Assert.DoesNotContain("Taste note", prompt);
        }

        [Fact]
        public static void ThePromptIsByteIdenticalToANoRulesPick()
        {
            // The all-dislike case degrades to exactly the empty-FiredRules shape — no replacement
            // line of any kind (gh-#291: excluded, never rephrased).
            var dislikeOnly = LlmPromptBuilder.BuildUserContent(
                LeadInRequest(Pick(GenreRule("Synthwave", -0.6))), ClockLine, []);
            var noRules = LlmPromptBuilder.BuildUserContent(LeadInRequest(Pick()), ClockLine, []);

            Assert.Equal(noRules, dislikeOnly);
        }

        [Fact]
        public static void AZeroWeightRuleEarnsNoCreditEither()
        {
            // Boundary pin: weight 0 voted neither way — no vote, no credit (DescribeFiredRules
            // keeps strictly-positive weights only).
            var prompt = LlmPromptBuilder.BuildUserContent(
                LeadInRequest(Pick(GenreRule("Synthwave", 0.0))), ClockLine, []);

            Assert.DoesNotContain("Taste note", prompt);
        }
    }

    // ---------------------------------------------------------------------
    // The taste memory never remembers a dislike it never offered
    // ---------------------------------------------------------------------

    public sealed class ScenarioTasteMemoryNeverRemembersADislike : IAsyncLifetime
    {
        // Arrange: break 1's only fired rule is a DISLIKE on "Folk"; break 2 fires a LIKE with the
        // identical label. The filter lives inside DescribeFiredRules — the same function
        // LlmCopyWriter uses to write previousBreakTasteNotes — so break 1 must record nothing,
        // and break 2 must NOT carry the F83.1 recently-voiced marker for a note that was never
        // actually offered on air.

        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task ALikeAfterASameLabelDislikeCarriesNoRepetitionMarker()
        {
            var writer = BuildWriter(mock.BaseUri.ToString());

            await writer.WriteAsync(LeadInRequest(Pick(GenreRule("Folk", -0.6))), CancellationToken.None);
            await writer.WriteAsync(LeadInRequest(Pick(GenreRule("Folk", 0.3))), CancellationToken.None);

            var secondContent = ExtractMessageContent(mock.Requests[1].Body, "user");

            Assert.DoesNotContain("vary the phrasing", secondContent);
        }
    }
}

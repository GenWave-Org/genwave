// STORY-214 — Taste becomes audible
//
// BDD specification — xUnit (SPEC F83.1, F83.2, F83.3). PLAN T65 carries
// PickResult{Track, IsExploration, FiredRules} into the copywriter prompt. Prompt-content
// assertions follow the F71.8 idiom (assert on the assembled prompt, never on model output) — the
// same MockCompletionsServer + real, unmodified LlmCopyWriter shape STORY-193's
// Story193_PersonaPromptAssemblyAndClock.cs already established for this project.

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureTasteBecomesAudible
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    // 2026-07-20 09:41 — a Monday, UTC station-local (same fixed instant STORY-193's
    // ScenarioStationClock already pins, so its correctness is not re-derived here).
    static readonly DateTimeOffset FixedLocalNow = new(2026, 7, 20, 9, 41, 0, TimeSpan.Zero);

    static readonly TasteContext AnyTime = new([], null, null);

    static TasteRule ArtistRule(string artist, double weight = 0.5) =>
        new(new TastePredicate(artist, Genre: null, Tag: null), AnyTime, weight);

    static TasteRule GenreRule(string genre, double weight = 0.3) =>
        new(new TastePredicate(Artist: null, genre, Tag: null), AnyTime, weight);

    static MediaItem PlainTrack(PersonaPickDiagnostics? personaPick = null) =>
        new(
            "m1", "/media/x.mp3", "Astral Plane", default, "Valerie June",
            Album: "The Order of Time", Genre: "Folk", Year: 2017, PersonaPick: personaPick);

    static SegmentRequest LeadInRequest(PersonaPickDiagnostics? personaPick = null) =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave", PlainTrack(personaPick), FixedLocalNow, "test-station");

    // Pairs with LeadInRequest above so a spec can mirror Orchestrator.EnqueuePatterAsync's own
    // shape for one unit: a BackAnnounce for the track just played, immediately followed by a
    // LeadIn for the next one.
    static SegmentRequest BackAnnounceRequest(PersonaPickDiagnostics? personaPick = null) =>
        new(SegmentKind.BackAnnounce, "af_heart", "GenWave", PlainTrack(personaPick), FixedLocalNow, "test-station");

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
    // F83.1 — fired rules as optional color, with an anti-repetition posture
    // ---------------------------------------------------------------------

    public sealed class ScenarioFiredRulesAsOptionalColor : IAsyncLifetime
    {
        // Arrange (T65): a pick with two fired taste rules; assemble the copywriter prompt.
        static PersonaPickDiagnostics TwoFiredRulesPick() =>
            new(PoolSize: 12, TopScores: [0.8, 0.6, 0.4], FiredRules: [ArtistRule("Valerie June"), GenreRule("Folk")], IsExploration: false);

        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task ThePromptCarriesTheFiredRules()
        {
            var writer = BuildWriter(mock.BaseUri.ToString());

            await writer.WriteAsync(LeadInRequest(TwoFiredRulesPick()), CancellationToken.None);

            var userContent = ExtractMessageContent(mock.Requests[0].Body, "user");

            // Then the prompt names both fired rules (F83.1)
            Assert.Contains("Valerie June", userContent);
            Assert.Contains("Folk", userContent);
        }

        [Fact]
        public async Task TheRulesArePhrasedAsOptionalNeverMandate()
        {
            var writer = BuildWriter(mock.BaseUri.ToString());

            await writer.WriteAsync(LeadInRequest(TwoFiredRulesPick()), CancellationToken.None);

            var userContent = ExtractMessageContent(mock.Requests[0].Body, "user");

            // prompt posture: "may mention", never "must mention" (F83.1)
            Assert.Contains("may mention", userContent);
            Assert.DoesNotContain("must", userContent, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ConsecutiveBreaksVaryTheAntiRepetitionPosture()
        {
            var writer = BuildWriter(mock.BaseUri.ToString());
            var samePick = TwoFiredRulesPick();

            // Two consecutive on-air breaks, the SAME rules firing both times.
            await writer.WriteAsync(LeadInRequest(samePick), CancellationToken.None);
            await writer.WriteAsync(LeadInRequest(samePick), CancellationToken.None);

            var firstContent = ExtractMessageContent(mock.Requests[0].Body, "user");
            var secondContent = ExtractMessageContent(mock.Requests[1].Body, "user");

            // same rule firing twice ⇒ second prompt carries the recently-voiced marker (F83.1)
            Assert.DoesNotContain("vary the phrasing", firstContent);
            Assert.Contains("vary the phrasing", secondContent);
        }

        [Fact]
        public async Task AConcurrentBackAnnounceAndLeadInPairStillVaryTheAntiRepetitionPosture()
        {
            // Mirrors Orchestrator.EnqueuePatterAsync's own fire-both-then-await shape for one unit
            // (T65 review finding): BOTH renders are started — neither one awaited yet — before
            // either result is awaited, exactly like pendingRenders.Add(tts.RenderAsync(...)) does
            // for the BackAnnounce and the LeadIn back to back. The sequential
            // ConsecutiveBreaksVaryTheAntiRepetitionPosture spec above cannot exercise this: it
            // awaits the first call to completion before the second one is even constructed, so it
            // never puts two WriteAsync calls in flight against the writer at the same time.
            var writer = BuildWriter(mock.BaseUri.ToString());
            var samePick = TwoFiredRulesPick();

            var backAnnounceTask = writer.WriteAsync(BackAnnounceRequest(samePick), CancellationToken.None);
            var leadInTask = writer.WriteAsync(LeadInRequest(samePick), CancellationToken.None);
            await Task.WhenAll(backAnnounceTask, leadInTask);

            // previousBreakTasteNotes is now read AND written inside RequestCompletionAsync's own
            // single-flight critical section, so the mock's arrival order IS the critical-section
            // acquisition order: whichever call reaches the gate first can never see a marker (there
            // is nothing to compare against yet), and whichever reaches it second is guaranteed to
            // observe the first's freshly-written notes — the same fired rules — and so must carry
            // the marker. Symmetric on the SAME pick, so this holds regardless of which of the two
            // concurrent tasks happens to win the race.
            var firstContent = ExtractMessageContent(mock.Requests[0].Body, "user");
            var secondContent = ExtractMessageContent(mock.Requests[1].Body, "user");

            Assert.DoesNotContain("vary the phrasing", firstContent);
            Assert.Contains("vary the phrasing", secondContent);
        }
    }

    // ---------------------------------------------------------------------
    // F83.2 — exploration picks are lampshade-eligible, never rule-attributed
    // ---------------------------------------------------------------------

    public sealed class ScenarioExplorationLampshade : IAsyncLifetime
    {
        // Arrange (T65): an IsExploration pick with zero fired rules.
        static PersonaPickDiagnostics ExplorationPick() =>
            new(PoolSize: 12, TopScores: [0.5, 0.4, 0.3], FiredRules: [], IsExploration: true);

        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync();

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task ThePromptMarksThePickOutsideThePersonasTaste()
        {
            var writer = BuildWriter(mock.BaseUri.ToString());

            await writer.WriteAsync(LeadInRequest(ExplorationPick()), CancellationToken.None);

            var userContent = ExtractMessageContent(mock.Requests[0].Body, "user");

            // lampshade-eligible: "not my usual…" (F83.2)
            Assert.Contains("outside the persona's usual taste", userContent);
        }

        [Fact]
        public async Task ThePromptAttributesNoFiredRule()
        {
            var writer = BuildWriter(mock.BaseUri.ToString());

            await writer.WriteAsync(LeadInRequest(ExplorationPick()), CancellationToken.None);

            var userContent = ExtractMessageContent(mock.Requests[0].Body, "user");

            Assert.DoesNotContain("matches the persona's taste for", userContent);
        }
    }

    // ---------------------------------------------------------------------
    // F83.3 — persona layer off: empty FiredRules, no exploration flag, byte-identical copy
    // ---------------------------------------------------------------------

    public static class SadPathPersonaLayerOff
    {
        // Arrange (T65): persona layer disabled; render copy for a plain pick.

        [Fact]
        public static void TheCopywriterReceivesEmptyFiredRules()
        {
            // Given a persona-off winning candidate: no PersonaRanker pick ever backed this track
            // (SPEC F81.6 — PersonaPick is null for every envelope-only ladder pick, including the
            // common persona-off case), even though a STALE "recently voiced" marker is still sitting
            // around from an earlier persona-ON break.
            var request = LeadInRequest(personaPick: null);
            var staleMarkerFromAnEarlierBreak = new[] { "Valerie June" };

            var userContent = LlmPromptBuilder.BuildUserContent(
                request, "Current date/time (station-local): irrelevant", staleMarkerFromAnEarlierBreak);

            // Then the copywriter reads no fired rules and no exploration flag at all — the stale
            // marker is never consulted once PersonaPick itself is null (F83.3).
            Assert.DoesNotContain("Taste note", userContent);
        }

        [Fact]
        public static async Task ThePromptMatchesPreF82BehaviorByteForByte()
        {
            // regression pin (F83.3): no taste/exploration fragments appear at all
            await using var mock = await MockCompletionsServer.StartAsync();
            var writer = BuildWriter(mock.BaseUri.ToString());

            await writer.WriteAsync(LeadInRequest(personaPick: null), CancellationToken.None);

            var userContent = ExtractMessageContent(mock.Requests[0].Body, "user");

            const string Expected =
                "Station: GenWave\n" +
                "Local time: 2026-07-20 09:41\n" +
                "Current date/time (station-local): Monday, July 20, 2026, 9:41 AM\n" +
                "Segment: lead-in for the upcoming track.\n" +
                "Title: Astral Plane\n" +
                "Artist: Valerie June\n" +
                "Album: The Order of Time\n" +
                "Genre: Folk\n" +
                "Year: 2017";

            Assert.Equal(Expected, userContent);
        }
    }
}

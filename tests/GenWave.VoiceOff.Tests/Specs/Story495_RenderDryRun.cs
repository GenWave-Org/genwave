// STORY-495 — `render --dry-run` (gh-#614 · SPEC brief extra · PLAN T613)

using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureRenderDryRun
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — paragraph count
    public sealed class ScenarioParagraphCount : IDisposable
    {
        // Given 2 cards, 8 paragraphs each
        // When  `render --dry-run` runs

        private readonly DryRunFixture fixture = new();
        private readonly RenderRun run;

        public ScenarioParagraphCount() => run = fixture.Run();

        [Fact]
        public void ItPrints16Paragraphs() => Assert.Contains("Dry run: 16 paragraphs across 2 cards", run.OutputLines);

        [Fact]
        public void ItExitsZero() => Assert.Equal(0, run.Exit);

        public void Dispose() => fixture.Dispose();
    }

    // AC2 — ETA from the last run
    public sealed class ScenarioETAFromTheLastRun : IDisposable
    {
        // Given a previous renders.jsonl with median wall 5 s
        // When  `render --dry-run` runs for 16 paragraphs

        private readonly DryRunFixture fixture = new();
        private readonly RenderRun run;

        public ScenarioETAFromTheLastRun()
        {
            fixture.SeedRun("second", 4000, 5000, 6000);
            run = fixture.Run();
        }

        [Fact]
        public void ItPrints2Min() => Assert.Contains("ETA ~2 min (median 5.0 s per paragraph from run second)", run.OutputLines);

        public void Dispose() => fixture.Dispose();
    }

    // AC3 — no engine is touched
    public sealed class ScenarioNoEngineIsTouched : IDisposable
    {
        // Given a stub endpoint counting requests
        // When  `render --dry-run` runs

        private readonly DryRunFixture fixture = new();
        private readonly RenderRun run;

        public ScenarioNoEngineIsTouched() => run = fixture.Run();

        [Fact]
        public void TheStubSawZeroRequests() => Assert.Equal(0, fixture.StubRequests);

        [Fact]
        public void DockerWasNeverAsked() => Assert.Equal(0, fixture.DockerCalls);

        public void Dispose() => fixture.Dispose();
    }

    // Nothing is written.
    public sealed class ScenarioNothingIsWritten : IDisposable
    {
        // Given a root that does not exist yet / a root with an earlier run
        // When  `render --dry-run` runs

        private readonly DryRunFixture fresh = new();
        private readonly DryRunFixture seeded = new();
        private readonly string[] before;

        public ScenarioNothingIsWritten()
        {
            seeded.SeedRun("second", 5000);
            before = seeded.Entries();
            fresh.Run();
            seeded.Run();
        }

        [Fact]
        public void TheRootIsNotCreated() => Assert.False(Directory.Exists(fresh.Root));

        [Fact]
        public void AnExistingRootIsUnchanged() => Assert.Equal(before, seeded.Entries());

        public void Dispose()
        {
            fresh.Dispose();
            seeded.Dispose();
        }
    }

    // The newest of two runs is the one used.
    public sealed class ScenarioTheNewestRunIsUsed : IDisposable
    {
        // Given an older run at 30 s and a newer one at 5 s
        // When  `render --dry-run` runs

        private readonly DryRunFixture fixture = new();
        private readonly RenderRun run;

        public ScenarioTheNewestRunIsUsed()
        {
            fixture.SeedRun("first", 30_000);
            fixture.SeedRun("second", 5000);
            run = fixture.Run();
        }

        [Fact]
        public void ItReadsTheNewerRun() => Assert.Contains("ETA ~2 min (median 5.0 s per paragraph from run second)", run.OutputLines);

        public void Dispose() => fixture.Dispose();
    }

    // Failed paragraphs do not count towards the median.
    public sealed class ScenarioFailedClipsAreIgnored : IDisposable
    {
        // Given a run of 5 s paragraphs and three failed 600 s paragraphs
        // When  `render --dry-run` runs

        private readonly DryRunFixture fixture = new();
        private readonly RenderRun run;

        public ScenarioFailedClipsAreIgnored()
        {
            fixture.SeedRun("second", 5000, -600_000, -600_000, -600_000, 5000);
            run = fixture.Run();
        }

        [Fact]
        public void TheMedianIgnoresThem() => Assert.Contains("ETA ~2 min (median 5.0 s per paragraph from run second)", run.OutputLines);

        public void Dispose() => fixture.Dispose();
    }

    // A card the words law refuses is listed with its reason and sends nothing.
    public sealed class ScenarioACardTheWordsLawRefuses : IDisposable
    {
        // Given a second card whose override changes a word
        // When  `render --dry-run` runs

        private readonly DryRunFixture fixture = new(secondCardBreaksTheWordsLaw: true);
        private readonly RenderRun run;

        public ScenarioACardTheWordsLawRefuses() => run = fixture.Run();

        [Fact]
        public void OnlyTheRenderableCardSParagraphsAreCounted() => Assert.Contains("Dry run: 8 paragraphs across 2 cards", run.OutputLines);

        [Fact]
        public void TheRefusedCardIsListedWithItsReason() =>
            Assert.Contains(run.OutputLines, l => l.StartsWith("card-two: ❌ words law: Paragraph 3 changes the script's words", StringComparison.Ordinal));

        public void Dispose() => fixture.Dispose();
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC4 — no prior run
    public sealed class ScenarioNoPriorRun : IDisposable
    {
        // Given no previous timing.jsonl
        // When  `render --dry-run` runs

        private readonly DryRunFixture fixture = new();
        private readonly RenderRun run;

        public ScenarioNoPriorRun() => run = fixture.Run();

        [Fact]
        public void ItPrintsETAUnknownNoPreviousRun() => Assert.Contains("ETA unknown (no previous run)", run.OutputLines);

        public void Dispose() => fixture.Dispose();
    }

    public sealed class ScenarioARunWithNoGoodParagraphs : IDisposable
    {
        // Given a newest run whose every paragraph failed
        // When  `render --dry-run` runs

        private readonly DryRunFixture fixture = new();
        private readonly RenderRun run;

        public ScenarioARunWithNoGoodParagraphs()
        {
            fixture.SeedRun("second", -5000, -5000);
            run = fixture.Run();
        }

        [Fact]
        public void TheETAIsUnknown() => Assert.Contains("ETA unknown (run second has no finished paragraphs)", run.OutputLines);

        public void Dispose() => fixture.Dispose();
    }

    public sealed class ScenarioACorruptRendersLog : IDisposable
    {
        // Given a newest run whose renders.jsonl is not render records
        // When  `render --dry-run` runs

        private readonly DryRunFixture fixture = new();
        private readonly RenderRun run;

        public ScenarioACorruptRendersLog()
        {
            fixture.SeedRun("first", 5000);
            fixture.SeedGarbage("second");
            run = fixture.Run();
        }

        [Fact]
        public void TheETAIsUnknownWithAReason() =>
            Assert.Contains("ETA unknown (renders.jsonl of run second is unreadable)", run.OutputLines);

        [Fact]
        public void ItStillExitsZero() => Assert.Equal(0, run.Exit);

        public void Dispose() => fixture.Dispose();
    }

    public sealed class ScenarioValidationMatchesARealRun : IDisposable
    {
        // Given an unknown card / a profile folder that is not there
        // When  `render --dry-run` runs

        private readonly DryRunFixture fixture = new();
        private readonly RenderRun unknownEngine;
        private readonly int missingProfiles;

        public ScenarioValidationMatchesARealRun()
        {
            unknownEngine = fixture.Run("--card", "nope");
            missingProfiles = Cli.Run(["render", "--run", "next", "--dry-run", "--engines", Path.Combine(fixture.Root, "no-engines")], TextWriter.Null, TextWriter.Null);
        }

        [Fact]
        public void AnUnknownCardExitsTwo() => Assert.Equal(2, unknownEngine.Exit);

        [Fact]
        public void AMissingProfileFolderExitsOne() => Assert.Equal(1, missingProfiles);

        public void Dispose() => fixture.Dispose();
    }
}

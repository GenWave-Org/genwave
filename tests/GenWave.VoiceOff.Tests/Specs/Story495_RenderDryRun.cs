// STORY-495 — `render --dry-run` (gh-#614 · SPEC brief extra · PLAN T613)

using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureRenderDryRun
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — clip count
    public sealed class ScenarioClipCount : IDisposable
    {
        // Given 2 attempted engines, 2 lanes, 10 lines
        // When  `render --dry-run` runs

        private readonly DryRunFixture fixture = new();
        private readonly RenderRun run;

        public ScenarioClipCount() => run = fixture.Run();

        [Fact]
        public void ItPrints40Clips() => Assert.Contains("Dry run: 40 clips across 2 engines", run.OutputLines);

        [Fact]
        public void ItExitsZero() => Assert.Equal(0, run.Exit);

        public void Dispose() => fixture.Dispose();
    }

    // AC2 — ETA from the last run
    public sealed class ScenarioETAFromTheLastRun : IDisposable
    {
        // Given a previous timing.jsonl with median wall 5 s
        // When  `render --dry-run` runs for 40 clips

        private readonly DryRunFixture fixture = new();
        private readonly RenderRun run;

        public ScenarioETAFromTheLastRun()
        {
            fixture.SeedRun("20261021T100000Z-cpu", 4000, 5000, 6000);
            run = fixture.Run();
        }

        [Fact]
        public void ItPrints4Min() => Assert.Contains("ETA ~4 min (median 5.0 s per clip from run 20261021T100000Z-cpu)", run.OutputLines);

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

        [Fact]
        public void NvidiaSmiWasNeverAsked() => Assert.Equal(0, run.GpuReads);

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
            seeded.SeedRun("20261021T100000Z-cpu", 5000);
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
            fixture.SeedRun("20261020T100000Z-cpu", 30_000);
            fixture.SeedRun("20261021T100000Z-cpu", 5000);
            run = fixture.Run();
        }

        [Fact]
        public void ItReadsTheNewerRun() => Assert.Contains("ETA ~4 min (median 5.0 s per clip from run 20261021T100000Z-cpu)", run.OutputLines);

        public void Dispose() => fixture.Dispose();
    }

    // Failed clips do not count towards the median.
    public sealed class ScenarioFailedClipsAreIgnored : IDisposable
    {
        // Given a run of 5 s clips and three failed 600 s clips
        // When  `render --dry-run` runs

        private readonly DryRunFixture fixture = new();
        private readonly RenderRun run;

        public ScenarioFailedClipsAreIgnored()
        {
            fixture.SeedRun("20261021T100000Z-cpu", 5000, -600_000, -600_000, -600_000, 5000);
            run = fixture.Run();
        }

        [Fact]
        public void TheMedianIgnoresThem() => Assert.Contains("ETA ~4 min (median 5.0 s per clip from run 20261021T100000Z-cpu)", run.OutputLines);

        public void Dispose() => fixture.Dispose();
    }

    // The engines a tier skips are explained, and left out of the count.
    public sealed class ScenarioAnEngineNotAttemptedAtTheTier : IDisposable
    {
        // Given a second engine that is gpu-only (2 lanes each, 10 lines)
        // When  `render --dry-run` runs on cpu

        private readonly DryRunFixture fixture = new(secondEngineGpuOnly: true);
        private readonly RenderRun run;

        public ScenarioAnEngineNotAttemptedAtTheTier() => run = fixture.Run();

        [Fact]
        public void OnlyTheAttemptedEngineIsCounted() => Assert.Contains("Dry run: 20 clips across 1 engine", run.OutputLines);

        [Fact]
        public void TheSkippedEngineIsNamed() => Assert.Contains("beta: not attempted at tier cpu", run.OutputLines);

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

    public sealed class ScenarioAGpuRunWithOnlyCpuHistory : IDisposable
    {
        // Given a cpu run in the root
        // When  `render --dry-run` runs on gpu

        private readonly DryRunFixture fixture = new();
        private readonly RenderRun run;

        public ScenarioAGpuRunWithOnlyCpuHistory()
        {
            fixture.SeedRun("20261021T100000Z-cpu", 5000);
            run = fixture.Run("gpu");
        }

        [Fact]
        public void TheETAIsUnknown() => Assert.Contains("ETA unknown (no previous run)", run.OutputLines);

        public void Dispose() => fixture.Dispose();
    }

    public sealed class ScenarioACorruptTimingLog : IDisposable
    {
        // Given a newest run whose timing.jsonl is not timing records
        // When  `render --dry-run` runs

        private readonly DryRunFixture fixture = new();
        private readonly RenderRun run;

        public ScenarioACorruptTimingLog()
        {
            fixture.SeedRun("20261020T100000Z-cpu", 5000);
            fixture.SeedGarbage("20261021T100000Z-cpu");
            run = fixture.Run();
        }

        [Fact]
        public void TheETAIsUnknownWithAReason() =>
            Assert.Contains("ETA unknown (timing.jsonl of run 20261021T100000Z-cpu is unreadable)", run.OutputLines);

        [Fact]
        public void ItStillExitsZero() => Assert.Equal(0, run.Exit);

        public void Dispose() => fixture.Dispose();
    }

    public sealed class ScenarioValidationMatchesARealRun : IDisposable
    {
        // Given an unknown engine slug / a profile folder that is not there
        // When  `render --dry-run` runs

        private readonly DryRunFixture fixture = new();
        private readonly RenderRun unknownEngine;
        private readonly int missingProfiles;

        public ScenarioValidationMatchesARealRun()
        {
            unknownEngine = fixture.Run("cpu", "--engine", "nope");
            missingProfiles = Cli.Run(["render", "--tier", "cpu", "--dry-run", "--engines", Path.Combine(fixture.Root, "no-engines")], TextWriter.Null, TextWriter.Null);
        }

        [Fact]
        public void AnUnknownEngineExitsTwo() => Assert.Equal(2, unknownEngine.Exit);

        [Fact]
        public void AMissingProfileFolderExitsOne() => Assert.Equal(1, missingProfiles);

        public void Dispose() => fixture.Dispose();
    }
}

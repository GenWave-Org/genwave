// STORY-494 — render progress and ETA (gh-#614 · SPEC brief extra · PLAN T612)

using System.Net;
using System.Net.Sockets;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureRenderProgressAndETA
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — progress is printed
    public sealed class ScenarioProgressIsPrinted
    {
        // Given a run of 4 clips with 3 done at a median of 10 s per clip
        // When  the 4th clip starts

        private readonly string line = RenderProgress.Line(4, 4, [10_000, 10_000, 10_000]);

        [Fact]
        public void StdoutShowsClip441MinLeft() => Assert.Equal("clip 4/4 · ~1 min left", line);
    }

    // AC2 — no ETA before the first clip
    public sealed class ScenarioNoETABeforeTheFirstClip
    {
        // Given a run with no clip done
        // When  progress prints

        private readonly string line = RenderProgress.Line(1, 7, []);

        [Fact]
        public void ItShowsClip1NWithNoETA() => Assert.Equal("clip 1/7", line);
    }

    // Entry point: the real render verb prints the lines.
    public sealed class ScenarioTheRenderVerbPrintsProgress : IDisposable
    {
        // Given 2 engines x 3 lines, plus a gpu-only engine that is not attempted on cpu
        // When  render runs

        private readonly RenderScenario scenario = new();
        private readonly string[] progress;

        public ScenarioTheRenderVerbPrintsProgress()
        {
            EngineStubServer stub = scenario.StartStubAsync().GetAwaiter().GetResult();
            scenario.AddEngine("alpha", stub.BaseUri);
            scenario.AddEngine("beta", stub.BaseUri);
            scenario.AddEngine("gamma", stub.BaseUri, tiers: ["gpu"]);
            RenderRun run = scenario.Run(scenario.EnginesDirectory, scenario.GauntletDirectory(RenderScenario.ThreeLines()), "cpu");
            progress = run.OutputLines.Where(l => l.StartsWith("clip ", StringComparison.Ordinal)).ToArray();
        }

        [Fact]
        public void TheFirstLineIsClip1Of6() => Assert.Equal("clip 1/6", progress[0]);

        [Fact]
        public void ALaterLineCarriesTheETA() => Assert.Contains(progress, l => l.EndsWith(" min left", StringComparison.Ordinal));

        [Fact]
        public void OneLinePerClip() => Assert.Equal(6, progress.Length);

        [Fact]
        public void TheLastLineIsClip6Of6WithAnETA() => Assert.StartsWith("clip 6/6 · ~", progress[^1], StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioETARoundsUp
    {
        // Given one done clip of 10 s, of 61 s, of 0 s, and a slow outlier among fast clips
        // When  the last clip starts

        private readonly string tenSeconds = RenderProgress.Line(2, 2, [10_000]);
        private readonly string sixtyOneSeconds = RenderProgress.Line(2, 2, [61_000]);
        private readonly string zeroWall = RenderProgress.Line(2, 2, [0]);
        private readonly string outlier = RenderProgress.Line(4, 5, [1_000, 1_000, 3_600_000]);

        [Fact]
        public void TenSecondsForOneClipIsOneMinute() => Assert.Equal("clip 2/2 · ~1 min left", tenSeconds);

        [Fact]
        public void SixtyOneSecondsForOneClipIsTwoMinutes() => Assert.Equal("clip 2/2 · ~2 min left", sixtyOneSeconds);

        [Fact]
        public void ZeroWallTimeStillSaysOneMinute() => Assert.Equal("clip 2/2 · ~1 min left", zeroWall);

        [Fact]
        public void TheMedianNotTheMeanDrivesIt() => Assert.Equal("clip 4/5 · ~1 min left", outlier);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnEngineLeavesTheTotal
    {
        // Given a run of 6 clips whose first engine (3 clips) fails its container gate
        // When  the next clip starts

        private readonly string line;

        public ScenarioAnEngineLeavesTheTotal()
        {
            RunProgress progress = new(6);
            progress.Drop(3);
            line = progress.NextLine();
        }

        [Fact]
        public void NShrinks() => Assert.Equal("clip 1/3", line);
    }

    public sealed class ScenarioAFailedGateLeavesTheRunTotal : IDisposable
    {
        // Given engine alpha on a closed port and engine beta on the stub, 3 lines each
        // When  render runs on cpu

        private readonly RenderScenario scenario = new();
        private readonly string[] progress;

        public ScenarioAFailedGateLeavesTheRunTotal()
        {
            EngineStubServer stub = scenario.StartStubAsync().GetAwaiter().GetResult();
            scenario.AddEngine("alpha", new Uri($"http://127.0.0.1:{ClosedPort()}/"));
            scenario.AddEngine("beta", stub.BaseUri);
            RenderRun run = scenario.Run(scenario.EnginesDirectory, scenario.GauntletDirectory(RenderScenario.ThreeLines()), "cpu");
            progress = run.OutputLines.Where(l => l.StartsWith("clip ", StringComparison.Ordinal)).ToArray();
        }

        [Fact]
        public void TheFirstLineIsClip1Of3() => Assert.Equal("clip 1/3", progress[0]);

        // A port nothing listens on: bind a free one, then let it go.
        private static int ClosedPort()
        {
            using TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioAStoppedClipAddsNoWallTime
    {
        // Given 1 real render of 100 s and 1 clip the dead engine never rendered
        // When  the 3rd clip starts

        private readonly string line;

        public ScenarioAStoppedClipAddsNoWallTime()
        {
            RunProgress progress = new(3);
            progress.Finished(ClipOutcome.Failed("x", 100_000), rendered: true);
            progress.Finished(ClipOutcome.Failed(EngineRun.StoppedError, 0), rendered: false);
            line = progress.NextLine();
        }

        [Fact]
        public void TheMedianIgnoresIt() => Assert.Equal("clip 3/3 · ~2 min left", line);
    }
}

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
        // Given a run of 4 paragraphs with 3 done at a median of 10 s per clip
        // When  the 4th paragraph starts

        private readonly string line = RenderProgress.Line(4, 4, [10_000, 10_000, 10_000]);

        [Fact]
        public void StdoutShowsClip441MinLeft() => Assert.Equal("paragraph 4/4 · ~1 min left", line);
    }

    // AC2 — no ETA before the first clip
    public sealed class ScenarioNoETABeforeTheFirstClip
    {
        // Given a run with no paragraph done
        // When  progress prints

        private readonly string line = RenderProgress.Line(1, 7, []);

        [Fact]
        public void ItShowsClip1NWithNoETA() => Assert.Equal("paragraph 1/7", line);
    }

    // Entry point: the real render verb prints the lines.
    public sealed class ScenarioTheRenderVerbPrintsProgress : IDisposable
    {
        // Given 2 cards of 8 paragraphs each
        // When  render runs

        private readonly RenderScenario scenario = new();
        private readonly string[] progress;

        public ScenarioTheRenderVerbPrintsProgress()
        {
            EngineStubServer stub = scenario.StartStubAsync().GetAwaiter().GetResult();
            scenario.AddEngine("alpha", stub.BaseUri);
            scenario.AddCard("card-one", "alpha");
            scenario.AddCard("card-two", "alpha");
            RenderRun run = scenario.Run("first");
            progress = run.OutputLines.Where(l => l.StartsWith("paragraph ", StringComparison.Ordinal)).ToArray();
        }

        [Fact]
        public void TheFirstLineIsParagraph1Of16() => Assert.Equal("paragraph 1/16", progress[0]);

        [Fact]
        public void ALaterLineCarriesTheETA() => Assert.Contains(progress, l => l.EndsWith(" min left", StringComparison.Ordinal));

        [Fact]
        public void OneLinePerParagraph() => Assert.Equal(16, progress.Length);

        [Fact]
        public void TheLastLineIsParagraph16Of16WithAnETA() => Assert.StartsWith("paragraph 16/16 · ~", progress[^1], StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioETARoundsUp
    {
        // Given one done paragraph of 10 s, of 61 s, of 0 s, and a slow outlier among fast paragraphs
        // When  the last paragraph starts

        private readonly string tenSeconds = RenderProgress.Line(2, 2, [10_000]);
        private readonly string sixtyOneSeconds = RenderProgress.Line(2, 2, [61_000]);
        private readonly string zeroWall = RenderProgress.Line(2, 2, [0]);
        private readonly string outlier = RenderProgress.Line(4, 5, [1_000, 1_000, 3_600_000]);

        [Fact]
        public void TenSecondsForOneClipIsOneMinute() => Assert.Equal("paragraph 2/2 · ~1 min left", tenSeconds);

        [Fact]
        public void SixtyOneSecondsForOneClipIsTwoMinutes() => Assert.Equal("paragraph 2/2 · ~2 min left", sixtyOneSeconds);

        [Fact]
        public void ZeroWallTimeStillSaysOneMinute() => Assert.Equal("paragraph 2/2 · ~1 min left", zeroWall);

        [Fact]
        public void TheMedianNotTheMeanDrivesIt() => Assert.Equal("paragraph 4/5 · ~1 min left", outlier);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioACardLeavesTheTotal
    {
        // Given a run of 16 paragraphs whose first card (8 paragraphs) fails its container gate
        // When  the next paragraph starts

        private readonly string line;

        public ScenarioACardLeavesTheTotal()
        {
            RunProgress progress = new(16);
            progress.Drop(8);
            line = progress.NextLine();
        }

        [Fact]
        public void NShrinks() => Assert.Equal("paragraph 1/8", line);
    }

    public sealed class ScenarioAFailedGateLeavesTheRunTotal : IDisposable
    {
        // Given engine alpha on a closed port and engine beta on the stub, one card each
        // When  render runs

        private readonly RenderScenario scenario = new();
        private readonly string[] progress;

        public ScenarioAFailedGateLeavesTheRunTotal()
        {
            EngineStubServer stub = scenario.StartStubAsync().GetAwaiter().GetResult();
            scenario.AddEngine("alpha", new Uri($"http://127.0.0.1:{ClosedPort()}/"));
            scenario.AddEngine("beta", stub.BaseUri);
            scenario.AddCard("card-one", "alpha");
            scenario.AddCard("card-two", "beta");
            RenderRun run = scenario.Run("first");
            progress = run.OutputLines.Where(l => l.StartsWith("paragraph ", StringComparison.Ordinal)).ToArray();
        }

        [Fact]
        public void TheFirstLineIsParagraph1Of8() => Assert.Equal("paragraph 1/8", progress[0]);

        // A port nothing listens on: bind a free one, then let it go.
        private static int ClosedPort()
        {
            using TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioAFailedParagraphLeavesTheRestOfItsCardOutOfTheTotal
    {
        // Given 1 paragraph of 100 s done in a run of 16, then the card fails and drops its other 7
        // When  the next paragraph starts

        private readonly string line;

        public ScenarioAFailedParagraphLeavesTheRestOfItsCardOutOfTheTotal()
        {
            RunProgress progress = new(16);
            progress.Finished(100_000);
            progress.Drop(7);
            line = progress.NextLine();
        }

        [Fact]
        public void TheTotalShrinksAndTheMedianDrivesTheETA() => Assert.Equal("paragraph 2/9 · ~14 min left", line);
    }
}

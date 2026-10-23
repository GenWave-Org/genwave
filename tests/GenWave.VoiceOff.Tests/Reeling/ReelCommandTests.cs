using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Reeling;

public static class ReelCommandTests
{
    private static readonly string[] SlugsOfTheFixture = ["quillbase", "wrenfox", "oakmoth"];

    public sealed class ScenarioTheRunIsNotPaired : IDisposable
    {
        private readonly PairScenario run = new(new PairFixtureOptions(Tones: true));
        private readonly int exit;

        public ScenarioTheRunIsNotPaired() => exit = run.Reel();

        [Fact]
        public void ExitsWithOne() => Assert.Equal(1, exit);

        [Fact]
        public void SaysToPairFirst() => Assert.Contains("voiceoff pair", run.Stderr, StringComparison.Ordinal);

        [Fact]
        public void LeavesNoSessionBehind() => Assert.False(Directory.Exists(run.SessionDirectory));

        public void Dispose() => run.Dispose();
    }

    public sealed class ScenarioTheRunAlreadyHasReels : IDisposable
    {
        private readonly PairScenario run = new(new PairFixtureOptions(Tones: true));
        private readonly byte[] manifestBefore;
        private readonly int exit;

        public ScenarioTheRunAlreadyHasReels()
        {
            run.Pair();
            run.MustReel();
            manifestBefore = File.ReadAllBytes(Path.Combine(run.RunDirectory, "manifest.json"));
            exit = run.Reel();
        }

        [Fact]
        public void ExitsWithOne() => Assert.Equal(1, exit);

        [Fact]
        public void SaysSo() => Assert.Contains("already has reels", run.Stderr, StringComparison.Ordinal);

        [Fact]
        public void LeavesTheManifestAsItWas() =>
            Assert.Equal(manifestBefore, File.ReadAllBytes(Path.Combine(run.RunDirectory, "manifest.json")));

        public void Dispose() => run.Dispose();
    }

    public sealed class ScenarioTheBaselineDidNotPassBothGates : IDisposable
    {
        private readonly PairScenario run = new(new PairFixtureOptions(Tones: true, FailedVoiceSelects: [PairScenario.Baseline]));
        private readonly int exit;

        public ScenarioTheBaselineDidNotPassBothGates()
        {
            run.Pair();
            exit = run.Reel();
        }

        [Fact]
        public void ExitsWithOne() => Assert.Equal(1, exit);

        [Fact]
        public void BuildsNoReels() => Assert.False(Directory.Exists(run.ReelsDirectory));

        public void Dispose() => run.Dispose();
    }

    public sealed class ScenarioNoChallengerPassedBothGates : IDisposable
    {
        private readonly PairScenario run = new(new PairFixtureOptions(Challengers: ["wrenfox"], Tones: true, FailedContainers: ["wrenfox"]));
        private readonly int exit;

        public ScenarioNoChallengerPassedBothGates()
        {
            run.Pair();
            exit = run.Reel();
        }

        [Fact]
        public void ExitsWithOne() => Assert.Equal(1, exit);

        [Fact]
        public void SaysNoChallengerPassed() => Assert.Contains("No challenger passed both gates", run.Stderr, StringComparison.Ordinal);

        [Fact]
        public void BuildsNoReels() => Assert.False(Directory.Exists(run.ReelsDirectory));

        public void Dispose() => run.Dispose();
    }

    // Silent clips cannot be measured, so the match fails: nothing may be left behind.
    public sealed class ScenarioTheMatchFails : IDisposable
    {
        private readonly PairScenario run = new(new PairFixtureOptions(Challengers: ["wrenfox"]));
        private readonly byte[] manifestBefore;
        private readonly int exit;

        public ScenarioTheMatchFails()
        {
            run.Pair();
            manifestBefore = File.ReadAllBytes(Path.Combine(run.RunDirectory, "manifest.json"));
            exit = run.Reel();
        }

        [Fact]
        public void ExitsWithOne() => Assert.Equal(1, exit);

        [Fact]
        public void LeavesNoReelsFolder() => Assert.False(Directory.Exists(run.ReelsDirectory));

        [Fact]
        public void LeavesNoPartialFolder() => Assert.False(Directory.Exists(Path.Combine(run.SessionDirectory, "reels.partial")));

        [Fact]
        public void LeavesTheManifestAsItWas() =>
            Assert.Equal(manifestBefore, File.ReadAllBytes(Path.Combine(run.RunDirectory, "manifest.json")));

        public void Dispose() => run.Dispose();
    }

    public sealed class ScenarioOneClipDidNotRender : IDisposable
    {
        private readonly PairScenario run = new(new PairFixtureOptions(Challengers: ["wrenfox"], Tones: true, FailedRenders: ["wrenfox/plain/3"]));
        private readonly int exit;

        public ScenarioOneClipDidNotRender()
        {
            run.Pair();
            exit = run.Reel();
        }

        [Fact]
        public void Succeeds() => Assert.Equal(0, exit);

        [Fact]
        public void StdoutCountsTheClipInTheSingular() => Assert.Contains("Left out 1 clip that did not render.", run.Stdout, StringComparison.Ordinal);

        [Fact]
        public void StdoutSaysWhatToDoNext() => Assert.Contains(
            $"Next: run voiceoff serve --run {run.RunId}, open the page, and choose \"Wince reels\".", run.Stdout, StringComparison.Ordinal);

        [Fact]
        public void StdoutNamesNoEngine() =>
            Assert.DoesNotContain(SlugsOfTheFixture, slug => run.Stdout.Contains(slug, StringComparison.OrdinalIgnoreCase));

        public void Dispose() => run.Dispose();
    }

    public sealed class ScenarioTwoClipsDidNotRender : IDisposable
    {
        private readonly PairScenario run = new(new PairFixtureOptions(Challengers: ["wrenfox"], Tones: true, FailedRenders: ["wrenfox/plain/3", PairScenario.Baseline + "/plain/4"]));
        private readonly int exit;

        public ScenarioTwoClipsDidNotRender()
        {
            run.Pair();
            exit = run.Reel();
        }

        [Fact]
        public void Succeeds() => Assert.Equal(0, exit);

        [Fact]
        public void StdoutCountsTheClipsInThePlural() => Assert.Contains("Left out 2 clips that did not render.", run.Stdout, StringComparison.Ordinal);

        public void Dispose() => run.Dispose();
    }

    public sealed class ScenarioTheManifestCannotBeWritten : IDisposable
    {
        private readonly PairScenario run = new(new PairFixtureOptions(Challengers: ["wrenfox"], Tones: true));
        private readonly int exit;

        public ScenarioTheManifestCannotBeWritten()
        {
            run.Pair();
            // A directory where the manifest's temp file goes makes the write fail after the reels are in place.
            Directory.CreateDirectory(Path.Combine(run.RunDirectory, "manifest.json.tmp"));
            exit = run.Reel();
        }

        [Fact]
        public void Fails() => Assert.Equal(1, exit);

        [Fact]
        public void LeavesNoReelsBehind() => Assert.False(Directory.Exists(run.ReelsDirectory));

        public void Dispose() => run.Dispose();
    }

    public sealed class ScenarioBadArguments
    {
        [Fact]
        public void ARunIsRequired()
        {
            Assert.Equal(2, Cli.Run(["reel"], new StringWriter(), new StringWriter()));
        }

        [Fact]
        public void ASeedIsNotAnOption()
        {
            Assert.Equal(2, Cli.Run(["reel", "--run", "20261022T100000Z-cpu", "--seed", "1"], new StringWriter(), new StringWriter()));
        }
    }
}

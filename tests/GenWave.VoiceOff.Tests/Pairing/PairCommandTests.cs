using System.Text.RegularExpressions;
using GenWave.VoiceOff.Listening;
using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Pairing;

public static class PairCommandTests
{
    public sealed class ScenarioAFreshRun : IDisposable
    {
        private readonly PairScenario scenario = new();
        private readonly int exit;
        private readonly PairManifest read;
        private readonly int scoreExit;
        private readonly string pairStdout;

        public ScenarioAFreshRun()
        {
            exit = scenario.Pair();
            pairStdout = scenario.Stdout;
            read = ManifestReader.ReadAsync(
                Path.Combine(scenario.RunDirectory, "manifest.json"), "manifest.json", PairScenario.Baseline, new HashSet<string>(scenario.EverySlug), CancellationToken.None)
                .GetAwaiter().GetResult();
            scoreExit = scenario.Run(
                ["score", "--run", scenario.RunId, "--root", scenario.Root, "--engines", scenario.EnginesDirectory, "--out", Path.Combine(scenario.Root, "RESULTS.md")]);
        }

        [Fact]
        public void ExitsZero() => Assert.Equal(0, exit);

        [Fact]
        public void PrintsTheSummaryLine() =>
            Assert.Contains("Paired 40 (skipped 0 pairs with a failed clip) into 1 session ->", pairStdout, StringComparison.Ordinal);

        [Fact]
        public void PrintsTheSessionHint() => Assert.Contains("Session 1: ?s=1", pairStdout, StringComparison.Ordinal);

        [Fact]
        public void TheManifestIsAcceptedByTheManifestReader() => Assert.Equal(40, read.Pairs.Count);

        [Fact]
        public void TheManifestStaysOutOfTheServedFolder() => Assert.DoesNotContain("manifest.json", scenario.SessionFiles());

        [Fact]
        public void EveryClipIdIsUnique()
        {
            string[] ids = [.. scenario.Manifest().Pairs.SelectMany(p => new[] { p.A, p.B })];
            Assert.Equal(80, ids.Distinct(StringComparer.Ordinal).Count());
        }

        [Fact]
        public void EveryClipAndPairIdIs32LowerCaseHexCharacters()
        {
            Regex hex = new(@"\A[0-9a-f]{32}\z", RegexOptions.CultureInvariant);
            Assert.All(scenario.Manifest().Pairs.SelectMany(p => new[] { p.PairId, p.A, p.B }), id => Assert.Matches(hex, id));
        }

        [Fact]
        public void EveryClipIsA44ByteHeaderPlusTheData() =>
            Assert.All(
                Directory.GetFiles(Path.Combine(scenario.SessionDirectory, "clips")),
                clip => Assert.Equal(44 + PairScenario.DataBytes, new FileInfo(clip).Length));

        [Fact]
        public void TheSessionFileHoldsTheCap() => Assert.Equal(40, scenario.Sessions().Cap);

        [Fact]
        public void ThePageIsWrittenAsIndexHtml() => Assert.Contains("index.html", scenario.SessionFiles());

        [Fact]
        public void ScoreAcceptsTheManifest() => Assert.Equal(0, scoreExit);

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioARunThatIsAlreadyPaired : IDisposable
    {
        private readonly PairScenario scenario = new();
        private readonly int second;
        private readonly string manifestBefore;

        public ScenarioARunThatIsAlreadyPaired()
        {
            scenario.Pair();
            manifestBefore = File.ReadAllText(Path.Combine(scenario.RunDirectory, "manifest.json"));
            second = scenario.Pair();
        }

        [Fact]
        public void IsRefusedWithExitOne() => Assert.Equal(1, second);

        [Fact]
        public void SaysSo() => Assert.Contains($"run {scenario.RunId} is already paired", scenario.Stderr, StringComparison.Ordinal);

        [Fact]
        public void KeepsTheFirstManifest() => Assert.Equal(manifestBefore, File.ReadAllText(Path.Combine(scenario.RunDirectory, "manifest.json")));

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioASessionFolderWithNoManifest : IDisposable
    {
        private readonly PairScenario scenario = new();
        private readonly int second;

        public ScenarioASessionFolderWithNoManifest()
        {
            scenario.Pair();
            File.Delete(Path.Combine(scenario.RunDirectory, "manifest.json"));
            second = scenario.Pair();
        }

        [Fact]
        public void IsRefusedWithExitOne() => Assert.Equal(1, second);

        [Fact]
        public void SaysToDeleteSessionToRetry() => Assert.Contains("Delete ", scenario.Stderr, StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioAnEngineThatFailedOnlyVoiceSelect : IDisposable
    {
        private readonly PairScenario scenario = new(new PairFixtureOptions(FailedVoiceSelects: ["oakmoth"]));

        public ScenarioAnEngineThatFailedOnlyVoiceSelect() => scenario.Pair();

        [Fact]
        public void GetsNoPairs() =>
            Assert.DoesNotContain(scenario.Manifest().Pairs, p => p.Truth.A == "oakmoth" || p.Truth.B == "oakmoth");

        [Fact]
        public void TheOtherEngineStillGetsItsPairs() => Assert.Equal(20, scenario.Manifest().Pairs.Count);

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioAFailedRender : IDisposable
    {
        private readonly PairScenario scenario = new(new PairFixtureOptions(FailedRenders: ["wrenfox/plain/3"]));

        public ScenarioAFailedRender() => scenario.Pair();

        [Fact]
        public void IsCountedInTheSummary() => Assert.Contains("Paired 39 (skipped 1 pairs with a failed clip)", scenario.Stdout, StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioACorruptClip : IDisposable
    {
        // A LIST chunk that declares 0xFFFFFFFF bytes must skip the pair, not crash the run.
        private readonly PairScenario scenario = new(new PairFixtureOptions(CorruptClips: ["wrenfox/plain/3"]));
        private readonly int exit;

        public ScenarioACorruptClip() => exit = scenario.Pair();

        [Fact]
        public void ExitsZero() => Assert.Equal(0, exit);

        [Fact]
        public void CountsThePairAsSkipped() => Assert.Contains("Paired 39 (skipped 1 pairs with a failed clip)", scenario.Stdout, StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioUnequalGroupsAfterFailedRenders : IDisposable
    {
        // 25 lines x 2 lanes for one engine, 5 x 2 for the other: 60 pairs, 2 sessions.
        private readonly PairScenario scenario = new(new PairFixtureOptions(Lines: 25, LinesByChallenger: new Dictionary<string, int> { ["oakmoth"] = 5 }));
        private readonly List<int> enginesPerSession = [];

        public ScenarioUnequalGroupsAfterFailedRenders()
        {
            scenario.Pair();
            IReadOnlyDictionary<string, string> engineByPair = scenario.ChallengerByPair();
            enginesPerSession.AddRange(scenario.Sessions().Sessions.Select(s => s.Pairs.Select(p => engineByPair[p.PairId]).Distinct().Count()));
        }

        [Fact]
        public void EverySessionStillHoldsMoreThanOneEngine() => Assert.All(enginesPerSession, count => Assert.True(count > 1));

        [Fact]
        public void TheCutHasTwoSessions() => Assert.Equal(2, enginesPerSession.Count);

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioAFailedBaselineClip : IDisposable
    {
        private readonly PairScenario scenario = new(new PairFixtureOptions(FailedRenders: ["quillbase/plain/3"]));

        public ScenarioAFailedBaselineClip() => scenario.Pair();

        [Fact]
        public void LeavesThatLineOutOfEveryPair() =>
            Assert.DoesNotContain(scenario.Manifest().Pairs, p => p.LineId == "leadin-bubbly-3");

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioARunWhoseGauntletChanged : IDisposable
    {
        private readonly PairScenario scenario = new(new PairFixtureOptions(RunHashOverride: "feedface"));
        private readonly int exit;

        public ScenarioARunWhoseGauntletChanged() => exit = scenario.Pair();

        [Fact]
        public void IsRefused() => Assert.Equal(1, exit);

        [Fact]
        public void NamesTheRunsHash() => Assert.Contains("feedface", scenario.Stderr, StringComparison.Ordinal);

        [Fact]
        public void WritesNothing() => Assert.False(Directory.Exists(scenario.SessionDirectory));

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioARunWithNoChallengerThatPassed : IDisposable
    {
        private readonly PairScenario scenario = new(new PairFixtureOptions(Challengers: ["wrenfox"], FailedContainers: ["wrenfox"]));
        private readonly int exit;

        public ScenarioARunWithNoChallengerThatPassed() => exit = scenario.Pair();

        [Fact]
        public void IsRefusedWithExitOne() => Assert.Equal(1, exit);

        [Fact]
        public void SaysThereIsNothingToPair() =>
            Assert.Contains("No challenger passed both gates in this run, so there is nothing to pair.", scenario.Stderr, StringComparison.Ordinal);

        [Fact]
        public void WritesNoSession() => Assert.False(Directory.Exists(scenario.SessionDirectory));

        [Fact]
        public void WritesNoManifest() => Assert.False(File.Exists(Path.Combine(scenario.RunDirectory, "manifest.json")));

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioAChallengerThatPassedButRenderedNothing : IDisposable
    {
        private readonly PairScenario scenario = new(new PairFixtureOptions(Challengers: ["wrenfox"], LinesByChallenger: new Dictionary<string, int> { ["wrenfox"] = 0 }));
        private readonly int exit;

        public ScenarioAChallengerThatPassedButRenderedNothing() => exit = scenario.Pair();

        [Fact]
        public void IsRefusedWithExitOne() => Assert.Equal(1, exit);

        [Fact]
        public void SaysNoClipRendered() =>
            Assert.Contains("every clip it needs failed to render, so there is nothing to pair.", scenario.Stderr, StringComparison.Ordinal);

        [Fact]
        public void WritesNoSession() => Assert.False(Directory.Exists(scenario.SessionDirectory));

        [Fact]
        public void WritesNoManifest() => Assert.False(File.Exists(Path.Combine(scenario.RunDirectory, "manifest.json")));

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioBadArguments : IDisposable
    {
        private readonly PairScenario scenario = new();

        [Fact]
        public void ARunIdIsRequired() => Assert.Equal(2, scenario.Run(["pair"]));

        [Fact]
        public void ARunIdMustHaveTheRightShape() => Assert.Equal(2, scenario.Run(["pair", "--run", "../escape"]));

        [Fact]
        public void ASeedMustBeANumber() => Assert.Equal(2, scenario.Run(["pair", "--run", scenario.RunId, "--seed", "abc"]));

        [Fact]
        public void AnUnknownRunIsRefusedWithExitOne() =>
            Assert.Equal(1, scenario.Run(["pair", "--run", "20200101T000000Z-cpu", "--root", scenario.Root, "--engines", scenario.EnginesDirectory]));

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioTheSameSeedTwice : IDisposable
    {
        private readonly PairScenario first = new();
        private readonly PairScenario second = new();
        private readonly string[] firstSides;
        private readonly string[] secondSides;

        public ScenarioTheSameSeedTwice()
        {
            first.Pair();
            second.Pair();
            firstSides = Sides(first);
            secondSides = Sides(second);
        }

        [Fact]
        public void GivesTheSameSidesForTheSameLines() => Assert.Equal(firstSides, secondSides);

        private static string[] Sides(PairScenario s) =>
            [.. s.Manifest().Pairs.OrderBy(p => p.Lane).ThenBy(p => p.LineId, StringComparer.Ordinal).ThenBy(p => p.Truth.A, StringComparer.Ordinal).Select(p => p.Truth.A)];

        public void Dispose()
        {
            first.Dispose();
            second.Dispose();
        }
    }

    public sealed class ScenarioThePage
    {
        private readonly string html = ListeningPage.Html();

        [Theory]
        [InlineData("http")]
        [InlineData("//cdn")]
        [InlineData("<link")]
        [InlineData("autoplay")]
        [InlineData("innerHTML")]
        public void HasNoExternalReferenceOrUnsafeSink(string forbidden) =>
            Assert.DoesNotContain(forbidden, html, StringComparison.OrdinalIgnoreCase);

        [Fact]
        public void NamesNoShippedEngine()
        {
            string[] slugs = [.. EngineProfileSet.Load(RealEngines.Directory).Profiles.Select(p => p.Slug)];
            Assert.DoesNotContain(slugs, slug => html.Contains(slug, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void HasExplicitPlayButtons() => Assert.Contains("Play A", html, StringComparison.Ordinal);
    }
}

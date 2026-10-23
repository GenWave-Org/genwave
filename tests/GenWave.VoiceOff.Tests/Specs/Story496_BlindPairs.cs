// STORY-496 — Blind pairs (gh-#614 · SPEC F214.9 · PLAN T615)
//
// BDD specification — xUnit. Activated by T615; each Scenario runs `pair` through Cli.Run over a fixture run in its constructor.

using GenWave.VoiceOff.Listening;
using GenWave.VoiceOff.Pairing;
using GenWave.VoiceOff.Script;
using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureBlindPairs
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — one pair per line per engine per lane
    public sealed class ScenarioOnePairPerLinePerEnginePerLane : IDisposable
    {
        // Given 2 gated challengers, 2 lanes, 10 ok lines
        // When  `pair` runs
        private readonly PairScenario scenario = new();

        public ScenarioOnePairPerLinePerEnginePerLane() => scenario.Pair();

        [Fact]
        public void ManifestJsonHolds40Pairs() => Assert.Equal(40, scenario.Manifest().Pairs.Count);

        public void Dispose() => scenario.Dispose();
    }

    // AC2 — A/B is a coin flip
    public sealed class ScenarioABIsACoinFlip
    {
        // Given a fixed seed
        // When  `pair` runs over 1000 pairs
        private readonly int baselineOnA;

        public ScenarioABIsACoinFlip()
        {
            EngineProfile challenger = EngineProfileReader.Parse(EngineProfileJson.Valid("wrenfox", baseline: null).ToJsonString(), "wrenfox.json");
            GauntletLine[] lines = [.. Enumerable.Range(1, 500).Select(n => new GauntletLine("line-" + n, GauntletClass.LeadIn, null, null, [], []))];
            PairPlan plan = PairPlanner.Plan("quillbase", [challenger], lines, (_, _, _) => true, new Random(1234));
            baselineOnA = plan.Pairs.Count(p => p.BaselineIsA);
        }

        [Fact]
        public void KokoroIsSideABetween450And550Times() => Assert.InRange(baselineOnA, 450, 550);
    }

    // AC3 — clip ids are opaque
    public sealed class ScenarioClipIdsAreOpaque : IDisposable
    {
        // Given the session directory
        // When  its file names are listed
        private readonly PairScenario scenario = new();

        private readonly IReadOnlyDictionary<string, string> contents;

        public ScenarioClipIdsAreOpaque()
        {
            scenario.Pair();
            contents = scenario.SessionContents();
        }

        [Fact]
        public void NoneContainsAnEngineSlugLineIdOrLane()
        {
            string[] forbidden = [.. scenario.EverySlug, .. scenario.LineIds, "plain", "markup"];
            Assert.DoesNotContain(scenario.SessionFiles(), name => forbidden.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)));
        }

        [Fact]
        public void NoFileHoldsALineId() =>
            Assert.DoesNotContain(contents, file => scenario.LineIds.Any(id => file.Value.Contains(id, StringComparison.OrdinalIgnoreCase)));

        [Theory]
        [InlineData("plain")]
        [InlineData("markup")]
        public void NoFileHoldsALaneWord(string word) =>
            Assert.DoesNotContain(contents, file => file.Value.Contains(word, StringComparison.OrdinalIgnoreCase));

        public void Dispose() => scenario.Dispose();
    }

    // AC4 — truth lives in the manifest only
    public sealed class ScenarioTruthLivesInTheManifestOnly : IDisposable
    {
        // Given the session directory served to the page
        // When  every served file is searched for engine slugs
        private readonly PairScenario scenario = new();

        private readonly IReadOnlyList<string> leaking;

        public ScenarioTruthLivesInTheManifestOnly()
        {
            scenario.Pair();
            leaking = [.. scenario.SessionContents().Where(file => scenario.EverySlug.Any(slug => file.Value.Contains(slug, StringComparison.OrdinalIgnoreCase))).Select(file => file.Key)];
        }

        [Fact]
        public void NoneMatches() => Assert.Empty(leaking);

        public void Dispose() => scenario.Dispose();
    }

    // AC5 — sessions mix engines and lanes
    public sealed class ScenarioSessionsMixEnginesAndLanes : IDisposable
    {
        // Given 120 pairs across 3 engines and 2 lanes
        // When  sessions of 40 are cut
        private readonly PairScenario scenario = new(new PairFixtureOptions(Lines: 20, Challengers: ["wrenfox", "oakmoth", "teaglen"]));
        private readonly List<int> enginesPerSession = [];

        public ScenarioSessionsMixEnginesAndLanes()
        {
            scenario.Pair();
            IReadOnlyDictionary<string, string> engineByPair = scenario.ChallengerByPair();
            enginesPerSession.AddRange(scenario.Sessions().Sessions.Select(s => s.Pairs.Select(p => engineByPair[p.PairId]).Distinct().Count()));
        }

        [Fact]
        public void EverySessionContainsPairsFromMoreThanOneEngine() => Assert.All(enginesPerSession, count => Assert.True(count > 1));

        public void Dispose() => scenario.Dispose();
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC6 — failed renders are not paired
    public sealed class ScenarioFailedRendersAreNotPaired : IDisposable
    {
        // Given a line whose engine render is ok=false
        // When  `pair` runs
        private readonly PairScenario scenario = new(new PairFixtureOptions(FailedRenders: ["wrenfox/plain/3"]));

        public ScenarioFailedRendersAreNotPaired() => scenario.Pair();

        [Fact]
        public void NoPairUsesThatLineForThatEngine() =>
            Assert.DoesNotContain(scenario.Manifest().Pairs, p =>
                p.LineId == "leadin-bubbly-3" && p.Lane == RenderLane.Plain && (p.Truth.A == "wrenfox" || p.Truth.B == "wrenfox"));

        public void Dispose() => scenario.Dispose();
    }

    // AC7 — gate failures get no pairs
    public sealed class ScenarioGateFailuresGetNoPairs : IDisposable
    {
        // Given an engine with container ❌
        // When  `pair` runs
        private readonly PairScenario scenario = new(new PairFixtureOptions(FailedContainers: ["oakmoth"]));

        public ScenarioGateFailuresGetNoPairs() => scenario.Pair();

        [Fact]
        public void NoPairNamesThatEngine() =>
            Assert.DoesNotContain(scenario.Manifest().Pairs, p => p.Truth.A == "oakmoth" || p.Truth.B == "oakmoth");

        public void Dispose() => scenario.Dispose();
    }
}

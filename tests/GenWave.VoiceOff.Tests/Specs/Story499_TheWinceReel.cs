// STORY-499 — The wince reel (gh-#614 · SPEC F214.13 · PLAN T618)
//
// BDD specification — xUnit. Activated by T618; each Scenario runs `reel` through Cli.Run over a fixture run of real tones in its constructor,
// and the serve scenarios drive the real routes in-process.

using System.Text.Json;
using GenWave.VoiceOff.Script;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureTheWinceReel
{
    private const double GapSeconds = 0.6;

    /// <summary>The line numbers of the fixture lines, in the order the gauntlet set yields them.</summary>
    private static int[] GauntletOrder(PairScenario run) =>
        [.. GauntletSet.Load(run.GauntletDirectory).Lines.Select(l => int.Parse(l.Id[(l.Id.LastIndexOf('-') + 1)..], System.Globalization.CultureInfo.InvariantCulture))];

    private static PairScenario ReeledRun(PairFixtureOptions fixture)
    {
        PairScenario run = new(fixture with { Tones = true });
        if (run.Pair() != 0)
        {
            throw new InvalidOperationException("The fixture run did not pair: " + run.Stderr);
        }

        return run;
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — one reel per engine in gauntlet order
    public sealed class ScenarioOneReelPerEngineInGauntletOrder : IDisposable
    {
        // Given 10 ok plain-lane clips for an engine
        // When  `reel` runs
        private readonly PairScenario run = ReeledRun(new PairFixtureOptions(Challengers: ["wrenfox"]));
        private readonly int[] order;
        private readonly string reel;

        public ScenarioOneReelPerEngineInGauntletOrder()
        {
            run.MustReel();
            order = GauntletOrder(run);
            reel = run.ReelPath(run.Reels().Single(r => r.Engine == "wrenfox").ReelId);
        }

        [Fact]
        public void TheReelHoldsThemInGauntletOrderWith06SGaps()
        {
            double expected = order.Sum(ToneClips.SecondsFor) + (GapSeconds * (order.Length - 1));
            Assert.InRange(ReelProbe.Seconds(reel), expected - 0.05, expected + 0.05);
        }

        [Fact]
        public void EachGapStartsWhereTheClipsBeforeItEnd()
        {
            double[] expected = [.. order.Take(order.Length - 1).Select((_, k) => order.Take(k + 1).Sum(ToneClips.SecondsFor) + (GapSeconds * k))];
            // Each gap start is within 0.05 s of where it should be, or it reads as a miss (NaN) so a wrong count fails too.
            IReadOnlyList<double> actual = ReelProbe.GapStarts(reel);
            IEnumerable<double> offBy = Enumerable.Range(0, Math.Max(expected.Length, actual.Count))
                .Select(k => k < expected.Length && k < actual.Count ? Math.Round(Math.Abs(actual[k] - expected[k]) / 0.05, MidpointRounding.ToZero) : double.NaN);
            Assert.Equal(Enumerable.Repeat(0d, expected.Length), offBy);
        }

        public void Dispose() => run.Dispose();
    }

    // AC2 — the reel is matched
    public sealed class ScenarioTheReelIsMatched : IDisposable
    {
        // Given a built reel
        // When  it is measured
        private readonly PairScenario run = ReeledRun(new PairFixtureOptions(Challengers: ["wrenfox"]));
        private readonly double lufs;

        public ScenarioTheReelIsMatched()
        {
            run.MustReel();
            lufs = ReelProbe.IntegratedLufs(run.ReelPath(run.Reels().First().ReelId));
        }

        [Fact]
        public void ItReads16LUFS1LU() => Assert.InRange(lufs, -17.0, -15.0);

        public void Dispose() => run.Dispose();
    }

    // AC3 — winner and two decoys
    public sealed class ScenarioWinnerAndTwoDecoys : IDisposable
    {
        // Given a ranking winner W, runner-up R, and the baseline
        // When  `reel` builds the wince session
        private readonly PairScenario run = ReeledRun(new PairFixtureOptions(Challengers: ["oakmoth", "teaglen", "wrenfox"]));

        public ScenarioWinnerAndTwoDecoys()
        {
            // The slug order (oakmoth, teaglen, wrenfox) is the opposite of the rate order, so only a real ranking gives {wrenfox, teaglen}.
            run.VoteFor("wrenfox", 10, 0);
            run.VoteFor("teaglen", 6, 4);
            run.VoteFor("oakmoth", 2, 8);
            run.MustReel();
        }

        [Fact]
        public void ItOffersExactlyThreeReelsUnderOpaqueIdsWinnerRunnerUpAndBaseline() =>
            Assert.Equal(["quillbase", "teaglen", "wrenfox"], run.Reels().Select(r => r.Engine).Order(StringComparer.Ordinal));

        [Fact]
        public void TheReelIdsAre32HexCharacters() =>
            Assert.All(run.Reels(), r => Assert.Matches("^[0-9a-f]{32}$", r.ReelId));

        [Fact]
        public void EachReelIdNamesAWavInTheReelsFolder() =>
            Assert.All(run.Reels(), r => Assert.True(File.Exists(run.ReelPath(r.ReelId))));

        [Fact]
        public void NothingElseIsInTheReelsFolder() =>
            Assert.Equal(3, Directory.GetFiles(run.ReelsDirectory).Length);

        [Fact]
        public void StdoutNamesNoEngine() =>
            Assert.DoesNotContain(run.EverySlug, slug => run.Stdout.Contains(slug, StringComparison.OrdinalIgnoreCase));

        public void Dispose() => run.Dispose();
    }

    // AC4 — truth after all three counts
    public sealed class ScenarioTruthAfterAllThreeCounts : IDisposable
    {
        // Given wince counts entered for two reels
        // When  GET /wince
        private readonly ServeScenario serve = new(
            new PairFixtureOptions(Challengers: ["wrenfox", "oakmoth"], Tones: true),
            run => run.MustReel());

        private readonly string body;

        public ScenarioTruthAfterAllThreeCounts()
        {
            IReadOnlyList<string> ids = [.. serve.Run.Reels().Select(r => r.ReelId)];
            serve.AcceptWince(ids[0], 0);
            serve.AcceptWince(ids[1], 3);
            body = serve.GetText("wince");
        }

        [Fact]
        public void NoEngineNameIsReturned() =>
            Assert.DoesNotContain(serve.Run.EverySlug, slug => body.Contains(slug, StringComparison.OrdinalIgnoreCase));

        [Fact]
        public void TheEnteredCountsAreReturned() =>
            Assert.Equal(2, JsonDocument.Parse(body).RootElement.GetProperty("reels").EnumerateArray().Count(r => r.TryGetProperty("count", out _)));

        public void Dispose() => serve.Dispose();
    }

    // AC5 — truth revealed
    public sealed class ScenarioTruthRevealed : IDisposable
    {
        // Given wince counts for all three reels
        // When  GET /wince
        private readonly ServeScenario serve = new(
            new PairFixtureOptions(Challengers: ["wrenfox", "oakmoth"], Tones: true),
            run => run.MustReel());

        private readonly Dictionary<string, string?> engineByReel;

        public ScenarioTruthRevealed()
        {
            foreach (Listening.ManifestReel reel in serve.Run.Reels())
            {
                serve.AcceptWince(reel.ReelId, 1);
            }

            using JsonDocument doc = JsonDocument.Parse(serve.GetText("wince"));
            engineByReel = doc.RootElement.GetProperty("reels").EnumerateArray().ToDictionary(
                r => r.GetProperty("reelId").GetString() ?? string.Empty,
                r => r.TryGetProperty("engine", out JsonElement engine) ? engine.GetString() : null,
                StringComparer.Ordinal);
        }

        [Fact]
        public void EachReelSEngineIsNamed() =>
            Assert.Equal(serve.Run.Reels().ToDictionary(r => r.ReelId, r => (string?)r.Engine, StringComparer.Ordinal), engineByReel);

        public void Dispose() => serve.Dispose();
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC6 — no runner-up
    public sealed class ScenarioNoRunnerUp : IDisposable
    {
        // Given a single gated challenger
        // When  `reel` builds the session
        // (oakmoth failed a gate, so only wrenfox is left.)
        private readonly PairScenario run = ReeledRun(new PairFixtureOptions(Challengers: ["wrenfox", "oakmoth"], FailedContainers: ["oakmoth"]));

        public ScenarioNoRunnerUp() => run.MustReel();

        [Fact]
        public void TheSecondDecoyIsPiperIfGatedElseTheReelSetIsTwo() =>
            // A gated fallback is ranked like any challenger, so it becomes the runner-up; here none is gated, so the set is two.
            Assert.Equal(["quillbase", "wrenfox"], run.Reels().Select(r => r.Engine).Order(StringComparer.Ordinal));

        [Fact]
        public void StdoutSaysSo() =>
            Assert.Contains("Only one challenger passed both gates, so the wince set has two reels.", run.Stdout, StringComparison.Ordinal);

        public void Dispose() => run.Dispose();
    }

    // AC6 — a second gated challenger is the runner-up, so the set stays three and stdout stays quiet
    public sealed class ScenarioASecondGatedChallengerIsTheRunnerUp : IDisposable
    {
        private readonly PairScenario run = ReeledRun(new PairFixtureOptions(Challengers: ["wrenfox", "oakmoth"]));

        public ScenarioASecondGatedChallengerIsTheRunnerUp() => run.MustReel();

        [Fact]
        public void TheSetIsWinnerRunnerUpAndBaseline() =>
            Assert.Equal(["oakmoth", "quillbase", "wrenfox"], run.Reels().Select(r => r.Engine).Order(StringComparer.Ordinal));

        [Fact]
        public void StdoutDoesNotSayTheSetIsShort() =>
            Assert.DoesNotContain("two reels", run.Stdout, StringComparison.Ordinal);

        public void Dispose() => run.Dispose();
    }
}

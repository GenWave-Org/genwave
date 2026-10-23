// STORY-498 — a returning judge resumes (gh-#614 · SPEC brief extra · PLAN T617)
//
// BDD specification — xUnit, against the real serve host in-process. The browser reload is the Playwright smoke.

using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureAReturningJudgeResumes
{
    private const int Voted = 12;

    // Dean votes the pairs in an order that is NOT his shuffle order, so "start at the voted count" alone would be wrong.
    private static void DeanVotes(ServeScenario serve)
    {
        IReadOnlyList<string> shuffled = serve.SessionOrder("Dean");
        foreach (string id in shuffled.Skip(6).Take(Voted))
        {
            serve.AcceptVote(id, "Dean", "a");
        }
    }

    // AC1 — voted pairs are skipped
    public sealed class ScenarioVotedPairsAreSkipped : IDisposable
    {
        // Given "Dean" voted 12 pairs of a session
        // When  Dean reloads the page
        private readonly ServeScenario serve = new();
        private readonly IReadOnlySet<string> votedIds;

        public ScenarioVotedPairsAreSkipped()
        {
            DeanVotes(serve);
            votedIds = serve.VotedPairIds();
        }

        [Fact]
        public void TheFirstPairShownIsOneDeanHasNotVoted()
        {
            int done = int.Parse(serve.Progress("Dean")["done"], System.Globalization.CultureInfo.InvariantCulture);
            string first = serve.SessionOrder("Dean")[done];
            Assert.DoesNotContain(first, votedIds);
        }

        [Fact]
        public void ProgressReads12Of40()
        {
            IReadOnlyDictionary<string, string> progress = serve.Progress("Dean");
            Assert.Equal(("12", "40"), (progress["done"], progress["cap"]));
        }

        public void Dispose() => serve.Dispose();
    }

    // Extra (T617 brief) — a revote of an earlier pair changes nothing
    public sealed class ScenarioARevoteChangesNothing : IDisposable
    {
        // Given "Dean" voted 12 pairs and the next pair is known
        // When  Dean votes again on a pair he already voted
        private readonly ServeScenario serve = new();
        private readonly string before;

        public ScenarioARevoteChangesNothing()
        {
            DeanVotes(serve);
            before = serve.SessionOrder("Dean")[Voted];
            serve.AcceptVote(serve.SessionOrder("Dean")[0], "Dean", "b");
        }

        [Fact]
        public void ProgressStillReads12() => Assert.Equal("12", serve.Progress("Dean")["done"]);

        [Fact]
        public void TheNextPairIsUnchanged() => Assert.Equal(before, serve.SessionOrder("Dean")[Voted]);

        public void Dispose() => serve.Dispose();
    }

    // AC2 — the other judge is unaffected
    public sealed class ScenarioTheOtherJudgeIsUnaffected : IDisposable
    {
        // Given "Dean" voted 12 pairs
        // When  a second judge loads the page
        private readonly ServeScenario serve = new();

        public ScenarioTheOtherJudgeIsUnaffected() => DeanVotes(serve);

        [Fact]
        public void ProgressReads0Of40()
        {
            IReadOnlyDictionary<string, string> progress = serve.Progress("Maya");
            Assert.Equal(("0", "40"), (progress["done"], progress["cap"]));
        }

        public void Dispose() => serve.Dispose();
    }
}

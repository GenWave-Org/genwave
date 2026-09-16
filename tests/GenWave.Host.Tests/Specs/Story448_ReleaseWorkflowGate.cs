// STORY-448 — release.yml gates promotion on the stack gate (gh-#777 · SPEC F179, F183.2 · PLAN T502)
//
// BDD specification — xUnit. Text pins over .github/workflows/release.yml in the Story176 shape:
// the job blocks are sliced by their two-space keys, so each pin reads one job and nothing else.
// The live Actions run is the v5.9.0 tag itself (F183.4); the shell body is replayed on the dev
// box by T503.
//
// RED at plan time: merge-manifests still tags home-latest; no stack-gate or promote job exists.

using System.Text.RegularExpressions;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureReleasePromotionWaitsForTheStackGate
{
    static string Workflow => File.ReadAllText(Path.Combine(
        RepoRootLocator.Find(AppContext.BaseDirectory), ".github", "workflows", "release.yml"));

    /// <summary>The text of one job block: from <c>  name:</c> to the next two-space key.</summary>
    static string Job(string name)
    {
        var m = Regex.Match(Workflow, $@"^  {Regex.Escape(name)}:\n(.*?)(?=^  [a-z-]+:\n|\z)", RegexOptions.Multiline | RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value : "";
    }

    static string Header => Workflow.Split("\non:")[0];

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioMergeManifestsNoLongerTagsHomeLatest
    {
        [Fact]
        public void NoHomeLatestInTheMergeJob() => Assert.DoesNotContain("home-latest", Job("merge-manifests"), StringComparison.Ordinal);
    }

    public sealed class ScenarioStackGateFollowsMergeManifests
    {
        readonly string job = Job("stack-gate");

        [Fact]
        public void TheJobExists() => Assert.NotEqual("", job);

        [Fact]
        public void ItNeedsMergeManifests() => Assert.Matches(@"needs:\s*merge-manifests", job);

        [Fact]
        public void ItHasAFortyMinuteBudget() => Assert.Matches(@"timeout-minutes:\s*40", job);

        [Fact]
        public void ItRunsTheFourLegs() =>
            Assert.Matches(@"tools/gate/stack_gate\.sh .*--fresh .*--upgrade .*--capture .*--report", job);

        [Fact]
        public void ItUploadsTheReportMarkdown() => Assert.Contains("gate-report.md", job, StringComparison.Ordinal);

        [Fact]
        public void ItUploadsTheReportJson() => Assert.Contains("gate-report.json", job, StringComparison.Ordinal);

        [Fact]
        public void ItUploadsTheCapture() => Assert.Contains("capture.wav", job, StringComparison.Ordinal);

        [Fact]
        public void ItUploadsTheComposeLogs() => Assert.Contains("compose-", job, StringComparison.Ordinal);

        [Fact]
        public void TheArtifactsKeepForSevenDays() => Assert.Matches(@"retention-days:\s*7", job);
    }

    public sealed class ScenarioPromoteRetagsAfterTheGate
    {
        readonly string job = Job("promote");

        [Fact]
        public void ItNeedsStackGate() => Assert.Matches(@"needs:\s*stack-gate", job);

        [Fact]
        public void ItUsesImagetoolsCreate() => Assert.Contains("imagetools create", job, StringComparison.Ordinal);

        [Fact]
        public void ItRetagsFiveImagesToHomeLatest() => Assert.Equal(5, Regex.Matches(job, @"-t\s+""?\S*:home-latest").Count);

        [Fact]
        public void EachRetagSourcesTheTaggedImage() => Assert.Equal(5, Regex.Matches(job, @"\S*:home-\$\{?GW_TAG\}?""?\s*$", RegexOptions.Multiline).Count);
    }

    public sealed class ScenarioCreateReleaseNeedsPromote
    {
        readonly string job = Job("create-release");

        [Fact]
        public void ItNeedsPromote() => Assert.Matches(@"needs:\s*promote", job);

        [Fact]
        public void ItGeneratesNotes() => Assert.Contains("--generate-notes", job, StringComparison.Ordinal);

        [Fact]
        public void ItAppendsTheNotesFile() => Assert.Contains("--notes-file", job, StringComparison.Ordinal);

        [Fact]
        public void TheNotesAreBuiltFromTheReport() => Assert.Contains("gate-report.md", job, StringComparison.Ordinal);

        [Fact]
        public void TheNotesCarryTheProofHeading() => Assert.Contains("## ✅ What this release proved", job, StringComparison.Ordinal);
    }

    public sealed class ScenarioTheHeaderTellsTheNewTruth
    {
        [Fact]
        public void HomeLatestMovesAfterStackGate() => Assert.Matches(@"home-latest[^\n]*(after|behind)[^\n]*stack-gate", Header);

        [Fact]
        public void TheOldPromiseIsGone() => Assert.DoesNotContain("only move after every merge succeeds", Header, StringComparison.Ordinal);
    }
}

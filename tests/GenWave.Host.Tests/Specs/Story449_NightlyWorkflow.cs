// STORY-449 — nightly.yml runs integration and chaos and keeps one red issue (gh-#777 · SPEC F180 · PLAN T499/T500)
//
// BDD specification — xUnit. AC1–AC5 are text pins over .github/workflows/nightly.yml (Story176
// shape); AC6–AC9 drive tools/gate/nightly_report.sh against a scripted `gh` that logs argv and
// answers `issue list` from GATE_STUB_OPEN_ISSUE; AC10 pins the README badge block. The dispatched
// run (AC11) is the wire task, T501.
//
// RED at plan time: nightly.yml, nightly_report.sh and the badge do not exist.

using System.Text.RegularExpressions;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureTheNightlyKeepsOneRedIssue
{
    const string PendingBadge = "pending: T500 — the README nightly badge (STORY-449)";

    static string Repo => RepoRootLocator.Find(AppContext.BaseDirectory);
    static string Workflow => File.ReadAllText(Path.Combine(Repo, ".github", "workflows", "nightly.yml"));

    static string Job(string name)
    {
        var m = Regex.Match(Workflow, $@"^  {Regex.Escape(name)}:\n(.*?)(?=^  [a-z-]+:\n|\z)", RegexOptions.Multiline | RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value : "";
    }

    /// <summary>`issue list --label nightly-red` prints the open issue from GATE_STUB_OPEN_ISSUE
    /// (JSON when --json is asked for), or nothing; every other call is logged and succeeds.</summary>
    const string GhStub = """
        printf 'gh %s\n' "$*" >> "$GATE_STUB_LOG"
        case "$*" in
          "issue list"*)
            if [ -n "${GATE_STUB_OPEN_ISSUE:-}" ]; then
              if [[ "$*" == *--json* ]]; then echo "[{\"number\":${GATE_STUB_OPEN_ISSUE},\"title\":\"Nightly red since 2026-09-14\"}]"
              else printf '%s\tOPEN\tNightly red since 2026-09-14\tnightly-red\t2026-09-14\n' "$GATE_STUB_OPEN_ISSUE"; fi
            elif [[ "$*" == *--json* ]]; then echo "[]"
            fi ;;
          "issue create"*) echo "https://github.com/GenWave-Org/genwave/issues/901" ;;
        esac
        exit 0
        """;

    static string[] Report(string integration, string chaos, string? openIssue)
    {
        var bin = ScriptProcess.MakeBinDir("jq");
        ScriptProcess.AddStub(bin, "gh", GhStub);
        var log = Path.Combine(TempDir.CreateForProcessLifetime(), "gh.log");
        var env = new Dictionary<string, string>
        {
            ["GATE_STUB_LOG"] = log,
            ["GH_TOKEN"] = "stub",
            ["GITHUB_REPOSITORY"] = "GenWave-Org/genwave",
            ["GITHUB_RUN_ID"] = "123456",
        };
        if (openIssue is not null)
            env["GATE_STUB_OPEN_ISSUE"] = openIssue;
        ScriptProcess.Run(Path.Combine(Repo, "tools", "gate", "nightly_report.sh"), bin, null, env,
            $"integration={integration}", $"chaos={chaos}");
        return File.Exists(log) ? File.ReadAllLines(log) : [];
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the workflow
    // ---------------------------------------------------------------------

    public sealed class ScenarioBothTriggers
    {
        [Fact]
        public void TheWorkflowExists() => Assert.True(File.Exists(Path.Combine(Repo, ".github", "workflows", "nightly.yml")));

        [Fact]
        public void ItRunsAtThreeUtc() => Assert.Matches(@"cron:\s*[""']0 3 \* \* \*[""']", Workflow);

        [Fact]
        public void ItCanBeDispatched() => Assert.Contains("workflow_dispatch", Workflow, StringComparison.Ordinal);
    }

    public sealed class ScenarioLeastPermissions
    {
        [Fact]
        public void TopLevelIsContentsRead() => Assert.Matches(@"^permissions:\n  contents: read\n", Workflow);

        [Fact]
        public void OnlyTheReportJobWritesIssues() =>
            Assert.Equal(["report"], new[] { "integration", "chaos", "report" }.Where(j => Job(j).Contains("issues: write", StringComparison.Ordinal)));
    }

    public sealed class ScenarioTheIntegrationJobRunsTierTwo
    {
        readonly string job = Job("integration");

        [Fact]
        public void ItHasASixtyMinuteBudget() => Assert.Matches(@"timeout-minutes:\s*60", job);

        [Fact]
        public void ItFiltersTheIntegrationCategory() => Assert.Contains("--filter \"Category=Integration\"", job, StringComparison.Ordinal);

        [Fact]
        public void ItLogsTrx() => Assert.Contains("--logger trx", job, StringComparison.Ordinal);
    }

    public sealed class ScenarioTheChaosJobTargetsTheLatestRelease
    {
        readonly string job = Job("chaos");

        [Fact]
        public void ItHasAThirtyMinuteBudget() => Assert.Matches(@"timeout-minutes:\s*30", job);

        [Fact]
        public void ItResolvesTheTagFromTheReleaseList() => Assert.Contains("gh release list", job, StringComparison.Ordinal);

        [Fact]
        public void ItRunsFreshCaptureChaos() => Assert.Matches(@"stack_gate\.sh .*--fresh .*--capture .*--chaos", job);
    }

    public sealed class ScenarioTheReportJobAlwaysRuns
    {
        readonly string job = Job("report");

        [Fact]
        public void ItNeedsBothJobs() => Assert.Matches(@"needs:\s*\[\s*integration,\s*chaos\s*\]", job);

        [Fact]
        public void ItRunsEvenWhenTheyFail() => Assert.Matches(@"if:\s*always\(\)", job);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — nightly_report.sh
    // ---------------------------------------------------------------------

    public sealed class ScenarioAFirstRedOpensTheIssue
    {
        readonly string[] calls = Report("failure", "success", openIssue: null);

        [Fact]
        public void ItCreatesOneIssue() => Assert.Single(calls, c => c.StartsWith("gh issue create", StringComparison.Ordinal));

        [Fact]
        public void TheTitleStartsWithNightlyRedSince() => Assert.Matches(@"gh issue create .*--title ""?Nightly red since ", string.Join("\n", calls));

        [Fact]
        public void TheLabelIsNightlyRed() => Assert.Matches(@"gh issue create .*--label ""?nightly-red", string.Join("\n", calls));
    }

    public sealed class ScenarioASecondRedCommentsInstead
    {
        readonly string[] calls = Report("failure", "success", openIssue: "900");

        [Fact]
        public void ItCommentsOnTheOpenIssue() => Assert.Contains(calls, c => c.StartsWith("gh issue comment 900", StringComparison.Ordinal));

        [Fact]
        public void ItCreatesNothing() => Assert.DoesNotContain(calls, c => c.StartsWith("gh issue create", StringComparison.Ordinal));
    }

    public sealed class ScenarioGreenClosesTheIssue
    {
        readonly string[] calls = Report("success", "success", openIssue: "900");

        [Fact]
        public void ItSaysGreen() => Assert.Matches(@"gh issue comment 900 .*green", string.Join("\n", calls));

        [Fact]
        public void ItClosesAfterTheComment() =>
            Assert.True(Array.FindIndex(calls, c => c.StartsWith("gh issue comment 900", StringComparison.Ordinal))
                < Array.FindIndex(calls, c => c.StartsWith("gh issue close 900", StringComparison.Ordinal)), string.Join("\n", calls));
    }

    public sealed class ScenarioGreenWithNothingOpenIsQuiet
    {
        readonly string[] calls = Report("success", "success", openIssue: null);

        [Fact]
        public void NoIssueCommandRuns() =>
            Assert.DoesNotContain(calls, c => c.StartsWith("gh issue ", StringComparison.Ordinal) && !c.StartsWith("gh issue list", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the README badge
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheReadmeBadge
    {
        readonly string[] badges = File.ReadLines(Path.Combine(Repo, "README.md")).Take(12)
            .Where(l => l.StartsWith("[![", StringComparison.Ordinal)).ToArray();

        [Fact(Skip = PendingBadge)]
        public void ItLinksTheNightlyBadge() =>
            Assert.Contains(badges, b => b.Contains("actions/workflows/nightly.yml/badge.svg", StringComparison.Ordinal));

        [Fact(Skip = PendingBadge)]
        public void ItSitsBesideTheDemoBadge() =>
            Assert.Equal(1, Math.Abs(
                Array.FindIndex(badges, b => b.Contains("nightly.yml/badge.svg", StringComparison.Ordinal))
                - Array.FindIndex(badges, b => b.Contains("demo-health.yml/badge.svg", StringComparison.Ordinal))));
    }
}

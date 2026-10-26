// STORY-504 — The demo plumbing is gone (F215 · PLAN T627)
//
// BDD specification — xUnit. Absence facts over the repo tree: the demo probe workflow, the demo
// drift check and its CI job no longer exist. AC3 (the dropped doc-prose pins) is the PR tier
// itself staying green — no spec.

using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureTheDemoPlumbingIsGone
{
    static string Root => RepoRootLocator.Find(AppContext.BaseDirectory);

    static string Read(string relative) => File.ReadAllText(Path.Combine(Root, relative));

    public static class ScenarioTheDemoProbeIsGone
    {
        [Fact]
        public static void The_demo_health_workflow_does_not_exist()
        {
            // Given the repo tree
            // When  .github/workflows is listed
            // Then  demo-health.yml does not exist
            Assert.False(File.Exists(Path.Combine(Root, ".github", "workflows", "demo-health.yml")));
        }

        [Fact]
        public static void The_readme_has_no_badge_pointing_at_it()
        {
            Assert.DoesNotContain("demo-health", Read("README.md"), StringComparison.Ordinal);
        }
    }

    public static class ScenarioTheDriftCheckIsGone
    {
        [Fact]
        public static void The_drift_script_does_not_exist()
        {
            Assert.False(File.Exists(Path.Combine(Root, "tools", "check-doc-drift.sh")));
        }

        [Fact]
        public static void Ci_has_no_doc_drift_job()
        {
            var ci = Read(Path.Combine(".github", "workflows", "ci.yml"));
            Assert.DoesNotContain("check-doc-drift", ci, StringComparison.Ordinal);
            Assert.DoesNotContain("doc-drift:", ci, StringComparison.Ordinal);
        }
    }
}

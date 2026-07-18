// STORY-176 — Release workflow: pushing a v* tag is the only manual act
//
// BDD specification — xUnit (SPEC F65.2). .github/workflows/release.yml triggers on v* tags,
// stamps GW_VERSION from the tag, publishes images, creates the GitHub Release — and never
// writes back to the repository (no CI version-bump commits, by design; MEMORY 2026-07-18).
// The live run is verified on the first real tag. Red until PLAN T09.

namespace GenWave.Host.Tests.Specs;

public static class FeatureReleaseWorkflowContract
{
    static string WorkflowPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent!;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, ".github", "workflows", "release.yml");
    }

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioWorkflowDefinition
    {
        [Fact]
        public void ReleaseWorkflowExists()
        {
            Assert.True(File.Exists(WorkflowPath()),
                ".github/workflows/release.yml is missing (F65.2).");
        }

        [Fact]
        public void TriggersOnVersionTags()
        {
            var workflow = File.ReadAllText(WorkflowPath());
            Assert.Contains("v*", workflow, StringComparison.Ordinal);
        }

        [Fact]
        public void PassesTheTagAsGwVersion()
        {
            var workflow = File.ReadAllText(WorkflowPath());
            Assert.Contains("GW_VERSION", workflow, StringComparison.Ordinal);
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioNoWriteBack
    {
        [Fact]
        public void NoStepPushesToTheRepository()
        {
            var workflow = File.ReadAllText(WorkflowPath());
            Assert.DoesNotContain("git push", workflow, StringComparison.OrdinalIgnoreCase);
        }
    }
}

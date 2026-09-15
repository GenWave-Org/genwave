// gh-#441 — launch.sh --pinned prunes superseded release images after a healthy up.
//
// BDD specification — xUnit. Drives the real ./launch.sh via Process, --dry-run only
// (the Story201 contract: the plan is the behavior, assertable with no docker daemon).
//
// Why this exists: nothing ever pruned old `home-v*` tags — 46 GB of dead images filled the
// demo box's 75 GB disk mid-deploy on 2026-08-09 (the db PANIC'd on a full device during
// db/34). The prune is success-path-only hygiene: a FAILED deploy must leave the previous
// images untouched, because they are what is still running. These specs pin the plan shape;
// the success-path-only property is structural (every failure bails via preflight_fail before
// the prune line is reached).

using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeaturePinnedFlowPrunesSupersededImages
{
    // These specs drive the real toolchain (docker/bash on the developer's own PATH), not a
    // scratch bin — ScriptProcess still starts the child from its sanitized environment (gh-#776).
    static readonly string RealPath = Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin";

    static (int ExitCode, string StdOut) RunLaunch(params string[] args)
    {
        var result = ScriptProcess.Run("launch.sh", RealPath, args: args);
        return (result.ExitCode, result.StdOut);
    }

    static string[] PlanLines(string stdOut) =>
        stdOut.Split('\n').Where(l => l.StartsWith("plan> ", StringComparison.Ordinal)).ToArray();

    public static class ScenarioPinnedFlowPrunes
    {
        static readonly Lazy<(int ExitCode, string StdOut)> Run =
            new(() => RunLaunch("--pinned", "--dry-run"));

        [Fact]
        public static void Plan_prunes_images_with_the_retention_filter()
        {
            // The 7-day CREATED-time filter is the rollback-safety contract: the freshly pulled
            // release and its recent predecessors survive; month-old tags go.
            Assert.Contains(
                PlanLines(Run.Value.StdOut),
                l => l.Contains("image prune", StringComparison.Ordinal) && l.Contains("until=168h", StringComparison.Ordinal));
        }

        [Fact]
        public static void Plan_prunes_the_builder_cache()
        {
            // A --pinned box never builds (BUILD=1 + --pinned errors at parse time); its build
            // cache is pure waste.
            Assert.Contains(
                PlanLines(Run.Value.StdOut),
                l => l.Contains("builder prune", StringComparison.Ordinal));
        }

        [Fact]
        public static void Prune_is_planned_after_the_stack_is_up()
        {
            // Order is the safety property the plan can express: pull → migrate → up → prune.
            // Pruning before `up -d` could remove the very images a rollback needs while the
            // new ones are still unproven.
            var lines = PlanLines(Run.Value.StdOut);
            // The FULL-stack up, not the earlier db-only bootstrap step (which also contains
            // "up -d", as "up -d --no-recreate db") — distinguished by the absence of
            // --no-recreate, not by an exact flag suffix, so this ordering pin survives the full
            // up's own flag set changing (T148 review finding F6 added --remove-orphans there).
            var upIndex = Array.FindIndex(lines, l =>
                l.Contains("up -d", StringComparison.Ordinal) && !l.Contains("--no-recreate", StringComparison.Ordinal));
            var pruneIndex = Array.FindIndex(lines, l => l.Contains("image prune", StringComparison.Ordinal));
            Assert.True(upIndex >= 0 && pruneIndex > upIndex, $"expected image prune after `up -d` (up at {upIndex}, prune at {pruneIndex})");
        }
    }

    public static class ScenarioDevFlowDoesNotPrune
    {
        [Fact]
        public static void Dev_plan_contains_no_prune()
        {
            // The dev flow builds from source — pruning there would eat build layers and
            // dangling intermediates a developer is actively using.
            var run = RunLaunch("--dry-run");
            Assert.DoesNotContain(
                PlanLines(run.StdOut),
                l => l.Contains("prune", StringComparison.Ordinal));
        }
    }
}

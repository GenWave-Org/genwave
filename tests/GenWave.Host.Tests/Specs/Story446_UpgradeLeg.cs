// STORY-446 — The upgrade leg migrates the previous release forward (gh-#777 · SPEC F178.6, F178.7 · PLAN T493/T494)
//
// BDD specification — xUnit. The upgrade leg of tools/gate/stack_gate.sh through GateHarness with
// two more stubs on the scratch PATH: `gh` (release list → v5.9.0, v5.8.3, v5.8.2) and `git`
// (logs argv; `worktree add` materialises the previous tree as a copy of the scratch so its
// launch.sh/migrate.sh stubs exist there too). psql answers come from the docker stub's
// GATE_STUB_BOUNDARY knob.
//
// RED at plan time: tools/gate/stack_gate.sh does not exist.

using GenWave.Host.Tests.Support;
using static GenWave.Host.Tests.Support.GateHarness;

namespace GenWave.Host.Tests.Specs;

public static class FeatureTheUpgradeLegMigratesThePreviousReleaseForward
{
    const string PendingLeg = "pending: T493 — previous-release resolution, worktree, the migrate sequence (STORY-446)";
    const string PendingBoundary = "pending: T494 — the role boundary inside the upgrade leg (STORY-446)";

    const string GhStub = """
        printf 'gh %s\n' "$*" >> "$GATE_STUB_LOG"
        case "$*" in
          "release list"*)
            if [[ "$*" == *--json* ]]; then
              echo '[{"tagName":"v5.9.0"},{"tagName":"v5.8.3"},{"tagName":"v5.8.2"}]'
            else
              printf 'v5.9.0\tLatest\tv5.9.0\t2026-09-20\nv5.8.3\t\tv5.8.3\t2026-09-15\nv5.8.2\t\tv5.8.2\t2026-09-13\n'
            fi ;;
        esac
        exit 0
        """;

    // `worktree add <path> <ref>` copies the current tree (stubs included) into <path>.
    const string GitStub = """
        printf 'git %s\n' "$*" >> "$GATE_STUB_LOG"
        if [ "${1:-}" = "worktree" ] && [ "${2:-}" = "add" ]; then
          dest="$3"; mkdir -p "$dest"
          cp -rL --no-preserve=ownership . "$dest" 2>/dev/null || cp -r . "$dest"
          rm -rf "$dest/.git"
        fi
        exit 0
        """;

    static string Bin() => MakeBinDir(new Dictionary<string, string> { ["gh"] = GhStub, ["git"] = GitStub });

    static Run Upgrade(FakeStation station, IReadOnlyDictionary<string, string>? env = null, params string[] extra) =>
        Execute(station, Bin(), env, ["--tag", "v5.9.0", "--upgrade", .. extra]);

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioThePreviousReleaseIsTheNewestOtherOne : IDisposable
    {
        readonly FakeStation station = new();
        readonly Run run;

        public ScenarioThePreviousReleaseIsTheNewestOtherOne() => run = Upgrade(station);

        public void Dispose() => station.Dispose();

        [Fact(Skip = PendingLeg)]
        public void TheLegPasses() => Assert.Equal(0, run.ExitCode);

        [Fact(Skip = PendingLeg)]
        public void PreviousIsV583() => Assert.Contains(run.Calls, c => c.StartsWith("git worktree add ", StringComparison.Ordinal) && c.EndsWith(" v5.8.3", StringComparison.Ordinal));

        [Fact(Skip = PendingLeg)]
        public void UpComesFirst() => Assert.True(run.IndexOf("up files=") >= 0 && run.IndexOf("up files=") < run.IndexOf("stop api"), string.Join("\n", run.Calls));

        [Fact(Skip = PendingLeg)]
        public void StopApiPrecedesMigrate() => Assert.True(run.IndexOf("stop api") < run.IndexOf("migrate.sh"), string.Join("\n", run.Calls));

        [Fact(Skip = PendingLeg)]
        public void MigratePrecedesTheApiRestart() =>
            Assert.True(run.IndexOf("migrate.sh") < Array.FindLastIndex(run.Calls, c => c.Contains("up", StringComparison.Ordinal) && c.EndsWith(" api", StringComparison.Ordinal)), string.Join("\n", run.Calls));

        [Fact(Skip = PendingLeg)]
        public void TheFirstUpPinsThePreviousTag() =>
            Assert.Contains(":home-v5.8.3", run.Overlay.Split("services:")[1], StringComparison.Ordinal);

        [Fact(Skip = PendingLeg)]
        public void TheWorktreeIsRemovedOnExit() => Assert.Contains(run.Calls, c => c.StartsWith("git worktree remove", StringComparison.Ordinal));

        [Fact(Skip = PendingBoundary)]
        public void TheBoundaryRowIsOk() => Assert.Contains("role boundary | ok", run.ReportMd, StringComparison.Ordinal);
    }

    public sealed class ScenarioFromOverridesThePreviousRelease : IDisposable
    {
        readonly FakeStation station = new();
        readonly Run run;

        public ScenarioFromOverridesThePreviousRelease() => run = Upgrade(station, null, "--from", "v5.8.1");

        public void Dispose() => station.Dispose();

        [Fact(Skip = PendingLeg)]
        public void PreviousIsV581() => Assert.Contains(run.Calls, c => c.StartsWith("git worktree add ", StringComparison.Ordinal) && c.EndsWith(" v5.8.1", StringComparison.Ordinal));

        [Fact(Skip = PendingLeg)]
        public void GhIsNotConsulted() => Assert.DoesNotContain(run.Calls, c => c.StartsWith("gh release list", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioAFailedMigrationFailsTheLeg : IDisposable
    {
        readonly FakeStation station = new();
        readonly Run run;

        public ScenarioAFailedMigrationFailsTheLeg() =>
            run = Upgrade(station, new Dictionary<string, string> { ["GATE_STUB_MIGRATE_EXIT"] = "1" });

        public void Dispose() => station.Dispose();

        [Fact(Skip = PendingLeg)]
        public void ExitIsOne() => Assert.Equal(1, run.ExitCode);

        [Fact(Skip = PendingLeg)]
        public void MigrateIsTheFirstFailingAssertion() => Assert.Equal("migrate", run.FirstFailure);

        [Fact(Skip = PendingLeg)]
        public void TheWorktreeIsStillRemoved() => Assert.Contains(run.Calls, c => c.StartsWith("git worktree remove", StringComparison.Ordinal));
    }

    public sealed class ScenarioARowCountAcrossTheBoundaryFails : IDisposable
    {
        readonly FakeStation station = new();
        readonly Run run;

        public ScenarioARowCountAcrossTheBoundaryFails() =>
            run = Upgrade(station, new Dictionary<string, string> { ["GATE_STUB_BOUNDARY"] = "42" });

        public void Dispose() => station.Dispose();

        [Fact(Skip = PendingBoundary)]
        public void ExitIsOne() => Assert.Equal(1, run.ExitCode);

        [Fact(Skip = PendingBoundary)]
        public void RoleBoundaryIsTheFirstFailingAssertion() => Assert.Equal("role boundary", run.FirstFailure);
    }
}

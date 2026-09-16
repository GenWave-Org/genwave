// STORY-436 — A failed migration stops the launch (gh-#770 · PLAN T468–T469)
//
// Runner: xUnit driving the REAL ./launch.sh dev flow (down → up db → health poll → migrate.sh →
// up) against a scripted `docker` on PATH — the Gh332 idiom, via ScriptProcess (gh-#776), which
// always starts the child from a sanitized environment. Every run happens inside a SCRATCH COPY
// of the repo root (symlinks to every top-level entry except .env/.git, plus a real db/
// directory) because the dev flow's tail writes COMPOSE_FILE into `.env` in its working
// directory: the developer's own .env must never be touched by a spec — ScriptProcess.Run's
// fixed WorkingDirectory is harmless here since launch.sh self-relocates via
// `cd "$(dirname "$0")"` as its first meaningful action, so it always operates on the scratch
// copy the absolute script path actually points into. The sad path plants one extra migration,
// db/99-spec-fail-migration.sh, which the docker stub fails on sight.
//
// RED at plan time: launch.sh runs `./migrate.sh --keep-going "${MIGRATE_ARGS[@]}" || true`, so a
// failed migration is reported once on stdout and the launch carries on to `compose up` and exit 0.

using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAFailedMigrationStopsTheLaunch
{
    const string FailingMigration = "db/99-spec-fail-migration.sh";
    const string PsqlErrorText = "ERROR:  relation \"station.nope\" does not exist";

    // Answers every docker call the dev flow makes and logs each one to $GW_DOCKER_LOG. The
    // migration runner pipes each db/*-migration.sh into `compose exec -T db bash -s`, so the
    // stub sees the script on stdin — the planted migration carries a marker and fails with a
    // psql-shaped error, every real one succeeds. Order matters: `ps -q db` before bare `ps`,
    // `up ... db` before bare `up`.
    const string DockerStub = """
        printf '%s\n' "$*" >> "$GW_DOCKER_LOG"
        case "${1:-}" in
          info) exit 0 ;;
          inspect)
            case "$*" in
              *State.Health.Status*) echo healthy; exit 0 ;;
              *State.Running*)       echo true;    exit 0 ;;
            esac
            exit 0 ;;
        esac
        case "$*" in
          *" version"*)              echo "Docker Compose version v2.29.0"; exit 0 ;;
          *" config"*)               exit 0 ;;
          *" ps -q db"*)             echo deadbeefcafe; exit 0 ;;
          *" exec -T db "*)
            if grep -q GW_SPEC_FAIL_MIGRATION; then
              echo "ERROR:  relation \"station.nope\" does not exist" >&2
              exit 1
            fi
            exit 0 ;;
        esac
        exit 0
        """;

    static string MakeBinDir()
    {
        var dir = ScriptProcess.MakeBinDir();
        ScriptProcess.AddStub(dir, "docker", DockerStub);
        return dir;
    }

    /// <summary>
    /// A scratch working directory that IS the repo for launch.sh's purposes (it cd's to its
    /// own dirname): symlinks to every top-level entry except .env and .git, and a real db/
    /// whose entries link the real migrations — so a planted migration lands here, never in
    /// the tree.
    /// </summary>
    static string MakeScratchRepo(bool plantFailingMigration)
    {
        var root = RepoRootLocator.Find(AppContext.BaseDirectory);
        var scratch = TempDir.CreateForProcessLifetime();
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            var name = Path.GetFileName(entry);
            if (name is ".env" or ".git" or "db")
                continue;
            if (Directory.Exists(entry))
                Directory.CreateSymbolicLink(Path.Combine(scratch, name), entry);
            else
                File.CreateSymbolicLink(Path.Combine(scratch, name), entry);
        }

        var db = Path.Combine(scratch, "db");
        Directory.CreateDirectory(db);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "db")))
            File.CreateSymbolicLink(Path.Combine(db, Path.GetFileName(file)), file);

        if (plantFailingMigration)
            File.WriteAllText(Path.Combine(scratch, FailingMigration),
                "#!/usr/bin/env bash\n# GW_SPEC_FAIL_MIGRATION — planted by Story436; the docker stub fails this one.\nexit 1\n");

        return scratch;
    }

    /// <summary>The six required secrets, via preflight's GW_ENV_FILE seam — never the real .env.</summary>
    static string WriteEnvFile()
    {
        var path = Path.Combine(TempDir.CreateForProcessLifetime(), "test.env");
        File.WriteAllLines(path,
        [
            "POSTGRES_PASSWORD=x", "LIBRARY_DB_PASSWORD=x", "STATION_DB_PASSWORD=x",
            "ICECAST_SOURCE_PASSWORD=x", "ICECAST_ADMIN_PASSWORD=x",
            $"MEDIA_DIR={Path.GetTempPath()}",
        ]);
        return path;
    }

    sealed record Run(int ExitCode, string StdOut, string StdErr, string[] DockerCalls, string Scratch)
    {
        public string Transcript => StdOut + "\n" + StdErr;

        /// <summary>Docker calls issued after the migration runner's first `exec -T db`.</summary>
        public string[] CallsAfterMigrate
        {
            get
            {
                var first = Array.FindIndex(DockerCalls, c => c.Contains(" exec -T db ", StringComparison.Ordinal));
                return first < 0 ? [] : DockerCalls[(first + 1)..];
            }
        }
    }

    static Run RunLaunch(bool plantFailingMigration, params string[] args)
    {
        var bin = MakeBinDir();
        var scratch = MakeScratchRepo(plantFailingMigration);
        var log = Path.Combine(TempDir.CreateForProcessLifetime(), "docker.log");

        var extraEnv = new Dictionary<string, string>
        {
            ["GW_DOCKER_LOG"] = log,
            // The machine checks (ports, disk, RAM) are Gh019/Story342's subject, not this
            // story's; the migration step under test sits well past them.
            ["SKIP_PREFLIGHT"] = "1",
        };

        var (exitCode, stdOut, stdErr) = ScriptProcess.Run(
            Path.Combine(scratch, "launch.sh"), bin, WriteEnvFile(), extraEnv, args);

        var calls = File.Exists(log) ? File.ReadAllLines(log) : [];
        return new Run(exitCode, stdOut, stdErr, calls, scratch);
    }

    static string[] PlanLines(string stdOut) =>
        stdOut.Split('\n').Where(l => l.StartsWith("plan> ", StringComparison.Ordinal)).ToArray();

    static readonly Lazy<Run> FailedMigration = new(() => RunLaunch(plantFailingMigration: true));
    static readonly Lazy<Run> CleanMigrations = new(() => RunLaunch(plantFailingMigration: false));
    static readonly Lazy<Run> DryRun = new(() => RunLaunch(plantFailingMigration: false, "--dry-run"));

    // ---------------------------------------------------------------------
    // HAPPY PATH — the launch stops, explains itself, and leaves the db up
    // ---------------------------------------------------------------------

    public static class ScenarioOneMigrationFailsDuringTheDevFlow
    {
        [Fact]
        public static void The_launch_exits_nonzero()
            => Assert.NotEqual(0, FailedMigration.Value.ExitCode);

        [Fact]
        public static void The_rest_of_the_stack_is_never_brought_up()
            => Assert.DoesNotContain(FailedMigration.Value.CallsAfterMigrate,
                c => c.Contains(" up ", StringComparison.Ordinal) && !c.EndsWith(" db", StringComparison.Ordinal));

        [Fact]
        public static void The_verdict_says_the_migration_failed()
            => Assert.Contains("Schema migration failed", FailedMigration.Value.StdErr);

        [Fact]
        public static void The_verdict_says_the_application_was_not_started()
            => Assert.Contains("was NOT started", FailedMigration.Value.StdErr);

        [Fact]
        public static void The_output_names_the_failing_migration()
            => Assert.Contains(FailingMigration, FailedMigration.Value.Transcript);

        [Fact]
        public static void The_migrations_own_error_text_is_shown()
            // Only fail-fast migrate.sh (no --keep-going) surfaces the psql text the operator
            // actually needs; --keep-going discards it by design.
            => Assert.Contains(PsqlErrorText, FailedMigration.Value.StdErr);

        [Fact]
        public static void The_database_is_left_up_for_inspection()
            // Ruling 2026-09-15: leave db up, exit nonzero — no rollback `down` after migrate.
            => Assert.DoesNotContain(FailedMigration.Value.CallsAfterMigrate,
                c => c.Contains(" down", StringComparison.Ordinal));

        [Fact]
        public static void The_recovery_advice_names_the_migration_runner()
            => Assert.Contains("./migrate.sh", FailedMigration.Value.StdErr);

        [Fact]
        public static void The_recovery_advice_names_the_relaunch()
            => Assert.Contains("./launch.sh", FailedMigration.Value.StdErr);

        [Fact]
        public static void The_compose_file_is_not_recorded_for_a_launch_that_did_not_happen()
            => Assert.False(File.Exists(Path.Combine(FailedMigration.Value.Scratch, ".env")));
    }

    public static class ScenarioEveryMigrationSucceeds
    {
        [Fact]
        public static void The_launch_exits_zero()
            => Assert.Equal(0, CleanMigrations.Value.ExitCode);

        [Fact]
        public static void The_rest_of_the_stack_comes_up_after_the_migrations()
            => Assert.Contains(CleanMigrations.Value.CallsAfterMigrate,
                c => c.Contains(" up ", StringComparison.Ordinal) && !c.EndsWith(" db", StringComparison.Ordinal));

        [Fact]
        public static void The_compose_file_is_recorded_in_the_scratch_env_only()
            // Proves the harness reached the flow's tail — and that the write landed in the
            // scratch copy, i.e. this spec can never touch the developer's own .env.
            => Assert.Contains("COMPOSE_FILE=", File.ReadAllText(Path.Combine(CleanMigrations.Value.Scratch, ".env")));

        [Fact]
        public static void No_migration_failure_is_reported()
            => Assert.DoesNotContain("Schema migration failed", CleanMigrations.Value.StdErr);
    }

    public static class ScenarioTheDryRunPlanDescribesTheNewStep
    {
        [Fact]
        public static void The_plan_runs_migrate_without_keep_going()
            => Assert.Contains("plan> ./migrate.sh", PlanLines(DryRun.Value.StdOut));

        [Fact]
        public static void The_plan_no_longer_mentions_keep_going()
            => Assert.DoesNotContain(PlanLines(DryRun.Value.StdOut),
                l => l.Contains("--keep-going", StringComparison.Ordinal));
    }

    public static class ScenarioThePinnedFlowKeepsItsFailFastCall
    {
        static readonly Lazy<string> LaunchScript = new(() =>
            File.ReadAllText(Path.Combine(RepoRootLocator.Find(AppContext.BaseDirectory), "launch.sh")));

        [Fact]
        public static void The_pinned_path_still_checks_migrate_sh()
            => Assert.Contains("if ! ./migrate.sh \"${MIGRATE_ARGS[@]}\"; then", LaunchScript.Value);

        [Fact]
        public static void No_call_to_migrate_sh_is_swallowed_anywhere()
            => Assert.DoesNotContain("./migrate.sh --keep-going", LaunchScript.Value);
    }

    public static class ScenarioTheDeploymentGuideExplainsTheRecovery
    {
        static readonly Lazy<string> Deployment = new(() =>
            File.ReadAllText(Path.Combine(RepoRootLocator.Find(AppContext.BaseDirectory), "DEPLOYMENT.md")));

        [Fact]
        public static void The_guide_names_the_failed_migration_verdict()
            => Assert.Contains("Schema migration failed", Deployment.Value);
    }
}

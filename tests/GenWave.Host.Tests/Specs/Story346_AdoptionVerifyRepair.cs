// STORY-346 — Adopt the existing box (F137)
//
// BDD specification — xUnit. Drives the REAL ./setup.sh via Process against scratch checkouts
// seeded with specific drift (the Gh019 idiom: scratch PATH, a scripted docker stub reporting
// container/image/schema state via GW_DOCKER_CMD, GW_ENV_FILE seam). The do-no-harm clause's own
// live-box proof (AC4) is additionally exercised on the real Pi 4 at T321 — the wire, not here;
// this file's own AC4 facts prove the SCRIPT is read-only by construction (zero file mutation,
// only read-safe docker subcommands ever invoked), which is what makes that wire attempt safe to
// run at all.
//
// Harness: the Story344/345 idiom (scratch PATH bin dir of coreutils symlinks, a scratch
// GW_ENV_FILE, ambient GW_*/SKIP_PREFLIGHT scrubbed from the child environment) — duplicated here
// rather than shared (T318's own pinned-for-Dean rider: harness dedup across Story344/345/346 is
// accepted debt for now). SKIP_PREFLIGHT=1 on every scenario: preflight_docker/preflight_env_secrets
// are Story342/344's own suite — this file's concern starts at adoption mode's own six drift
// probes and its repair loop, exercised through a GW_DOCKER_CMD-seamed docker stub (mirrors
// GW_LAUNCH_CMD's own shape) so no real daemon is ever needed, hard rule 5.
//
// House rule: one assert per Fact — several facts assert one combined boolean via a single
// Assert.True(...) call where the observation is genuinely one logical fact (several conditions
// that only mean something together), the same idiom Story344/345 already use.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAdoptionVerifyRepair
{
    // ─────────────────────────────────────────────────────────────────────────
    // Shared harness (the Story344/345 idiom, duplicated per T318's pinned rider)
    // ─────────────────────────────────────────────────────────────────────────

    static readonly string[] RequiredEnvVars =
    [
        "POSTGRES_PASSWORD", "LIBRARY_DB_PASSWORD", "STATION_DB_PASSWORD",
        "ICECAST_SOURCE_PASSWORD", "ICECAST_ADMIN_PASSWORD", "MEDIA_DIR",
    ];

    /// <summary>setup.sh/preflight.sh test seams this suite might otherwise inherit from the
    /// ambient shell — scrubbed so the developer's real .env/exports can never sway a fact.</summary>
    static readonly string[] SeamEnvVars =
    [
        "ADMIN_PASSWORD", "COMPOSE_PROFILES", "COMPOSE_FILE", "GW_PRESET", "GW_ENV_FILE",
        "GW_MEMINFO_FILE", "GW_ARCH", "GW_PREFLIGHT_TOPOLOGY", "GW_PREFLIGHT_DEMO",
        "GW_CMDLINE_FILE", "GW_MOUNTS_FILE", "GW_SS_CMD", "GW_DF_CMD", "GW_FIND_CMD",
        "GW_DOCKER_ROOT_FALLBACK", "GW_DOCKER_CMD", "SKIP_PREFLIGHT", "GW_LAUNCH_CMD",
        "GW_STREAM_URL", "GW_ONAIR_TIMEOUT_SECONDS",
    ];

    static readonly string[] BaseTools =
    [
        "bash", "sh", "grep", "sed", "tail", "head", "cut", "seq", "sleep", "awk", "dirname",
        "cat", "paste", "find", "tr", "mktemp", "mv", "rm", "uname", "date",
        // N1 (round-2 review): verify_pinned_image_tags mirrors launch.sh's own
        // print_pinned_image_tags, `sort -u` included — needed once verify_stale_images can
        // take the pinned branch.
        "sort",
    ];

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;

        if (dir is null) throw new InvalidOperationException("repo root (GenWave.sln) not found");
        return dir.FullName;
    }

    static string ResolveTool(string tool)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':'))
        {
            var candidate = Path.Combine(dir, tool);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new InvalidOperationException($"required tool not on PATH: {tool}");
    }

    static string MakeBinDir()
    {
        var dir = Directory.CreateTempSubdirectory("gw-setup-story346-bin-").FullName;
        foreach (var tool in BaseTools)
            File.CreateSymbolicLink(Path.Combine(dir, tool), ResolveTool(tool));
        return dir;
    }

    /// <summary>A bin dir like <see cref="MakeBinDir"/>, plus the REAL `docker` binary — only
    /// the B1 real-Postgres fact needs this (GW_DOCKER_CMD is left unset there, so setup.sh
    /// resolves the literal name `docker` off PATH, same as it would on a real box).</summary>
    static string MakeBinDirWithDocker()
    {
        var dir = MakeBinDir();
        File.CreateSymbolicLink(Path.Combine(dir, "docker"), ResolveTool("docker"));
        return dir;
    }

    /// <summary>A scratch COPY of the real checkout's setup.sh + tools/preflight.sh + the whole
    /// db/ directory + .env.example + every compose*.yaml — B2's own derivation facts need a
    /// db/ they can freely add a scratch migration file into, and setup.sh's
    /// `cd "$(dirname "$0")"` means that has to be a real copy of the script sitting next to a
    /// real copy of db/, not the actual repo checkout this test binary itself lives under.
    /// .env.example and compose*.yaml (round-3 delta review): without them, a script run
    /// through this checkout short-circuits verify_env_completeness (no .env.example — UNKNOWN,
    /// "not found in this checkout — skipped") and starves verify_compose_overrides of a
    /// "shipped" set (an empty `compose*.yaml` glob), leaving both probes' own write paths
    /// outside VerifyModeMakesZeroWritesToTheBox's do-no-harm proof even though every OTHER
    /// probe's write path was covered.</summary>
    static string MakeScratchCheckout()
    {
        var repoRoot = RepoRoot();
        var root = Directory.CreateTempSubdirectory("gw-setup-story346-checkout-").FullName;

        File.Copy(Path.Combine(repoRoot, "setup.sh"), Path.Combine(root, "setup.sh"));
        MakeExecutable(Path.Combine(root, "setup.sh"));

        Directory.CreateDirectory(Path.Combine(root, "tools"));
        File.Copy(Path.Combine(repoRoot, "tools", "preflight.sh"), Path.Combine(root, "tools", "preflight.sh"));

        var dbDir = Path.Combine(root, "db");
        Directory.CreateDirectory(dbDir);
        foreach (var f in Directory.EnumerateFiles(Path.Combine(repoRoot, "db")))
            File.Copy(f, Path.Combine(dbDir, Path.GetFileName(f)));

        File.Copy(Path.Combine(repoRoot, ".env.example"), Path.Combine(root, ".env.example"));

        foreach (var f in Directory.EnumerateFiles(repoRoot, "compose*.yaml", SearchOption.TopDirectoryOnly))
            File.Copy(f, Path.Combine(root, Path.GetFileName(f)));

        return root;
    }

    static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    static string ScratchEnvDir() => Directory.CreateTempSubdirectory("gw-setup-story346-env-").FullName;

    static string ScratchEnvPath() => Path.Combine(ScratchEnvDir(), ".env");

    /// <summary>Every key .env.example sets UNCOMMENTED by default (F137.1's own ".env
    /// completeness" scope — a commented-by-default key like #STATION_NAME documents an OPTIONAL
    /// setting, its absence is normal, not drift), each given a real, non-placeholder value —
    /// the "adopted box has everything preflight/verify would ever want" baseline every scenario
    /// below starts from and perturbs.</summary>
    static Dictionary<string, string> HealthyEnvValues(string mediaDir, string composeFile) => new()
    {
        ["COMPOSE_PROFILES"] = "admin",
        ["MEDIA_DIR"] = mediaDir,
        ["COMPOSE_FILE"] = composeFile,
        ["POSTGRES_PASSWORD"] = "a-real-postgres-secret-0123456789",
        ["LIBRARY_DB_PASSWORD"] = "a-real-library-secret-0123456789",
        ["STATION_DB_PASSWORD"] = "a-real-station-secret-0123456789",
        ["ICECAST_SOURCE_PASSWORD"] = "a-real-icecast-source-0123456789",
        ["ICECAST_ADMIN_PASSWORD"] = "a-real-icecast-admin-0123456789",
        ["PUBLIC_HOST"] = "radio.example.com",
        ["ADMIN_PASSWORD"] = "a-real-admin-ui-secret-0123456789",
        ["TUNNEL_TOKEN"] = "",
    };

    static void WriteEnvFile(string path, IReadOnlyDictionary<string, string> values)
    {
        var lines = values.Select(kv => $"{kv.Key}={kv.Value}");
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
    }

    static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>Writes a scripted `docker` stand-in — every adoption-mode docker/docker-compose
    /// call in setup.sh goes through GW_DOCKER_CMD (this file's path, mirroring GW_LAUNCH_CMD's
    /// own shape), so no real daemon is ever needed. Case-matches on the exact joined argv
    /// ("$*") setup.sh's own probes construct — see each probe's own remarks in setup.sh for the
    /// precise command shape each pattern below corresponds to. Every knob defaults to the
    /// "healthy box" answer; a scenario overrides only what it needs to perturb.</summary>
    static string WriteDockerStub(
        string composeArgs = "-f compose.yaml",
        bool dbReachable = true,
        string migrationMarker = "t",
        string migrationMarkerTable = "station.station_image",
        string? stationNameJson = null,
        string[]? services = null,
        string composeConfigBody = "services:\n  db:\n    image: postgres\n  api:\n    image: genwave/api\n",
        string projectName = "genwave",
        (string Service, string Name)[]? actualContainers = null,
        string containerState = "running",
        string reclaimable = "0B",
        string? logPath = null,
        string? builtServiceName = null,
        (string Cid, string ImageId, string CreatedIso)? builtImage = null,
        string[]? pinnedImageTags = null)
    {
        services ??= ["db", "api"];
        actualContainers ??= services.Select(s => (s, $"genwave-{s}-1")).ToArray();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("#!/usr/bin/env bash");
        if (logPath is not null)
            sb.AppendLine($"printf '%s\\n' \"$*\" >> {Quote(logPath)}");
        // T321 wire finding 2: setup.sh's own compose argv now carries `--env-file "$ENV_FILE"`
        // ahead of the `-f` pairs on every adoption-mode invocation (verify_resolve_env_facts).
        // Stripped back out here, AFTER the raw-argv log line above (so that log — the one
        // ScenarioComposeInterpolatesFromTheSameEnvFileSetupShReads and the AC4 do-no-harm proof
        // both read) still shows the real argv), but BEFORE the case match below — every
        // composeArgs literal this whole file writes stays a plain `-f`-only string rather than
        // having to spell out a not-yet-known-at-call-site scratch env path in every pattern.
        sb.AppendLine("if [ \"${1:-}\" = compose ] && [ \"${2:-}\" = --env-file ]; then set -- \"$1\" \"${@:4}\"; fi");
        sb.AppendLine("case \"$*\" in");

        if (dbReachable)
        {
            sb.AppendLine($"  {Quote($"compose {composeArgs} ps -q db")})");
            sb.AppendLine("    echo cid123");
            sb.AppendLine("    ;;");
        }

        sb.AppendLine($"  *\"to_regclass('{migrationMarkerTable}') is not null\")");
        if (migrationMarker.Length > 0)
            sb.AppendLine($"    echo {Quote(migrationMarker)}");
        sb.AppendLine("    ;;");

        sb.AppendLine("  *\"station.settings\"*)");
        sb.AppendLine(stationNameJson is null ? "    echo ''" : $"    echo {Quote(stationNameJson)}");
        sb.AppendLine("    ;;");

        sb.AppendLine($"  {Quote($"compose {composeArgs} config --services")})");
        sb.AppendLine($"    printf '%s\\n' {string.Join(" ", services.Select(Quote))}");
        sb.AppendLine("    ;;");

        sb.AppendLine($"  {Quote($"compose {composeArgs} config --format json")})");
        sb.AppendLine($"    printf '%s\\n' {Quote($"{{\"name\": \"{projectName}\"}}")}");
        sb.AppendLine("    ;;");

        sb.AppendLine($"  {Quote($"compose {composeArgs} config")})");
        foreach (var line in composeConfigBody.Split('\n'))
            sb.AppendLine($"    printf '%s\\n' {Quote(line)}");
        sb.AppendLine("    ;;");

        if (pinnedImageTags is not null)
        {
            sb.AppendLine($"  {Quote($"compose {composeArgs} config --images")})");
            foreach (var tag in pinnedImageTags)
                sb.AppendLine($"    printf '%s\\n' {Quote(tag)}");
            sb.AppendLine("    ;;");
        }

        if (builtServiceName is not null && builtImage is { } img)
        {
            sb.AppendLine($"  {Quote($"compose {composeArgs} ps -a -q {builtServiceName}")})");
            sb.AppendLine($"    echo {Quote(img.Cid)}");
            sb.AppendLine("    ;;");
            sb.AppendLine($"  {Quote($"inspect {img.Cid} --format {{{{.Image}}}}")})");
            sb.AppendLine($"    echo {Quote(img.ImageId)}");
            sb.AppendLine("    ;;");
            sb.AppendLine($"  {Quote($"image inspect {img.ImageId} --format {{{{.Created}}}}")})");
            sb.AppendLine($"    echo {Quote(img.CreatedIso)}");
            sb.AppendLine("    ;;");
        }

        var psArgv = $"ps -a --filter label=com.docker.compose.project={projectName} " +
            "--format {{.Label \"com.docker.compose.service\"}}|{{.Names}}|{{.State}}";
        sb.AppendLine($"  {Quote(psArgv)})");
        foreach (var (svc, name) in actualContainers)
            sb.AppendLine($"    printf '%s\\n' {Quote($"{svc}|{name}|{containerState}")}");
        sb.AppendLine("    ;;");

        sb.AppendLine("  \"system df\")");
        sb.AppendLine("    cat <<'DF'");
        sb.AppendLine("TYPE            TOTAL     ACTIVE    SIZE      RECLAIMABLE");
        sb.AppendLine($"Images          4         4         500MB     {reclaimable}");
        sb.AppendLine("DF");
        sb.AppendLine("    ;;");

        // Repair-only commands (verify itself never reaches these — proven by
        // VerifyModeMakesZeroWritesToTheBox's own argv-log assertion).
        sb.AppendLine("  \"rm -f \"*)");
        sb.AppendLine("    echo removed");
        sb.AppendLine("    ;;");

        sb.AppendLine("  *)");
        sb.AppendLine("    echo \"UNHANDLED: $*\" >&2");
        sb.AppendLine("    exit 1");
        sb.AppendLine("    ;;");
        sb.AppendLine("esac");

        var path = Path.Combine(
            Directory.CreateTempSubdirectory("gw-setup-story346-docker-").FullName, "docker-stub.sh");
        File.WriteAllText(path, sb.ToString());
        MakeExecutable(path);
        return path;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The AC4 do-no-harm proof's own machinery (B5, round-2 review) — an ALLOWLIST of the exact
    // argv shapes verify mode is permitted to invoke, plus a whole-tree snapshot, replacing the
    // old denylist-of-verbs-plus-.env-only-snapshot pair the reviewer mutation-proved blind: a
    // `delete from station.settings` sailed through the denylist (no SQL verb on it at all), and
    // a stray file write anywhere outside the .env directory sailed through the file-count-only
    // snapshot. Both gaps are closed here; setup.sh's own verify_db_psql also refuses non-select
    // SQL outright now (defense in depth, not a substitute for this fact actually proving it).
    //
    // F1 (round-3 review, BLOCKING): the "whole tree" the paragraph above promises was, until
    // this fix, only ever the scratch .env's OWN directory — a single file — because
    // VerifyModeMakesZeroWritesToTheBox snapshotted Path.GetDirectoryName(envFile) while
    // RunSetup's own WorkingDirectory is the REAL repo checkout. A cwd-relative write from
    // setup.sh (`: > stray-mutant-file` at the top of a probe) lands in that repo checkout,
    // nowhere near the snapshot — reviewer-reproduced, live. Fixed by driving this one fact
    // through MakeScratchCheckout()/RunSetupInCheckout instead of RunSetup: the script now runs
    // WITH its own cwd set to the scratch checkout SnapshotTree actually walks, so a
    // cwd-relative write anywhere in it is caught by construction, not by coincidence.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>The compose global-options prefix every allowlisted `compose ...` shape below
    /// tolerates ahead of its own subcommand — a repeated, any-order mix of `-f &lt;file&gt;` and
    /// (T321 wire finding 2) `--env-file &lt;path&gt;` pairs, matching verify_resolve_env_facts' own
    /// GW_VERIFY_COMPOSE_ARGS construction (`--env-file "$ENV_FILE"` ahead of the `-f` pairs,
    /// setup.sh). Factored into one constant so every pattern below — and
    /// <see cref="IsAllowedExecPsql"/>'s own prefix check — reads the SAME shape rather than three
    /// independent copies that could drift apart.</summary>
    const string ComposeArgsPrefix = @"(?: (?:-f|--env-file) \S+)*";

    /// <summary>Every read-only `docker`/`docker compose` argv shape adoption-mode verify is
    /// permitted to invoke — anything NOT matching one of these (or <see cref="IsAllowedExecPsql"/>)
    /// fails <see cref="ScenarioGreenBoxZeroChanges.VerifyModeMakesZeroWritesToTheBox"/> outright,
    /// including a subcommand this list never anticipated (fail-closed, not fail-open).</summary>
    static readonly Regex[] AllowedReadOnlyArgvPatterns =
    [
        new Regex(@"^compose" + ComposeArgsPrefix + @" ps -q db$"),
        new Regex(@"^compose" + ComposeArgsPrefix + @" ps -a -q \S+$"),
        new Regex(@"^compose" + ComposeArgsPrefix + @" config$"),
        new Regex(@"^compose" + ComposeArgsPrefix + @" config --services$"),
        new Regex(@"^compose" + ComposeArgsPrefix + @" config --format json$"),
        new Regex(@"^compose" + ComposeArgsPrefix + @" config --images$"),
        new Regex(@"^inspect \S+ --format \{\{\.Image\}\}$"),
        new Regex(@"^image inspect \S+ --format \{\{\.Created\}\}$"),
        new Regex(@"^ps -a --filter label=com\.docker\.compose\.project=\S+ --format \{\{\.Label ""com\.docker\.compose\.service""\}\}\|\{\{\.Names\}\}\|\{\{\.State\}\}$"),
        new Regex(@"^system df$"),
    ];

    /// <summary>The literal shape setup.sh's own verify_db_psql emits (B1's fix) between the
    /// compose args and the SQL text itself — matched as a plain substring, not folded into the
    /// regex list above, since the trailing SQL needs its OWN independent `^select` check rather
    /// than a single pattern trying to enforce both shape and content at once.</summary>
    const string ExecPsqlInfix = "exec -T db sh -c psql -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -tAc \"$1\" _ ";

    /// <summary>True only for `compose &lt;-f/--env-file args&gt;* exec -T db &lt;the verify_db_psql
    /// shape&gt; &lt;sql&gt;` where &lt;sql&gt; itself starts with `select` (case-insensitive) — B5's own
    /// "ONLY when its SQL matches ^select, after flag args" requirement. A `delete from
    /// station.settings` (or any other non-select) landing after the infix fails this, and
    /// therefore fails the fact.
    /// F2 (round-3 review): a bare `^select` check alone is bypassable — `select 1; delete from
    /// station.settings` still starts with `select ` (reviewer-proven live: printed `1`, then
    /// `DELETE 0`) — so any embedded `;` fails this too, the same defense-in-depth setup.sh's own
    /// verify_db_psql now applies.</summary>
    static bool IsAllowedExecPsql(string line)
    {
        var idx = line.IndexOf(ExecPsqlInfix, StringComparison.Ordinal);
        if (idx < 0) return false;
        var before = line[..idx];
        var sql = line[(idx + ExecPsqlInfix.Length)..];
        return Regex.IsMatch(before, @"^compose" + ComposeArgsPrefix + @" $") &&
            Regex.IsMatch(sql, "^select ", RegexOptions.IgnoreCase) &&
            !sql.Contains(';', StringComparison.Ordinal);
    }

    static bool IsAllowedArgvLine(string line) =>
        AllowedReadOnlyArgvPatterns.Any(p => p.IsMatch(line)) || IsAllowedExecPsql(line);

    /// <summary>Recursive (path relative to <paramref name="root"/>) -> (mtime, content hash)
    /// snapshot of every file under <paramref name="root"/> — B5's own "the WHOLE tree, not just
    /// the env dir" fix. A file written anywhere else under <paramref name="root"/> (the
    /// reviewer's second mutant) changes this snapshot's key set even when it never touches
    /// ${ENV_FILE} itself — PROVIDED <paramref name="root"/> is actually the tree the script
    /// under test runs with as its own working directory (F1, round-3 review: this function
    /// itself has always walked whatever root it's given faithfully; the round-2 bug was in
    /// VerifyModeMakesZeroWritesToTheBox passing it the wrong root, not in here).</summary>
    static Dictionary<string, (DateTime Mtime, string Hash)> SnapshotTree(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(root, path),
                path => (File.GetLastWriteTimeUtc(path), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))));

    // ─────────────────────────────────────────────────────────────────────────
    // B1's own real-Postgres fixture — provisioned the same way
    // tests/GenWave.MediaLibrary.Tests/DatabaseFixture.cs brings up its own disposable Postgres
    // (db/01-library.sh + db/06-station-settings-migration.sh as init scripts against a real
    // postgres:16.4, matching db-compose.yaml's own shape) — a single ad hoc fixture rather than
    // a shared IAsyncLifetime, since only one fact needs it. A DIFFERENT project name than that
    // fixture's own "genwave-libtest" and NO published host port (gh-#569's fixture-collision
    // lesson): this probe only ever execs INTO the container, never over a TCP port, so there is
    // nothing to collide on even with that fixture's own container running at the same time.
    // ─────────────────────────────────────────────────────────────────────────

    static string WriteRealDbCompose(string repoRoot, string projectName)
    {
        var lib = Path.Combine(repoRoot, "db", "01-library.sh");
        var station = Path.Combine(repoRoot, "db", "06-station-settings-migration.sh");
        var dir = Directory.CreateTempSubdirectory("gw-setup-story346-realdb-").FullName;
        var compose =
            $"""
            name: {projectName}
            services:
              db:
                image: postgres:16.4
                environment:
                  POSTGRES_DB: genwave
                  POSTGRES_USER: genwave
                  POSTGRES_PASSWORD: test
                  LIBRARY_DB_PASSWORD: libtest
                  STATION_DB_PASSWORD: stationtest
                volumes:
                  - {lib}:/docker-entrypoint-initdb.d/01-library.sh:ro
                  - {station}:/docker-entrypoint-initdb.d/06-station-settings-migration.sh:ro
                healthcheck:
                  test: ["CMD-SHELL", "pg_isready -U genwave -d genwave"]
                  interval: 2s
                  timeout: 3s
                  retries: 30
            """;
        var path = Path.Combine(dir, "compose.yaml");
        File.WriteAllText(path, compose);
        return path;
    }

    static void RunDockerCompose(string composePath, string projectName, params string[] verbAndArgs)
    {
        var args = new List<string> { "compose", "-p", projectName, "-f", composePath };
        args.AddRange(verbAndArgs);

        var psi = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("failed to start docker compose");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"docker {string.Join(' ', args)} failed:\n{stderr.Result}{stdout.Result}");
    }

    /// <summary>Runs the real setup.sh, feeding the given text verbatim to stdin (then closing
    /// it) and returning the whole run's exit code/stdout/stderr — the Gh019/Story344/345 idiom,
    /// extended with a trailing CLI-args array for adoption mode's own surface (--repair[,
    /// --yes]). SKIP_PREFLIGHT=1 always rides along (extraEnv is applied after it, so a scenario
    /// could still override it, though none here do) — preflight's own checks are Story342/344's
    /// suite. Bare verify calls simply pass no args.</summary>
    static (int ExitCode, string StdOut, string StdErr) RunSetup(
        string binDir, string envFile, string stdinAnswers, IReadOnlyDictionary<string, string> extraEnv,
        params string[] args) =>
        RunSetupScript(RepoRoot(), Path.Combine(RepoRoot(), "setup.sh"), binDir, envFile, stdinAnswers, extraEnv, args);

    /// <summary>Same as <see cref="RunSetup"/>, but against a SCRATCH checkout's own copy of
    /// setup.sh — B2's own derivation facts need this: setup.sh's `cd "$(dirname "$0")"` means
    /// db/*-migration.sh is always read relative to wherever setup.sh itself lives, so proving
    /// the migration-marker derivation against a scratch db/38 means running a scratch COPY of
    /// the script, not the real repo's, with its own scratch db/ directory beside it.</summary>
    static (int ExitCode, string StdOut, string StdErr) RunSetupInCheckout(
        string checkoutRoot, string binDir, string envFile, string stdinAnswers, IReadOnlyDictionary<string, string> extraEnv,
        params string[] args) =>
        RunSetupScript(checkoutRoot, Path.Combine(checkoutRoot, "setup.sh"), binDir, envFile, stdinAnswers, extraEnv, args);

    static (int ExitCode, string StdOut, string StdErr) RunSetupScript(
        string workingDirectory, string scriptPath, string binDir, string envFile, string stdinAnswers,
        IReadOnlyDictionary<string, string> extraEnv, string[] args)
    {
        var startInfo = new ProcessStartInfo("bash")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);

        startInfo.Environment["PATH"] = binDir;
        foreach (var name in RequiredEnvVars) startInfo.Environment.Remove(name);
        foreach (var name in SeamEnvVars) startInfo.Environment.Remove(name);
        startInfo.Environment["GW_ENV_FILE"] = envFile;
        startInfo.Environment["SKIP_PREFLIGHT"] = "1";
        foreach (var (key, value) in extraEnv)
            startInfo.Environment[key] = value;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("failed to start setup.sh");

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();

        try
        {
            process.StandardInput.Write(stdinAnswers);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // Child already exited without reading stdin — nothing left to write to.
        }

        Task.WaitAll(stdOutTask, stdErrTask);
        process.WaitForExit();

        return (process.ExitCode, stdOutTask.Result, stdErrTask.Result);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the drift probes (AC1)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioVerifyReportsEachDriftClass
    {
        [Fact]
        public void AMissingEnvKeyVsEnvExampleIsReported()
        {
            // COMPOSE_PROFILES, not PUBLIC_HOST (round-2 review B3 moved PUBLIC_HOST/TUNNEL_TOKEN
            // to the overlay-gated set — see ScenarioOverlayGatedKeysAreNeverFalseFindings below
            // for that half of the fix): COMPOSE_PROFILES is never gated, so its absence is
            // "missing" on every box regardless of topology — this fact keeps proving the
            // ordinary case.
            var envFile = ScratchEnvPath();
            var values = HealthyEnvValues(Path.GetTempPath(), "compose.yaml");
            values.Remove("COMPOSE_PROFILES");
            WriteEnvFile(envFile, values);
            var docker = WriteDockerStub();

            var (_, stdOut, _) = RunSetup(MakeBinDir(), envFile, "", new Dictionary<string, string> { ["GW_DOCKER_CMD"] = docker });

            Assert.True(
                stdOut.Contains(".env completeness", StringComparison.Ordinal) &&
                stdOut.Contains("COMPOSE_PROFILES", StringComparison.Ordinal),
                $"expected a missing-key finding naming COMPOSE_PROFILES; stdout:\n{stdOut}");
        }

        [Fact]
        public void ASurvivingPlaceholderIsReported()
        {
            var envFile = ScratchEnvPath();
            var values = HealthyEnvValues(Path.GetTempPath(), "compose.yaml");
            var realIcecastSourceSecret = values["ICECAST_SOURCE_PASSWORD"];
            values["POSTGRES_PASSWORD"] = "change-me-postgres";
            WriteEnvFile(envFile, values);
            var docker = WriteDockerStub();

            var (_, stdOut, _) = RunSetup(MakeBinDir(), envFile, "", new Dictionary<string, string> { ["GW_DOCKER_CMD"] = docker });

            // Key names only, never a value (hard rule 6): the placeholder key is named, but a
            // real secret's own value from elsewhere in the same file never appears anywhere.
            Assert.True(
                stdOut.Contains("POSTGRES_PASSWORD", StringComparison.Ordinal) &&
                !stdOut.Contains(realIcecastSourceSecret, StringComparison.Ordinal),
                $"expected the placeholder key named and no secret VALUE ever printed; stdout:\n{stdOut}");
        }

        [Fact]
        public void AnUnappliedMigrationIsReportedAgainstTheRepoDbMax()
        {
            var envFile = ScratchEnvPath();
            WriteEnvFile(envFile, HealthyEnvValues(Path.GetTempPath(), "compose.yaml"));
            var docker = WriteDockerStub(migrationMarker: "f");

            var (_, stdOut, _) = RunSetup(MakeBinDir(), envFile, "", new Dictionary<string, string> { ["GW_DOCKER_CMD"] = docker });

            Assert.Contains("db/37", stdOut, StringComparison.Ordinal);
        }

        [Fact]
        public void StaleBuiltImagesReportTheGh351AgeSkew()
        {
            var envFile = ScratchEnvPath();
            WriteEnvFile(envFile, HealthyEnvValues(Path.GetTempPath(), "compose.yaml"));
            var docker = WriteDockerStub(
                composeConfigBody: "services:\n  api:\n    build: .\n",
                builtServiceName: "api",
                builtImage: ("apicid", "imgid", "2020-01-01T00:00:00Z"));

            var (_, stdOut, _) = RunSetup(MakeBinDir(), envFile, "", new Dictionary<string, string> { ["GW_DOCKER_CMD"] = docker });

            Assert.Contains("gh-#351", stdOut, StringComparison.Ordinal);
        }

        [Fact]
        public void APinnedBoxGetsThePinnedTagsReadoutNeverTheBuildAdvice()
        {
            // N1 (round-2 review): compose.yaml + compose.pinned.yaml still renders `build:` for
            // api/icecast (that overlay only resets admin_ui/engine/piper's build context) — a
            // pinned/home* box must get launch.sh's OTHER readout (published tags), never the
            // dev-flow "Run ./build.sh" advice launch.sh's own pinned flow rejects outright.
            var envFile = ScratchEnvPath();
            WriteEnvFile(envFile, HealthyEnvValues(Path.GetTempPath(), "compose.yaml:compose.pinned.yaml"));
            var docker = WriteDockerStub(
                composeArgs: "-f compose.yaml -f compose.pinned.yaml",
                composeConfigBody: "services:\n  api:\n    build: .\n    image: ghcr.io/genwave-org/genwave:home-v5.2.2\n",
                pinnedImageTags: ["ghcr.io/genwave-org/genwave:home-v5.2.2"]);

            var (_, stdOut, _) = RunSetup(MakeBinDir(), envFile, "",
                new Dictionary<string, string> { ["GW_DOCKER_CMD"] = docker });

            Assert.True(
                stdOut.Contains("Pinned image tags", StringComparison.Ordinal) &&
                stdOut.Contains("ghcr.io/genwave-org/genwave:home-v5.2.2", StringComparison.Ordinal) &&
                !stdOut.Contains("Run ./build.sh", StringComparison.Ordinal) &&
                !stdOut.Contains("Built image ages", StringComparison.Ordinal),
                $"expected the pinned-tags readout, never the dev-flow build advice; stdout:\n{stdOut}");
        }

        [Fact]
        public void AnOldVintageBoxStackingOnlyTheDemoOverlayStillGetsThePinnedReadout()
        {
            // T321 wire finding 1 (live evidence: run-2 on the Pi 4 printed "Built image ages ...
            // Run ./build.sh" over a healthy, published-image appliance). This box's own
            // persisted COMPOSE_FILE — written before the F136.5 pins/topology split — names
            // compose.demo.yaml but never compose.pinned.yaml, which did not exist yet for that
            // launch to have named: compose.demo.yaml ALONE used to carry the published-GHCR-
            // image mechanics compose.pinned.yaml owns today, so this box is still a pinned
            // appliance and must get the SAME pinned-tags readout as
            // APinnedBoxGetsThePinnedTagsReadoutNeverTheBuildAdvice above — never the dev-flow
            // advice, which a box this shape could never act on (launch.sh's own pinned flow
            // rejects BUILD=1 outright at parse time).
            var envFile = ScratchEnvPath();
            WriteEnvFile(envFile, HealthyEnvValues(Path.GetTempPath(), "compose.yaml:compose.demo.yaml:compose.piper-only.yaml"));
            var docker = WriteDockerStub(
                composeArgs: "-f compose.yaml -f compose.demo.yaml -f compose.piper-only.yaml",
                composeConfigBody: "services:\n  api:\n    build: .\n    image: ghcr.io/genwave-org/genwave:home-v5.2.1\n",
                pinnedImageTags: ["ghcr.io/genwave-org/genwave:home-v5.2.1"]);

            var (_, stdOut, _) = RunSetup(MakeBinDir(), envFile, "",
                new Dictionary<string, string> { ["GW_DOCKER_CMD"] = docker });

            Assert.True(
                stdOut.Contains("Pinned image tags", StringComparison.Ordinal) &&
                stdOut.Contains("ghcr.io/genwave-org/genwave:home-v5.2.1", StringComparison.Ordinal) &&
                !stdOut.Contains("Run ./build.sh", StringComparison.Ordinal) &&
                !stdOut.Contains("Built image ages", StringComparison.Ordinal),
                $"expected the pinned-tags readout on an old-vintage demo-without-pinned box, never the dev-flow build advice; stdout:\n{stdOut}");
        }

        [Fact]
        public void AnAbsolutePathComposeFileStillClassifiesByBasename()
        {
            // T321 wire finding 1 follow-up (reviewer): the Pi 4's OWN persisted COMPOSE_FILE is
            // path-qualified — /home/dmills/genwave/compose.yaml:/home/dmills/genwave/compose.
            // demo.yaml:... — never bare filenames. verify_compose_file_is_stacked's exact-
            // basename comparison must still classify this exact shape as pinned; a comparison
            // against the FULL element (rather than its basename) would silently stop matching
            // the instant a real box's COMPOSE_FILE is path-qualified like this one.
            var envFile = ScratchEnvPath();
            const string dir = "/home/dmills/genwave/";
            WriteEnvFile(envFile, HealthyEnvValues(Path.GetTempPath(),
                $"{dir}compose.yaml:{dir}compose.demo.yaml:{dir}compose.piper-only.yaml"));
            var docker = WriteDockerStub(
                composeArgs: $"-f {dir}compose.yaml -f {dir}compose.demo.yaml -f {dir}compose.piper-only.yaml",
                composeConfigBody: "services:\n  api:\n    build: .\n    image: ghcr.io/genwave-org/genwave:home-v5.2.1\n",
                pinnedImageTags: ["ghcr.io/genwave-org/genwave:home-v5.2.1"]);

            var (_, stdOut, _) = RunSetup(MakeBinDir(), envFile, "",
                new Dictionary<string, string> { ["GW_DOCKER_CMD"] = docker });

            Assert.True(
                stdOut.Contains("Pinned image tags", StringComparison.Ordinal) &&
                stdOut.Contains("ghcr.io/genwave-org/genwave:home-v5.2.1", StringComparison.Ordinal) &&
                !stdOut.Contains("Run ./build.sh", StringComparison.Ordinal),
                $"expected an absolute-path COMPOSE_FILE (the Pi 4's real persisted shape) to still classify as pinned by basename; stdout:\n{stdOut}");
        }

        [Fact]
        public void ABackupOrLookalikeComposeFileNameIsNeverMistakenForTheRealOverlay()
        {
            // T321 wire finding 1 follow-up (reviewer-proven mutant, live): a plain substring
            // test against the whole COMPOSE_FILE string used to false-positive on
            // compose.demo.yaml.bak, overlays/compose.demo.yaml.local, and my-compose.demo.yaml —
            // none of which stack the real overlay this repo ships.
            // verify_compose_file_is_stacked's exact-basename comparison must reject all three.
            var envFile = ScratchEnvPath();
            var values = HealthyEnvValues(Path.GetTempPath(),
                "compose.yaml:compose.demo.yaml.bak:overlays/compose.demo.yaml.local:my-compose.demo.yaml");
            values.Remove("PUBLIC_HOST");
            WriteEnvFile(envFile, values);
            var docker = WriteDockerStub(
                composeArgs: "-f compose.yaml -f compose.demo.yaml.bak -f overlays/compose.demo.yaml.local -f my-compose.demo.yaml");

            var (_, stdOut, _) = RunSetup(MakeBinDir(), envFile, "",
                new Dictionary<string, string> { ["GW_DOCKER_CMD"] = docker });

            Assert.True(
                !stdOut.Contains("Pinned image tags", StringComparison.Ordinal) &&
                !stdOut.Contains("PUBLIC_HOST", StringComparison.Ordinal) &&
                stdOut.Contains("no locally-built services", StringComparison.Ordinal),
                $"expected lookalike compose file names to never count as the real demo/pinned overlay; stdout:\n{stdOut}");
        }

        [Fact]
        public void AnOrphanedProfileContainerIsReported()
        {
            // The de-selected piper/kokoro leftover the compose orphan pass misses.
            var envFile = ScratchEnvPath();
            WriteEnvFile(envFile, HealthyEnvValues(Path.GetTempPath(), "compose.yaml"));
            var docker = WriteDockerStub(
                services: ["db", "api"],
                actualContainers: [("db", "genwave-db-1"), ("api", "genwave-api-1"), ("kokoro", "genwave-kokoro-1")]);

            var (_, stdOut, _) = RunSetup(MakeBinDir(), envFile, "", new Dictionary<string, string> { ["GW_DOCKER_CMD"] = docker });

            Assert.True(
                stdOut.Contains("Orphaned container", StringComparison.Ordinal) &&
                stdOut.Contains("kokoro", StringComparison.Ordinal),
                $"expected the kokoro leftover reported as an orphaned container; stdout:\n{stdOut}");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — overlay-gated .env keys are never false findings (B3, round-2 review)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioOverlayGatedKeysAreNeverFalseFindings
    {
        [Fact]
        public void AWizardWrittenEnvVerifiesGreenNeverRecommendingThePublicApplianceKeys()
        {
            // B3's own MUST-PASS proof: a .env byte-for-byte as build_env_content emits (SPEC
            // F132.2-.5 — PUBLIC_HOST/TUNNEL_TOKEN written COMMENTED, per F136.5's split-overlays
            // ruling) plus COMPOSE_FILE the way launch.sh's own persist_compose_file (gh-#309)
            // adds it after a successful launch — must verify GREEN, exit 0, and never recommend
            // PUBLIC_HOST/TUNNEL_TOKEN at all. Before this fix, verify on exactly this box printed
            // WARN + exit 5 + "add each with a real value" — steering a home operator toward the
            // exact public-appliance value F136.5 removed.
            var envFile = ScratchEnvPath();
            var content = string.Join("\n",
                "# .env — written by setup.sh (SPEC F132.2-.5).",
                "",
                "COMPOSE_PROFILES=admin",
                $"MEDIA_DIR={Path.GetTempPath()}",
                "",
                "POSTGRES_PASSWORD=a-real-postgres-secret-0123456789",
                "LIBRARY_DB_PASSWORD=a-real-library-secret-0123456789",
                "STATION_DB_PASSWORD=a-real-station-secret-0123456789",
                "ICECAST_SOURCE_PASSWORD=a-real-icecast-source-0123456789",
                "ICECAST_ADMIN_PASSWORD=a-real-icecast-admin-0123456789",
                "ADMIN_PASSWORD=a-real-admin-ui-secret-0123456789",
                "",
                "# for the public appliance — see DEPLOYMENT.md",
                "#PUBLIC_HOST=",
                "# for the public appliance — see DEPLOYMENT.md",
                "#TUNNEL_TOKEN=",
                "",
                "# Written by setup.sh (SPEC F132.5).",
                "GW_PRESET=dev",
                "",
                "# persisted by launch.sh's own persist_compose_file (gh-#309)",
                "COMPOSE_FILE=compose.yaml") + "\n";
            File.WriteAllText(envFile, content);
            var docker = WriteDockerStub();

            var (exitCode, stdOut, _) = RunSetup(MakeBinDir(), envFile, "",
                new Dictionary<string, string> { ["GW_DOCKER_CMD"] = docker });

            Assert.True(
                exitCode == 0 &&
                !stdOut.Contains("PUBLIC_HOST", StringComparison.Ordinal) &&
                !stdOut.Contains("TUNNEL_TOKEN", StringComparison.Ordinal),
                $"expected a wizard-written .env to verify green, never naming PUBLIC_HOST/TUNNEL_TOKEN; exit={exitCode} stdout:\n{stdOut}");
        }

        [Fact]
        public void PublicHostIsReportedMissingOnceTheDemoOverlayIsActuallyStacked()
        {
            // The other half of B3: PUBLIC_HOST becomes a genuine finding the MOMENT this box's
            // own COMPOSE_FILE actually stacks compose.demo.yaml — the gate tracks what the
            // stack REQUIRES, not a blanket exemption.
            var envFile = ScratchEnvPath();
            var values = HealthyEnvValues(Path.GetTempPath(), "compose.yaml:compose.demo.yaml");
            values.Remove("PUBLIC_HOST");
            WriteEnvFile(envFile, values);
            var docker = WriteDockerStub(composeArgs: "-f compose.yaml -f compose.demo.yaml");

            var (_, stdOut, _) = RunSetup(MakeBinDir(), envFile, "",
                new Dictionary<string, string> { ["GW_DOCKER_CMD"] = docker });

            Assert.True(
                stdOut.Contains(".env completeness", StringComparison.Ordinal) &&
                stdOut.Contains("PUBLIC_HOST", StringComparison.Ordinal),
                $"expected PUBLIC_HOST reported missing once compose.demo.yaml is actually stacked; stdout:\n{stdOut}");
        }
    }

    // ---------------------------------------------------------------------
    // FIXED — the compose invocation reads the SAME .env this script itself does (T321 wire
    // finding 2). Before this fix, a GW_ENV_FILE naming a file outside the checkout (run 1's own
    // shape: `cd /tmp/genwave-t321; GW_ENV_FILE=/home/dmills/genwave/.env ./setup.sh`) left every
    // `docker compose` call interpolating from CWD's own .env — absent here — even though
    // setup.sh's OWN reads honored GW_ENV_FILE correctly throughout; three probes (migrations,
    // image ages, orphans) degraded to an honest ❓ UNKNOWN rather than reaching their real
    // verdict.
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioComposeInterpolatesFromTheSameEnvFileSetupShReads
    {
        [Fact]
        public void VerifyPassesEnvFileToEveryComposeInvocation()
        {
            // MakeScratchCheckout() ships no .env of its own (T321 run 1's own shape: no .env in
            // cwd), and envFile lives in a WHOLLY SEPARATE scratch dir — the exact "GW_ENV_FILE
            // points outside the checkout" shape the wire ran under.
            var checkoutRoot = MakeScratchCheckout();
            var envFile = ScratchEnvPath();
            WriteEnvFile(envFile, HealthyEnvValues(Path.GetTempPath(), "compose.yaml"));

            var logPath = Path.Combine(Directory.CreateTempSubdirectory("gw-setup-story346-log-").FullName, "argv.log");
            var docker = WriteDockerStub(logPath: logPath);   // every knob at its healthy default

            var (exitCode, stdOut, _) = RunSetupInCheckout(checkoutRoot, MakeBinDir(), envFile, "",
                new Dictionary<string, string> { ["GW_DOCKER_CMD"] = docker });

            var log = File.ReadAllText(logPath);
            var composeLines = log.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.StartsWith("compose ", StringComparison.Ordinal))
                .ToArray();

            Assert.True(
                composeLines.Length > 0 &&
                composeLines.All(line => line.Contains($"--env-file {envFile} ", StringComparison.Ordinal)),
                $"expected every compose invocation to carry --env-file {envFile}; argv log:\n{log}");

            // The three probes T321 run 1 saw degrade to UNKNOWN, each reaching its ordinary
            // healthy-box verdict instead — proof the plumbing above actually lets compose render
            // (through the stub) rather than merely appearing in the logged argv.
            Assert.True(
                exitCode == 0 &&
                stdOut.Contains("schema is current through db/37", StringComparison.Ordinal) &&
                stdOut.Contains("no locally-built services in this box's compose config", StringComparison.Ordinal) &&
                stdOut.Contains("none found for project 'genwave'", StringComparison.Ordinal),
                $"expected the migrations/image-ages/orphans probes to reach their normal (non-UNKNOWN) branches; exit={exitCode} stdout:\n{stdOut}");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the overlay-gated exemption map is pinned to what the overlays actually
    // reference (F3, round-3 review)
    //
    // B3's gate (verify_env_key_is_needed) is a hand-maintained map: PUBLIC_HOST gated on
    // compose.demo.yaml, TUNNEL_TOKEN gated on COMPOSE_PROFILES containing "tunnel" — correct
    // only for as long as those two keys stay referenced exactly where the map assumes. This
    // pins the TWO drifts these facts can actually prove: an added or removed case arm reddens
    // TheGatedKeySetIsExactlyPublicHostAndTunnelToken, and a PUBLIC_HOST reference leaking into
    // compose.yaml itself (or vanishing from compose.demo.yaml) reddens
    // PublicHostIsStillOnlyReferencedByTheDemoOverlay.
    //
    // What this does NOT catch (round-3 delta review, mutation-proven): a brand-new
    // overlay-gated key — some future `${NEW_KEY:?}` landing in an overlay with no matching
    // case arm at all — leaves both facts above green. That is still the SAFE direction, not a
    // silent hole: setup.sh's own wildcard `*) return 0 ;;` arm treats any ungated key as
    // "needed" by default, so the failure mode for an unmapped key is a noisy false "missing
    // from .env" finding on every box, never a false green — just not something this cheap pin
    // detects. Full derivation of the gated set from rendered compose config stays out of
    // scope — this is the cheap pin, not that.
    // ---------------------------------------------------------------------

    public sealed class ScenarioOverlayGatedKeyMapIsPinnedToWhatTheOverlaysReference
    {
        /// <summary>The exact key names verify_env_key_is_needed's own `case "$key" in` block
        /// gates — parsed straight out of setup.sh's own source rather than hand-copied a
        /// second time here, so this fact can never itself drift from the map it exists to pin.
        /// Matches only a bare 4-space-indented `KEY)` case arm; the wildcard `*)` default and
        /// any nested `case` inside an arm's own body are deliberately excluded. A pipe-joined
        /// arm (`KEY|OTHER)` — a shape setup.sh doesn't use today) is NOT matched either, but
        /// that is a loud gap, not a silent one: such a key would simply vanish from the parsed
        /// set entirely, and immediately redden
        /// <see cref="TheGatedKeySetIsExactlyPublicHostAndTunnelToken"/> below rather than pass
        /// unnoticed.</summary>
        static HashSet<string> ParseGatedKeysFromSetupSh(string setupShSource)
        {
            var function = Regex.Match(setupShSource, @"verify_env_key_is_needed\(\)\s*\{.*?\n\}", RegexOptions.Singleline);
            if (!function.Success)
                throw new InvalidOperationException("verify_env_key_is_needed not found in setup.sh — has it been renamed?");

            return Regex.Matches(function.Value, @"^ {4}([A-Z][A-Z0-9_]*)\)", RegexOptions.Multiline)
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);
        }

        /// <summary>Every compose*.yaml at repo root that literally references
        /// <paramref name="envVarName"/> as a compose variable substitution (${NAME...).</summary>
        static HashSet<string> ComposeFilesReferencing(string repoRoot, string envVarName)
        {
            var pattern = new Regex(@"\$\{" + Regex.Escape(envVarName) + @"\b");
            var referencing = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in Directory.EnumerateFiles(repoRoot, "compose*.yaml", SearchOption.TopDirectoryOnly))
            {
                if (pattern.IsMatch(File.ReadAllText(path)))
                    referencing.Add(Path.GetFileName(path));
            }
            return referencing;
        }

        [Fact]
        public void TheGatedKeySetIsExactlyPublicHostAndTunnelToken()
        {
            var setupSh = File.ReadAllText(Path.Combine(RepoRoot(), "setup.sh"));
            var gatedKeys = ParseGatedKeysFromSetupSh(setupSh);

            Assert.True(
                gatedKeys.SetEquals(["PUBLIC_HOST", "TUNNEL_TOKEN"]),
                $"expected setup.sh's own overlay-gated key set to be exactly {{PUBLIC_HOST, TUNNEL_TOKEN}}; found: {string.Join(", ", gatedKeys)}");
        }

        [Fact]
        public void PublicHostIsStillOnlyReferencedByTheDemoOverlay()
        {
            // verify_demo_from_compose_file's own gate (compose.demo.yaml stacked) is correct
            // only while PUBLIC_HOST stays exactly that overlay's own concern — if a future
            // change moved the reference into compose.yaml itself, or dropped it from
            // compose.demo.yaml entirely, the gate would silently stop matching what actually
            // needs the key.
            var referencing = ComposeFilesReferencing(RepoRoot(), "PUBLIC_HOST");

            Assert.True(
                referencing.SetEquals(["compose.demo.yaml"]),
                $"expected PUBLIC_HOST referenced by compose.demo.yaml alone; found: {string.Join(", ", referencing)}");
        }

        [Fact]
        public void TunnelTokenIsStillReferencedByTheBaseComposeFile()
        {
            // verify_env_key_is_needed's own TUNNEL_TOKEN arm gates on COMPOSE_PROFILES
            // containing "tunnel", never on any overlay file — correct only while the
            // cloudflared service (and its TUNNEL_TOKEN reference) stays in the BASE
            // compose.yaml, profile-selected rather than overlay-selected. If that reference
            // ever moved into a new overlay file instead, the profile-based gate would need to
            // become a file-based one, and this is what would catch the mismatch.
            var referencing = ComposeFilesReferencing(RepoRoot(), "TUNNEL_TOKEN");

            Assert.Contains("compose.yaml", referencing);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the completeness probe reads the FILE, not the process env (B6, round-2 review)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioEnvCompletenessReadsTheFileNotProcessEnv
    {
        [Fact]
        public void AnAmbientRealValueNeverMasksARealPlaceholderInTheFile()
        {
            // B6 (round-2 review, a T318 F2 regression): preflight_env_value's process-env-wins
            // precedence is correct for ITS OWN callers (the interview/preflight seam contract),
            // but this probe reports drift found IN ${ENV_FILE} — an ambient exported
            // POSTGRES_PASSWORD (a developer's own shell) must never make a real change-me*
            // placeholder SITTING IN THE FILE read as green.
            var envFile = ScratchEnvPath();
            var values = HealthyEnvValues(Path.GetTempPath(), "compose.yaml");
            values["POSTGRES_PASSWORD"] = "change-me-postgres";
            WriteEnvFile(envFile, values);
            var docker = WriteDockerStub();

            var (_, stdOut, _) = RunSetup(MakeBinDir(), envFile, "",
                new Dictionary<string, string>
                {
                    ["GW_DOCKER_CMD"] = docker,
                    ["POSTGRES_PASSWORD"] = "an-ambient-ordinary-secret-0123456789",
                });

            Assert.True(
                stdOut.Contains(".env completeness", StringComparison.Ordinal) &&
                stdOut.Contains("POSTGRES_PASSWORD", StringComparison.Ordinal),
                $"expected the FILE's own placeholder reported despite an ambient real-looking value; stdout:\n{stdOut}");
        }

        [Fact]
        public void AmbientGarbageNeverFabricatesDriftOverACleanFile()
        {
            // The other direction of the same trap: a clean file's real value must never be
            // shadowed by an ambient change-me* garbage value, which would otherwise fabricate a
            // placeholder finding for a key that is, in the file, entirely fine.
            var envFile = ScratchEnvPath();
            WriteEnvFile(envFile, HealthyEnvValues(Path.GetTempPath(), "compose.yaml"));
            var docker = WriteDockerStub();

            var (exitCode, stdOut, _) = RunSetup(MakeBinDir(), envFile, "",
                new Dictionary<string, string>
                {
                    ["GW_DOCKER_CMD"] = docker,
                    ["POSTGRES_PASSWORD"] = "change-me-ambient-garbage",
                });

            Assert.True(
                exitCode == 0 && !stdOut.Contains("POSTGRES_PASSWORD", StringComparison.Ordinal),
                $"expected the clean file's own value to win, no fabricated drift from ambient garbage; exit={exitCode} stdout:\n{stdOut}");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — repair confirms per item (AC2)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioRepairConfirmsPerItem
    {
        [Fact]
        public void EachFindingPrintsTheExactCommandBeforeTheConfirm()
        {
            // B4 (round-2 review) took prune out of the repairable set entirely (INFO-only
            // advice, never a finding) — an orphaned container is this suite's own remaining
            // repairable finding with a safe, idempotent mock command.
            var envFile = ScratchEnvPath();
            WriteEnvFile(envFile, HealthyEnvValues(Path.GetTempPath(), "compose.yaml"));
            var docker = WriteDockerStub(
                actualContainers: [("db", "genwave-db-1"), ("api", "genwave-api-1"), ("kokoro", "genwave-kokoro-1")]);

            var (_, stdOut, _) = RunSetup(MakeBinDir(), envFile, "n\n",
                new Dictionary<string, string> { ["GW_DOCKER_CMD"] = docker }, "--repair");

            var fixIndex = stdOut.IndexOf($"Fix: {docker} rm -f genwave-kokoro-1", StringComparison.Ordinal);
            var confirmIndex = stdOut.IndexOf("Apply this fix?", StringComparison.Ordinal);
            Assert.True(
                fixIndex >= 0 && confirmIndex > fixIndex,
                $"expected the exact fix command printed before the confirm prompt; stdout:\n{stdOut}");
        }

        [Fact]
        public void ADeclinedItemIsSkippedAndTheNextIsOffered()
        {
            // Two orphaned-container findings (B4 retired prune from the repairable set) — the
            // orphan probe records them in the order `docker ps` reports the containers, so
            // kokoro's finding is offered first and ollama's second.
            var envFile = ScratchEnvPath();
            WriteEnvFile(envFile, HealthyEnvValues(Path.GetTempPath(), "compose.yaml"));
            var logPath = Path.Combine(Directory.CreateTempSubdirectory("gw-setup-story346-log-").FullName, "argv.log");
            var docker = WriteDockerStub(
                actualContainers:
                [
                    ("db", "genwave-db-1"), ("api", "genwave-api-1"),
                    ("kokoro", "genwave-kokoro-1"), ("ollama", "genwave-ollama-1"),
                ],
                logPath: logPath);

            // First finding (kokoro) declined, second (ollama) accepted.
            RunSetup(MakeBinDir(), envFile, "n\ny\n",
                new Dictionary<string, string> { ["GW_DOCKER_CMD"] = docker }, "--repair");

            var log = File.ReadAllText(logPath);
            Assert.True(
                !log.Contains("rm -f genwave-kokoro-1", StringComparison.Ordinal) &&
                log.Contains("rm -f genwave-ollama-1", StringComparison.Ordinal),
                $"expected the declined item's own fix command to never run, and the next item's to run; argv log:\n{log}");
        }

        [Fact]
        public void DashDashYesAppliesAllFindingsWithoutPrompts()
        {
            var envFile = ScratchEnvPath();
            WriteEnvFile(envFile, HealthyEnvValues(Path.GetTempPath(), "compose.yaml"));
            var logPath = Path.Combine(Directory.CreateTempSubdirectory("gw-setup-story346-log-").FullName, "argv.log");
            var docker = WriteDockerStub(
                actualContainers:
                [
                    ("db", "genwave-db-1"), ("api", "genwave-api-1"),
                    ("kokoro", "genwave-kokoro-1"), ("ollama", "genwave-ollama-1"),
                ],
                logPath: logPath);

            // Empty stdin: --yes must never block on a prompt for either finding.
            RunSetup(MakeBinDir(), envFile, "",
                new Dictionary<string, string> { ["GW_DOCKER_CMD"] = docker }, "--repair", "--yes");

            var log = File.ReadAllText(logPath);
            Assert.True(
                log.Contains("rm -f genwave-kokoro-1", StringComparison.Ordinal) &&
                log.Contains("rm -f genwave-ollama-1", StringComparison.Ordinal),
                $"expected --yes to apply every finding with zero stdin; argv log:\n{log}");
        }

        [Fact]
        public void AContainerRestartingRepairSaysSoBeforeTheConfirm()
        {
            var envFile = ScratchEnvPath();
            WriteEnvFile(envFile, HealthyEnvValues(Path.GetTempPath(), "compose.yaml"));
            var docker = WriteDockerStub(
                actualContainers: [("db", "genwave-db-1"), ("api", "genwave-api-1"), ("kokoro", "genwave-kokoro-1")]);

            var (_, stdOut, _) = RunSetup(MakeBinDir(), envFile, "n\n",
                new Dictionary<string, string> { ["GW_DOCKER_CMD"] = docker }, "--repair");

            var warnIndex = stdOut.IndexOf("stop/restart a running container", StringComparison.Ordinal);
            var confirmIndex = stdOut.IndexOf("Apply this fix?", StringComparison.Ordinal);
            Assert.True(
                warnIndex >= 0 && confirmIndex > warnIndex,
                $"expected the restart warning printed before the confirm prompt; stdout:\n{stdOut}");
        }

        [Fact]
        public void AnAlreadyExitedOrphanGetsNoRestartWarning()
        {
            // N3 (round-2 review): the orphan probe lists `docker ps -a` — every state, not just
            // running — but used to print the stop/restart caution unconditionally for all of
            // them. A long-exited leftover carries no such caution; `rm -f` on something already
            // stopped restarts nothing.
            var envFile = ScratchEnvPath();
            WriteEnvFile(envFile, HealthyEnvValues(Path.GetTempPath(), "compose.yaml"));
            var docker = WriteDockerStub(
                actualContainers: [("db", "genwave-db-1"), ("api", "genwave-api-1"), ("kokoro", "genwave-kokoro-1")],
                containerState: "exited");

            var (_, stdOut, _) = RunSetup(MakeBinDir(), envFile, "n\n",
                new Dictionary<string, string> { ["GW_DOCKER_CMD"] = docker }, "--repair");

            Assert.True(
                stdOut.Contains("state: exited", StringComparison.Ordinal) &&
                !stdOut.Contains("stop/restart a running container", StringComparison.Ordinal),
                $"expected the observed state named and no restart warning for an already-exited orphan; stdout:\n{stdOut}");
        }

        [Fact]
        public void EofOnAPerItemConfirmIsTreatedAsDeclinedNotACrash()
        {
            // N2 (round-2 review): closed stdin mid-repair (ssh/cron, a shorter answer stream
            // than there are findings) used to hit `prompt`'s own EOF handling — the interview's
            // "Nothing was written" wording and exit 1, false the moment an earlier item in this
            // same run was already applied. EOF on a per-item confirm is a decline for THAT item,
            // never a crash — the run still ends via the ordinary "still outstanding" exit 5.
            var envFile = ScratchEnvPath();
            WriteEnvFile(envFile, HealthyEnvValues(Path.GetTempPath(), "compose.yaml"));
            var logPath = Path.Combine(Directory.CreateTempSubdirectory("gw-setup-story346-log-").FullName, "argv.log");
            var docker = WriteDockerStub(
                actualContainers:
                [
                    ("db", "genwave-db-1"), ("api", "genwave-api-1"),
                    ("kokoro", "genwave-kokoro-1"), ("ollama", "genwave-ollama-1"),
                ],
                logPath: logPath);

            // Only ONE answer for TWO findings — stdin closes before the second item's own
            // confirm read.
            var (exitCode, stdOut, _) = RunSetup(MakeBinDir(), envFile, "y\n",
                new Dictionary<string, string> { ["GW_DOCKER_CMD"] = docker }, "--repair");

            var log = File.ReadAllText(logPath);
            Assert.True(
                exitCode == 5 &&
                !stdOut.Contains("Nothing was written", StringComparison.Ordinal) &&
                log.Contains("rm -f genwave-kokoro-1", StringComparison.Ordinal) &&
                !log.Contains("rm -f genwave-ollama-1", StringComparison.Ordinal),
                $"expected the first item applied, the EOF'd second item declined (not crashed), and exit 5; exit={exitCode} stdout:\n{stdOut}\nargv log:\n{log}");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — deliberate divergence is not drift (AC3)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioDeliberateDivergenceIsInfo
    {
        [Fact]
        public void ADbSettingsOverrideReportsAsInfoNeverAsAFix()
        {
            var envFile = ScratchEnvPath();
            WriteEnvFile(envFile, HealthyEnvValues(Path.GetTempPath(), "compose.yaml"));
            var docker = WriteDockerStub(stationNameJson: "\"My Override\"");   // otherwise green

            // --repair --yes with nothing repairable: an exit 0 (no outstanding findings) is
            // itself part of the proof this was never offered as a fix.
            var (exitCode, stdOut, _) = RunSetup(MakeBinDir(), envFile, "",
                new Dictionary<string, string> { ["GW_DOCKER_CMD"] = docker }, "--repair", "--yes");

            Assert.True(
                exitCode == 0 &&
                stdOut.Contains("operator-set override", StringComparison.Ordinal) &&
                stdOut.Contains("My Override", StringComparison.Ordinal),
                $"expected the DB override reported as INFO with nothing left to repair; exit={exitCode} stdout:\n{stdOut}");
        }

        [Fact]
        public void AnOperatorComposeOverrideReportsAsInfoNeverAsAFix()
        {
            var envFile = ScratchEnvPath();
            WriteEnvFile(envFile, HealthyEnvValues(Path.GetTempPath(), "compose.yaml:compose.override.yaml"));
            var docker = WriteDockerStub(composeArgs: "-f compose.yaml -f compose.override.yaml");

            var (exitCode, stdOut, _) = RunSetup(MakeBinDir(), envFile, "",
                new Dictionary<string, string> { ["GW_DOCKER_CMD"] = docker }, "--repair", "--yes");

            // The unshipped file is named as an INFO-level customization, and — since nothing
            // else in this fixture is repairable either — the run completes clean (exit 0) with
            // no "Fix:" line ever naming it, proving it was never offered as a fix.
            Assert.True(
                exitCode == 0 &&
                stdOut.Contains("compose.override.yaml", StringComparison.Ordinal) &&
                !stdOut.Contains("Fix:", StringComparison.Ordinal),
                $"expected the compose override reported as INFO with nothing to repair; exit={exitCode} stdout:\n{stdOut}");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the do-no-harm gate (AC4)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioGreenBoxZeroChanges
    {
        [Fact]
        public void AHealthyBoxReportsGreenAndExitsZero()
        {
            var envFile = ScratchEnvPath();
            WriteEnvFile(envFile, HealthyEnvValues(Path.GetTempPath(), "compose.yaml"));
            var docker = WriteDockerStub();   // every knob at its healthy default

            var (exitCode, _, _) = RunSetup(MakeBinDir(), envFile, "",
                new Dictionary<string, string> { ["GW_DOCKER_CMD"] = docker });

            Assert.Equal(0, exitCode);
        }

        [Fact]
        public void ReclaimableDiskSpaceIsAdviceNotAFindingAndStillExitsZero()
        {
            // B4 (round-2 review): any box that has ever upgraded has SOME reclaimable image
            // space — this used to be a WARN/exit-5 finding, so a perfectly healthy, just-
            // upgraded box could never verify green (and T321's Pi 4 wire expects exit 0 on a
            // healthy box). Otherwise-healthy box, but with reclaimable space, must still be
            // exit 0, with the prune command printed as INFO and never offered as a "Fix:".
            var envFile = ScratchEnvPath();
            WriteEnvFile(envFile, HealthyEnvValues(Path.GetTempPath(), "compose.yaml"));
            var docker = WriteDockerStub(reclaimable: "1.2GB");

            var (exitCode, stdOut, _) = RunSetup(MakeBinDir(), envFile, "",
                new Dictionary<string, string> { ["GW_DOCKER_CMD"] = docker });

            Assert.True(
                exitCode == 0 &&
                stdOut.Contains("1.2GB reclaimable", StringComparison.Ordinal) &&
                stdOut.Contains($"{docker} image prune", StringComparison.Ordinal) &&
                !stdOut.Contains("Fix:", StringComparison.Ordinal),
                $"expected reclaimable space reported as INFO advice, never a fix, with exit 0; exit={exitCode} stdout:\n{stdOut}");
        }

        [Fact]
        public void VerifyModeMakesZeroWritesToTheBox()
        {
            // B5 (round-2 review): the WHOLE scratch checkout tree (not just the .env directory)
            // snapshotted before/after, plus an ALLOWLIST of the exact argv shapes verify is
            // permitted to invoke (not a denylist of mutating verbs) — the old pair let both of
            // the reviewer's mutants through (a `delete from station.settings` carries no verb a
            // denylist would catch; a file written outside the .env directory was invisible to a
            // file-count-only snapshot). Verify is read-only by construction — this fact is what
            // proves it, and T321's Pi 4 wire leans on that proof.
            //
            // F1 (round-3 review, BLOCKING): the tree snapshotted has to be the tree setup.sh's
            // own cwd actually IS. setup.sh's first act is `cd "$(dirname "$0")"` — so a
            // cwd-relative write always lands next to wherever the SCRIPT ITSELF lives, not
            // wherever GW_ENV_FILE happens to point. The round-2 build snapshotted
            // Path.GetDirectoryName(envFile) — a one-file scratch .env directory the script
            // never once `cd`s into — while actually running the real repo checkout's own
            // setup.sh; a `: > stray-mutant-file` mutant at the top of a probe therefore landed
            // in the repo checkout and sailed past the snapshot entirely (reviewer-reproduced,
            // live). MakeScratchCheckout()/RunSetupInCheckout (built for B2) fix this by
            // construction: the script under test IS a scratch copy, its cwd IS that scratch
            // checkout's own root, and that root is exactly what gets snapshotted — a
            // cwd-relative write anywhere in it can no longer escape unnoticed. The .env itself
            // now lives inside that same checkout root too (a real adopted box's own layout —
            // .env sits right next to setup.sh), so this fact also proves ${ENV_FILE} itself is
            // untouched, not merely everything else around it.
            var checkoutRoot = MakeScratchCheckout();
            var envFile = Path.Combine(checkoutRoot, ".env");
            WriteEnvFile(envFile, HealthyEnvValues(Path.GetTempPath(), "compose.yaml"));
            var logPath = Path.Combine(Directory.CreateTempSubdirectory("gw-setup-story346-log-").FullName, "argv.log");
            var docker = WriteDockerStub(logPath: logPath);
            var before = SnapshotTree(checkoutRoot);

            RunSetupInCheckout(checkoutRoot, MakeBinDir(), envFile, "",
                new Dictionary<string, string> { ["GW_DOCKER_CMD"] = docker });

            var after = SnapshotTree(checkoutRoot);
            var log = File.ReadAllText(logPath);
            var logLines = log.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var onlyAllowedArgv = logLines.Length > 0 && logLines.All(IsAllowedArgvLine);

            Assert.True(
                before.Count == after.Count && before.All(kv => after.TryGetValue(kv.Key, out var v) && v == kv.Value) &&
                onlyAllowedArgv,
                $"expected zero file mutation anywhere in the checkout and only allowlisted docker argv; argv log:\n{log}");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — --repair on a virgin box (N7, round-2 review)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioRepairOnAVirginBox
    {
        [Fact]
        public void PrintsAnHonestLineBeforeFallingThroughToTheInterview()
        {
            // N7 (round-2 review): --repair is adoption mode's own surface, meaningless on a
            // virgin box (no .env yet) — it used to fall through to the ordinary interview with
            // no acknowledgment the flag did nothing. One honest line, printed BEFORE the
            // interview's own first question, then the ordinary interview proceeds (this fact
            // doesn't complete it — no GW_LAUNCH_CMD stub is wired here, that's Story345's own
            // suite — an empty stdin's own EOF on the first prompt ends the run harmlessly).
            var envFile = ScratchEnvPath();   // creates the scratch dir only — no .env written

            var (_, stdOut, _) = RunSetup(MakeBinDir(), envFile, "",
                new Dictionary<string, string>(), "--repair");

            var honestLineIndex = stdOut.IndexOf("--repair has nothing to fix", StringComparison.Ordinal);
            var interviewIndex = stdOut.IndexOf("How should GenWave run?", StringComparison.Ordinal);
            Assert.True(
                honestLineIndex >= 0 && (interviewIndex < 0 || honestLineIndex < interviewIndex),
                $"expected the honest --repair-on-a-virgin-box line before the interview's own first question; stdout:\n{stdOut}");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the psql probe against a REAL Postgres container (B1, round-2 review)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioRealPostgresProbe
    {
        [Fact]
        public void VerifyMigrationsAgainstARealPostgresContainerSucceedsAsTheContainersOwnRole()
        {
            // B1 (round-2 review): reviewer-proven real-box repro — `docker compose exec -T db
            // psql ...` with no `-U` lands as the container's OWN default exec user (root —
            // postgres:16.4 sets no USER directive), and a bare `psql` then tries to connect as
            // role "root", which does not exist: `FATAL: role "root" does not exist`, every
            // time, on every real box. The mocked-docker facts above can't catch this class of
            // bug at all (the stub never enforces anything about *how* it's invoked, only *that*
            // it matches) — only a REAL container proves the fix actually authenticates.
            //
            // T321 wire finding 2: this is also the ONE fact in this file that runs the REAL
            // `docker compose` binary (no GW_DOCKER_CMD stub) with `--env-file` sitting in the
            // compose GLOBAL-options position (verify_resolve_env_facts), so it is the only
            // proof that the real CLI actually accepts that flag there rather than merely
            // matching a scripted stub's own case pattern — do not simplify HealthyEnvValues'
            // composePath argument away.
            var repoRoot = RepoRoot();
            var projectName = $"genwave-hosttest-story346-{Guid.NewGuid():N}";
            var composePath = WriteRealDbCompose(repoRoot, projectName);

            // F10 (round-3 review): `up -d --wait` moved INSIDE the try — it used to sit ahead
            // of it, so a bring-up that creates the container/volume but then fails (e.g. a
            // `--wait` healthcheck timeout, itself a non-zero exit RunDockerCompose throws on)
            // skipped the finally entirely and stranded a GUID-named container+volume with no
            // caller left to clean it up.
            try
            {
                RunDockerCompose(composePath, projectName, "up", "-d", "--wait");

                var envFile = ScratchEnvPath();
                WriteEnvFile(envFile, HealthyEnvValues(Path.GetTempPath(), composePath));

                // No GW_DOCKER_CMD override — the REAL `docker` binary, resolved off PATH
                // (MakeBinDirWithDocker), exactly as it runs on a real box.
                var (_, stdOut, stdErr) = RunSetup(MakeBinDirWithDocker(), envFile, "",
                    new Dictionary<string, string>());

                Assert.True(
                    stdOut.Contains("Schema migrations", StringComparison.Ordinal) &&
                    stdOut.Contains("current through db/37", StringComparison.Ordinal) &&
                    !stdOut.Contains("could not determine", StringComparison.Ordinal) &&
                    !stdErr.Contains("role \"root\" does not exist", StringComparison.Ordinal),
                    $"expected the psql probe to succeed as the container's own role, not root; stdout:\n{stdOut}\nstderr:\n{stdErr}");
            }
            finally
            {
                RunDockerCompose(composePath, projectName, "down", "-v");
            }
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the migration-marker derivation (B2, round-2 review)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioMigrationMarkerDerivation
    {
        [Fact]
        public void AScratchTableCreatingMigrationBecomesTheNewMarkerAndItsOwnArtifactIsDemanded()
        {
            // B2 (round-2 review): a scratch db/38 that CREATES a table must become the new
            // marker (both the migration number AND the artifact checked) — never left pointing
            // at db/37's own station.station_image once a newer table-creating migration exists.
            var checkoutRoot = MakeScratchCheckout();
            File.WriteAllText(Path.Combine(checkoutRoot, "db", "38-scratch-migration.sh"),
                "create table if not exists station.new_thing (id serial primary key);\n");

            var envFile = ScratchEnvPath();
            WriteEnvFile(envFile, HealthyEnvValues(Path.GetTempPath(), "compose.yaml"));
            var docker = WriteDockerStub(migrationMarker: "t", migrationMarkerTable: "station.new_thing");

            var (_, stdOut, _) = RunSetupInCheckout(checkoutRoot, MakeBinDir(), envFile, "",
                new Dictionary<string, string> { ["GW_DOCKER_CMD"] = docker });

            Assert.True(
                stdOut.Contains("current through db/38", StringComparison.Ordinal) &&
                stdOut.Contains("station.new_thing", StringComparison.Ordinal) &&
                !stdOut.Contains("station.station_image", StringComparison.Ordinal),
                $"expected db/38's own new table to become the marker, demanding ITS artifact; stdout:\n{stdOut}");
        }

        [Fact]
        public void AScratchNonTableMigrationNeverFabricatesGreenThroughItsOwnNumber()
        {
            // B2's own counter-proof: a scratch db/38 that adds NO table (an ALTER-only
            // migration, the shape that went silently stale under the old hand-maintained
            // constant) must never claim "current through db/38" — honest UNKNOWN instead, even
            // though db/37's own marker table is present and would otherwise report PASS.
            var checkoutRoot = MakeScratchCheckout();
            File.WriteAllText(Path.Combine(checkoutRoot, "db", "38-scratch-migration.sh"),
                "alter table station.station_image add column if not exists caption text;\n");

            var envFile = ScratchEnvPath();
            WriteEnvFile(envFile, HealthyEnvValues(Path.GetTempPath(), "compose.yaml"));
            var docker = WriteDockerStub();   // station.station_image (db/37's marker) present

            var (_, stdOut, _) = RunSetupInCheckout(checkoutRoot, MakeBinDir(), envFile, "",
                new Dictionary<string, string> { ["GW_DOCKER_CMD"] = docker });

            // UNKNOWN, not a fabricated PASS — an honest "can't verify past db/37" naming
            // db/38 as the reason, even though db/37's own marker table (station.station_image)
            // IS present and the old hand-maintained constant would have reported it green.
            Assert.True(
                stdOut.Contains("can't verify past db/37", StringComparison.Ordinal) &&
                stdOut.Contains("db/38 adds no new table", StringComparison.Ordinal) &&
                !stdOut.Contains("current through db/38", StringComparison.Ordinal),
                $"expected an honest UNKNOWN naming db/37 as the verifiable ceiling, never a fabricated green through db/38; stdout:\n{stdOut}");
        }
    }
}

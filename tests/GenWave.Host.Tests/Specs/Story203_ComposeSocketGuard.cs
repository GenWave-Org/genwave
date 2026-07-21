// STORY-203 — Docker-socket carve-out is a guarded invariant
//
// BDD specification — xUnit (SPEC F78.2). Drives the real guard script
// (tools/check-compose-socket.sh) via Process — Story181 idiom throughout: real renders
// carry Category=Integration; hand-built --config-file fixtures run in the ordinary suite.
//
// Invariant under guard: across base and base+demo renders, any profile combination,
// /var/run/docker.sock is bind-mounted read-only into `alloy` and into nothing else.
// The guard lands (T48) BEFORE the carve-out exists (T49) — it first passes proving the
// trivial no-socket case.
//
// Pending until T48 (/build-loop unskips).

using System.Diagnostics;

namespace GenWave.Host.Tests.Specs;

public static class FeatureComposeSocketGuard
{
    const string Pending = "pending: T48 — socket guard (unskip in /build-loop)";

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;

        if (dir is null) throw new InvalidOperationException("repo root (GenWave.sln) not found");
        return dir.FullName;
    }

    static (int ExitCode, string StdOut, string StdErr) RunGuardScript(params string[] args)
    {
        var startInfo = new ProcessStartInfo("bash")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(Path.Combine(RepoRoot(), "tools", "check-compose-socket.sh"));
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("failed to start check-compose-socket.sh");
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdOut, stdErr);
    }

    public static class ScenarioCurrentConfigPasses
    {
        [Fact(Skip = Pending)]
        [Trait("Category", "Integration")]
        public static void Guard_exits_zero_across_all_profile_combinations()
        {
            // Given the real compose files (the guard renders base and base+demo itself,
            //       with every profile combination — its default mode, like the publish guard)
            // When  the guard runs with no arguments
            // Then  it exits 0: no service beyond alloy touches docker.sock, alloy (once it
            //       exists) mounts it read-only
            var (exitCode, stdOut, stdErr) = RunGuardScript();

            Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}\nstdout:\n{stdOut}\nstderr:\n{stdErr}");
        }
    }

    public static class SadPathRegressions
    {
        const string SocketSource = "/var/run/docker.sock";

        static string FixtureJson(string service, bool readOnly) => $$"""
            {
              "services": {
                "alloy": {
                  "volumes": [
                    {"type": "bind", "source": "{{SocketSource}}", "target": "{{SocketSource}}", "read_only": true}
                  ]
                },
                "{{service}}": {
                  "volumes": [
                    {"type": "bind", "source": "{{SocketSource}}", "target": "{{SocketSource}}"{{(readOnly ? ", \"read_only\": true" : "")}}}
                  ]
                }
              }
            }
            """;

        static (int ExitCode, string Output) RunAgainstFixture(string fixtureJson)
        {
            var fixturePath = Path.Combine(Path.GetTempPath(), $"check-compose-socket-{Guid.NewGuid():N}.json");
            File.WriteAllText(fixturePath, fixtureJson);
            try
            {
                var (exitCode, stdOut, stdErr) = RunGuardScript("--config-file", fixturePath);
                return (exitCode, stdOut + stdErr);
            }
            finally
            {
                File.Delete(fixturePath);
            }
        }

        [Fact(Skip = Pending)]
        public static void Another_service_mounting_the_socket_fails_naming_it()
        {
            // Given a render where `api` also mounts docker.sock (even read-only)
            // When  the guard runs in --config-file mode
            // Then  it exits non-zero naming the offender
            var (exitCode, output) = RunAgainstFixture(FixtureJson("api", readOnly: true));

            Assert.True(exitCode != 0 && output.Contains("api", StringComparison.Ordinal),
                $"expected failure naming 'api' (exit {exitCode}):\n{output}");
        }

        [Fact(Skip = Pending)]
        public static void A_writable_alloy_socket_mount_fails()
        {
            // Given a render where alloy's own socket mount lost read_only
            // When  the guard runs in --config-file mode
            // Then  it exits non-zero — the carve-out is ro or it is nothing
            const string fixtureJson = $$"""
                {
                  "services": {
                    "alloy": {
                      "volumes": [
                        {"type": "bind", "source": "{{SocketSource}}", "target": "{{SocketSource}}"}
                      ]
                    }
                  }
                }
                """;
            var (exitCode, output) = RunAgainstFixture(fixtureJson);

            Assert.True(exitCode != 0 && output.Contains("alloy", StringComparison.Ordinal),
                $"expected failure naming 'alloy' (exit {exitCode}):\n{output}");
        }
    }
}

// STORY-181 — Compose host-publish guard in CI
//
// BDD specification — xUnit (SPEC F67.1). Drives the real guard script
// (tools/check-compose-publish.sh) via Process against real/fixture overlays.
//
// AC1 exercises the actual repo compose files through `docker compose config` — it needs
// the docker CLI, so it carries the repo's established Category=Integration trait (docker
// may not be available in every test environment; the CI wiring for this exact path is a
// dedicated workflow step that always has docker — see .github/workflows/
// compose-publish-guard.yml, reviewed for AC2 rather than unit-specced here).
//
// AC3 drives the script's --config-file mode with a hand-built fixture, so it needs no
// docker CLI at all and runs in the ordinary (non-Integration) suite.
//
// ScenarioTunnelProfilePasses (cloudflared observability, Q3 housekeeping) pins that the
// optional `cloudflared` service (profiles: ["tunnel"], off by default, no `ports:` at all)
// never regresses the host-publish posture: it renders the real merged config WITH the
// "tunnel" profile active via `docker compose --profile tunnel config --format json`, then
// drives the guard's --config-file mode against that render. Needs the docker CLI (to render),
// so it carries Category=Integration like AC1.

using System.Diagnostics;

namespace GenWave.Host.Tests.Specs;

public static class FeatureComposeHostPublishGuard
{
    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;

        if (dir is null) throw new InvalidOperationException("repo root (GenWave.sln) not found");
        return dir.FullName;
    }

    static string ScriptPath => Path.Combine(RepoRoot(), "tools", "check-compose-publish.sh");

    static (int ExitCode, string StdOut, string StdErr) RunGuardScript(params string[] args)
    {
        var startInfo = new ProcessStartInfo("bash")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(ScriptPath);
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("failed to start check-compose-publish.sh");

        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, stdOut, stdErr);
    }

    public static class ScenarioCurrentOverlayPasses
    {
        [Fact]
        [Trait("Category", "Integration")]
        public static void Guard_exits_zero_reporting_only_caddy_published()
        {
            // Given the merged config of compose.yaml + compose.demo.yaml
            // When  the guard script runs
            // Then  it exits 0, reporting 0.0.0.0 publishes only for caddy 80/443 (F67.1)
            var (exitCode, stdOut, stdErr) = RunGuardScript();

            Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}\nstdout:\n{stdOut}\nstderr:\n{stdErr}");
            Assert.Contains("caddy:80", stdOut, StringComparison.Ordinal);
            Assert.Contains("caddy:443", stdOut, StringComparison.Ordinal);
        }
    }

    public static class ScenarioTunnelProfilePasses
    {
        [Fact]
        [Trait("Category", "Integration")]
        public static void Guard_exits_zero_with_tunnel_profile_rendered_via_config_file()
        {
            // Given the merged compose.yaml + compose.demo.yaml config, rendered WITH the
            // optional "tunnel" profile active (cloudflared observability, Q3 housekeeping)
            // When  that render is fed to the guard through --config-file mode
            // Then  it still exits 0 — cloudflared publishes no host ports at all, so activating
            //       its profile can never regress the caddy-80/443-only invariant (F67.1)
            var configJson = RenderMergedConfigWithTunnelProfile(RepoRoot());

            var fixturePath = Path.Combine(Path.GetTempPath(), $"check-compose-publish-tunnel-profile-{Guid.NewGuid():N}.json");
            File.WriteAllText(fixturePath, configJson);
            try
            {
                var (exitCode, stdOut, stdErr) = RunGuardScript("--config-file", fixturePath);

                Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}\nstdout:\n{stdOut}\nstderr:\n{stdErr}");
                Assert.Contains("caddy:80", stdOut, StringComparison.Ordinal);
                Assert.Contains("caddy:443", stdOut, StringComparison.Ordinal);
            }
            finally
            {
                File.Delete(fixturePath);
            }
        }

        static string RenderMergedConfigWithTunnelProfile(string repoRoot)
        {
            var startInfo = new ProcessStartInfo("docker")
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var arg in new[]
            {
                "compose", "-f", "compose.yaml", "-f", "compose.demo.yaml",
                "--profile", "tunnel", "config", "--format", "json",
            })
            {
                startInfo.ArgumentList.Add(arg);
            }

            // Same dummy-secret idiom as the guard script's own docker-invoking path (see its
            // header comment): `config` only merges and substitutes text, it never talks to a
            // daemon or starts a container, so these never reach a running service.
            startInfo.Environment["POSTGRES_PASSWORD"] = "story181-dummy";
            startInfo.Environment["LIBRARY_DB_PASSWORD"] = "story181-dummy";
            startInfo.Environment["STATION_DB_PASSWORD"] = "story181-dummy";
            startInfo.Environment["ICECAST_SOURCE_PASSWORD"] = "story181-dummy";
            startInfo.Environment["ICECAST_ADMIN_PASSWORD"] = "story181-dummy";
            startInfo.Environment["ADMIN_PASSWORD"] = "story181-dummy";
            startInfo.Environment["MEDIA_DIR"] = Path.GetTempPath();
            startInfo.Environment["PUBLIC_HOST"] = "story181-tunnel-profile.invalid";
            // cloudflared's TUNNEL_TOKEN is deliberately `${TUNNEL_TOKEN:-}` (compose.yaml) —
            // config renders fine whether or not this is set, so it's left to the ambient
            // environment / unset.

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("failed to start docker compose config");

            var stdOut = process.StandardOutput.ReadToEnd();
            var stdErr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"docker compose config (tunnel profile) failed (exit {process.ExitCode}): {stdErr}");

            return stdOut;
        }
    }

    public static class SadPathRegression
    {
        [Fact]
        public static void Reintroduced_public_publish_fails_naming_service_and_port()
        {
            // Given a test overlay re-adding a 0.0.0.0 publish on a non-proxy service
            // When  the guard script runs against it
            // Then  it exits non-zero naming the offending service and port (F67.1)
            //
            // Fixture reintroduces exactly the regression fixed in 05303ce: caddy still
            // publishes 80/443, but icecast is (incorrectly) re-published to every
            // interface — pre-rendered as `docker compose config --format json` would
            // shape it, so this needs no docker CLI (--config-file mode).
            const string fixtureJson = """
                {
                  "services": {
                    "caddy": {
                      "ports": [
                        {"mode": "ingress", "target": 80, "published": "80", "protocol": "tcp"},
                        {"mode": "ingress", "target": 443, "published": "443", "protocol": "tcp"}
                      ]
                    },
                    "api": {
                      "ports": [
                        {"mode": "ingress", "host_ip": "127.0.0.1", "target": 8080, "published": "8080", "protocol": "tcp"}
                      ]
                    },
                    "icecast": {
                      "ports": [
                        {"mode": "ingress", "target": 8000, "published": "8000", "protocol": "tcp"}
                      ]
                    }
                  }
                }
                """;

            var fixturePath = Path.Combine(Path.GetTempPath(), $"check-compose-publish-regression-{Guid.NewGuid():N}.json");
            File.WriteAllText(fixturePath, fixtureJson);
            try
            {
                var (exitCode, stdOut, stdErr) = RunGuardScript("--config-file", fixturePath);
                var combinedOutput = stdOut + stdErr;

                Assert.NotEqual(0, exitCode);
                Assert.Contains("icecast", combinedOutput, StringComparison.Ordinal);
                Assert.Contains("8000", combinedOutput, StringComparison.Ordinal);
            }
            finally
            {
                File.Delete(fixturePath);
            }
        }

        [Fact]
        public static void Ipv6_wildcard_publish_fails_naming_service_and_port()
        {
            // Given a test overlay publishing a non-caddy service with host_ip "::"
            // When  the guard script runs against it
            // Then  it exits non-zero naming the offending service and port (F67.1)
            //
            // Locks the fail-open bug shut: an IPv6 wildcard bind ("::" binds every
            // interface, IPv4 and IPv6 alike) is not "" or "0.0.0.0", so a check that only
            // special-cases those two literal strings would wrongly wave it through as
            // loopback. host_ip must be recognized as loopback-only (127.*, ::1,
            // ::ffff:127.*) to be treated as safe — everything else, including this one,
            // stays subject to the caddy-80/443 allowlist.
            const string fixtureJson = """
                {
                  "services": {
                    "caddy": {
                      "ports": [
                        {"mode": "ingress", "target": 80, "published": "80", "protocol": "tcp"},
                        {"mode": "ingress", "target": 443, "published": "443", "protocol": "tcp"}
                      ]
                    },
                    "icecast": {
                      "ports": [
                        {"mode": "ingress", "host_ip": "::", "target": 8000, "published": "8000", "protocol": "tcp"}
                      ]
                    }
                  }
                }
                """;

            var fixturePath = Path.Combine(Path.GetTempPath(), $"check-compose-publish-ipv6-{Guid.NewGuid():N}.json");
            File.WriteAllText(fixturePath, fixtureJson);
            try
            {
                var (exitCode, stdOut, stdErr) = RunGuardScript("--config-file", fixturePath);
                var combinedOutput = stdOut + stdErr;

                Assert.NotEqual(0, exitCode);
                Assert.Contains("icecast", combinedOutput, StringComparison.Ordinal);
                Assert.Contains("8000", combinedOutput, StringComparison.Ordinal);
            }
            finally
            {
                File.Delete(fixturePath);
            }
        }
    }
}

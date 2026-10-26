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

using System.Diagnostics;

using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureComposeHostPublishGuard
{
    // This guard renders compose itself against the real toolchain on the developer's own PATH,
    // not a scratch bin — ScriptProcess still starts the child from its sanitized environment
    // (gh-#776).
    static readonly string RealPath = Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin";

    static (int ExitCode, string StdOut, string StdErr) RunGuardScript(params string[] args) =>
        ScriptProcess.Run(
            "tools/check-compose-publish.sh", RealPath,
            // gh-#249: the guard's default mode renders compose itself with no --profile flags,
            // inheriting this process's environment — pin the profile set empty so ambient
            // COMPOSE_PROFILES / a dev box's .env (e.g. admin -> admin_ui 3000:3000 on the base
            // file) can never leak extra services into the render it checks.
            extraEnv: new Dictionary<string, string> { ["COMPOSE_PROFILES"] = "" },
            args: args);

    public static class ScenarioCurrentOverlayPasses
    {
        [Fact]
        [Trait("Category", "Integration")]
        public static void Guard_exits_zero_reporting_only_the_lan_allowlist_published()
        {
            // Given the merged config of compose.yaml + compose.pinned.yaml
            // When  the guard script runs
            // Then  it exits 0, reporting 0.0.0.0 publishes only for the LAN allowlist (F67.1)
            var (exitCode, stdOut, stdErr) = RunGuardScript();

            Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}\nstdout:\n{stdOut}\nstderr:\n{stdErr}");
            Assert.Contains("icecast:8000", stdOut, StringComparison.Ordinal);
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
            // Fixture: the LAN allowlist publishes as usual, but db is (incorrectly)
            // published to every interface — pre-rendered as `docker compose config --format json` would
            // shape it, so this needs no docker CLI (--config-file mode).
            const string fixtureJson = """
                {
                  "services": {
                    "icecast": {
                      "ports": [
                        {"mode": "ingress", "target": 8000, "published": "8000", "protocol": "tcp"}
                      ]
                    },
                    "api": {
                      "ports": [
                        {"mode": "ingress", "host_ip": "127.0.0.1", "target": 8080, "published": "8080", "protocol": "tcp"}
                      ]
                    },
                    "db": {
                      "ports": [
                        {"mode": "ingress", "target": 5432, "published": "5432", "protocol": "tcp"}
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
                Assert.Contains("db", combinedOutput, StringComparison.Ordinal);
                Assert.Contains("5432", combinedOutput, StringComparison.Ordinal);
            }
            finally
            {
                File.Delete(fixturePath);
            }
        }

        [Fact]
        public static void Ipv6_wildcard_publish_fails_naming_service_and_port()
        {
            // Given a test overlay publishing a non-allowlisted service with host_ip "::"
            // When  the guard script runs against it
            // Then  it exits non-zero naming the offending service and port (F67.1)
            //
            // Locks the fail-open bug shut: an IPv6 wildcard bind ("::" binds every
            // interface, IPv4 and IPv6 alike) is not "" or "0.0.0.0", so a check that only
            // special-cases those two literal strings would wrongly wave it through as
            // loopback. host_ip must be recognized as loopback-only (127.*, ::1,
            // ::ffff:127.*) to be treated as safe — everything else, including this one,
            // stays subject to the allowlist.
            const string fixtureJson = """
                {
                  "services": {
                    "icecast": {
                      "ports": [
                        {"mode": "ingress", "target": 8000, "published": "8000", "protocol": "tcp"}
                      ]
                    },
                    "db": {
                      "ports": [
                        {"mode": "ingress", "host_ip": "::", "target": 5432, "published": "5432", "protocol": "tcp"}
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
                Assert.Contains("db", combinedOutput, StringComparison.Ordinal);
                Assert.Contains("5432", combinedOutput, StringComparison.Ordinal);
            }
            finally
            {
                File.Delete(fixturePath);
            }
        }
    }
}

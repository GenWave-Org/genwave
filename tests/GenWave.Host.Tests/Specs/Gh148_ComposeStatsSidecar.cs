// gh-#148 — the dockerproxy stats sidecar's compose pins
//
// BDD specification — xUnit, Story202 render idiom + Story203 guard-fixture idiom. Render-driving
// scenarios carry Category=Integration (docker CLI); guard fixtures run in the ordinary suite.
//
// Pins under guard: the sidecar image tag is exact (never latest), it publishes NO host ports on
// either render (public-path unreachability), it lives on the `stats` network alone, its env
// allowlist grants ONLY the /containers section (default-deny for everything else, POST included),
// its docker.sock mount is read-only — and check-compose-socket.sh accepts dockerproxy's read-only
// carve-out while still failing a read-write drift.

using System.Diagnostics;
using System.Text.Json;

namespace GenWave.Host.Tests.Specs;

public static class FeatureComposeStatsSidecar
{
    const string PinnedImage = "tecnativa/docker-socket-proxy:v0.4.2";

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;

        if (dir is null) throw new InvalidOperationException("repo root (GenWave.sln) not found");
        return dir.FullName;
    }

    static JsonDocument RenderConfig(bool demoOverlay)
    {
        var startInfo = new ProcessStartInfo("docker")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        var args = new List<string> { "compose", "-f", "compose.yaml" };
        if (demoOverlay) { args.Add("-f"); args.Add("compose.demo.yaml"); }
        args.AddRange(new[] { "config", "--format", "json" });
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);

        // Same dummy-secret idiom as Story181/Story202: `config` only merges text, no daemon reached.
        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["POSTGRES_PASSWORD"] = "gh148-dummy",
            ["LIBRARY_DB_PASSWORD"] = "gh148-dummy",
            ["STATION_DB_PASSWORD"] = "gh148-dummy",
            ["ICECAST_SOURCE_PASSWORD"] = "gh148-dummy",
            ["ICECAST_ADMIN_PASSWORD"] = "gh148-dummy",
            ["ADMIN_PASSWORD"] = "gh148-dummy",
            ["MEDIA_DIR"] = Path.GetTempPath(),
            ["PUBLIC_HOST"] = "gh148.invalid",
        })
        {
            startInfo.Environment[key] = value;
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("failed to start docker compose config");
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"docker compose config failed (exit {process.ExitCode}): {stdErr}");

        return JsonDocument.Parse(stdOut);
    }

    public static class ScenarioSidecarShape
    {
        static readonly Lazy<JsonDocument> Base = new(() => RenderConfig(demoOverlay: false));
        static readonly Lazy<JsonDocument> BaseDemo = new(() => RenderConfig(demoOverlay: true));

        static JsonElement Sidecar(JsonDocument render) =>
            render.RootElement.GetProperty("services").GetProperty("dockerproxy");

        [Fact]
        [Trait("Category", "Integration")]
        public static void ImageIsThePinnedTagNeverLatest()
        {
            Assert.Equal(PinnedImage, Sidecar(Base.Value).GetProperty("image").GetString());
        }

        [Fact]
        [Trait("Category", "Integration")]
        public static void NoHostPortsOnEitherRender()
        {
            // Never host-published — the admin UI reads stats through the api, and on the public
            // overlay nothing may widen that: the sidecar must stay invisible to the public path.
            Assert.False(Sidecar(Base.Value).TryGetProperty("ports", out _));
            Assert.False(Sidecar(BaseDemo.Value).TryGetProperty("ports", out _));
        }

        [Fact]
        [Trait("Category", "Integration")]
        public static void LivesOnTheStatsNetworkAlone()
        {
            // Not on core: nothing on the public path (caddy/cloudflared/admin_ui) has a route to
            // it — same isolation posture as db on `data`.
            var networks = Sidecar(Base.Value).GetProperty("networks");
            var names = networks.EnumerateObject().Select(property => property.Name).ToArray();
            Assert.Equal(["stats"], names);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public static void TheEnvAllowlistGrantsOnlyTheContainersSection()
        {
            // CONTAINERS=1 and NOTHING else — every other section (and POST) stays the image's
            // default-deny, so even the granted section is read-only.
            var environment = Sidecar(Base.Value).GetProperty("environment");
            var keys = environment.EnumerateObject().Select(property => property.Name).ToArray();
            Assert.Equal(["CONTAINERS"], keys);
            Assert.Equal("1", environment.GetProperty("CONTAINERS").GetString());
        }

        [Fact]
        [Trait("Category", "Integration")]
        public static void TheSocketMountIsReadOnly()
        {
            var volume = Assert.Single(Sidecar(Base.Value).GetProperty("volumes").EnumerateArray());
            Assert.Equal("/var/run/docker.sock", volume.GetProperty("source").GetString());
            Assert.Equal("/var/run/docker.sock", volume.GetProperty("target").GetString());
            Assert.True(volume.GetProperty("read_only").GetBoolean());
        }

        [Fact]
        [Trait("Category", "Integration")]
        public static void TheApiReachesTheSidecarOverTheStatsNetwork()
        {
            var api = Base.Value.RootElement.GetProperty("services").GetProperty("api");
            Assert.True(api.GetProperty("networks").TryGetProperty("stats", out _));
            Assert.Equal("http://dockerproxy:2375",
                api.GetProperty("environment").GetProperty("DockerStats__BaseUrl").GetString());
        }
    }

    public static class SadPathSocketGuardCoversTheSidecar
    {
        const string SocketSource = "/var/run/docker.sock";

        static (int ExitCode, string Output) RunGuardAgainstFixture(string fixtureJson)
        {
            var fixturePath = Path.Combine(Path.GetTempPath(), $"gh148-socket-{Guid.NewGuid():N}.json");
            File.WriteAllText(fixturePath, fixtureJson);
            try
            {
                var startInfo = new ProcessStartInfo("bash")
                {
                    WorkingDirectory = RepoRoot(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                startInfo.ArgumentList.Add(Path.Combine(RepoRoot(), "tools", "check-compose-socket.sh"));
                startInfo.ArgumentList.Add("--config-file");
                startInfo.ArgumentList.Add(fixturePath);

                using var process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("failed to start check-compose-socket.sh");
                var stdOut = process.StandardOutput.ReadToEnd();
                var stdErr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                return (process.ExitCode, stdOut + stdErr);
            }
            finally
            {
                File.Delete(fixturePath);
            }
        }

        [Fact]
        public static void AReadOnlyDockerproxyMountPassesTheGuard()
        {
            const string fixtureJson = $$"""
                {
                  "services": {
                    "dockerproxy": {
                      "volumes": [
                        {"type": "bind", "source": "{{SocketSource}}", "target": "{{SocketSource}}", "read_only": true}
                      ]
                    }
                  }
                }
                """;
            var (exitCode, output) = RunGuardAgainstFixture(fixtureJson);

            Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}:\n{output}");
        }

        [Fact]
        public static void AWritableDockerproxyMountFailsNamingIt()
        {
            // The carve-out is ro or it is nothing — same rule Story203 pins for alloy.
            const string fixtureJson = $$"""
                {
                  "services": {
                    "dockerproxy": {
                      "volumes": [
                        {"type": "bind", "source": "{{SocketSource}}", "target": "{{SocketSource}}"}
                      ]
                    }
                  }
                }
                """;
            var (exitCode, output) = RunGuardAgainstFixture(fixtureJson);

            Assert.True(exitCode != 0 && output.Contains("dockerproxy", StringComparison.Ordinal),
                $"expected failure naming 'dockerproxy' (exit {exitCode}):\n{output}");
        }
    }
}

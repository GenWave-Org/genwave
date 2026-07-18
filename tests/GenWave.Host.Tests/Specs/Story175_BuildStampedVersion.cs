// STORY-175 — Build-stamped version: git describe → GW_VERSION → InformationalVersion
//
// BDD specification — xUnit (SPEC F65.1). The version is DERIVED from the release tag at build
// time — never committed, never bumped by hand. These specs pin the build-contract files; the
// end-to-end docker stamp is observed via /spectator/api/about (STORY-170) on a stamped image.
// Red until PLAN T08.

using System.Reflection;

namespace GenWave.Host.Tests.Specs;

public static class FeatureBuildStampedVersion
{
    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent!;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioBuildScriptDerivesTheVersion
    {
        [Fact]
        public void BuildScriptUsesGitDescribe()
        {
            var script = File.ReadAllText(Path.Combine(RepoRoot(), "build.sh"));
            Assert.Contains("git describe", script, StringComparison.Ordinal);
        }

        [Fact]
        public void BuildScriptPassesGwVersion()
        {
            var script = File.ReadAllText(Path.Combine(RepoRoot(), "build.sh"));
            Assert.Contains("GW_VERSION", script, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioDockerfileStampsTheBinary
    {
        static string Dockerfile() =>
            File.ReadAllText(Path.Combine(RepoRoot(), "src", "GenWave.Host", "Dockerfile"));

        [Fact]
        public void DockerfileDeclaresTheBuildArgWithDevDefault()
        {
            Assert.Contains("ARG GW_VERSION=0.0.0-dev", Dockerfile(), StringComparison.Ordinal);
        }

        [Fact]
        public void PublishStampsInformationalVersion()
        {
            Assert.Contains("InformationalVersion", Dockerfile(), StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioRuntimeExposesAVersion
    {
        [Fact]
        public void HostAssemblyCarriesAnInformationalVersion()
        {
            var version = typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            Assert.False(string.IsNullOrWhiteSpace(version));
        }
    }
}

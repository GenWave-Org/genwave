// STORY-174 — Reference public topology: shipped files, not just prose
//
// BDD specification — xUnit (SPEC F64.3). The public-station topology ships as first-class
// repo-root files: DEPLOYMENT.md (the doc), compose.demo.yaml (the overlay), Caddyfile (the
// front door). docs/ is private planning space and is NOT part of the public contract.
// Cleanliness contract: none of the public topology files reference the maintainer's real
// deployment hostname — the hostname is operator config (${PUBLIC_HOST}), not repo content.

namespace GenWave.Host.Tests.Specs;

public static class FeaturePublicTopologyDocs
{
    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent!;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    static string ReadRootFile(string name)
    {
        var path = Path.Combine(RepoRoot(), name);
        Assert.True(File.Exists(path), $"{name} is missing from the repo root (F64.3).");
        return File.ReadAllText(path);
    }

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioTopologyShipsAsFiles
    {
        [Fact]
        public void DeploymentDocExists()
        {
            Assert.True(File.Exists(Path.Combine(RepoRoot(), "DEPLOYMENT.md")),
                "DEPLOYMENT.md is missing — the public-station reference topology has no home (F64.3).");
        }

        [Fact]
        public void ComposeOverlayExists()
        {
            Assert.True(File.Exists(Path.Combine(RepoRoot(), "compose.demo.yaml")),
                "compose.demo.yaml is missing — the reference overlay must ship, not live only on a box.");
        }

        [Fact]
        public void CaddyfileExists()
        {
            Assert.True(File.Exists(Path.Combine(RepoRoot(), "Caddyfile")),
                "Caddyfile is missing — the front door must ship alongside compose.demo.yaml.");
        }
    }

    public sealed class ScenarioDocCoversTheContract
    {
        [Fact]
        public void TopologyTargetsThePublicListener()
        {
            Assert.Contains("8081", ReadRootFile("DEPLOYMENT.md"), StringComparison.Ordinal);
        }

        [Fact]
        public void StreamIsRoutedToIcecast()
        {
            Assert.Contains("/stream", ReadRootFile("DEPLOYMENT.md"), StringComparison.Ordinal);
        }

        [Fact]
        public void OperatingModesDocumentComposeProfiles()
        {
            Assert.Contains("COMPOSE_PROFILES", ReadRootFile("DEPLOYMENT.md"), StringComparison.Ordinal);
        }

        [Fact]
        public void OverlayLocksThePortsWithAnOverrideTag()
        {
            Assert.Contains("!override", ReadRootFile("compose.demo.yaml"), StringComparison.Ordinal);
        }

        [Fact]
        public void CaddyfileUsesTheEnvHostname()
        {
            Assert.Contains("{$PUBLIC_HOST}", ReadRootFile("Caddyfile"), StringComparison.Ordinal);
        }
    }

    // ── SAD PATH (cleanliness contract) ───────────────────────────────────

    public sealed class ScenarioNoMaintainerHostnameInPublicTopologyFiles
    {
        [Theory]
        [InlineData("DEPLOYMENT.md")]
        [InlineData("compose.demo.yaml")]
        [InlineData("Caddyfile")]
        public void FileDoesNotReferenceTheRealDeploymentHostname(string file)
        {
            Assert.DoesNotContain("genwaveradio.com", ReadRootFile(file), StringComparison.OrdinalIgnoreCase);
        }
    }
}

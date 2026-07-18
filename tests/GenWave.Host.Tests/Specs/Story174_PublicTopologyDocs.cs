// STORY-174 — Reference public topology documentation
//
// BDD specification — xUnit (SPEC F64.3). docs/DEPLOYMENT.md carries the Caddyfile + compose
// reference: public hostname `/` → api:8081, `/stream` → Icecast, COMPOSE_PROFILES documented
// for all four operating modes. Applying it to demo.genwaveradio.com is PLAN T20 (manual/ops).
// Red until PLAN T19.

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

    static string DeploymentDocPath() => Path.Combine(RepoRoot(), "docs", "DEPLOYMENT.md");

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioReferenceSnippetExists
    {
        [Fact]
        public void DeploymentDocExists()
        {
            Assert.True(File.Exists(DeploymentDocPath()),
                "docs/DEPLOYMENT.md is missing — the public-station reference topology has no home (F64.3).");
        }

        [Fact]
        public void TopologyTargetsThePublicListener()
        {
            var doc = File.ReadAllText(DeploymentDocPath());
            Assert.Contains("8081", doc, StringComparison.Ordinal);
        }

        [Fact]
        public void StreamIsRoutedToIcecast()
        {
            var doc = File.ReadAllText(DeploymentDocPath());
            Assert.Contains("/stream", doc, StringComparison.Ordinal);
        }

        [Fact]
        public void OperatingModesDocumentComposeProfiles()
        {
            var doc = File.ReadAllText(DeploymentDocPath());
            Assert.Contains("COMPOSE_PROFILES", doc, StringComparison.Ordinal);
        }
    }
}

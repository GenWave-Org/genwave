// STORY-518 — the demo overlay and the proxy config are gone (F216 · PLAN T647)
//
// BDD specification — xUnit. Absence facts over the repo tree and every spec under tests/. AC3
// (the retargeted specs stay green) is the PR tier itself — no fact here. Every fact is
// green.

using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureTheDemoOverlayIsGone
{
    static string Root => RepoRootLocator.Find(AppContext.BaseDirectory);

    // Assembled at runtime so this file never names what it forbids.
    static readonly string DemoOverlay = "compose." + "demo.yaml";
    static readonly string ProxyConfig = "Cad" + "dyfile";

    public static class ScenarioTheFilesAreGone
    {
        [Fact]
        public static void The_demo_overlay_does_not_exist()
        {
            Assert.False(File.Exists(Path.Combine(Root, DemoOverlay)));
        }

        [Fact]
        public static void The_proxy_config_does_not_exist()
        {
            Assert.False(File.Exists(Path.Combine(Root, ProxyConfig)));
        }
    }

    public static class ScenarioNoSpecRendersThem
    {
        static IEnumerable<string> SpecFiles() =>
            Directory.EnumerateFiles(Path.Combine(Root, "tests"), "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                         && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        static string[] Naming(string word) =>
            SpecFiles().Where(f => File.ReadAllText(f).Contains(word, StringComparison.Ordinal)).Select(f => Path.GetFileName(f)).ToArray();

        [Fact]
        public static void No_spec_names_the_demo_overlay()
        {
            Assert.Empty(Naming(DemoOverlay));
        }

        [Fact]
        public static void No_spec_names_the_proxy_config()
        {
            Assert.Empty(Naming(ProxyConfig));
        }
    }
}

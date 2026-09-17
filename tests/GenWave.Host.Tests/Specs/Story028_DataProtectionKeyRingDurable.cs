// STORY-028 — data-protection key ring persists across api restarts

namespace GenWave.Host.Tests.Specs;

public static class FeatureDataProtectionKeyRingDurable
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioComposeDeclaresDpKeysVolumeMount
    {
        [Fact]
        public void ComposeYamlDeclaresNamedDpKeysVolume()
        {
            var compose = File.ReadAllText(ComposeYamlPath());
            Assert.Contains("dp_keys", compose);
        }

        private static string ComposeYamlPath()
        {
            var here = AppContext.BaseDirectory;
            for (var dir = new DirectoryInfo(here); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "compose.yaml");
                if (File.Exists(candidate)) return candidate;
            }
            throw new FileNotFoundException("compose.yaml not found above test base directory.");
        }
    }
}

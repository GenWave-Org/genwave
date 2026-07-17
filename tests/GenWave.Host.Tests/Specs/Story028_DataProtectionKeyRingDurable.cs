// STORY-028 — data-protection key ring persists across api restarts

namespace GenWave.Host.Tests.Specs;

public static class FeatureDataProtectionKeyRingDurable
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioKeyRingPathIsBoundFromConfig
    {
        [Fact(Skip = "Pending T035: AddDataProtection().PersistKeysToFileSystem(DataProtection:KeyRingPath)")]
        public void HostStartupRegistersPersistedKeysAtConfiguredPath() { /* IServiceProvider -> IDataProtectionProvider; inspect key manager storage */ }
    }

    public sealed class ScenarioComposeDeclaresDpKeysVolumeMount
    {
        [Fact]
        public void ComposeYamlDeclaresNamedDpKeysVolume()
        {
            var compose = File.ReadAllText(ComposeYamlPath());
            Assert.Contains("dp_keys", compose);
        }

        [Fact(Skip = "Pending T032: api service mount of dp_keys at /var/lib/genwave/dp-keys")]
        public void ApiServiceMountsDpKeysAtConfiguredPath() { /* parse compose YAML; assert api.volumes contains dp_keys:/var/lib/genwave/dp-keys */ }

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

    public sealed class ScenarioCookieFromInstance1AcceptedAfterRestart
    {
        [Fact(Skip = "Pending T036: live wire-up — login, docker compose restart api, GET /api/auth/me with same cookie returns 200")]
        public void SameCookieReturns200AfterApiRestart() { /* integration test against docker compose */ }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioRecreatingKeyRingInvalidatesCookies
    {
        [Fact(Skip = "Pending T036: docker volume rm dp_keys + docker compose up api -> same cookie returns 401")]
        public void SameCookieReturns401AfterDpKeysVolumeRecreated() { /* integration */ }
    }
}

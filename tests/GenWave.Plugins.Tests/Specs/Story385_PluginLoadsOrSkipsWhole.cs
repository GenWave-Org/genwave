// STORY-385 — A plugin loads from a mounted folder, or is skipped whole (F156 · pending T391/T392)
// Loader happy/sad paths run against throwaway plugin assemblies EMITTED AT TEST TIME (Roslyn)
// so CI stays hermetic — the real-world proof is the genwave-plugin-example repo DLL at T394.

namespace GenWave.Plugins.Tests.Specs;

public static class FeaturePluginLoadsOrSkipsWhole
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioManifestDrivenDiscoveryOnly
    {
        [Fact(Skip = "Pending T391 — see docs/PLAN.md")]
        public void OnlyManifestDirectoriesAreConsidered()
        {
            // Root with <slug>/plugin.json AND a loose stray.dll beside it:
            //   discovery yields exactly the manifest directory; the loose DLL is never probed.
            Assert.Fail("pending T391");
        }

        [Fact(Skip = "Pending T391 — see docs/PLAN.md")]
        public void AWellFormedManifestParsesAllFiveFields()
        {
            // name/version/assembly/entryType/abstractions round-trip from JSON.
            Assert.Fail("pending T391");
        }
    }

    public sealed class ScenarioAValidPluginLoadsInItsOwnContext
    {
        [Fact(Skip = "Pending T392 — see docs/PLAN.md")]
        public void TheAssemblyLoadsInADedicatedLoadContext()
        {
            // Emit a minimal IGenWavePlugin assembly; load; its ALC is not Default and is per-plugin.
            Assert.Fail("pending T392");
        }

        [Fact(Skip = "Pending T392 — see docs/PLAN.md")]
        public void AbstractionsTypesUnifyWithTheHost()
        {
            // typeof(IGenWavePlugin) from the loaded plugin instance == the host's type identity
            //   (a plugin-carried Abstractions copy is never loaded — F156.3).
            Assert.Fail("pending T392");
        }

        [Fact(Skip = "Pending T392 — see docs/PLAN.md")]
        public void RegisterRunsAndItsRegistrationsAreCollected()
        {
            // The emitted plugin's Register(IPluginHost) adds one IContextProvider;
            //   the collector holds exactly that instance.
            Assert.Fail("pending T392");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — every failure skips the WHOLE plugin, boot continues (F156.4)
    // ---------------------------------------------------------------------

    public sealed class ScenarioRejectingBadManifests
    {
        [Fact(Skip = "Pending T391 — see docs/PLAN.md")]
        public void AMissingEntryTypeSkipsWithAWarnNamingTheField()
        {
            Assert.Fail("pending T391");
        }

        [Fact(Skip = "Pending T391 — see docs/PLAN.md")]
        public void AnAssemblyValueWithAPathSeparatorIsRejected()
        {
            // "sub/dir.dll" and "..\\up.dll" both refuse — the manifest names a FILE, never a path.
            Assert.Fail("pending T391");
        }
    }

    public sealed class ScenarioSkippingBrokenPlugins
    {
        [Fact(Skip = "Pending T392 — see docs/PLAN.md")]
        public void ACorruptDllSkipsTheWholePluginAndBootContinues()
        {
            // Manifest whose assembly file is garbage bytes: zero registrations from that dir,
            //   one WARN naming the cause, the loader returns normally.
            Assert.Fail("pending T392");
        }

        [Fact(Skip = "Pending T392 — see docs/PLAN.md")]
        public void AThrowingRegisterLeavesNoPartialRegistrations()
        {
            // Plugin adds one provider then throws: the collector holds NOTHING from it.
            Assert.Fail("pending T392");
        }

        [Fact(Skip = "Pending T392 — see docs/PLAN.md")]
        public void AContextKeyCollisionSkipsTheColliderWhole()
        {
            // Emitted plugin whose IContextProvider.Key == "weather" (a built-in key):
            //   pre-validation skips that plugin entirely — ContextPipeline's fail-fast ctor
            //   must never be the thing that discovers the collision (F156.6).
            Assert.Fail("pending T392");
        }

        [Fact(Skip = "Pending T392 — see docs/PLAN.md")]
        public void TwoPluginsCollidingOnAKeyLoadFirstSkipSecond()
        {
            Assert.Fail("pending T392");
        }
    }
}

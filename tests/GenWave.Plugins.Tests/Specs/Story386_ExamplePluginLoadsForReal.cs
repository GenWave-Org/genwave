// STORY-386 — What loaded is visible, and additive by construction (F156.5, F156.7, F157.2)
//
// PLAN T393's own reference-consumer half: every other fact in this project loads a THROWAWAY
// assembly Support/EmittedPluginAssembly.cs compiles at test time with Roslyn — proof the loader
// itself works, but never proof a REAL third-party build produces something it can load. This file
// points PluginLoader.LoadAll at examples/genwave-plugin-example's own genuine `dotnet build` output
// (Support/ExamplePluginPayload.cs — see that file's own remarks for how it stays hermetic without
// loading the example's assembly into this test process).

namespace GenWave.Plugins.Tests.Specs;

using System.Runtime.Loader;
using GenWave.Core.Abstractions;
using GenWave.Plugins;
using GenWave.Plugins.Tests.Support;

public static class FeatureExamplePluginLoadsForReal
{
    public sealed class ScenarioLoadingTheExampleProjectsRealBuildOutput : IDisposable
    {
        readonly string root = Directory.CreateTempSubdirectory("genwave-example-plugin-").FullName;

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public void TheExamplePluginLoadsAndItsContextProviderCommits()
        {
            // Given a plugins root composed from the example project's OWN dotnet build output —
            // copied on disk, never referenced by this test assembly (ExamplePluginPayload's own
            // remarks) — mounted under a slug, exactly the shape a real operator's
            // compose.plugins.yaml mount produces...
            ExamplePluginPayload.CopyInto(root, "dice-roll-example");

            // When the loader runs exactly as it would against a real mount...
            var loader = new PluginLoader(settingKey => null);
            var result = loader.LoadAll(root, new HashSet<string>());

            // Then it loaded — naming the manifest's own "name"/"version" fields and the one
            // contract its Register call adds...
            var report = Assert.Single(result.Reports);
            Assert.Equal(PluginLoadState.Loaded, report.State);
            Assert.Equal("Dice Roll Example Plugin", report.Name);
            Assert.Equal("1.0.0", report.Version);
            Assert.Equal(new[] { nameof(IContextProvider) }, report.Contracts);

            // ...and the committed provider carries the exact Key the example's own README
            // documents.
            var provider = Assert.Single(result.ContextProviders);
            Assert.Equal("example-dice", provider.Key);
        }

        [Fact]
        public void TheExamplePluginLoadsIntoItsOwnAssemblyLoadContext()
        {
            // Given/When: the same real build-output payload, run through the same loader...
            ExamplePluginPayload.CopyInto(root, "dice-roll-example");
            var loader = new PluginLoader(settingKey => null);
            var result = loader.LoadAll(root, new HashSet<string>());
            var provider = Assert.Single(result.ContextProviders);

            // Then it loaded into its OWN AssemblyLoadContext, never Default (SPEC F156.3) — the
            // same isolation proof Story385's emitted-assembly facts already pin, now against a
            // REAL, on-disk third-party build rather than a Roslyn throwaway. Kept as its own Fact
            // (not folded into the commit-shape assertions above) so a future isolation regression
            // fails under its own unambiguous name.
            var loadContext = AssemblyLoadContext.GetLoadContext(provider.GetType().Assembly);
            Assert.NotNull(loadContext);
            Assert.NotSame(AssemblyLoadContext.Default, loadContext);
        }
    }
}

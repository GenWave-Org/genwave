// STORY-388 — The ads seam's floor registers LAST (SPEC F158.2, F163.3 · PLAN T397 review carry-forward F7)
//
// Drives the REAL production composition root (WebApplicationFactory<Program>, via the shared
// Support/PluginDoorWebFactory.cs — also Story386_PluginDoorVisibleAndAdditive.cs's own "deployed
// pipeline" idiom, that file's own remarks) with a real plugins root on disk carrying a compiled fake
// IAdSpotSource, and asserts the COMPOSED order IEnumerable<IAdSpotSource> resolves in: the plugin's
// own source FIRST, GenWave.Ads' own LibraryAdSpotSource (the library floor) AFTER it. This is the F7
// carry-forward's own demanded proof — AddGenWavePluginDoor (which buffers a loaded plugin's
// IAdSpotSource registrations) must run BEFORE AddGenWaveAds (which appends the floor) in
// Program.cs's own call sequence; a future reordering of those two lines fails this fact rather than
// silently inverting "first non-null wins, floor last" (AdSpotPipeline's own contract, SPEC F158.2).

using Microsoft.Extensions.DependencyInjection;
using GenWave.Core.Abstractions;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAdSpotSourceRegistrationOrder
{
    /// <summary>A trivial <c>IGenWavePlugin</c> whose <c>Register</c> body does nothing but commit
    /// one <c>IAdSpotSource</c> — the compiled fake this whole fact hinges on, mirroring
    /// <c>Story386_PluginDoorVisibleAndAdditive.PluginSource</c>'s own template idiom one file over.
    /// Never invoked (this fact asserts registration ORDER, not vend behavior), so its own
    /// <c>GetNextSpotAsync</c> body is a placeholder null answer — legal (F158.1), and irrelevant
    /// here.</summary>
    const string PluginSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using GenWave.Core.Abstractions;
        using GenWave.Core.Domain;

        namespace AdOrderPlugin;

        public sealed class EntryPoint : IGenWavePlugin
        {
            public string Name => "Ad Order Plugin";

            public void Register(IPluginHost host) => host.AddAdSpotSource(new Source());

            sealed class Source : IAdSpotSource
            {
                public ValueTask<MediaItem?> GetNextSpotAsync(CancellationToken ct) =>
                    ValueTask.FromResult<MediaItem?>(null);
            }
        }
        """;

    public sealed class ScenarioThePluginSourceComesFirst : IDisposable
    {
        readonly string root = Directory.CreateTempSubdirectory("genwave-host-ad-spot-order-").FullName;

        public ScenarioThePluginSourceComesFirst() =>
            EmittedHostTestPlugin.CreateInto(
                root, "ad-order-plugin", "Ad Order Plugin", "AdOrderPlugin.EntryPoint", PluginSource);

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public async Task ThePluginSourceResolvesBeforeTheLibraryFloor()
        {
            // PluginDoorWebFactory (tests/GenWave.Host.Tests/Support/) — shared with
            // Story386_PluginDoorVisibleAndAdditive.cs (PLAN T397 review fold), the SAME "real plugin
            // door, real WebApplicationFactory<Program>" composition that file's own facts use.
            await using var factory = new PluginDoorWebFactory(root, enabled: true);

            // The narrowest trigger that forces the host to actually build (the plugin-door
            // suite's own precedent) — no HTTP round trip needed just to resolve a DI-registered
            // enumerable.
            var sources = factory.Services.GetServices<IAdSpotSource>().ToList();

            // Two sources: the plugin's own commit (AddGenWavePluginDoor) and GenWave.Ads' own
            // LibraryAdSpotSource floor (AddGenWaveAds) — nothing else registers IAdSpotSource in
            // this composition.
            Assert.Equal(2, sources.Count);
            Assert.IsNotType<GenWave.Ads.LibraryAdSpotSource>(sources[0]);
            Assert.IsType<GenWave.Ads.LibraryAdSpotSource>(sources[1]);
        }
    }
}

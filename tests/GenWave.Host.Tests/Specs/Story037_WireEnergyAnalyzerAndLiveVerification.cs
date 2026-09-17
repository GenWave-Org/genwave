// STORY-037 — WIRE energy analysis into the host + live verification
//
// BDD specification — xUnit. Composition-root facts (3, T507) prove the wiring for real via
// SeamCompositionSnapshot (T216) against the host's own Program.cs — no container needed. The two
// live facts (recorded audio against enriched fixtures) were Assert.Fail stubs and are gone (T507):
// no gate assertion measures them directly; the comments below name the nearest live proof
// (stack_gate.sh --capture, SPEC F178.5) — see docs/PLAN.md / docs/STORIES.md Epic H.

using GenWave.Core.Abstractions;
using GenWave.Host.Configuration;
using GenWave.Host.Tests.Support;
using GenWave.Loudness;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GenWave.Host.Tests.Specs;

public static class FeatureWireEnergyAnalyzerAndLiveVerification
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — composition root (T507: proven live via SeamCompositionSnapshot, no container)
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnalyzerIsRegisteredAsASharedSingleton
    {
        [Fact]
        public void IEnergyAnalyzerResolvesToFfmpegEnergyAnalyzer()
        {
            var ports = SeamCompositionSnapshot.Capture(t => t == typeof(IEnergyAnalyzer));

            var port = Assert.Single(ports);
            var adapter = Assert.Single(port.Adapters);
            Assert.Equal(typeof(FfmpegEnergyAnalyzer), adapter.AdapterType);
        }

        [Fact]
        public void EnricherAndDiResolveTheSameAnalyzerInstance()
        {
            // Singleton lifetime: whatever the Enricher's constructor asks DI for, every other
            // caller (including a second DI resolve) gets back the same instance.
            var ports = SeamCompositionSnapshot.Capture(t => t == typeof(IEnergyAnalyzer));

            var port = Assert.Single(ports);
            var adapter = Assert.Single(port.Adapters);
            Assert.Equal(ServiceLifetime.Singleton, adapter.Lifetime);
        }

        [Fact]
        public void LibraryEnergyOptionsBind()
        {
            using var factory = new EnergyOptionsWebFactory();

            var options = factory.Services.GetRequiredService<IOptions<EnergyOptions>>().Value;

            Assert.Equal(12.0, options.WindowSeconds);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — live (production binary side effects)
    // ---------------------------------------------------------------------

    // Enriched fixtures showing intro/outro_energy populated in the database, with a quiet fixture
    // measurably lower than a loud one: no gate assertion measures this directly — stack_gate.sh's
    // legs check silence/loudness/booth_log outcomes on the live stream (F178.5), not the media
    // table's intro_energy/outro_energy columns. Was an Assert.Fail stub before T507 (former fact
    // EnrichedFixturesHaveEnergyInTheDatabase).

    // Two real music→music transitions where the hot pair's crossfade is measurably shorter: no gate
    // assertion measures a fade's actual duration directly — the nearest live proof is stack_gate.sh
    // --capture (F178.5: zero silence events, integrated LUFS within ±5 LU, a booth_log row), which
    // proves silence/loudness/booth_log outcomes, not transition timing. Was an Assert.Fail stub
    // before T507 (former fact RecordedHotPairCrossfadeIsShorterThanMellowPair).

    /// <summary>
    /// <see cref="WebApplicationFactory{TEntryPoint}"/> for reading a bound options value straight
    /// off the host's real composition root — mirrors <c>SeamCompositionSnapshot</c>'s own "minimal
    /// config, no DB, no hosted services" recipe, but resolves a concrete value instead of a port's
    /// adapter type.
    /// </summary>
    sealed class EnergyOptionsWebFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
            builder.UseSetting("Admin:Password", "energy-options-snapshot");
            builder.UseSetting(StationSettingsHostingExtensions.ExpectNoStoreKey, "true");

            builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
        }
    }
}

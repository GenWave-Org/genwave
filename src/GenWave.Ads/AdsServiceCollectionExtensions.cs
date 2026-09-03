using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;

namespace GenWave.Ads;

/// <summary>
/// SPEC F158.2/F159.1 (STORY-388, PLAN T396/T397) — composes the ads seam: <see cref="AdsOptions"/>
/// (env-only, boot-validated) and <see cref="AdSpotAntiRepeatOptions"/> (Live-shaped, unvalidated
/// here — see that class's own remarks), <see cref="AdsLibrarySeeder"/> + its hosted-service shell
/// (the marker-gated boot seed), and <see cref="AdSpotPipeline"/> with its floor source
/// <see cref="LibraryAdSpotSource"/> — additionally registered as
/// <see cref="Core.Abstractions.IAdSpotVend"/> (PLAN T397), the seam <c>Orchestrator</c>'s ad drain
/// actually consumes.
///
/// <para>
/// <b>Registration order is load-bearing (F158.2's "the floor registers last" idiom).</b> The BCL's
/// own DI container resolves an open <c>IEnumerable&lt;TService&gt;</c>
/// in REGISTRATION order — every <c>services.AddSingleton&lt;IAdSpotSource, T&gt;()</c> call the
/// container has seen by the time <see cref="AdSpotPipeline"/> is first resolved, in the order those
/// calls were made. <see cref="AdSpotPipeline"/>'s "first non-null wins, floor last" contract (SPEC
/// F158.2) therefore depends entirely on THIS method being called AFTER every plugin/business
/// <see cref="IAdSpotSource"/> registration in <c>Program.cs</c> — concretely, the plugin door
/// (<c>AddGenWavePluginDoor</c>, PLAN T394, which registers a loaded plugin's own
/// <see cref="IAdSpotSource"/> implementations) MUST run BEFORE this call. <b>PLAN T397's own
/// <c>Program.cs</c> line</b> calls this method immediately after <c>AddGenWavePluginDoor</c>,
/// preserving that order — a DI-order fitness fact (<c>Story388_AdSpotSourceRegistrationOrder</c>,
/// GenWave.Host.Tests) pins it against a real <c>WebApplicationFactory&lt;Program&gt;</c> boot with a
/// fake plugin source, so a future reordering fails loudly rather than silently inverting the floor.
/// </para>
/// </summary>
public static class AdsServiceCollectionExtensions
{
    public static IServiceCollection AddGenWaveAds(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<AdsOptions>()
            .Bind(configuration.GetSection(AdsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Station:Ads:AntiRepeatWindow (SPEC F163.1) — Live-shaped, so plain .Bind() with no
        // ValidateDataAnnotations()/ValidateOnStart(): its 0-50 range is enforced at the settings
        // allowlist/validator (PLAN T397), never by DataAnnotations on a bound options class — the
        // same split GardenerOptions documents for its own one Live exception.
        services
            .AddOptions<AdSpotAntiRepeatOptions>()
            .Bind(configuration.GetSection(AdSpotAntiRepeatOptions.SectionName));

        services.AddSingleton<AdsLibrarySeeder>();
        services.AddHostedService<AdsLibrarySeedHostedService>();

        // F158.2's floor idiom: registered LAST relative to any plugin/business IAdSpotSource — see
        // this method's own remarks above for why that is this METHOD's call-order requirement, not
        // something it can enforce internally.
        services.AddSingleton<IAdSpotSource, LibraryAdSpotSource>();

        // Registered as a singleton VALUE (not resolved as a service) so AdSpotPipeline's own factory
        // just below and AdRenderService's DI-injected ctor param (SPEC F161.2, PLAN T401) share the
        // SAME resolved roots rather than each re-reading IConfiguration independently.
        var locatorRoots = AdSpotLocatorRoots.FromConfiguration(configuration);
        services.AddSingleton(locatorRoots);

        services.AddSingleton(sp => new AdSpotPipeline(
            sp.GetServices<IAdSpotSource>(),
            locatorRoots,
            sp.GetRequiredService<ILogger<AdSpotPipeline>>()));

        // The render seam (SPEC F161.1-F161.3; STORY-391; PLAN T401) — a plain singleton with zero
        // eager I/O in its constructor (Story125's zero-I/O invariant): every dependency here is
        // itself a cheap seam (CastSegmentAuthor, the Core store/lookup/repository abstractions,
        // options, a logger). AdSpotWorker (below) is this seam's one caller — it claims a spot via
        // IAdSpotStore.ClaimNextApprovedAsync, then calls AdRenderService.RenderAsync with what it
        // claimed.
        services.AddSingleton<AdRenderService>();

        // The off-air-clock tick loop + its stuck-rendering guardian (SPEC F159.3, F159.4, F161.1;
        // STORY-389, STORY-391; PLAN T402) — both live in THIS project (unlike CrosstalkStockWorker/
        // AnnouncementLifecycleGuardianService, which are GenWave.Host types registered from a
        // Host-side extension method), so both self-register here rather than needing a matching
        // Host-side wiring call. Every dependency either resolves within GenWave.Ads itself or through
        // a Core seam a Host-side registration (elsewhere in Program.cs) satisfies — see
        // IOnAirRenderSignal's own remarks for the one that closes the Host-layering gap.
        services.AddHostedService<AdSpotWorker>();
        services.AddHostedService<AdSpotLifecycleGuardianService>();

        // PLAN T397 — the drain seam: overrides AddGenWaveOrchestration's own
        // TryAddSingleton<IAdSpotVend>(NoOpAdSpotVend.Instance) default (the override-after-the-
        // default idiom IStationImagingSettingsProvider's own registration already establishes one
        // project over). A plain AddSingleton resolving the SAME AdSpotPipeline singleton just
        // registered above — never a second instance — mirrors Program.cs's own
        // AddSingleton<IListenerStatsSource>(sp => sp.GetRequiredService<IcecastListenerStatsSource>())
        // "expose an additional interface over an existing singleton" idiom.
        services.AddSingleton<IAdSpotVend>(sp => sp.GetRequiredService<AdSpotPipeline>());

        return services;
    }
}

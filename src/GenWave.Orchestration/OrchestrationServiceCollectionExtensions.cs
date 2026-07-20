using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using GenWave.Core.Abstractions;

namespace GenWave.Orchestration;

/// <summary>
/// Composition of the orchestration service (gitea-#243). The host wires the station's selection brain
/// with one call; a module that wants a different selection strategy overrides the
/// <see cref="INextItemProvider"/> binding after this runs.
/// </summary>
public static class OrchestrationServiceCollectionExtensions
{
    /// <summary>
    /// SEAM 1: <see cref="Orchestrator"/> is the <see cref="INextItemProvider"/> — interleaved
    /// music + TTS patter per the live cadence config. Every constructor dependency is a seam the
    /// host (or a module) has already registered: identity/scope/cadence/rotation/render-budget/
    /// boundary-bias providers, <c>IMediaCatalog</c>, <c>ITtsSegmentSource</c>,
    /// <c>IActivePersonaAccessor</c>, and the <see cref="SpeechDeferralQueue"/>/<see cref="TimeProvider"/>
    /// this method also registers.
    /// </summary>
    public static IServiceCollection AddGenWaveOrchestration(this IServiceCollection services)
    {
        // The clock SpeechDeferralQueue reads for its default "due" and NextDue (SPEC F74.1) —
        // TryAdd so a host or test that already registers its own TimeProvider wins (mirrors
        // GenWave.Tts's own TryAddSingleton(TimeProvider.System)).
        services.TryAddSingleton(TimeProvider.System);

        // One queue per station process (SPEC F74.1/F74.2/F74.4, STORY-197): in-memory only, so a
        // restart drops it along with the rest of the process and a fresh one starts empty — no
        // persistence, no stale entry to double-air. Shared singleton so a future deferral
        // producer besides the Orchestrator's own cadence check can enqueue into the SAME
        // instance the Orchestrator drains.
        services.TryAddSingleton<SpeechDeferralQueue>();

        return services.AddSingleton<INextItemProvider, Orchestrator>();
    }
}

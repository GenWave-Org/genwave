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
    /// boundary-bias providers, <see cref="MusicSelectionPolicy"/>, <c>ITtsSegmentSource</c>,
    /// <c>IActivePersonaAccessor</c>, and the <see cref="SpeechDeferralQueue"/>/<see cref="TimeProvider"/>
    /// this method also registers. <see cref="MusicSelectionPolicy"/> itself (F112, STORY-295) owns
    /// the pick ladder — <c>IMediaCatalog</c>/<c>IEnvelopeProvider</c>/<see cref="IPersonaPickProvider"/>/
    /// <see cref="IRequestFulfillmentSource"/> moved with it off <see cref="Orchestrator"/>'s own
    /// constructor.
    ///
    /// <para>
    /// <b>Handoff ceremony seams (SPEC F92.1, STORY-243, PLAN T124) — deliberately NOT registered
    /// here:</b> <see cref="Orchestrator"/>'s optional <c>CachingScheduleResolver</c>/<c>IPersonaStore</c>
    /// constructor parameters come from whatever the HOST wired for the format-clock feature —
    /// <c>StationSettingsHostingExtensions.AddGenWaveStationSettings</c> (GenWave.Host) registers the
    /// singleton <c>CachingScheduleResolver</c>; <c>PersonaServiceCollectionExtensions.AddPersonaStore</c>
    /// (GenWave.MediaLibrary) registers <c>IPersonaStore</c>. This method owns neither registration —
    /// a host that never calls <c>AddGenWaveStationSettings</c> (no format-clock schedule) simply
    /// leaves both parameters at their constructor default (<see langword="null"/>) rather than
    /// failing composition; <see cref="Orchestrator"/>'s handoff producer then logs ONE WARN on its
    /// first unit and stays a permanent, silent-after-that no-op (the pre-F91 station shape).
    /// </para>
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

        // The SPEC F81.6 rung-0 seam (STORY-212/213): TryAdd so a module that binds a real
        // ranker-backed IPersonaPickProvider (PLAN T64) wins over this default — until then every
        // pick is envelope-only (F81.2).
        services.TryAddSingleton<IPersonaPickProvider, NoOpPersonaPickProvider>();

        // The SPEC F87.6 fulfillment rung, one step ahead of the persona seam above (STORY-227,
        // PLAN T90): TryAdd so a host that binds the real RequestFulfillmentProvider wins over this
        // default — until then no pending request ever short-circuits a pick.
        services.TryAddSingleton<IRequestFulfillmentSource, NoOpRequestFulfillmentSource>();

        // gh-#253: the patter-duration estimation seam — one process-wide instance so the render
        // loop's observations and the boundary-fit consumer (gh-#254) read the SAME rolling history.
        // ICopyBoundsProvider is optional by design (GetService, not GetRequiredService): a host
        // that never wires AddGenWaveTts falls back to the estimator's own built-in default bound.
        // TryAdd so a module that binds a different estimator wins.
        services.TryAddSingleton<IPatterDurationEstimator>(sp =>
            new RollingPatterDurationEstimator(sp.GetService<ICopyBoundsProvider>()));

        // F112 (STORY-295, PLAN T218): the pick ladder itself — request rung, persona rung,
        // trust-but-verify, degradation ladder — TryAdd so a module that binds a different policy
        // wins; consumes the SAME IPersonaPickProvider/IRequestFulfillmentSource seams registered
        // above plus IMediaCatalog/IEnvelopeProvider from wherever the host wires those.
        services.TryAddSingleton<MusicSelectionPolicy>();

        return services.AddSingleton<INextItemProvider, Orchestrator>();
    }
}

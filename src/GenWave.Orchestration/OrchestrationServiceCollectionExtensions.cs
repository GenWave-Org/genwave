using Microsoft.Extensions.DependencyInjection;
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
    /// host (or a module) has already registered: identity/scope/cadence/rotation/render-budget
    /// providers, <c>IMediaCatalog</c>, <c>ITtsSegmentSource</c>, <c>IActivePersonaAccessor</c>.
    /// </summary>
    public static IServiceCollection AddGenWaveOrchestration(this IServiceCollection services) =>
        services.AddSingleton<INextItemProvider, Orchestrator>();
}

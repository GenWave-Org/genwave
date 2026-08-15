using GenWave.Core.Abstractions;
using GenWave.Host.Options;
using GenWave.Orchestration;

namespace GenWave.Host.Crosstalk;

/// <summary>
/// The Host's composition of the crosstalk stock-timer loop (SPEC F127.7, STORY-328, PLAN T286).
/// <see cref="CrosstalkPlanner"/> (GenWave.Orchestration) is registered HERE, not inside
/// <c>OrchestrationServiceCollectionExtensions.AddGenWaveOrchestration</c> — see
/// <c>StationOptionsServiceCollectionExtensions</c>'s own recorded rider on
/// <see cref="ICrosstalkScopeProvider"/>: "no consumer resolves this yet — CrosstalkPlanner is
/// registered by a LATER task's own Host wiring (PLAN T286)". <see cref="CrosstalkScriptWriter"/>/
/// <see cref="CrosstalkAssembler"/> are already singletons (<c>AddGenWaveTts</c>); this method wires
/// nothing new for either.
///
/// <para>
/// <b>Ordering — must run after <c>.AddGenWaveTts()</c>, <c>.AddGenWaveOrchestration()</c>, and
/// <c>.AddGenWavePlayout()</c> in Program.cs.</b> Needs
/// <see cref="GenWave.Tts.CrosstalkScriptWriter"/>/<see cref="GenWave.Tts.CrosstalkAssembler"/>/
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>&lt;<see cref="GenWave.Tts.TtsOptions"/>&gt;
/// from the first, <see cref="CachingScheduleResolver"/> from <c>AddGenWaveStationSettings</c> (already
/// run earlier in Program.cs) and <see cref="ICrosstalkScopeProvider"/>/<see cref="IPersonaStore"/>
/// from <c>AddGenWaveStationOptions</c>/<c>AddMediaLibrary</c> (both already run) for the second, and
/// <see cref="GenWave.Host.Playout.NowPlayingService"/>/<see cref="GenWave.Host.Playout.OnAirRenderGate"/>
/// (PLAN T286 review F1 — the real on-air-render-in-flight signal <c>PlayoutFeederService</c> itself
/// sets) from the third.
/// </para>
/// </summary>
static class CrosstalkHostServiceCollectionExtensions
{
    public static IServiceCollection AddGenWaveCrosstalkHost(this IServiceCollection services)
    {
        services.AddSingleton<CrosstalkPlanner>();
        services.AddHostedService<CrosstalkStockWorker>();

        return services;
    }
}

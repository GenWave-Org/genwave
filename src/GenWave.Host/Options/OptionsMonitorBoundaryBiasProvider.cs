using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;

namespace GenWave.Host.Options;

/// <summary>
/// The Host-side half of the <see cref="IBoundaryBiasProvider"/> seam (SPEC F74.3, STORY-198):
/// wraps <see cref="IOptionsMonitor{TOptions}"/> so a config-provider reload of
/// <c>Station:BoundaryBias:LookaheadMinutes</c> reaches the Orchestrator without a process
/// restart (mirrors <see cref="OptionsMonitorRenderBudgetProvider"/>) — though, unlike that seam,
/// no <c>PUT /api/settings</c> write path reaches this value yet (v1, boot/env-tunable only).
///
/// Nothing is cached here — <see cref="IOptionsMonitor{T}.CurrentValue"/> already is the cache.
/// </summary>
sealed class OptionsMonitorBoundaryBiasProvider(IOptionsMonitor<StationOptions> stationMonitor)
    : IBoundaryBiasProvider
{
    public TimeSpan Current =>
        TimeSpan.FromMinutes(stationMonitor.CurrentValue.BoundaryBias.LookaheadMinutes);
}

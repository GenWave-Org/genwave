using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;

namespace GenWave.Host.Options;

/// <summary>
/// The Host-side half of the <see cref="IAdCadenceProvider"/> seam (SPEC F158.3, STORY-388, PLAN
/// T397): wraps <see cref="IOptionsMonitor{TOptions}"/> so <c>Orchestrator</c> reads the SAME live
/// value <c>PUT /api/settings</c> writes to <c>Station:Ads:EveryNUnits</c> (mirrors
/// <see cref="OptionsMonitorCadenceProvider"/>, its own <c>StationIdEveryNUnits</c> twin).
///
/// Nothing is cached here — <see cref="IOptionsMonitor{T}.CurrentValue"/> already is the cache.
/// </summary>
sealed class OptionsMonitorAdCadenceProvider(IOptionsMonitor<StationOptions> stationMonitor)
    : IAdCadenceProvider
{
    public int Current => stationMonitor.CurrentValue.Ads.EveryNUnits;
}

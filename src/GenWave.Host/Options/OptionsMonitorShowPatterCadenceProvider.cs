using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;

namespace GenWave.Host.Options;

/// <summary>
/// The Host-side half of the <see cref="IShowPatterCadenceProvider"/> seam (SPEC F116.3, STORY-308,
/// PLAN T249): wraps <see cref="IOptionsMonitor{TOptions}"/> so
/// <c>GenWave.Orchestration.ShowFlavorLineGate</c> reads the SAME live value <c>PUT /api/settings</c>
/// writes — mirrors <see cref="OptionsMonitorCadenceProvider"/> one seam over.
/// </summary>
sealed class OptionsMonitorShowPatterCadenceProvider(IOptionsMonitor<StationOptions> stationMonitor)
    : IShowPatterCadenceProvider
{
    public int PatterCadenceMinutes => stationMonitor.CurrentValue.Shows.PatterCadenceMinutes;
}

using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Options;

/// <summary>
/// The Host-side half of the <see cref="IRotationSettingsProvider"/> seam (SPEC F41.6): wraps
/// <see cref="IOptionsMonitor{TOptions}"/> so both the Orchestrator (artist separation) and
/// <see cref="GenWave.Core.Playout.PlayoutFeeder"/> (anti-repeat window) read the SAME live
/// value <c>PUT /api/settings</c> writes (mirrors <see cref="OptionsMonitorCadenceProvider"/>).
///
/// Builds a new <see cref="RotationSettings"/> from <see cref="StationOptions.Rotation"/> on every
/// call — nothing is cached here — <see cref="IOptionsMonitor{T}.CurrentValue"/> already is the
/// cache, and caching again would reintroduce the boot-snapshot staleness this seam exists to fix.
/// </summary>
sealed class OptionsMonitorRotationSettingsProvider(IOptionsMonitor<StationOptions> stationMonitor)
    : IRotationSettingsProvider
{
    public RotationSettings Current
    {
        get
        {
            var rotation = stationMonitor.CurrentValue.Rotation;
            return new RotationSettings
            {
                RecentWindow = rotation.RecentWindow,
                ArtistSeparation = rotation.ArtistSeparation,
            };
        }
    }
}

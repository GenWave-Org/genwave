using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Options;

/// <summary>
/// The Host-side half of the <see cref="ICadenceProvider"/> seam (gitea-#211, the F30.1 precedent
/// applied to cadence): wraps <see cref="IOptionsMonitor{TOptions}"/> so Orchestrator reads the
/// SAME live value <c>PUT /api/settings</c> writes (mirrors <see cref="OptionsMonitorStationScopeProvider"/>).
///
/// Builds a new <see cref="CadenceConfig"/> from <see cref="StationOptions.Cadence"/> on every
/// call — the same mapping <c>Program.cs</c> used to perform once at boot. Nothing is cached here —
/// <see cref="IOptionsMonitor{T}.CurrentValue"/> already is the cache, and caching again would
/// reintroduce the boot-snapshot staleness this seam exists to fix.
/// </summary>
sealed class OptionsMonitorCadenceProvider(IOptionsMonitor<StationOptions> stationMonitor)
    : ICadenceProvider
{
    public CadenceConfig Current
    {
        get
        {
            var cadence = stationMonitor.CurrentValue.Cadence;
            return new CadenceConfig
            {
                LeadInBeforeEachTrack = cadence.LeadInBeforeEachTrack,
                BackAnnounceAfterEachTrack = cadence.BackAnnounceAfterEachTrack,
                StationIdEveryNUnits = cadence.StationIdEveryNUnits,
            };
        }
    }
}

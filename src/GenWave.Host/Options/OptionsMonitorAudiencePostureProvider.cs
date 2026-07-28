using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Options;

/// <summary>
/// The Host-side half of the <see cref="IAudiencePostureProvider"/> seam (SPEC F95.1/F95.4, STORY-250,
/// PLAN T111/T114): wraps <see cref="IOptionsMonitor{TOptions}"/> so every pool-predicate query — the
/// rotation/envelope candidate queries and the request-catalog probe — reads the SAME live
/// <c>Station:Audience</c> value, through the SAME idiom <see cref="OptionsMonitorSafeScopeProvider"/>
/// established for the safe-content scope.
///
/// Resolves <see cref="StationOptions.Audience"/>'s raw string through
/// <see cref="AudiencePostureParser.Parse"/> on every call — nothing cached here;
/// <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> already is the cache, so a live
/// <c>Station:Audience</c> edit governs the very next pool query.
/// </summary>
sealed class OptionsMonitorAudiencePostureProvider(IOptionsMonitor<StationOptions> stationMonitor)
    : IAudiencePostureProvider
{
    public AudiencePosture Current => AudiencePostureParser.Parse(stationMonitor.CurrentValue.Audience);
}

using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Options;

/// <summary>
/// The Host-side half of the <see cref="ISafeScopeProvider"/> seam (gh-#99): wraps
/// <see cref="IOptionsMonitor{TOptions}"/> so every safe-content exclusion check — the rating
/// endpoints, the taste-thumb surfaces — reads the SAME live <c>Station:SafeScope:LibraryIds</c>
/// value the safe-track picker itself uses (<see cref="Api.InternalEndpoints"/>), through the SAME
/// idiom <see cref="OptionsMonitorStationScopeProvider"/> established for the rotation scope.
///
/// Builds a new <see cref="LibraryScope"/> from <see cref="StationOptions.SafeScope"/> on every
/// call — nothing cached here; <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> already is the
/// cache, so a live SafeScope edit governs the very next exclusion check.
/// </summary>
sealed class OptionsMonitorSafeScopeProvider(IOptionsMonitor<StationOptions> stationMonitor)
    : ISafeScopeProvider
{
    public LibraryScope Current => new(stationMonitor.CurrentValue.SafeScope.LibraryIds.ToArray());
}

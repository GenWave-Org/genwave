using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Options;

/// <summary>
/// The Host-side half of the <see cref="IStationScopeProvider"/> seam (SPEC F30.1): wraps
/// <see cref="IOptionsMonitor{TOptions}"/> so every consumer — Orchestrator, MediaController,
/// ReenrichController, the <c>/media/*</c> minimal API, <see cref="GenWave.Host.Selection.RandomSelectionProvider"/> —
/// reads the SAME live value through the SAME idiom (mirrors <see cref="Api.StatusController"/> and
/// <see cref="Api.InternalEndpoints"/>'s SafeScope reads).
///
/// Builds a new <see cref="LibraryScope"/> from <see cref="StationOptions.Scope"/> on every call.
/// Nothing is cached here — <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> already is the
/// cache, and caching again would reintroduce the boot-snapshot staleness this seam exists to fix.
/// </summary>
sealed class OptionsMonitorStationScopeProvider(IOptionsMonitor<StationOptions> stationMonitor)
    : IStationScopeProvider
{
    public LibraryScope Current => new(stationMonitor.CurrentValue.Scope.LibraryIds.ToArray());
}

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.TestSupport.Fakes;

/// <summary>
/// Mutable <see cref="IStationScopeProvider"/> double. Set <see cref="Scope"/> between calls to
/// simulate a live <c>IOptionsMonitor&lt;StationOptions&gt;</c> reload (SPEC F30.1) without
/// standing up a real options stack in a unit test.
/// </summary>
public sealed class FakeStationScopeProvider(LibraryScope scope) : IStationScopeProvider
{
    /// <summary>The library scope a spec can mutate between calls.</summary>
    public LibraryScope Scope { get; set; } = scope;

    /// <inheritdoc/>
    public LibraryScope Current => Scope;
}

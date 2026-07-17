using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests;

/// <summary>
/// Mutable <see cref="IStationScopeProvider"/> double. Set <see cref="Scope"/> between calls to
/// simulate a live <c>IOptionsMonitor&lt;StationOptions&gt;</c> reload (SPEC F30.1) without
/// standing up a real options stack in a unit test.
/// </summary>
sealed class FakeStationScopeProvider(LibraryScope scope) : IStationScopeProvider
{
    public LibraryScope Scope { get; set; } = scope;

    public LibraryScope Current => Scope;
}

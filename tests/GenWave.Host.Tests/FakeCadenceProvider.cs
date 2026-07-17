using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests;

/// <summary>
/// Mutable <see cref="ICadenceProvider"/> double (gitea-#211, mirrors <see cref="FakeStationScopeProvider"/>
/// one seam over). Set <see cref="Cadence"/> between calls to simulate a live
/// <c>IOptionsMonitor&lt;StationOptions&gt;</c> reload without standing up a real options stack in a
/// unit test.
/// </summary>
sealed class FakeCadenceProvider(CadenceConfig cadence) : ICadenceProvider
{
    public CadenceConfig Cadence { get; set; } = cadence;

    public CadenceConfig Current => Cadence;
}

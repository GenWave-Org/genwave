using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Mutable <see cref="IStationIdentityProvider"/> double (SPEC F44.1, gitea-#196, mirrors
/// <see cref="FakeCadenceProvider"/> one seam over). Set <see cref="Identity"/> between calls to
/// simulate a live <c>IOptionsMonitor&lt;StationOptions&gt;</c> reload without standing up a real
/// options stack in a unit test.
/// </summary>
sealed class FakeStationIdentityProvider(StationIdentity identity) : IStationIdentityProvider
{
    public StationIdentity Identity { get; set; } = identity;

    public StationIdentity Current => Identity;
}

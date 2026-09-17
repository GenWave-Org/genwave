using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.TestSupport.Fakes;

/// <summary>
/// Mutable <see cref="IStationIdentityProvider"/> double (SPEC F44.1, gitea-#196, mirrors
/// <see cref="FakeCadenceProvider"/> one seam over). Set <see cref="Identity"/> between calls to
/// simulate a live <c>IOptionsMonitor&lt;StationOptions&gt;</c> reload without standing up a real
/// options stack in a unit test.
/// </summary>
public sealed class FakeStationIdentityProvider(StationIdentity identity) : IStationIdentityProvider
{
    /// <summary>The station identity a spec can mutate between calls.</summary>
    public StationIdentity Identity { get; set; } = identity;

    /// <inheritdoc/>
    public StationIdentity Current => Identity;
}

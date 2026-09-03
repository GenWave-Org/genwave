using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Fakes;

/// <summary><see cref="IStationIdentityProvider"/> double for <see cref="AdRenderService"/> specs
/// (T401 review F1) — a fixed identity; nothing this project exercises needs live re-evaluation.</summary>
public sealed class FakeStationIdentityProvider(StationIdentity identity) : IStationIdentityProvider
{
    public StationIdentity Current => identity;
}

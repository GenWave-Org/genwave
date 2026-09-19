namespace GenWave.Tts.Tests.Fakes;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>Mutable <see cref="IStationIdentityProvider"/> double (STORY-456, PLAN T525) — this
/// project (GenWave.Tts.Tests) references only Core/Tts/Loudness, not GenWave.TestSupport, so this
/// is a local copy of the same shape TestSupport's own <c>FakeStationIdentityProvider</c> carries;
/// Ads.Tests and Host.Tests each carry their own copy for the same reason.</summary>
public sealed class FakeStationIdentityProvider(StationIdentity identity) : IStationIdentityProvider
{
    public StationIdentity Identity { get; set; } = identity;

    public StationIdentity Current => Identity;
}

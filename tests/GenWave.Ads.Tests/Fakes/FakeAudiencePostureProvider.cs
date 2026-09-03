using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Fakes;

/// <summary><see cref="IAudiencePostureProvider"/> double (PLAN T402) — a fixed, settable posture;
/// nothing this project's specs exercise needs live re-evaluation mid-test.</summary>
public sealed class FakeAudiencePostureProvider(AudiencePosture posture = AudiencePosture.Everyone) : IAudiencePostureProvider
{
    public AudiencePosture Current { get; set; } = posture;
}

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>An audience posture that never changes, for the real snapshot sources.</summary>
internal sealed class FixedAudiencePosture : IAudiencePostureProvider
{
    public AudiencePosture Current => AudiencePosture.Everyone;
}

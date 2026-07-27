using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Tests.Fakes;

/// <summary>
/// SPEC F95.4 — fixed-value <see cref="IAudiencePostureProvider"/> for repository specs. Defaults to
/// <see cref="AudiencePosture.Everyone"/> (the fail-closed default every pre-existing spec assumes, so
/// specs that never mention explicit classification see no behavior change); explicit-pool-exclusion
/// specs construct it with <see cref="AudiencePosture.Mature"/> to prove the posture flip.
/// </summary>
public sealed class FakeAudiencePostureProvider(AudiencePosture posture = AudiencePosture.Everyone)
    : IAudiencePostureProvider
{
    public AudiencePosture Current { get; } = posture;
}

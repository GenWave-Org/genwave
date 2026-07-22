using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Scripted <see cref="IPersonaPickProvider"/> double (SPEC F81.5/F81.6, STORY-212). <see cref="NextResult"/>
/// defaults to <see langword="null"/> — the ordinary "no persona opinion" outcome
/// <see cref="NoOpPersonaPickProvider"/> itself always returns. Set <see cref="ThrowOnPick"/> to
/// simulate a ranker fault/timeout (SPEC F81.6 rung 0); the Orchestrator must degrade to the
/// envelope-only ladder with a WARN rather than let the fault escape as a faulted pick.
/// </summary>
sealed class FakePersonaPickProvider : IPersonaPickProvider
{
    public RotationCandidate? NextResult { get; set; }
    public Exception? ThrowOnPick { get; set; }

    /// <summary>Every envelope this provider was called with, in call order.</summary>
    public List<SegmentEnvelope> Calls { get; } = [];

    public Task<RotationCandidate?> TryPickAsync(
        LibraryScope scope,
        IReadOnlyList<string> orderedRecentIds,
        int artistSeparation,
        SegmentEnvelope envelope,
        CancellationToken ct)
    {
        Calls.Add(envelope);
        if (ThrowOnPick is { } ex) throw ex;
        return Task.FromResult(NextResult);
    }
}

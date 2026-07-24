namespace GenWave.Orchestration;

using GenWave.Core.Domain;

/// <summary>
/// One resolved fulfillment (SPEC F87.6, STORY-227, PLAN T90): the candidate <see cref="Orchestrator"/>
/// should air immediately, the <c>station.request</c> row id it came from (for logging only — never a
/// wish-text carrier, F87.7/F87.8 discipline), and whether it resolved through the T89 catalog match
/// (<see cref="WasVibe"/> <see langword="false"/>) or the mood-machinery vibe path
/// (<see langword="true"/>).
/// </summary>
public sealed record RequestFulfillment(RotationCandidate Candidate, long RequestId, bool WasVibe);

namespace GenWave.Orchestration;

using GenWave.Abstractions.Playout;
using GenWave.Core.Domain;

/// <summary>
/// The default <see cref="IPersonaPickProvider"/> binding: always "no opinion" (SPEC F81.2 — playout
/// never depends on the persona layer existing). Registered via <c>TryAddSingleton</c>
/// (<see cref="OrchestrationServiceCollectionExtensions.AddGenWaveOrchestration"/>) so a future
/// module (PLAN T64, STORY-213) can bind a real ranker-backed implementation ahead of this one.
/// </summary>
public sealed class NoOpPersonaPickProvider : IPersonaPickProvider
{
    /// <summary>Shared instance for non-DI construction (tests).</summary>
    public static readonly NoOpPersonaPickProvider Instance = new();

    /// <inheritdoc/>
    public Task<RotationCandidate?> TryPickAsync(
        LibraryScope scope,
        IReadOnlyList<string> orderedRecentIds,
        int artistSeparation,
        SegmentEnvelope envelope,
        CancellationToken ct) =>
        Task.FromResult<RotationCandidate?>(null);
}

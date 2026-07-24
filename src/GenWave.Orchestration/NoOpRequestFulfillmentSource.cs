namespace GenWave.Orchestration;

using GenWave.Abstractions.Playout;

/// <summary>
/// The default <see cref="IRequestFulfillmentSource"/> binding: always "nothing to fulfill" — mirrors
/// <see cref="NoOpPersonaPickProvider"/>'s own precedent one rung up. Registered via
/// <c>TryAddSingleton</c> (<see cref="OrchestrationServiceCollectionExtensions.AddGenWaveOrchestration"/>)
/// so a host that wires the real listener-request pipeline (SPEC F87.6, STORY-227, PLAN T90) can bind
/// <see cref="RequestFulfillmentProvider"/> ahead of this one.
/// </summary>
public sealed class NoOpRequestFulfillmentSource : IRequestFulfillmentSource
{
    /// <summary>Shared instance for non-DI construction (tests).</summary>
    public static readonly NoOpRequestFulfillmentSource Instance = new();

    /// <inheritdoc/>
    public Task<RequestFulfillment?> TryFulfillAsync(SegmentEnvelope envelope, CancellationToken ct) =>
        Task.FromResult<RequestFulfillment?>(null);
}

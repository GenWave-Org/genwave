using GenWave.Abstractions.Playout;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Scripted <see cref="IRequestFulfillmentSource"/> double (SPEC F87.6, STORY-227, PLAN T90) — proves
/// <see cref="Orchestrator"/>'s OWN consultation order/short-circuit wiring (it asks this seam BEFORE
/// <see cref="IPersonaPickProvider"/>, and trusts a non-null result without re-running the envelope
/// ladder) without exercising <see cref="RequestFulfillmentProvider"/>'s own business logic — that
/// logic is proven separately, directly, against <see cref="FakeRequestStore"/>/
/// <see cref="FakeRequestCatalogProbe"/>. Mirrors <see cref="FakePersonaPickProvider"/>'s own shape one
/// rung up.
/// </summary>
sealed class FakeRequestFulfillmentSource : IRequestFulfillmentSource
{
    public RequestFulfillment? NextResult { get; set; }

    /// <summary>Every envelope this source was called with, in call order.</summary>
    public List<SegmentEnvelope> Calls { get; } = [];

    public Task<RequestFulfillment?> TryFulfillAsync(SegmentEnvelope envelope, CancellationToken ct)
    {
        Calls.Add(envelope);
        return Task.FromResult(NextResult);
    }
}

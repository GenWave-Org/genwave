namespace GenWave.Orchestration;

using GenWave.Abstractions.Playout;

/// <summary>
/// SPEC F87.6 (STORY-227, PLAN T90) — the fulfillment seam <see cref="Orchestrator"/> consults BEFORE
/// <see cref="IPersonaPickProvider"/>'s own rung 0: a live pending listener request short-circuits the
/// pick entirely, ahead of persona ranking and the envelope-only ladder alike.
///
/// <para>
/// Mirrors <see cref="IPersonaPickProvider"/>'s own shape and home one rung up: this interface lives
/// beside it in <c>GenWave.Orchestration</c> (not <c>GenWave.Abstractions</c>), because every
/// dependency <see cref="RequestFulfillmentProvider"/> needs
/// (<see cref="GenWave.Core.Abstractions.IRequestStore"/>, <see cref="GenWave.Core.Abstractions.IRequestCatalogProbe"/>,
/// <see cref="GenWave.Core.Abstractions.IRequestOverrideEnvelopeProvider"/>,
/// <see cref="GenWave.Core.Abstractions.IStationEventSink"/>) is already a Core/Abstractions seam — so
/// nothing here needs to reach into <c>GenWave.Host</c> or <c>GenWave.MediaLibrary</c>, and only
/// <see cref="Orchestrator"/> itself ever consumes this interface, exactly as only it consumes
/// <see cref="IPersonaPickProvider"/>.
/// </para>
///
/// <para>
/// <see cref="Orchestrator"/> wraps every call in try/catch: an implementation that throws degrades to
/// the normal pick chain (persona rung, then the envelope-only ladder) with one loud WARN, never a
/// faulted pick — the same posture <see cref="IPersonaPickProvider"/>'s own remarks document. A
/// <see langword="null"/> result is the ordinary "nothing live to fulfill right now" outcome and is
/// never itself logged as a degradation.
/// </para>
/// </summary>
public interface IRequestFulfillmentSource
{
    /// <summary>
    /// Attempts to resolve the oldest live pending request against the current envelope. A
    /// <see langword="null"/> result means no pending request exists, or none currently satisfies the
    /// active fulfillment policy — not an error.
    /// </summary>
    Task<RequestFulfillment?> TryFulfillAsync(SegmentEnvelope envelope, CancellationToken ct);
}

using GenWave.Abstractions.Playout;

namespace GenWave.Core.Abstractions;

/// <summary>
/// The default <see cref="IEnvelopeProvider"/> binding: always
/// <see cref="SegmentEnvelope.StationDefault"/> — 24/7, every genre, the full energy range (SPEC
/// F81.3). Exists so a caller constructed without a live options stack (a unit test, or a module
/// built before F81 shipped) never has to null-check, mirroring
/// <c>GenWave.Core.Events.NoOpStationEventSink</c>'s "shared instance for non-DI construction" idiom
/// one seam over. The Host binds the real <c>IOptionsMonitor&lt;StationOptions&gt;</c>-backed
/// implementation instead.
/// </summary>
public sealed class StationDefaultEnvelopeProvider : IEnvelopeProvider
{
    /// <summary>Shared instance for non-DI construction (Core types, tests).</summary>
    public static readonly StationDefaultEnvelopeProvider Instance = new();

    /// <inheritdoc/>
    public SegmentEnvelope Current => SegmentEnvelope.StationDefault;
}

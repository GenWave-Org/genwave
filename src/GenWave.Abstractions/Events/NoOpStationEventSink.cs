using GenWave.Core.Abstractions;

namespace GenWave.Core.Events;

/// <summary>
/// The default <see cref="IStationEventSink"/> binding: events vanish. Exists so publishers never
/// null-check and hosts/tests without a subscriber pay nothing.
/// </summary>
public sealed class NoOpStationEventSink : IStationEventSink
{
    /// <summary>Shared instance for non-DI construction (Core types, tests).</summary>
    public static readonly NoOpStationEventSink Instance = new();

    /// <inheritdoc/>
    public void Publish(StationEvent evt)
    {
    }
}

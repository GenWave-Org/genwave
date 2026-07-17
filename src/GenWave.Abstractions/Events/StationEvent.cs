namespace GenWave.Core.Events;

/// <summary>
/// Base type for the in-process domain events published at the station's choke points
/// (SPEC gap H2, gitea-#246): feeder advance, enricher flip, TTS render, settings write, admin writes.
/// Deliberately minimal — no bus, no outbox, no persistence. An analytics/audit module subscribes
/// by replacing or decorating the <see cref="Abstractions.IStationEventSink"/> binding.
/// </summary>
public abstract record StationEvent
{
    /// <summary>Producer-side timestamp, stamped at construction.</summary>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

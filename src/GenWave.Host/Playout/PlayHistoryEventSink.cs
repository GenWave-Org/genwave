using GenWave.Core.Abstractions;
using GenWave.Core.Events;

namespace GenWave.Host.Playout;

/// <summary>
/// The host's <see cref="IStationEventSink"/> binding (gitea-#246): forwards <see cref="TrackAired"/>
/// into <see cref="PlayHistoryService"/> — the side effect that used to ride the feeder's
/// single-cast <c>OnAdvance</c> callback — and ignores every other event (the no-op posture an
/// analytics/audit module replaces or decorates). Push is an in-memory ring write: never throws,
/// returns promptly, per the sink contract.
/// </summary>
sealed class PlayHistoryEventSink(PlayHistoryService history) : IStationEventSink
{
    public void Publish(StationEvent evt)
    {
        if (evt is TrackAired t)
        {
            history.Push(new PlayHistoryEntry(
                SingleStation.IdString, t.MediaId, t.Title, t.Artist, t.GainDb, t.StartedAt, null, t.DurationMs));
        }
    }
}

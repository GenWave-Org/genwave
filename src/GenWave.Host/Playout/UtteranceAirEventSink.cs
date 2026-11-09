using System.Threading.Channels;
using GenWave.Core.Abstractions;
using GenWave.Core.Events;

namespace GenWave.Host.Playout;

/// <summary>
/// SPEC F218.3 (STORY-532, PLAN T685) — joins a rendered utterance to the moment it is proven on
/// air ("stamp when proven"). <see cref="SegmentGenerated"/> (render) remembers
/// <c>tts:&lt;hash&gt; -> UtteranceId</c>; the feeder then resolves that id one of two ways, and
/// both hand the utterance id to <see cref="UtteranceAirDrainService"/>: the engine-confirmed
/// <see cref="TrackAired"/> (the observed start), or <see cref="TrackProvenAired"/> (a short line
/// that aired and departed between two polls, proven by a later observed sibling in the same engine
/// epoch). A render that is dropped never airs, so its entry never resolves and <c>aired_at</c>
/// stays null.
/// <para>
/// Latest render wins: the same cached <c>tts:&lt;hash&gt;</c> file can air again with a new utterance,
/// and a re-render replaces the previous mapping (and moves to the newest end of the eviction
/// order). An entry is consumed on air; unaired entries are bounded by <see cref="MaxPending"/>
/// (oldest evicted). Two pending renders of the same hash mean the first airing stamps the newer
/// utterance; this is accepted.
/// </para>
/// </summary>
sealed class UtteranceAirEventSink(
    ChannelWriter<UtteranceAirSignal> queue, ILogger<UtteranceAirEventSink> logger) : IStationEventSink
{
    internal const int MaxPending = 256;

    readonly object gate = new();
    // Insertion order IS eviction order: Remember moves a re-render to the end, TryTake removes.
    readonly OrderedDictionary<string, long> bySegment = new(StringComparer.Ordinal);

    public void Publish(StationEvent evt)
    {
        switch (evt)
        {
            case SegmentGenerated { UtteranceId: { } id } generated:
                Remember(generated.SegmentId, id);
                break;
            case TrackAired aired when TryTake(aired.MediaId, out var utteranceId):
                Enqueue(utteranceId);
                break;
            // A short line can air and depart between two feeder polls, so no TrackAired is ever
            // published for it; the feeder instead proves it aired when a later sibling is observed.
            // Treated exactly like TrackAired. The stamp therefore means "it aired", and for a
            // proven-late line the time is when it was proven, not its exact start (accepted).
            case TrackProvenAired proven when TryTake(proven.MediaId, out var provenUtteranceId):
                Enqueue(provenUtteranceId);
                break;
        }
    }

    void Enqueue(long utteranceId)
    {
        if (!queue.TryWrite(new UtteranceAirSignal(utteranceId)))
            logger.LogWarning("Utterance air queue full — dropping air stamp for utterance {UtteranceId}", utteranceId);
    }

    void Remember(string segmentId, long utteranceId)
    {
        lock (gate)
        {
            bySegment.Remove(segmentId);
            bySegment.Add(segmentId, utteranceId);

            while (bySegment.Count > MaxPending)
                bySegment.RemoveAt(0);
        }
    }

    bool TryTake(string mediaId, out long utteranceId)
    {
        lock (gate)
            return bySegment.Remove(mediaId, out utteranceId);
    }
}

using System.Threading.Channels;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;

namespace GenWave.Memory;

/// <summary>
/// SPEC F218.15 (STORY-536) — turns what the station does into the episodes a persona remembers. Three
/// things accrue and nothing else: a show the persona hosted (written when the show ends), a crosstalk (one
/// shared <c>memory_event</c> plus an episode per participant naming the other), and a context fact the
/// persona read on air (the copy the truth gate accepted used it).
/// <para>
/// <b>Where each signal comes from.</b> The show boundary is read off <see cref="IOnAirSnapshotSource"/> on each
/// <see cref="TrackAired"/>: a changed (or vanished) show id ends the run the consumer was watching. The
/// boundary is therefore noticed on the first track after it, which is the first track of the next show. The
/// crosstalk's two personas ride <see cref="TrackAired.CrosstalkScript"/> (stamped at stock time). The fact rides
/// <see cref="SegmentGenerated.FactRead"/> and is written when the segment with that id airs, never at render.
/// </para>
/// <para>
/// <see cref="Publish"/> only reads memory and writes a bounded queue, so it never throws and returns at once;
/// <see cref="RunAsync"/> drains the queue into <see cref="IEpisodeStore"/> on its own schedule. A full queue
/// drops the newest write and counts it in <see cref="DroppedWrites"/>.
/// </para>
/// </summary>
public sealed class EpisodeAccrualSink(
    IEpisodeStore store, IOnAirSnapshotSource onAir, IStationClockProvider stationClock) : IStationEventSink
{
    /// <summary>Most queued writes before the newest is dropped.</summary>
    public const int QueueCapacity = 256;

    /// <summary>Most rendered-but-not-yet-aired facts held; the oldest is forgotten past this (a segment that never airs).</summary>
    public const int PendingFactCapacity = 64;

    readonly Channel<EpisodeWrite> queue = Channel.CreateBounded<EpisodeWrite>(
        new BoundedChannelOptions(QueueCapacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    readonly object gate = new();
    readonly Dictionary<string, PendingFact> pendingFacts = new(StringComparer.Ordinal);
    readonly Queue<string> pendingFactOrder = new();
    ShowRun? current;
    bool observedATrack;
    long droppedWrites;

    /// <summary>Writes lost because the queue was full.</summary>
    public long DroppedWrites => Interlocked.Read(ref droppedWrites);

    public void Publish(StationEvent evt)
    {
        switch (evt)
        {
            case TrackAired aired:
                lock (gate)
                    OnTrackAired(aired);
                break;
            case SegmentGenerated { PersonaId: { } personaId, FactRead: { } fact } rendered:
                lock (gate)
                    HoldUntilAired(rendered.SegmentId, new PendingFact(personaId, fact));
                break;
        }
    }

    /// <summary>
    /// Drains queued writes into the store until <paramref name="ct"/> is cancelled or <see cref="Complete"/> is
    /// called and the queue empties. A store fault is handed to <paramref name="onFault"/> and the write is lost;
    /// playout never sees it.
    /// </summary>
    public async Task RunAsync(Action<Exception> onFault, CancellationToken ct)
    {
        await foreach (var write in queue.Reader.ReadAllAsync(ct))
        {
            try
            {
                await WriteAsync(write, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                onFault(ex);
            }
        }
    }

    /// <summary>Ends <see cref="RunAsync"/> once what is queued has been written.</summary>
    public void Complete() => queue.Writer.TryComplete();

    Task WriteAsync(EpisodeWrite write, CancellationToken ct) => write.Happening is { } happening
        ? store.AppendSharedAsync(happening, write.Episodes, ct)
        : store.AppendAsync(write.Episodes[0], ct);

    /// <summary>A fact is read when its segment airs, not when it renders: hold it, bounded, until the matching <see cref="TrackAired"/>.</summary>
    void HoldUntilAired(string segmentId, PendingFact fact)
    {
        if (pendingFacts.TryAdd(segmentId, fact))
            pendingFactOrder.Enqueue(segmentId);

        while (pendingFactOrder.Count > PendingFactCapacity)
            pendingFacts.Remove(pendingFactOrder.Dequeue());
    }

    void OnTrackAired(TrackAired aired)
    {
        // A null snapshot is "not resolved yet" (boot, before the schedule loads), not "no show": say nothing
        // about the show rather than end the run or count this track as watched. An unscheduled gap is a
        // non-null snapshot whose Show is null.
        if (onAir.Current is { } snapshot)
        {
            FollowShow(snapshot);
            observedATrack = true;
        }

        if (pendingFacts.Remove(aired.MediaId, out var fact))
            Enqueue(new(null, [new NewEpisode(fact.PersonaId, EpisodeWording.ReadFact(fact.Fact), aired.StartedAt)]));

        if (aired.SegmentKind is null)
            current?.CountTrack(EpisodeWording.Track(aired.Title, aired.Artist));
        else if (aired.SegmentKind == SegmentKind.Crosstalk && aired.CrosstalkScript is { Host: { } host, Neighbor: { } neighbor })
            Enqueue(Crosstalk(host, neighbor, aired.StartedAt));
    }

    static EpisodeWrite Crosstalk(CrosstalkParticipant host, CrosstalkParticipant neighbor, DateTimeOffset at) => new(
        new NewMemoryEvent(MemoryEventKinds.Crosstalk, EpisodeWording.CrosstalkSummary(host, neighbor), at),
        [
            new NewEpisode(host.PersonaId, EpisodeWording.TalkedWith(neighbor), at),
            new NewEpisode(neighbor.PersonaId, EpisodeWording.TalkedWith(host), at),
        ]);

    /// <summary>Ends the watched show when the on-air show id differs, then starts watching the new one.</summary>
    void FollowShow(OnAirSnapshot snapshot)
    {
        var show = snapshot.Show;
        if (current is { } run && run.ShowId == show?.Id)
            return;

        if (current is { PersonaId: { } hostId, TrackCount: > 0, Partial: false } ended)
            Enqueue(new(null, [new NewEpisode(hostId, EpisodeWording.HostedShow(ended), ended.StartedAt)]));

        current = snapshot is { Segment: { } segment, Show: { } startedShow }
            ? new ShowRun(startedShow.Id, snapshot.PersonaId, startedShow.Name, ScheduledStart(segment), partial: !observedATrack)
            : null;
    }

    /// <summary>When <paramref name="segment"/> began: its start minute on the station-local day, the day before when that is still ahead (it crossed midnight).</summary>
    DateTimeOffset ScheduledStart(ScheduleSegment segment)
    {
        var local = stationClock.LocalNow;
        var start = local.Date.AddMinutes(segment.StartMinute);
        if (start > local.DateTime)
            start = start.AddDays(-1);
        return new DateTimeOffset(start, stationClock.Zone.GetUtcOffset(start));
    }

    sealed record PendingFact(long PersonaId, FactReadOnAir Fact);

    void Enqueue(EpisodeWrite write)
    {
        if (!queue.Writer.TryWrite(write))
            Interlocked.Increment(ref droppedWrites);
    }
}

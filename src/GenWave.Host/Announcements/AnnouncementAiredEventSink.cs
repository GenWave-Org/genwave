using System.Threading.Channels;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;

namespace GenWave.Host.Announcements;

/// <summary>
/// The host's <see cref="IStationEventSink"/> binding for the announcement lifecycle's own aired
/// confirmation (SPEC F143.3, STORY-358, PLAN T343) — mirrors
/// <c>GenWave.Host.Playout.CrosstalkRetirementEventSink</c>'s own "forward <see cref="TrackAired"/>,
/// ignore everything else" shape one seam over, with one structural difference: the actual work here
/// is a genuine async Postgres write (<see cref="IAnnouncementLifecycle.MarkAiredAsync"/>) plus a
/// booth_log append, neither of which <see cref="Publish"/> can perform itself —
/// <see cref="IStationEventSink"/>'s own contract ("MUST NOT throw and MUST return promptly") is the
/// SAME reason <c>GenWave.MediaLibrary.Station.BoothLogWriter</c> queues rather than writes. This sink
/// only ever does the cheap, synchronous part — the <see cref="SegmentKind.Announcement"/> filter and
/// <see cref="AnnouncementMediaId.TryUnwrap"/> — and hands the extracted id to
/// <see cref="AnnouncementAiredDrainService"/> via a bounded queue, mirroring
/// BoothLogWriter/BoothLogDrainService's own split one seam over.
///
/// <para>
/// The gh-#612 lesson (ARCHITECTURE.md): aired is stamped ONLY on this genuine, engine-confirmed
/// <see cref="TrackAired"/> observation of the announcement's OWN segment — never on push/vend alone.
/// A push that never airs (a lost advance, a process restart mid-flight) simply never reaches
/// <see cref="Publish"/> at all; the row stays <c>claimed</c> until
/// <see cref="AnnouncementLifecycleGuardianService"/>'s own re-arm sweep (SPEC F144.5) catches it.
/// </para>
/// </summary>
sealed class AnnouncementAiredEventSink(
    ChannelWriter<AnnouncementAiredSignal> queue, ILogger<AnnouncementAiredEventSink> logger) : IStationEventSink
{
    public void Publish(StationEvent evt)
    {
        if (evt is not TrackAired { SegmentKind: SegmentKind.Announcement } t) return;
        if (!AnnouncementMediaId.TryUnwrap(t.MediaId, out var announcementId)) return;

        if (!queue.TryWrite(new AnnouncementAiredSignal(announcementId)))
        {
            logger.LogWarning(
                "Announcement aired-confirmation queue full — dropping confirmation for announcement {AnnouncementId}",
                announcementId);
        }
    }
}

using System.Globalization;
using System.Threading.Channels;
using GenWave.Core.Abstractions;
using GenWave.Core.Events;

namespace GenWave.Host.Playout;

/// <summary>
/// The host's <see cref="IStationEventSink"/> binding for the rotation ledger (SPEC F149.2,
/// STORY-367, PLAN T355) — mirrors <c>GenWave.Host.Announcements.AnnouncementAiredEventSink</c>'s
/// own "cheap synchronous filter, hand off to a bounded queue" shape one seam over:
/// <see cref="IMediaRotationSink.RecordAiringAsync"/> is a genuine async Postgres write, which
/// <see cref="Publish"/> can never perform itself (<see cref="IStationEventSink"/>'s own "MUST NOT
/// throw and MUST return promptly" contract).
///
/// <b>Music discrimination reused verbatim, never re-invented here — but it is only PART of the real
/// filter.</b> <see cref="TrackAired.SegmentKind"/> is <see langword="null"/> for every music row AND
/// every engine-initiated advance (<see cref="TrackAired.SegmentKind"/>'s own remarks) — never for a
/// TTS-kind item (idents/patter/crosstalk/announcements always carry a specific
/// <see cref="SegmentKind"/>, stamped by the feeder off the pushed <c>MediaItem</c>) — the same
/// predicate <c>CrosstalkRetirementEventSink</c>/<c>AnnouncementAiredEventSink</c> match a SPECIFIC
/// non-null kind against. A music row's own MediaId is always the bare numeric catalog id (never a
/// <c>"tts:*"</c>-prefixed synthetic one, exactly the parse <c>BoothLogWriter.ParseMediaId</c> already
/// performs), so the <see cref="long.TryParse(string?, NumberStyles, IFormatProvider?, out long)"/>
/// below is the belt this codebase's other two SegmentKind-filtered sinks don't need.
///
/// <para>
/// <b>The gh-#99 safe loop IS a null-SegmentKind, numeric-id event — today, not hypothetically.</b>
/// <c>GET /internal/safe-track</c> serves a real <c>library.media</c> row from
/// <c>Station:SafeScope:LibraryIds</c> with a bare numeric <c>track_id</c>; <c>PlayoutFeeder</c> stamps
/// its <see cref="TrackAired.SegmentKind"/> null exactly like a genuine music advance (it is not a
/// pushed TTS <c>MediaItem</c>). Both filters below pass it. This sink CANNOT distinguish "safe loop"
/// from "music" — that would need an async library-membership read this hot-path method must never
/// perform (<see cref="IStationEventSink"/>'s own "MUST NOT throw and MUST return promptly" contract)
/// — so the exclusion lives one seam downstream instead: <see cref="IMediaRotationSink.RecordAiringAsync"/>'s
/// own implementation (<c>MediaRotationRepository</c>) applies the gh-#99 safe-scope carve-out at write
/// time, the same way <c>MediaRatingRepository</c> already does for votes/never-play.
/// </para>
/// </summary>
sealed class MediaRotationEventSink(
    ChannelWriter<MediaRotationAiredSignal> queue, ILogger<MediaRotationEventSink> logger) : IStationEventSink
{
    public void Publish(StationEvent evt)
    {
        if (evt is not TrackAired { SegmentKind: null } t) return;
        if (!long.TryParse(t.MediaId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mediaId)) return;

        if (!queue.TryWrite(new MediaRotationAiredSignal(mediaId, t.StartedAt)))
            logger.LogWarning("Rotation ledger queue full — dropping airing for media {MediaId}", mediaId);
    }
}

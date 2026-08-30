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
/// <b>Music discrimination via <see cref="MusicAiring.TryReadMusicAiring"/> (PLAN T358 review
/// MED-2) — shared with <see cref="AiringTokenRing"/>, never re-derived here — but it is only PART
/// of the real filter.</b> See that method's own remarks for the SegmentKind/numeric-id rule and
/// the gh-#99 safe-loop caveat it cannot see through.
///
/// <para>
/// <b>The gh-#99 safe loop passes that same predicate — today, not hypothetically — and this sink
/// CANNOT distinguish "safe loop" from "music."</b> Telling them apart would need an async
/// library-membership read this hot-path method must never perform (<see cref="IStationEventSink"/>'s
/// own "MUST NOT throw and MUST return promptly" contract) — so the exclusion lives one seam
/// downstream instead: <see cref="IMediaRotationSink.RecordAiringAsync"/>'s own implementation
/// (<c>MediaRotationRepository</c>) applies the gh-#99 safe-scope carve-out at write time, the same
/// way <c>MediaRatingRepository</c> already does for votes/never-play.
/// </para>
/// </summary>
sealed class MediaRotationEventSink(
    ChannelWriter<MediaRotationAiredSignal> queue, ILogger<MediaRotationEventSink> logger) : IStationEventSink
{
    public void Publish(StationEvent evt)
    {
        if (!MusicAiring.TryReadMusicAiring(evt, out var mediaId, out var startedAt)) return;

        if (!queue.TryWrite(new MediaRotationAiredSignal(mediaId, startedAt)))
            logger.LogWarning("Rotation ledger queue full — dropping airing for media {MediaId}", mediaId);
    }
}

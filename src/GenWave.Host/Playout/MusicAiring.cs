using System.Globalization;
using GenWave.Core.Events;

namespace GenWave.Host.Playout;

/// <summary>
/// The ONE music-vs-non-music discrimination rule every <see cref="TrackAired"/> consumer that
/// cares about "was this a real music row" reads through — extracted here (SPEC F149.2/F149.4,
/// PLAN T355/T358 review MED-2) so <see cref="MediaRotationEventSink"/> and
/// <see cref="AiringTokenRing"/> share ONE implementation instead of two independently-maintained
/// copies of the same predicate.
/// </summary>
static class MusicAiring
{
    /// <summary>
    /// True when <paramref name="evt"/> is a music <see cref="TrackAired"/>:
    /// <see cref="TrackAired.SegmentKind"/> is <see langword="null"/> AND
    /// <see cref="TrackAired.MediaId"/> parses as a bare numeric catalog id (never a
    /// <c>"tts:*"</c>-prefixed synthetic one). <see cref="TrackAired.SegmentKind"/> is null for
    /// every music row AND every engine-initiated advance — never for a TTS-kind item
    /// (idents/patter/crosstalk/announcements always carry a specific
    /// <see cref="GenWave.Core.Domain.SegmentKind"/>, stamped by the feeder off the pushed
    /// <c>MediaItem</c>) — the same predicate <c>CrosstalkRetirementEventSink</c>/
    /// <c>AnnouncementAiredEventSink</c> match a SPECIFIC non-null kind against.
    /// <para>
    /// <b>The gh-#99 safe loop IS a null-SegmentKind, numeric-id event — today, not
    /// hypothetically.</b> <c>GET /internal/safe-track</c> serves a real <c>library.media</c> row
    /// from <c>Station:SafeScope:LibraryIds</c> with a bare numeric <c>track_id</c>;
    /// <c>PlayoutFeeder</c> stamps its <see cref="TrackAired.SegmentKind"/> null exactly like a
    /// genuine music advance (it is not a pushed TTS <c>MediaItem</c>). This predicate CANNOT
    /// distinguish "safe loop" from "music" — that would need an async library-membership read
    /// neither caller's synchronous, must-return-promptly <see cref="GenWave.Core.Abstractions.IStationEventSink"/>
    /// seam may perform — so each caller's own exclusion (or deliberate non-exclusion) lives one
    /// seam downstream instead: <see cref="MediaRotationEventSink"/>'s own remarks explain the
    /// rotation ledger's write-time carve-out (<c>MediaRotationRepository</c>, SPEC F149.2
    /// amendment); <see cref="AiringTokenRing"/>'s own remarks explain why the token has no
    /// equivalent downstream write to defer to (SPEC F149.4, PLAN T358).
    /// </para>
    /// </summary>
    public static bool TryReadMusicAiring(StationEvent evt, out long mediaId, out DateTimeOffset startedAt)
    {
        if (evt is TrackAired { SegmentKind: null } aired
            && long.TryParse(aired.MediaId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            mediaId = parsed;
            startedAt = aired.StartedAt;
            return true;
        }

        mediaId = 0;
        startedAt = default;
        return false;
    }

    /// <summary>
    /// The numeric-id half of <see cref="TryReadMusicAiring"/>'s own rule, applied where no
    /// <see cref="TrackAired.SegmentKind"/> is available — <see cref="GenWave.Core.Playout.OnAirState"/>
    /// carries no such field (see <see cref="PlayoutFeederService"/>'s own remarks). True when
    /// <paramref name="mediaId"/> is shaped like a music item: never <see langword="null"/> (a
    /// drain) and never <c>tts:*</c>-prefixed (patter/ident/crosstalk/announcement) — a bare
    /// numeric catalog id. Used to gate <see cref="NowPlayingSnapshot.Airing"/> so that record's
    /// "null for non-music" contract holds BY CONSTRUCTION, never merely by convention.
    /// </summary>
    public static bool IsMusicMediaId(string? mediaId) =>
        long.TryParse(mediaId, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
}

using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using GenWave.Core.Abstractions;
using GenWave.Core.Events;

namespace GenWave.Host.Playout;

/// <summary>
/// Mints a 128-bit, base64url airing token per real music advance (SPEC F149.4, F150.4, STORY-369,
/// PLAN T358) and remembers the CURRENT and the immediately PREVIOUS airing only — older entries
/// are evicted. The token is deliberately NOT an id: opaque, random, unguessable, unique per
/// airing, and meaningless off this box (it cannot be reverse-mapped to a catalog id by
/// inspection, unlike the artwork token's per-track-forever shape — SPEC F149.4's own "rejected:
/// the artwork token" note). Registered as an <see cref="IStationEventSink"/> member of
/// <see cref="CompositeStationEventSink"/> (<see cref="PlayoutServiceCollectionExtensions"/>) —
/// shares <see cref="MusicAiring.TryReadMusicAiring"/> with <see cref="MediaRotationEventSink"/>
/// (PLAN T358 review MED-2 — ONE discrimination rule, not two independently-maintained copies), so
/// a music row (never TTS) mints a token while an ident/patter/crosstalk/announcement (always a
/// non-null SegmentKind) never does.
///
/// <para>
/// <b>Token↔snapshot consistency, by construction.</b> <see cref="Publish"/> runs synchronously
/// inside <see cref="GenWave.Core.Playout.PlayoutFeeder.ObserveAsync"/>, strictly BEFORE that same
/// call publishes <see cref="GenWave.Core.Playout.OnAirState"/> — itself strictly before
/// <see cref="PlayoutFeederService"/>'s own snapshot publish reads <see cref="Current"/> and stamps
/// it onto the SAME <see cref="NowPlayingSnapshot"/> record that carries the airing's title/artist/
/// startedAt (one immutable record, one atomic dictionary write — see
/// <see cref="NowPlayingService.Update"/>). One thread, one tick, no interleaving (the feeder's
/// <c>PeriodicTimer</c> loop never runs two ticks concurrently), so by the time any HTTP reader
/// calls <see cref="NowPlayingService.GetSnapshot"/> the token it observes was baked into the SAME
/// record as the track it names — there is no seam where the two could be read, or written,
/// independently of one another. A payload naming track B's title beside track A's token cannot
/// happen.
/// </para>
///
/// <para>
/// <b>Safe-loop caveat (deliberate, matches the F149.2 precedent).</b> Like
/// <see cref="MediaRotationEventSink"/>'s own documented limitation, this sink cannot distinguish a
/// safe-loop airing (a real <c>library.media</c> row with a numeric id and a null
/// <see cref="GenWave.Core.Domain.SegmentKind"/> — the one non-music shape the kind/id guard cannot
/// see) from genuine music: telling them apart needs an async library-membership read this
/// synchronous, must-return-promptly seam (<see cref="IStationEventSink"/>'s own contract) cannot
/// perform. Unlike the rotation ledger — whose F149.2 amendment moved the exclusion into
/// <c>MediaRotationRepository</c>'s own async write, where <see cref="ISafeScopeProvider"/> is
/// genuinely affordable — a listener-facing token mints here with no downstream write of its own to
/// defer the check to; the "nobody thumbs Please Stand By" invariant belongs to the thumb WRITE
/// path instead (PLAN T366), which — like <c>MediaRatingRepository</c>/<c>MediaRotationRepository</c>
/// — has real async DB access and can apply the exact same <see cref="ISafeScopeProvider"/>
/// exclusion there. A safe-loop airing therefore DOES carry a token on now-playing today; it simply
/// resolves to nothing thumbable once T366 lands.
/// </para>
/// </summary>
sealed class AiringTokenRing : IStationEventSink, IAiringTokenResolver
{
    /// <summary>128 bits (SPEC F149.4).</summary>
    const int TokenBytes = 16;

    readonly object gate = new();
    AiringTokenEntry? current;
    AiringTokenEntry? previous;

    /// <summary>
    /// The last music airing's token — <b>survives an intervening non-music item</b> (SPEC F150.4's
    /// grace: an ident/patter airing between two tracks never clears this), so it stays whatever a
    /// real music <see cref="Publish"/> last set it to. This is deliberately NOT the same as "is
    /// music currently on air": <see cref="PlayoutFeederService.PublishSnapshot"/> is the seam that
    /// suppresses <see cref="NowPlayingSnapshot.Airing"/> to null while a non-music item is airing —
    /// see that method's own remarks — this property alone does not encode "on air right now".
    /// </summary>
    public string? Current
    {
        get { lock (gate) return current?.Token; }
    }

    /// <summary>
    /// Mints a fresh token for a qualifying music advance — <see cref="MusicAiring.TryReadMusicAiring"/>
    /// (SPEC F149.2 PLAN T355 review MED-2: the ONE shared discrimination rule, never re-derived
    /// per sink). Never throws (satisfies <see cref="IStationEventSink"/>'s own contract): a
    /// non-music event, or a music-shaped one whose id fails to parse, is silently ignored — the
    /// CURRENT/PREVIOUS entries are left exactly as they were.
    /// </summary>
    public void Publish(StationEvent evt)
    {
        if (!MusicAiring.TryReadMusicAiring(evt, out var mediaId, out var startedAt)) return;

        // Never plain base64 ('+'/'/' are not URL-safe) — WebEncoders.Base64UrlEncode is the same
        // unpadded, url-safe helper AnnouncementTokenController already mints a token through.
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));

        lock (gate)
        {
            previous = current;
            current = new AiringTokenEntry(token, mediaId, startedAt);
        }
    }

    public bool TryResolve(string token, out long mediaId, out DateTimeOffset startedAt)
    {
        lock (gate)
        {
            if (current is { } c && c.Token == token)
            {
                mediaId = c.MediaId;
                startedAt = c.StartedAt;
                return true;
            }

            if (previous is { } p && p.Token == token)
            {
                mediaId = p.MediaId;
                startedAt = p.StartedAt;
                return true;
            }
        }

        mediaId = 0;
        startedAt = default;
        return false;
    }

    /// <summary>One ring slot: the minted token and the (mediaId, startedAt) it names. Constant-time
    /// comparison is unnecessary here (SPEC F150.3's own "no oracle" posture is about the RESPONSE
    /// shape, not timing — tokens are random, not secrets to be guessed one byte at a time) — but a
    /// token is never written to a log line anywhere in this type.</summary>
    sealed record AiringTokenEntry(string Token, long MediaId, DateTimeOffset StartedAt);
}

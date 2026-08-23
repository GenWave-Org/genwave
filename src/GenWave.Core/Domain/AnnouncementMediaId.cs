using System.Globalization;

namespace GenWave.Core.Domain;

/// <summary>
/// Wraps/unwraps an owner announcement's row id onto the MediaId a rendered verbatim segment
/// carries onto air (SPEC F144.1, STORY-358, PLAN T341) — the gh-#612 "pushed ≠ aired" lesson
/// means the id must survive claim → <c>TrackAired</c> for a later task's own aired-stamp, and
/// MediaId is the one string that already crosses the ENTIRE pipeline unmodified (the feeder's
/// push metadata, the engine's own push/pull echo, <c>TrackAired.MediaId</c>) with no member
/// added anywhere along that path.
///
/// <para>
/// Mirrors the crosstalk precedent one sub-namespace over: <c>Orchestrator.EnqueuePatterAsync</c>
/// already mints <c>tts:crosstalk:{asset}</c> for a vended exchange — extending the SAME
/// <c>tts:</c> id convention with a second segment, rather than inventing a parallel identity
/// scheme or a stateful Host-side lookup table, keeps this a pure, dependency-free string
/// operation any layer can call.
/// </para>
///
/// <para>
/// <b>T343's own job becomes a lookup:</b> given a <c>TrackAired.MediaId</c>,
/// <see cref="TryUnwrap"/> answers "was this an announcement, and if so, which row" in one call —
/// no registry to keep in sync, no state that can outlive (or fall behind) the process.
/// </para>
/// </summary>
public static class AnnouncementMediaId
{
    const string Prefix = "tts:announcement:";

    /// <summary>
    /// Wraps a freshly-rendered segment's own MediaId (whatever <c>IVerbatimSegmentRenderer</c>
    /// minted, e.g. <c>tts:{hash}</c>) with the announcement id that produced it. Called once, by
    /// the vend caller, immediately after a successful render — never by the renderer itself, which
    /// has no reason to know an announcement is even involved (see <c>IVerbatimSegmentRenderer</c>'s
    /// own remarks).
    ///
    /// <b>Formats <paramref name="announcementId"/> via <see cref="CultureInfo.InvariantCulture"/>
    /// explicitly (PLAN T343 review carry-forward), not a bare interpolation left to the ambient
    /// current culture.</b> This id round-trips through <see cref="TryUnwrap"/> (below), which now
    /// parses with the SAME invariant culture and <see cref="NumberStyles.None"/> — the symmetric,
    /// belt-and-braces choice: <c>Wrap</c> writes exactly the digit shape <c>TryUnwrap</c> demands,
    /// rather than relying on plain <see langword="long"/> interpolation happening to already agree
    /// with it on every culture this process could ever run under.
    /// </summary>
    public static string Wrap(long announcementId, string renderedMediaId) =>
        $"{Prefix}{announcementId.ToString(CultureInfo.InvariantCulture)}:{renderedMediaId}";

    /// <summary>
    /// Recovers the announcement id from a MediaId <see cref="Wrap"/> produced. Returns
    /// <see langword="false"/> for any other shape — every non-announcement segment (plain
    /// <c>tts:{hash}</c> renders, <c>tts:crosstalk:*</c>, music) included — so a caller never needs
    /// a separate "is this even an announcement" check first.
    ///
    /// <para>
    /// <b>Tightened (PLAN T343 review carry-forward) now that this lookup is load-bearing (SPEC
    /// F143.3 — the aired stamp itself hangs off this unwrap succeeding).</b> Two leniencies T341
    /// shipped as harmless — because nothing downstream trusted the result yet — are closed:
    /// </para>
    /// <list type="bullet">
    /// <item><description>The id span parses with <see cref="NumberStyles.None"/> under
    /// <see cref="CultureInfo.InvariantCulture"/> — no leading sign, no leading/trailing whitespace,
    /// no thousands separator, no decimal point. <c>Wrap</c> never produces any of those; a MediaId
    /// carrying one is malformed, not a valid-but-unusual id.</description></item>
    /// <item><description>A bare <c>tts:announcement:{id}</c> with no trailing
    /// <c>:{renderedMediaId}</c> segment now fails outright rather than falling back to treating the
    /// whole remainder as the id. <c>Wrap</c> ALWAYS appends the inner id — a two-part MediaId can
    /// only mean a shape <c>Wrap</c> never produced, so tolerating it risked silently accepting a
    /// hand-crafted or truncated id as genuine.</description></item>
    /// </list>
    /// </summary>
    public static bool TryUnwrap(string mediaId, out long announcementId)
    {
        announcementId = 0;
        if (!mediaId.StartsWith(Prefix, StringComparison.Ordinal)) return false;

        var rest = mediaId.AsSpan(Prefix.Length);
        var separator = rest.IndexOf(':');
        if (separator < 0) return false;

        var idSpan = rest[..separator];
        return long.TryParse(idSpan, NumberStyles.None, CultureInfo.InvariantCulture, out announcementId);
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GenWave.Core.Domain;

/// <summary>
/// Content fingerprint of a stored schedule week (gh-#255): the opaque token
/// <c>GET/PUT /api/schedule</c> use for optimistic concurrency, so a stale editor (a second tab, a
/// long-lived session, a restored page) can never silently wipe a week someone else — or the same
/// operator in another view — saved after that editor loaded. Demonstrated live on the demo box:
/// a full-replace PUT built from stale state destroyed six just-saved segments with no error
/// anywhere (Loki, 2026-07-28 12:59:47 → 13:00:30, segmentCount 54 → 48).
///
/// <para>
/// Pure CONTENT hash: SHA-256 over an invariant-culture rendering of every segment's
/// day/start-minute/end-minute/persona-id/genres/energy-min/energy-max, ordered by day then start
/// minute. Row ids are deliberately EXCLUDED — <c>ReplaceWeekAsync</c> is delete-then-insert, so ids
/// churn on every write even when the content is identical; a version that changed with them would
/// 409 an editor whose grid still matches the stored week exactly. Two weeks with the same content
/// are, for staleness purposes, the same week.
/// </para>
///
/// <para>
/// <b>PLAN T243 — <see cref="ScheduleSegment.ShowId"/> now rides this hash.</b> Show assignment
/// stopped being read-only the moment <c>IScheduleStore.AssignShowAsync</c> shipped: a concurrent
/// show-assignment write changes <c>show_id</c> on one or more rows exactly the way a concurrent
/// <see cref="ReplaceWeekAsync"/> changes <c>persona_id</c>, and a stale full-replace built before
/// that assignment landed would silently overwrite it — precisely the gh-#255 save-loss class this
/// type exists to prevent, now extended to the field this remarks paragraph used to name as the gap.
/// <see cref="ScheduleSegment.ShowId"/> is hashed — deliberately NOT <see cref="ScheduleSegment.Show"/>'s
/// own <c>Name</c>/<c>Tagline</c>/<c>Flavor</c>: those are <c>station.show</c>'s own entity fields,
/// resolved by the LOAD-time LEFT JOIN (SPEC F116.1) rather than written by
/// <see cref="ReplaceWeekAsync"/> or <c>AssignShowAsync</c> — renaming a show elsewhere via
/// <c>ShowsController</c> must never itself 409 an unrelated schedule editor whose grid never touched
/// that show's identity at all. <c>ShowId</c> is the one field that is actually WRITTEN into
/// <c>segment_schedule.show_id</c> (see <see cref="ScheduleSegment"/>'s own remarks on why it, and not
/// <c>Show?.Id</c>, is the field every writer and this hash agree on) — the same reasoning
/// <see cref="ScheduleSegment.PersonaId"/>'s own inclusion above already rests on.
/// </para>
///
/// <para>
/// <b>What actually changed, precisely.</b> Only ONE thing about this method's output changed for
/// T243: a trailing <c>show_id</c> field was appended to each segment's rendering (immediately after
/// <c>energy_max</c>, the same position <see cref="ScheduleSegment.ShowId"/> occupies in the record).
/// The field ordering and <c>:</c>/<c>\n</c> separator layout are UNCHANGED — no reshuffle happened
/// alongside this addition. Every fingerprint this method previously produced for a week with at least
/// one segment therefore changes shape (the new field is always present in the rendering, even when its
/// value is the same "<c>-</c>" placeholder every other absent-optional field already uses). The ONE
/// exception: <see cref="Compute"/>'s empty-week fingerprint — the loop renders nothing when
/// <paramref name="segments"/> is empty, so <c>Compute([])</c>'s output is byte-identical before and
/// after this change. A tab or session that loaded its <c>BaseVersion</c> from a non-empty week before
/// this shipped will get exactly ONE <see cref="ScheduleReplaceResult.VersionConflict"/> 409 on its very
/// next <see cref="ReplaceWeekAsync"/> call — <c>ScheduleController</c>'s existing
/// <c>StaleWeekProblem</c> 409 already tells that operator to reload, and the reload fetches a document
/// carrying the new field, so every write after that one reload compares cleanly again. This is the
/// intended, acceptable cost of widening what the guard protects — not a bug, and not something a
/// rolling/zero-downtime deploy needs to sequence around: the guard fails CLOSED (a 409, never a silent
/// overwrite) for the one request that straddles the shape change.
/// </para>
/// </summary>
public static class ScheduleWeekVersion
{
    /// <summary>Computes the fingerprint for <paramref name="segments"/> — input order does not
    /// matter (segments are ordered by day/start internally), so a caller may pass a snapshot's
    /// already-ordered rows or a freshly built submission alike.</summary>
    public static string Compute(IReadOnlyList<ScheduleSegment> segments)
    {
        var builder = new StringBuilder();
        foreach (var seg in segments.OrderBy(s => s.Day).ThenBy(s => s.StartMinute).ThenBy(s => s.EndMinute))
        {
            builder
                .Append((int)seg.Day).Append(':')
                .Append(seg.StartMinute).Append(':')
                .Append(seg.EndMinute).Append(':')
                .Append(seg.PersonaId?.ToString(CultureInfo.InvariantCulture) ?? "-").Append(':')
                .Append(seg.Genres is null ? "-" : string.Join(',', seg.Genres)).Append(':')
                .Append(seg.EnergyMin?.ToString("R", CultureInfo.InvariantCulture) ?? "-").Append(':')
                .Append(seg.EnergyMax?.ToString("R", CultureInfo.InvariantCulture) ?? "-").Append(':')
                .Append(seg.ShowId?.ToString(CultureInfo.InvariantCulture) ?? "-").Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}

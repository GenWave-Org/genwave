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
/// <see cref="ScheduleSegment.Show"/> (SPEC F116.1, PLAN T241) is ALSO deliberately excluded — show
/// assignment is read-only through this epic, so this hash has nothing to protect yet. T243's writer
/// (the first caller that lets an editor SET <c>Show</c>) must add it to the fields rendered here, or
/// this staleness guard goes blind to concurrent show-assignment writes — the exact gh-#255
/// save-loss class this type exists to prevent, just for a field it doesn't yet know about.
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
                .Append(seg.EnergyMax?.ToString("R", CultureInfo.InvariantCulture) ?? "-").Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}

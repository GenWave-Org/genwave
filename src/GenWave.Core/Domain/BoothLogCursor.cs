namespace GenWave.Core.Domain;

/// <summary>
/// Opaque keyset-paging cursor over <c>station.booth_log</c>'s <c>(occurred_at, id)</c> newest-first
/// ordering (SPEC F72.2, STORY-195) — no OFFSET, so a row inserted while an operator pages through the
/// feed can never shift an already-served page (the classic offset-paging bug). <see cref="ToString"/>
/// and <see cref="TryParse"/> round-trip the same plain <c>"ticks_id"</c> encoding the admin
/// endpoint's <c>?before=</c> query parameter and <c>nextBefore</c> response field both use.
/// </summary>
public sealed record BoothLogCursor(DateTime OccurredAt, long Id)
{
    /// <inheritdoc/>
    public override string ToString() => $"{OccurredAt.Ticks}_{Id}";

    /// <summary>Parses a cursor previously produced by <see cref="ToString"/>. False for anything else.</summary>
    public static bool TryParse(string? value, out BoothLogCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var parts = value.Split('_', 2);
        if (parts.Length != 2) return false;
        if (!long.TryParse(parts[0], out var ticks)) return false;
        if (!long.TryParse(parts[1], out var id)) return false;

        cursor = new BoothLogCursor(new DateTime(ticks, DateTimeKind.Utc), id);
        return true;
    }
}

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The <c>station.show</c> LEFT JOIN column list shared by <see cref="ScheduleRepository"/> (the
/// weekly grid) and <see cref="SpecialsRepository"/> (both its own load query and its
/// insert-then-join CTE) — SPEC F116.1's show-identity projection plus the ONE <c>envelope</c> key
/// SPEC F152.3/PLAN T360 wakes (<c>rotation</c>). Aliased <c>show_*</c> so both repositories' own
/// Dapper row types (<c>MatchNamesWithUnderscores</c>) bind it identically. Extracted once (PLAN T360
/// review LOW-4 — this exact six-column list used to be typed out three separate times, one per query)
/// so a future show-identity column change never risks the three literals drifting apart.
/// </summary>
static class ScheduleShowJoinColumns
{
    public const string Select =
        "sh.id::bigint as show_id, sh.name as show_name, sh.slug as show_slug, " +
        "sh.tagline as show_tagline, sh.flavor as show_flavor, sh.envelope ->> 'rotation' as show_rotation_json";
}

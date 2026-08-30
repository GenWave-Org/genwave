namespace GenWave.MediaLibrary.Garden;

/// <summary>
/// Flat Dapper projection of <see cref="MediaRotationRepository.GetRotationHealthAsync"/>'s own
/// grouped <c>count(*) filter (where ...)</c> query (SPEC F149.5, STORY-368, PLAN T371) — mirrors
/// <c>Catalog.MediaRow</c>'s own "one settable-property class per query shape" convention (T371
/// review LOW-5: a bare positional value tuple is not itself a house type, one type per file). Column
/// names are snake_case (<c>never_aired</c>, <c>aired_once</c>, <c>not_aired_days90</c>); Dapper's
/// global <c>MatchNamesWithUnderscores</c> maps them onto these properties the same way it already
/// maps <c>xmin::text as xmin</c> onto <c>MediaRow.Xmin</c>.
/// </summary>
sealed class RotationHealthCountsRow
{
    public long Playable { get; set; }
    public long NeverAired { get; set; }
    public long AiredOnce { get; set; }
    public long NotAiredDays90 { get; set; }
}

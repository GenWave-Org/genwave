namespace GenWave.Core.Domain;

/// <summary>
/// Wire/storage tokens for <see cref="RotKind"/> (SPEC F153.1, F153.9; STORY-374; PLAN T377) — the
/// snake_case strings <c>library.rot_kind</c> itself uses in Postgres and every HTTP surface
/// round-trips (<c>dead_file</c>, <c>near_duplicate</c>, <c>stale_metadata</c>, <c>shelf_dust</c>,
/// <c>unreachable</c>). Mirrors <see cref="ImagingKindTokens"/>'s own enum↔token idiom exactly — the
/// ONE map <c>Garden.RotFindingRepository</c> and <c>GardenerController</c> both call through,
/// replacing the five independent copies a T377 review found (a kind added to the enum but missed in
/// one copy would silently 500 out of <c>GardenerController.BuildGroup</c> rather than fail to
/// compile).
/// </summary>
public static class RotKindTokens
{
    /// <summary>The wire token for <paramref name="kind"/> — also what
    /// <c>Garden.RotFindingRepository</c> binds as the <c>::library.rot_kind</c> SQL parameter.</summary>
    public static string ToToken(RotKind kind) => kind switch
    {
        RotKind.DeadFile => "dead_file",
        RotKind.NearDuplicate => "near_duplicate",
        RotKind.StaleMetadata => "stale_metadata",
        RotKind.ShelfDust => "shelf_dust",
        RotKind.Unreachable => "unreachable",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "unknown rot kind"),
    };

    /// <summary>Parses a wire token — exact-match, case-sensitive (the SAME strict shape both
    /// former copies held: this is a machine token off a fixed enum, never operator free text).</summary>
    public static bool TryParse(string raw, out RotKind kind)
    {
        switch (raw)
        {
            case "dead_file": kind = RotKind.DeadFile; return true;
            case "near_duplicate": kind = RotKind.NearDuplicate; return true;
            case "stale_metadata": kind = RotKind.StaleMetadata; return true;
            case "shelf_dust": kind = RotKind.ShelfDust; return true;
            case "unreachable": kind = RotKind.Unreachable; return true;
            default: kind = default; return false;
        }
    }

    /// <summary>Every token, derived from <see cref="ToToken"/> itself (never a second literal
    /// list) — <c>GardenerController</c>'s own 400 "allowed set" reads this rather than re-typing it.</summary>
    public static readonly IReadOnlyList<string> Tokens = Enum.GetValues<RotKind>().Select(ToToken).ToArray();
}

namespace GenWave.Core.Domain;

/// <summary>
/// Wire/storage tokens for <see cref="RotState"/> (SPEC F153.1, F153.9; STORY-374; PLAN T377) — the
/// snake_case strings <c>library.rot_state</c> itself uses in Postgres and every HTTP surface
/// round-trips (<c>open</c>, <c>dismissed</c>, <c>resolved</c>). Mirrors
/// <see cref="RotKindTokens"/>'s own idiom (itself mirroring <see cref="ImagingKindTokens"/>) — the
/// ONE map <c>Garden.RotFindingRepository</c> and <c>GardenerController</c> both call through.
/// </summary>
public static class RotStateTokens
{
    /// <summary>The wire token for <paramref name="state"/> — also what
    /// <c>Garden.RotFindingRepository</c> binds as the <c>::library.rot_state</c> SQL parameter.</summary>
    public static string ToToken(RotState state) => state switch
    {
        RotState.Open => "open",
        RotState.Dismissed => "dismissed",
        RotState.Resolved => "resolved",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "unknown rot state"),
    };

    /// <summary>Parses a wire token — exact-match, case-sensitive (the SAME strict shape both
    /// former copies held: this is a machine token off a fixed enum, never operator free text).</summary>
    public static bool TryParse(string raw, out RotState state)
    {
        switch (raw)
        {
            case "open": state = RotState.Open; return true;
            case "dismissed": state = RotState.Dismissed; return true;
            case "resolved": state = RotState.Resolved; return true;
            default: state = default; return false;
        }
    }

    /// <summary>Every token, derived from <see cref="ToToken"/> itself (never a second literal
    /// list) — <c>GardenerController</c>'s own 400 "allowed set" reads this rather than re-typing it.</summary>
    public static readonly IReadOnlyList<string> Tokens = Enum.GetValues<RotState>().Select(ToToken).ToArray();
}

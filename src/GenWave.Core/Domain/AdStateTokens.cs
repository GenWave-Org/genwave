namespace GenWave.Core.Domain;

/// <summary>
/// Wire/storage tokens for <see cref="AdState"/> (SPEC F159.1, F159.2; STORY-389; PLAN T398) — the
/// lowercase strings <c>station.ad_state</c> itself uses in Postgres. Mirrors
/// <see cref="RotStateTokens"/>'s own idiom exactly (PLAN T377) — the ONE map
/// <c>Station.AdSpotRepository</c> calls through for every read/write of this column, so a state
/// added to the enum but missed here fails to compile at every call site instead of silently
/// mis-mapping at runtime.
/// </summary>
public static class AdStateTokens
{
    /// <summary>The wire token for <paramref name="state"/> — also what
    /// <c>Station.AdSpotRepository</c> binds as the <c>::station.ad_state</c> SQL parameter.</summary>
    public static string ToToken(AdState state) => state switch
    {
        AdState.Draft => "draft",
        AdState.Approved => "approved",
        AdState.Rendering => "rendering",
        AdState.Ready => "ready",
        AdState.Failed => "failed",
        AdState.Retired => "retired",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "unknown ad state"),
    };

    /// <summary>Parses a wire token — exact-match, case-sensitive (the same strict shape
    /// <see cref="RotStateTokens.TryParse"/> holds: this is a machine token off a fixed enum, never
    /// operator free text).</summary>
    public static bool TryParse(string raw, out AdState state)
    {
        switch (raw)
        {
            case "draft": state = AdState.Draft; return true;
            case "approved": state = AdState.Approved; return true;
            case "rendering": state = AdState.Rendering; return true;
            case "ready": state = AdState.Ready; return true;
            case "failed": state = AdState.Failed; return true;
            case "retired": state = AdState.Retired; return true;
            default: state = default; return false;
        }
    }

    /// <summary>Every token, derived from <see cref="ToToken"/> itself (never a second literal
    /// list).</summary>
    public static readonly IReadOnlyList<string> Tokens = Enum.GetValues<AdState>().Select(ToToken).ToArray();
}

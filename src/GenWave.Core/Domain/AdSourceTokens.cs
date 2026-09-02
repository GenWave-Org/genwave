namespace GenWave.Core.Domain;

/// <summary>
/// Wire/storage tokens for <see cref="AdSource"/> (SPEC F159.1; STORY-389; PLAN T398) — the
/// lowercase strings <c>station.ad_source</c> itself uses in Postgres. Mirrors
/// <see cref="AdStateTokens"/>'s own idiom exactly, one column over.
/// </summary>
public static class AdSourceTokens
{
    /// <summary>The wire token for <paramref name="source"/> — also what
    /// <c>Station.AdSpotRepository</c> binds as the <c>::station.ad_source</c> SQL parameter.</summary>
    public static string ToToken(AdSource source) => source switch
    {
        AdSource.Llm => "llm",
        AdSource.Owner => "owner",
        AdSource.Pack => "pack",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "unknown ad source"),
    };

    /// <summary>Parses a wire token — exact-match, case-sensitive (the same strict shape
    /// <see cref="AdStateTokens.TryParse"/> holds).</summary>
    public static bool TryParse(string raw, out AdSource source)
    {
        switch (raw)
        {
            case "llm": source = AdSource.Llm; return true;
            case "owner": source = AdSource.Owner; return true;
            case "pack": source = AdSource.Pack; return true;
            default: source = default; return false;
        }
    }

    /// <summary>Every token, derived from <see cref="ToToken"/> itself (never a second literal
    /// list).</summary>
    public static readonly IReadOnlyList<string> Tokens = Enum.GetValues<AdSource>().Select(ToToken).ToArray();
}

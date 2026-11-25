using System.Text.Json;
using GenWave.Core.TalkRules;

namespace GenWave.Host.Api;

/// <summary>The URL and JSON spelling of a kind of talk: <c>dj-line</c>, <c>chat</c>, <c>ad</c> (the <c>talk_check</c> column spelling).</summary>
internal static class TalkKindSlugs
{
    public static readonly IReadOnlyList<TalkKinds> All = [TalkKinds.DjLine, TalkKinds.Chat, TalkKinds.Ad];

    public static string Of(TalkKinds kind) => JsonNamingPolicy.KebabCaseLower.ConvertName(kind.ToString());

    /// <summary>One single kind from its slug; anything else (a combination, a different case, a typo) is not a kind.</summary>
    public static bool TryParse(string? slug, out TalkKinds kind)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(Of(candidate), slug, StringComparison.Ordinal))
            {
                kind = candidate;
                return true;
            }
        }

        kind = TalkKinds.None;
        return false;
    }
}

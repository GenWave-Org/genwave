using System.Text.Json;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The spelling of an enum in a <c>talk_check</c> text column: kebab-case, so <c>ReAsked</c> is <c>re-asked</c>
/// and <c>DjLine</c> is <c>dj-line</c> (the same spelling <c>TalkSnapshotJson</c> and the column checks use).
/// </summary>
static class TalkStoredTokens
{
    /// <exception cref="ArgumentException">The value is not one defined name (a [Flags] enum's <c>None</c> or a combination has no single token).</exception>
    public static string Of<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var name = value.ToString();
        if (!Enum.IsDefined(value) || (IsFlags<TEnum>() && !IsSingleFlag(Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture))))
        {
            throw new ArgumentException($"{typeof(TEnum).Name} value '{name}' has no single stored token.", nameof(value));
        }

        return Spell(name);
    }

    static string Spell(string name) => JsonNamingPolicy.KebabCaseLower.ConvertName(name);

    static bool IsFlags<TEnum>() where TEnum : struct, Enum => typeof(TEnum).IsDefined(typeof(FlagsAttribute), inherit: false);

    static bool IsSingleFlag(long bits) => bits > 0 && (bits & (bits - 1)) == 0;

    public static TEnum Parse<TEnum>(string token) where TEnum : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (Spell(candidate.ToString()) == token)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"'{token}' is not a stored {typeof(TEnum).Name}.");
    }
}

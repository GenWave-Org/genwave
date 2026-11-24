namespace GenWave.Core.TalkRules;

/// <summary>The one vocabulary for the <c>TalkRules:Mode</c> setting (F220.18): lowercase names, <see cref="Default"/> legacy.</summary>
public static class TalkRulesModes
{
    public const string Legacy = "legacy";
    public const string Shadow = "shadow";
    public const string Rules = "rules";

    /// <summary>The shipped default: today's code decides, the rule book is not called.</summary>
    public const string Default = Legacy;

    /// <summary>Every accepted setting value, in the order the admin UI lists them.</summary>
    public static readonly IReadOnlyList<string> Accepted = [Legacy, Shadow, Rules];

    /// <summary>Whether <paramref name="value"/> names a mode (case-insensitive, surrounding whitespace ignored). Blank is not accepted.</summary>
    public static bool IsValid(string? value) => TryParse(value) is not null;

    /// <summary>The mode <paramref name="value"/> names, or null when it names none.</summary>
    public static TalkRulesMode? TryParse(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            Legacy => TalkRulesMode.Legacy,
            Shadow => TalkRulesMode.Shadow,
            Rules => TalkRulesMode.Rules,
            _ => null,
        };
}

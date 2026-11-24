using GenWave.Core.TalkRules;

namespace GenWave.Tts;

/// <summary>
/// The writer-side leaf of the <c>TalkRules</c> section: <c>TalkRules:Mode</c> (F220.18). Read fresh from
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/> for every written talk, so a live PUT applies to the
/// next one with no restart. Allowlisted and validated in <c>GenWave.Host</c>.
/// </summary>
public sealed class TalkRulesOptions
{
    public const string Section = "TalkRules";

    /// <summary><c>legacy</c> (default) | <c>shadow</c> | <c>rules</c>; vocabulary in <see cref="TalkRulesModes"/>.</summary>
    public string Mode { get; set; } = TalkRulesModes.Default;
}

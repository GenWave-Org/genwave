using System.Text.RegularExpressions;
using GenWave.Core.TalkRules;
using GenWave.Tts;

namespace GenWave.TalkRules;

/// <summary>
/// No made-up station details. Tries the crosstalk parser's own frequency, letter-code and date patterns in its order, first
/// match wins; the station's own name is exempt.
/// </summary>
public sealed class StationDetailsRule : IJudgingTalkRule
{
    public string Id => "station-details";

    public TalkKinds CanCover => TalkKinds.Chat;

    public TalkKinds OnByDefault => TalkKinds.Chat;

    public TalkRuleDescription Describe() => new(
        "No made-up station details.",
        "Flags a chat that names a station frequency like 98.7 FM, a station's letter code like KXYZ, or a date like August 20. A bare year like 2026 and a date like the 5th of count as dates. The station's own name is left alone.",
        "A chat is made up, so any real-looking frequency, letter code or date would be invented.",
        "The neighbor says you are listening to 98.7 FM.");

    public RuleFlag? Judge(TalkSnapshot after)
    {
        // Kind gates this, not data: a DJ-line snapshot carries text too.
        if (after.Kind != TalkKinds.Chat)
            return null;

        var text = RuleText.Of(after);
        foreach (var (pattern, reason, ask) in Shapes)
        {
            if (CopyClaims.FirstNonExemptMatch(pattern, text, after.Station.Name) is { } match)
                return new RuleFlag(reason, ask, match.Value);
        }

        return null;
    }

    static readonly (Regex Pattern, string Reason, string Ask)[] Shapes =
    [
        (CrosstalkScriptParser.FrequencyRx(), "The chat names a station frequency.",
            "Remove the station frequency and talk about the station without a real-world number."),
        (CrosstalkScriptParser.CallSignRx(), "The chat names a station letter code.",
            "Remove the letter code and refer to the station by its name."),
        (CrosstalkScriptParser.DateClaimRx(), "The chat names a date.",
            "Remove the date and talk without naming a day, month or year."),
    ];
}

using GenWave.Core.TalkRules;
using GenWave.Tts;

namespace GenWave.TalkRules;

/// <summary>
/// Only offered facts. A DJ line is checked against the facts offered with <see cref="CopyClaims.CheckFacts"/>
/// (digits, weekday words, weather words; the station's own name is exempt); with no facts offered it is not judged.
/// A chat holds no facts, so it is checked for weather words alone, as the crosstalk parser does.
/// </summary>
public sealed class OfferedFactsRule : IJudgingTalkRule
{
    public string Id => "offered-facts";

    public TalkKinds CanCover => TalkKinds.DjLine | TalkKinds.Chat;

    public TalkKinds OnByDefault => TalkKinds.DjLine | TalkKinds.Chat;

    public TalkRuleDescription Describe() => new(
        "Only offered facts",
        "Flags a number, a day of the week or a weather word that the facts offered for this talk never state. A chat is offered no facts, so any weather word in it is flagged. A line with no facts offered is not judged.",
        "Talk that invents a temperature, a day or the weather tells listeners something untrue.",
        "The facts say it is 21 degrees, and the line says it is 25 degrees and snowing.");

    public RuleFlag? Judge(TalkSnapshot after) => after.Kind switch
    {
        TalkKinds.DjLine => JudgeDjLine(after),
        TalkKinds.Chat => JudgeChat(after),
        _ => null,
    };

    static RuleFlag? JudgeDjLine(TalkSnapshot after)
    {
        if (after.Facts is null)
            return null;

        var result = CopyClaims.CheckFacts(RuleText.Of(after), string.Join('\n', after.Facts), after.Station.Name);
        if (result.Passed)
            return null;

        var claims = result.Violations.Select(violation => violation.Token).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new RuleFlag(
            "The line says something the offered facts never state.",
            $"Remove {string.Join(" and ", claims.Select(claim => $"\"{claim}\""))} or say only what the offered facts state.",
            string.Join(", ", claims));
    }

    static RuleFlag? JudgeChat(TalkSnapshot after)
    {
        var match = CopyClaims.FirstNonExemptMatch(CopyClaims.ConditionWordRx(), RuleText.Of(after), after.Station.Name);
        return match is null
            ? null
            : new RuleFlag(
                "The chat names the weather, which it was never offered.",
                $"Remove the weather word \"{match.Value}\" and keep the chat to things you were given.",
                match.Value);
    }
}

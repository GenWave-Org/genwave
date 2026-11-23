using GenWave.Core.TalkRules;
using GenWave.Tts;

namespace GenWave.TalkRules;

/// <summary>
/// Right day and time (the judge part). Calls <see cref="CopyClaims.CheckClock"/> on the station's local time. A DJ line
/// exempts the track's title and the owner's message; every kind exempts the station's own name.
/// </summary>
public sealed class DayAndTimeRule : IJudgingTalkRule
{
    public string Id => "day-and-time";

    public TalkKinds CanCover => TalkKinds.DjLine | TalkKinds.Chat;

    public TalkKinds OnByDefault => TalkKinds.DjLine | TalkKinds.Chat;

    public TalkRuleDescription Describe() => new(
        "Right day and time.",
        "Flags a day of the week, or a time of day like morning or tonight, that does not match the station's clock when the line was written. A track title, the owner's message and the station's own name are left alone.",
        "Saying it is Monday on a Sunday, or morning at night, tells listeners something untrue.",
        "The clock says Sunday evening, and the line says Monday morning.");

    public RuleFlag? Judge(TalkSnapshot after)
    {
        // A chat snapshot carries no track and no owner's message, so this is the chat check too.
        var result = CopyClaims.CheckClock(
            RuleText.Of(after), after.LocalNow, after.Track?.Title, after.OwnerMessageCore, after.Station.Name);

        if (result.Passed)
            return null;

        var fixes = result.Violations.Select(violation => $"\"{violation.Expected}\" instead of \"{violation.Token}\"").ToList();
        return new RuleFlag(
            "The line gets the day or the time of day wrong.",
            $"Say {string.Join(" and ", fixes)}, or leave the day and time out.",
            string.Join(", ", result.Violations.Select(violation => violation.Token)));
    }
}

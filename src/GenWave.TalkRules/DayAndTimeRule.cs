using GenWave.Core.TalkRules;
using GenWave.Tts;

namespace GenWave.TalkRules;

/// <summary>
/// Right day and time. Judging: calls <see cref="CopyClaims.CheckClock"/> on the station's local time. A DJ line
/// exempts the track's title and the owner's message; every kind exempts the station's own name.
/// <para>Shaping (T798): tells the writer the clock before it writes, as <see cref="LlmPromptBuilder.BuildClockGuardLine"/> builds it for the legacy
/// system prompt (and for the chat system prompt) too, on the snapshot's local time. It reaches a DJ line and a chat, the kinds the check covers:
/// today's guard rides every DJ render, preview and announcement included, and every chat. It marks nothing and keeps no state.</para>
/// </summary>
public sealed class DayAndTimeRule : IJudgingTalkRule, IShapingTalkRule
{
    public string Id => "day-and-time";

    public TalkKinds CanCover => TalkKinds.DjLine | TalkKinds.Chat;

    public TalkKinds OnByDefault => TalkKinds.DjLine | TalkKinds.Chat;

    public TalkRuleDescription Describe() => new(
        "Right day and time.",
        "Flags a day of the week, or a time of day like morning or tonight, that does not match the station's clock when the line was written. A track title, the owner's message and the station's own name are left alone. Before the line is written, the writer is also told the day and the time of day, and not to name another.",
        "Saying it is Monday on a Sunday, or morning at night, tells listeners something untrue.",
        "The clock says Sunday evening, and the line says Monday morning.");

    public ValueTask<RuleShaping?> ShapeAsync(TalkSnapshot before, CancellationToken ct) =>
        ValueTask.FromResult<RuleShaping?>(new RuleShaping(LlmPromptBuilder.BuildClockGuardLine(before.LocalNow), null));

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

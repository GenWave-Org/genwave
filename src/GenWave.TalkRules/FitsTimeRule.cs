using System.Globalization;
using GenWave.Ads;
using GenWave.Core.TalkRules;

namespace GenWave.TalkRules;

/// <summary>
/// Fits the time. A chat is judged as <c>CrosstalkScriptParser.Parse</c> does: its total characters (sound tags kept, as the
/// parser counts them) over the spoken rate, against the chat's target seconds. An ad is judged as <c>AdScriptValidator</c>'s
/// duration check does, from the snapshot alone: each line's seconds are its text length over the rate, raised by the estimator's
/// measurement-grounded answer the snapshot source captured, summed, against the spot's seconds plus its tolerance. The estimator is
/// never called here, so the judge stays pure. Ad text is measured as the model wrote it (no phone rewrite), the one difference from today.
/// </summary>
public sealed class FitsTimeRule : IJudgingTalkRule
{
    public string Id => "fits-time";

    public TalkKinds CanCover => TalkKinds.Chat | TalkKinds.Ad;

    public TalkKinds OnByDefault => TalkKinds.Chat | TalkKinds.Ad;

    public TalkRuleDescription Describe() => new(
        "Fits the time",
        $"Flags a chat or an ad that would take longer to say than the time it has. Reading time is the number of characters, spaces and punctuation included, divided by {AdScriptValidator.CharsPerSecond:0} a second. A chat may run to its target length. An ad may run to its spot length plus the extra allowance set for ads (40 percent unless changed), and a line of an ad that has been recorded before counts as long as that recording took if it was longer.",
        "Talk that runs over its time gets cut off or pushes the music back.",
        "A 30 second ad whose words take 50 seconds to read.");

    public RuleFlag? Judge(TalkSnapshot after) => after.Kind switch
    {
        TalkKinds.Chat => JudgeChat(after),
        TalkKinds.Ad => JudgeAd(after),
        _ => null,
    };

    static RuleFlag? JudgeChat(TalkSnapshot after)
    {
        if (after.Chat is not { } chat)
            return null;

        var seconds = chat.LineChars.Sum() / chat.CharsPerSecond;
        return seconds > chat.DurationTargetSeconds
            ? Flag("chat", seconds, chat.DurationTargetSeconds)
            : null;
    }

    static RuleFlag? JudgeAd(TalkSnapshot after)
    {
        if (after.Ad is not { } ad)
            return null;

        // One entry per line from the real source; a shorter list can only come from a malformed stored snapshot,
        // whose extra lines are judged on text length alone.
        var seconds = after.Lines.Select((line, index) => AdScriptValidator.LineSeconds(
            line.Text.Length, ad.CharsPerSecond, index < ad.ObservedLineSeconds.Count ? ad.ObservedLineSeconds[index] : 0)).Sum();
        var ceiling = AdScriptValidator.CeilingSeconds(ad.SpotSeconds, ad.ToleranceRatio);
        return seconds > ceiling ? Flag("ad", seconds, ceiling) : null;
    }

    static RuleFlag Flag(string what, double seconds, double allowedSeconds) => new(
        $"The {what} would take about {Seconds(seconds)} seconds to say, and it has {Seconds(allowedSeconds)}.",
        $"Make it shorter so it takes no more than {Seconds(allowedSeconds)} seconds to say.",
        $"{Seconds(seconds)}s over {Seconds(allowedSeconds)}s");

    static string Seconds(double seconds) => seconds.ToString("0.#", CultureInfo.InvariantCulture);
}

using GenWave.Core.TalkRules;

namespace GenWave.Architecture.Tests.TalkRules.Fixtures.Good;

/// <summary>Good fixture: a second pure judge over a different part of the snapshot, so verdict sets are not trivial.</summary>
public sealed class LineLengthJudge : IJudgingTalkRule
{
    private const int MaxWords = 40;

    public string Id => "line-length-cap";

    public TalkKinds CanCover => TalkKinds.DjLine | TalkKinds.Chat | TalkKinds.Ad;

    public TalkKinds OnByDefault => TalkKinds.Chat | TalkKinds.Ad;

    public TalkRuleDescription Describe() => new(
        "Line length cap.",
        "Flags a line of more than 40 words.",
        "Long lines lose the listener.",
        "A fifty word sentence is flagged.");

    public RuleFlag? Judge(TalkSnapshot after)
    {
        foreach (var line in after.Lines)
        {
            if (line.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > MaxWords)
            {
                return new RuleFlag("Too long", "Keep each line shorter.", line.Text);
            }
        }

        return null;
    }
}

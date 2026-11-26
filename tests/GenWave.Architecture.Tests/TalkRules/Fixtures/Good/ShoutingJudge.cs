using GenWave.Core.TalkRules;

namespace GenWave.Architecture.Tests.TalkRules.Fixtures.Good;

/// <summary>Good fixture: a pure judge that states its own number.</summary>
public sealed class ShoutingJudge : IJudgingTalkRule
{
    public const int MaxShoutedWords = 2;

    public string Id => "no-shouting";

    public TalkKinds CanCover => TalkKinds.DjLine | TalkKinds.Chat | TalkKinds.Ad;

    public TalkKinds OnByDefault => TalkKinds.DjLine | TalkKinds.Chat;

    public TalkRuleDescription Describe() => new(
        "No shouting",
        "Flags a line with more than 2 words in capitals.",
        "Capitals read as shouting on air.",
        "WHAT A SONG THAT WAS FOLKS is flagged.");

    public RuleFlag? Judge(TalkSnapshot after)
    {
        foreach (var line in after.Lines)
        {
            var shouted = line.Text
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Count(word => word.Count(char.IsLetter) >= 2 && word.Where(char.IsLetter).All(char.IsUpper));
            if (shouted > MaxShoutedWords)
            {
                return new RuleFlag("Shouting", "Write it in a normal voice.", line.Text);
            }
        }

        return null;
    }
}

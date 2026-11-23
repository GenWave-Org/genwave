using GenWave.Core.TalkRules;

namespace GenWave.Architecture.Tests.TalkRules.Fixtures;

/// <summary>Fixture: states its own number as the last word of the sentence ("... 40."), which law R6 must still count as stated. Not in Good, so the catalog does not scan it.</summary>
public sealed class SentenceEndingNumberJudge : IJudgingTalkRule
{
    public const int Limit = 40;

    public string Id => "sentence-ending-number";

    public TalkKinds CanCover => TalkKinds.Chat;

    public TalkKinds OnByDefault => TalkKinds.Chat;

    public TalkRuleDescription Describe() => new("Sentence ending number.", "Flags a line with words past the limit of 40.", "A test.", "None.");

    public RuleFlag? Judge(TalkSnapshot after) => null;
}

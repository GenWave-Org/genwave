using GenWave.Core.TalkRules;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>Flags text that contains "invented fact". Stands in for a facts rule.</summary>
public sealed class FactsFixtureRule : IJudgingTalkRule
{
    public string Id => "fixture-facts";

    public TalkKinds CanCover => TalkKinds.DjLine;

    public TalkKinds OnByDefault => TalkKinds.DjLine;

    public TalkRuleDescription Describe() => new(
        "Fixture facts",
        "Flags a line that contains the words invented fact.",
        "A fixture for the rule book facts.",
        "It is an invented fact.");

    public RuleFlag? Judge(TalkSnapshot after) =>
        after.Lines.Any(line => line.Text.Contains("invented fact", StringComparison.Ordinal))
            ? new RuleFlag("Not offered", "Use only the offered facts.", "invented fact")
            : null;
}

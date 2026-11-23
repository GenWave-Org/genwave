using GenWave.Core.TalkRules;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>Flags text that contains "said before". Stands in for a repeats rule.</summary>
public sealed class RepeatsFixtureRule : IJudgingTalkRule
{
    public string Id => "fixture-repeats";

    public TalkKinds CanCover => TalkKinds.DjLine;

    public TalkKinds OnByDefault => TalkKinds.DjLine;

    public TalkRuleDescription Describe() => new(
        "Fixture repeats",
        "Flags a line that contains the words said before.",
        "A fixture for the rule book facts.",
        "As I said before.");

    public RuleFlag? Judge(TalkSnapshot after) =>
        after.Lines.Any(line => line.Text.Contains("said before", StringComparison.Ordinal))
            ? new RuleFlag("Repeated", "Say something new.", "said before")
            : null;
}

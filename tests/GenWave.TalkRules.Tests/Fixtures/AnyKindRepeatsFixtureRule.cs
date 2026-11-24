using GenWave.Core.TalkRules;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>Flags text that contains "said before", for every kind of talk.</summary>
public sealed class AnyKindRepeatsFixtureRule : IJudgingTalkRule
{
    public string Id => "fixture-any-repeats";

    public TalkKinds CanCover => TalkKinds.DjLine | TalkKinds.Chat | TalkKinds.Ad;

    public TalkKinds OnByDefault => TalkKinds.DjLine | TalkKinds.Chat | TalkKinds.Ad;

    public TalkRuleDescription Describe() => new(
        "Fixture any-kind repeats",
        "Flags text that contains the words said before.",
        "A fixture for the replay facts.",
        "As I said before.");

    public RuleFlag? Judge(TalkSnapshot after) =>
        after.Lines.Any(line => line.Text.Contains("said before", StringComparison.Ordinal))
            ? new RuleFlag("Repeated", "Say something new.", "said before")
            : null;
}

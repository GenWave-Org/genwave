using GenWave.Core.TalkRules;

namespace GenWave.Architecture.Tests.TalkRules.Fixtures.Bad;

/// <summary>Bad fixture: shares its id with its twin (law R6).</summary>
public sealed class DuplicateIdSecond : IJudgingTalkRule
{
    public string Id => "same-id";

    public TalkKinds CanCover => TalkKinds.Chat;

    public TalkKinds OnByDefault => TalkKinds.Chat;

    public TalkRuleDescription Describe() => new("Duplicate Second", "Judges nothing.", "A test.", "None.");

    public RuleFlag? Judge(TalkSnapshot after) => null;
}

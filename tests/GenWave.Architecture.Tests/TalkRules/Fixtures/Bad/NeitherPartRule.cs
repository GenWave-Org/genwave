using GenWave.Core.TalkRules;

namespace GenWave.Architecture.Tests.TalkRules.Fixtures.Bad;

/// <summary>Bad fixture: a rule that neither shapes nor judges (law R6).</summary>
public sealed class NeitherPartRule : ITalkRule
{
    public string Id => "neither-part";

    public TalkKinds CanCover => TalkKinds.Chat;

    public TalkKinds OnByDefault => TalkKinds.Chat;

    public TalkRuleDescription Describe() => new("Neither part.", "Does nothing at all.", "A test.", "None.");
}

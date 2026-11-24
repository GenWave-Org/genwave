using System.Text.Json;
using GenWave.Core.TalkRules;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>A judge that remembers which memo the snapshot handed it.</summary>
public abstract class MemoProbeRule : IJudgingTalkRule
{
    public abstract string Id { get; }

    public TalkKinds CanCover => TalkKinds.DjLine;

    public TalkKinds OnByDefault => TalkKinds.DjLine;

    /// <summary>The memo seen on the last judge call, or null.</summary>
    public JsonElement? Seen { get; private set; }

    public TalkRuleDescription Describe() => new(
        "Fixture memo probe",
        "Remembers its own memo.",
        "A fixture for the rule book facts.",
        "Any line.");

    public RuleFlag? Judge(TalkSnapshot after)
    {
        Seen = after.MemoOf(this);
        return null;
    }
}

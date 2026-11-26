using GenWave.Core.TalkRules;

namespace GenWave.Architecture.Tests.TalkRules.Fixtures.Bad;

/// <summary>
/// Bad fixture: a judge that reads the switches and flags when "no-shouting" is explicitly off, so turning
/// that rule off changes this rule's verdict (law R4).
/// </summary>
public sealed class SwitchPeekingJudge(ITalkRuleSwitches switches) : IJudgingTalkRule
{
    public string Id => "switch-peeking";

    public TalkKinds CanCover => TalkKinds.DjLine | TalkKinds.Chat | TalkKinds.Ad;

    public TalkKinds OnByDefault => CanCover;

    public TalkRuleDescription Describe() => new("Switch peeking", "Flags when a sibling is off.", "A test.", "None.");

    public RuleFlag? Judge(TalkSnapshot after)
    {
        var stored = switches.GetAllAsync(after.Kind, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return stored.TryGetValue("no-shouting", out var on) && !on
            ? new RuleFlag("Sibling off", "Write it again.", string.Empty)
            : null;
    }
}

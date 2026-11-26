using GenWave.Core.TalkRules;
using GenWave.Tts;

namespace GenWave.TalkRules;

/// <summary>Says the owner's message. Calls <see cref="CopyClaims.CheckContainment"/> when the talk carries an owner's message.</summary>
public sealed class OwnerMessageRule : IJudgingTalkRule
{
    public string Id => "owner-message";

    public TalkKinds CanCover => TalkKinds.DjLine;

    public TalkKinds OnByDefault => TalkKinds.DjLine;

    public TalkRuleDescription Describe() => new(
        "Says the owner's message",
        "Flags an announcement that leaves out or rewords the owner's message. The owner's words must appear in order. Capital letters and punctuation do not matter.",
        "The owner wrote the message, and the announcement is meant to say it, not a paraphrase of it.",
        "The owner wrote that dinner is ready, and the line talks about food without saying so.");

    public RuleFlag? Judge(TalkSnapshot after)
    {
        if (after.OwnerMessageCore is null)
            return null;

        var result = CopyClaims.CheckContainment(RuleText.Of(after), after.OwnerMessageCore);
        return result.Passed
            ? null
            : new RuleFlag(
                "The line leaves out or rewords the owner's message.",
                "Say the owner's message again, word for word and in full.",
                result.Violations[0].Token);
    }
}

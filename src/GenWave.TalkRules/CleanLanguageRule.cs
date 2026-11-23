using GenWave.Ads;
using GenWave.Core.Domain;
using GenWave.Core.TalkRules;

namespace GenWave.TalkRules;

/// <summary>
/// Clean language. Under the Everyone audience, matches the ad's folded text against the shipped list of words, as
/// <c>AdScriptValidator</c>'s audience check does (the same <see cref="AdScriptValidator.FirstProfaneWord"/> body). Any other audience is not judged.
/// </summary>
public sealed class CleanLanguageRule : IJudgingTalkRule
{
    public string Id => "clean-language";

    public TalkKinds CanCover => TalkKinds.Ad;

    public TalkKinds OnByDefault => TalkKinds.Ad;

    public TalkRuleDescription Describe() => new(
        "Clean language.",
        "Flags an ad that uses a swear word from the station's list of words, but only when the station's audience is set to Everyone. Spelling tricks like swapped letters or spaces are seen through. With any other audience this rule does nothing.",
        "A station for everyone should not air swearing in its ads.",
        "An ad that calls the sponsor's rival a rude name.");

    public RuleFlag? Judge(TalkSnapshot after)
    {
        if (after.Kind != TalkKinds.Ad
            || !string.Equals(after.Station.AudiencePosture, nameof(AudiencePosture.Everyone), StringComparison.Ordinal))
        {
            return null;
        }

        var variants = AdCopyFold.FoldVariants(RuleText.Of(after));
        return AdScriptValidator.FirstProfaneWord(variants) is { } word
            ? new RuleFlag(
                "The ad uses a word that is not allowed for everyone.",
                "Remove the swear word and say it in a clean way.",
                word)
            : null;
    }
}

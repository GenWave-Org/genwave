using GenWave.Ads;
using GenWave.Core.TalkRules;

namespace GenWave.TalkRules;

/// <summary>
/// No other brands. Folds the ad's text and matches it against the shipped brand list, as <c>AdScriptValidator</c>'s brand check does
/// (the same <see cref="AdScriptValidator.FirstBlocklistedBrand"/> body). The sponsor's own name is left alone unless the sponsor is
/// pack-owned, the same exemption the legacy check uses.
/// </summary>
public sealed class OtherBrandsRule : IJudgingTalkRule
{
    public string Id => "other-brands";

    public TalkKinds CanCover => TalkKinds.Ad;

    public TalkKinds OnByDefault => TalkKinds.Ad;

    public TalkRuleDescription Describe() => new(
        "No other brands",
        "Flags an ad that names a real company or product from the station's list of brands, such as a soft drink or a shoe maker. Spelling tricks like swapped letters or spaces are seen through. The sponsor's own name is allowed, unless the sponsor came from a pack of made-up sponsors.",
        "An ad for one sponsor should not advertise, or poke fun at, someone else's real brand.",
        "An ad for a local diner that says it is better than Coca Cola.");

    public RuleFlag? Judge(TalkSnapshot after)
    {
        if (after.Kind != TalkKinds.Ad || after.Ad is not { } ad)
            return null;

        var variants = AdCopyFold.FoldVariants(RuleText.Of(after));
        return AdScriptValidator.FirstBlocklistedBrand(variants, ad.PackOwned, ad.SponsorName) is { } brand
            ? new RuleFlag(
                "The ad names another company's brand.",
                $"Remove the brand \"{brand}\" and talk only about the sponsor.",
                brand)
            : null;
    }
}

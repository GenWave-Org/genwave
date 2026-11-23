using GenWave.Ads;
using GenWave.Core.TalkRules;

namespace GenWave.TalkRules;

/// <summary>
/// The right phone number. Checks each ad line on its own for a phone-shaped run, with <c>PhoneShapeCheck</c> as the validator's phone
/// check does. With a sponsor phone on file (and the sponsor not pack-owned) only that number is allowed; with none on file, a run needs a
/// 555 in it.
/// <para>
/// Difference from today: legacy hygiene rewrites a wrong number into the sponsor's number (or drops its clause) before the checks run, so
/// a wrong number never reached them. The snapshot holds the text as the model wrote it, so this rule flags what legacy silently fixed
/// (PLAN T786, F220.17). The shadow report must expect flags here with no legacy refusal.
/// </para>
/// </summary>
public sealed class PhoneNumberRule : IJudgingTalkRule
{
    public string Id => "phone-number";

    public TalkKinds CanCover => TalkKinds.Ad;

    public TalkKinds OnByDefault => TalkKinds.Ad;

    public TalkRuleDescription Describe() => new(
        "The right phone number.",
        "Flags an ad that says a phone number that is not the sponsor's. When the sponsor has a phone number, any other number in the ad is flagged, even one with 555 in it. When the sponsor has none, or is from a pack of made-up sponsors, a number is flagged unless it has 555 in it. Each line is read on its own. The ad's words are never changed.",
        "A made-up or wrong number could send a listener to a real person who never asked for the call.",
        "An ad for a diner that says to call 222-0199 when the diner's number is 222-0100.");

    public RuleFlag? Judge(TalkSnapshot after)
    {
        if (after.Kind != TalkKinds.Ad || after.Ad is not { } ad)
            return null;

        var allowedPhone = ad.PackOwned ? null : ad.SponsorPhone?.Trim();
        var hasSponsorPhone = !string.IsNullOrWhiteSpace(allowedPhone);

        foreach (var line in after.Lines)
        {
            if (PhoneShapeCheck.FindViolation(line.Text, allowedPhone) is not { } run)
                continue;

            return new RuleFlag(
                "The ad says a phone number that is not the sponsor's.",
                hasSponsorPhone
                    ? $"Say only the sponsor's phone number, {allowedPhone}, or leave the number out."
                    : "Leave the phone number out, or use a number with 555 in it.",
                run);
        }

        return null;
    }
}

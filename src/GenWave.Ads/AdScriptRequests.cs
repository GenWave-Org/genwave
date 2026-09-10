namespace GenWave.Ads;

using System.Diagnostics;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts;

/// <summary>
/// The one place that builds an <see cref="AdScriptWriter.WriteAsync"/> call's own request pair —
/// the <see cref="AdScriptWriteRequest"/> plus the validate delegate <see cref="AdScriptValidator"/>
/// adapts into (PLAN T441) — hoisted out of <see cref="AdSpotWorker"/>'s own
/// <c>GenerateOneAsync</c>/<c>BuildValidateDelegate</c> (T402) so both that tick loop and
/// <see cref="AdSpotJobService"/>'s own write job build the SAME two requests from the SAME sponsor
/// the SAME way — one adapter, not two copies drifting apart. Neither caller's own choice of premise/
/// tone/target length changes: each still hands in whatever it always did, this type just assembles
/// them.
/// </summary>
internal static class AdScriptRequests
{
    /// <summary>
    /// Builds the <see cref="AdScriptWriteRequest"/> a caller hands to
    /// <see cref="AdScriptWriter.WriteAsync"/>, paired with the validate delegate that adapts the pure
    /// <see cref="AdScriptValidator.Validate"/> into the minimal <see cref="AdScriptValidationOutcome"/>
    /// contract that method accepts.
    /// </summary>
    /// <param name="sponsor">The sponsor the generated line is written for — every contact fact
    /// (<see cref="Sponsor.Tagline"/>/<see cref="Sponsor.About"/>/<see cref="Sponsor.Phone"/>/
    /// <see cref="Sponsor.Address"/>/<see cref="Sponsor.Website"/>) rides straight through, and
    /// <see cref="Sponsor.Tone"/> rides as <see cref="AdScriptWriteRequest.HouseTone"/> on every
    /// caller (the house-tone fallback a brief-less spot still gets).</param>
    /// <param name="premise">The writing hint — <c>AdBrief.Premise</c> for the stock worker, an owner
    /// spot's own <c>Brief</c> for a write job — or <see langword="null"/>.</param>
    /// <param name="tone">A per-call tone override — <c>AdBrief.Tone</c> for the stock worker, or
    /// <see langword="null"/> for a write job (a spot has no tone column of its own; the sponsor's own
    /// <see cref="Sponsor.Tone"/> already rides as <see cref="AdScriptWriteRequest.HouseTone"/>
    /// regardless of this parameter).</param>
    /// <param name="spotSeconds">The target air length both requests validate against.</param>
    public static (AdScriptWriteRequest Write, Func<string, AdScriptValidationOutcome> Validate) Build(
        Sponsor sponsor, string? premise, string? tone, int spotSeconds, AudiencePosture posture,
        int maxLineChars, double toleranceRatio, IPatterDurationEstimator durationEstimator)
    {
        var writeRequest = new AdScriptWriteRequest(
            sponsor.Name, premise, tone, spotSeconds, posture, maxLineChars, toleranceRatio,
            Tagline: sponsor.Tagline, About: sponsor.About, Phone: sponsor.Phone, Address: sponsor.Address,
            Website: sponsor.Website, HouseTone: sponsor.Tone);

        // The owner-sponsor skips (SPEC F172.5, PLAN T438 ruling): IsPackOwned answers "does the
        // SPONSOR belong to a pack" — it reads sponsor.PackSlug, never brief.PackSlug.
        var validationRequest = new AdScriptValidationRequest(
            posture, maxLineChars, spotSeconds, toleranceRatio,
            SponsorName: sponsor.Name, SponsorPhone: sponsor.Phone, IsPackOwned: sponsor.PackSlug is not null);

        return (writeRequest, BuildValidateDelegate(validationRequest, durationEstimator));
    }

    static Func<string, AdScriptValidationOutcome> BuildValidateDelegate(
        AdScriptValidationRequest validationRequest, IPatterDurationEstimator durationEstimator) =>
        rawScript => AdScriptValidator.Validate(rawScript, validationRequest, durationEstimator) switch
        {
            AdScriptValidationResult.Accepted => new AdScriptValidationOutcome.Accepted(),
            AdScriptValidationResult.Refused refused =>
                new AdScriptValidationOutcome.Refused(refused.Violation.RuleId, refused.Violation.Reason),
            _ => throw new UnreachableException($"Unhandled {nameof(AdScriptValidationResult)} case."),
        };
}

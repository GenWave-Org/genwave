namespace GenWave.Ads;

/// <summary>
/// The house's FIRST profanity list (SPEC F160.3, STORY-390 AC6) — guards ONLY ad copy, under the
/// <c>everyone</c> <see cref="GenWave.Core.Domain.AudiencePosture"/>. SCOPE PIN: no non-ad production
/// path may reference this type — see <c>Story390_AdScriptValidator.ScenarioAudiencePostureRefuses
/// .TheProfanityListGuardsOnlyAdCopy</c>'s source-text pin. Loaded and folded exactly like <see
/// cref="AdBrandBlocklist"/> — see that type's own remarks for the shared loader/fold/match pipeline.
/// </summary>
internal static class AdProfanityList
{
    const string ResourceName = "GenWave.Ads.Data.ProfanityList.txt";

    public static readonly IReadOnlyList<string> FoldedEntries =
        EmbeddedWordList.Load(ResourceName)
            .Select(AdCopyFold.Fold)
            .Distinct(StringComparer.Ordinal)
            .ToList();
}

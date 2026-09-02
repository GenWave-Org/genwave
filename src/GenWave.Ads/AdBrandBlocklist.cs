namespace GenWave.Ads;

/// <summary>
/// The shipped brand blocklist (SPEC F160.3) — real-world brand names an ad script must never name, so
/// a fictional-brand parody spot can never smuggle a real trademark. Loaded once from the embedded
/// <c>Data/BrandBlocklist.txt</c> resource, folded once (<see cref="AdCopyFold.Fold"/> — the single
/// canonical form, since this data is curated and trusted, unlike the untrusted script text, which
/// <see cref="AdScriptValidator"/> folds into several candidate variants via <see
/// cref="AdCopyFold.FoldVariants"/>) so <see cref="FoldedWordListMatcher.FirstMatch"/> compares
/// folded-to-folded.
///
/// <para>
/// <b>Precision bias (data file's own header has the full curation rationale):</b> entries are
/// overwhelmingly multi-word/distinctive forms ("coca cola", "mcdonald's") rather than bare common
/// English words — a single-word entry that doubles as an ordinary dictionary word (apple, target,
/// visa, amazon) is the noisy false-positive class this list deliberately avoids.
/// </para>
/// </summary>
internal static class AdBrandBlocklist
{
    const string ResourceName = "GenWave.Ads.Data.BrandBlocklist.txt";

    public static readonly IReadOnlyList<string> FoldedEntries =
        EmbeddedWordList.Load(ResourceName)
            .Select(AdCopyFold.Fold)
            .Distinct(StringComparer.Ordinal)
            .ToList();
}

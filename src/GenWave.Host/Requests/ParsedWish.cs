namespace GenWave.Host.Requests;

using GenWave.Core.Domain;

/// <summary>
/// The predicates one wish parse produced (SPEC F87.4, STORY-225, PLAN T88; <see cref="Genre"/>
/// added by gh-#131). <see cref="Artist"/>/<see cref="Title"/>/<see cref="Genre"/> null and
/// <see cref="Moods"/> empty is the legal "no confident interpretation" outcome every
/// <see cref="IWishParser"/> implementation can return — never an exception, never a partial write.
/// </summary>
sealed record ParsedWish(string? Artist, string? Title, string? Genre, IReadOnlyList<string> Moods)
{
    /// <summary>The universal "nothing recognized" result — reused by both <see cref="IWishParser"/>
    /// implementations rather than each constructing their own equivalent empty instance.</summary>
    public static readonly ParsedWish Empty = new(null, null, null, []);

    /// <summary>
    /// True when every predicate is empty — exactly the condition <see cref="RequestParserService"/>
    /// maps to <c>unmatched: true</c> (SPEC F87.4's "unparseable ⇒ empty predicates ⇒
    /// status=unmatched").
    /// </summary>
    public bool IsEmpty => Artist is null && Title is null && Genre is null && Moods.Count == 0;

    /// <summary>
    /// gh-#131 — folds the intake endpoint's server-validated picker values into this parse outcome
    /// (predicates merge as AND; free text still parses exactly as before). A picked genre wins over
    /// any free-text genre guess — it was validated against the live requestable-genre list at POST
    /// time, so it is the more authoritative of the two. A picked mood joins the parsed moods FIRST
    /// (it always survives the <see cref="MoodVocabulary.MaxMoodsPerTrack"/> cap), deduplicated
    /// exactly — every value on both sides is already a lowercase vocabulary member.
    /// </summary>
    public ParsedWish MergePicked(string? pickedGenre, string? pickedMood)
    {
        var mergedGenre = pickedGenre ?? Genre;
        if (pickedMood is null)
            return mergedGenre == Genre ? this : this with { Genre = mergedGenre };

        var mergedMoods = new List<string> { pickedMood };
        foreach (var mood in Moods)
        {
            if (mergedMoods.Contains(mood)) continue;

            mergedMoods.Add(mood);
            if (mergedMoods.Count == MoodVocabulary.MaxMoodsPerTrack) break;
        }

        return this with { Genre = mergedGenre, Moods = mergedMoods };
    }
}

namespace GenWave.Host.Requests;

/// <summary>
/// The predicates one wish parse produced (SPEC F87.4, STORY-225, PLAN T88). <see cref="Artist"/>/
/// <see cref="Title"/> null and <see cref="Moods"/> empty is the legal "no confident interpretation"
/// outcome every <see cref="IWishParser"/> implementation can return — never an exception, never a
/// partial write.
/// </summary>
sealed record ParsedWish(string? Artist, string? Title, IReadOnlyList<string> Moods)
{
    /// <summary>The universal "nothing recognized" result — reused by both <see cref="IWishParser"/>
    /// implementations rather than each constructing their own equivalent empty instance.</summary>
    public static readonly ParsedWish Empty = new(null, null, []);

    /// <summary>
    /// True when every predicate is empty — exactly the condition <see cref="RequestParserService"/>
    /// maps to <c>unmatched: true</c> (SPEC F87.4's "unparseable ⇒ empty predicates ⇒
    /// status=unmatched").
    /// </summary>
    public bool IsEmpty => Artist is null && Title is null && Moods.Count == 0;
}

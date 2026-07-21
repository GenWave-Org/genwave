namespace GenWave.Core.Domain;

/// <summary>
/// A <see cref="TasteRule"/>'s match criteria (SPEC F82.1, F82.5): every non-null field must match
/// the candidate track for the rule to fire — AND semantics, never OR. A rule that opinions only
/// about <see cref="Artist"/> leaves <see cref="Genre"/> and <see cref="Tag"/> null so they never
/// constrain the match. <see cref="Tag"/> doubles as the mood channel once F85 stamps tracks with
/// mood tags (SPEC F85.5) — moods are just another tag value here, no parallel matching system.
/// Case-insensitive comparison against catalog data is the ranker's concern (F82.5, a later task);
/// this shape only carries what the rule is about.
/// </summary>
public sealed record TastePredicate(string? Artist, string? Genre, string? Tag);

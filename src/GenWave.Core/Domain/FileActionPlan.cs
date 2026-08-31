namespace GenWave.Core.Domain;

/// <summary>
/// A file action <see cref="Abstractions.IFileActionPlanner"/> is prepared to run, once confirmed
/// (SPEC F154.1, F154.5; STORY-379; PLAN T379, gh-#529) — <see cref="From"/>/<see cref="To"/> are the
/// jailed source/destination paths, <see cref="TagDiff"/> is the (possibly empty, for rename/move)
/// list of tag fields a retag will write, and <see cref="ExpiresAt"/> is the plan token's own
/// 10-minute horizon (<see cref="Abstractions.IFileActionPlanTokens"/>'s <c>PlanTtl</c>). The tuple
/// <c>(</c><see cref="MediaId"/><c>, </c><see cref="Xmin"/><c>, </c><see cref="From"/><c>, </c>
/// <see cref="To"/><c>)</c> is what a plan token binds (F154.5) — <see cref="PlanBinding"/> re-checks
/// the (mediaId, xmin, from) half of it at confirm time.
///
/// <para>
/// <b>Equality is overridden, not the compiler-synthesized record default</b> — <see cref="TagDiff"/>
/// is an <see cref="IReadOnlyList{T}"/>, and the record-generated <c>Equals</c> would compare it by
/// reference (two lists with identical contents but different instances would compare UNEQUAL),
/// which would silently break a round-trip proof like "mint then read back yields an equal plan"
/// (STORY-379 AC2). Mirrors <c>EnvelopeTuple</c>'s own precedent exactly:
/// <see cref="Equals(FileActionPlan?)"/>/<see cref="GetHashCode"/> below compare/hash
/// <see cref="TagDiff"/> element-by-element (<c>SequenceEqual</c>, order-sensitive — the planner's
/// own fixed field order is what makes two plans built the same way compare equal, not this type).
/// </para>
/// </summary>
/// <param name="MediaId">The subject's <c>library.media</c> row id.</param>
/// <param name="Xmin">The subject's <c>xmin</c> token, as observed when this plan was built.</param>
/// <param name="Verb">Which of the three file actions this plan performs.</param>
/// <param name="From">The subject's current path — always <see cref="FileActionSubject.Path"/>
/// verbatim, never re-canonicalised.</param>
/// <param name="To">The computed destination path (retag: identical to <see cref="From"/>; rename:
/// the same directory, a new file name; move: a new directory, the same file name).</param>
/// <param name="TagDiff">The tag fields a retag will write; always empty for rename/move.</param>
/// <param name="ExpiresAt">When a plan token minted for this plan stops being readable (F154.5:
/// 10 minutes from the moment the plan was built).</param>
public sealed record FileActionPlan(
    long MediaId,
    string Xmin,
    FileActionVerb Verb,
    string From,
    string To,
    IReadOnlyList<TagChange> TagDiff,
    DateTimeOffset ExpiresAt)
{
    public bool Equals(FileActionPlan? other) =>
        other is not null
        && MediaId == other.MediaId
        && Xmin == other.Xmin
        && Verb == other.Verb
        && From == other.From
        && To == other.To
        && TagDiff.SequenceEqual(other.TagDiff)
        && ExpiresAt.Equals(other.ExpiresAt);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(MediaId);
        hash.Add(Xmin);
        hash.Add(Verb);
        hash.Add(From);
        hash.Add(To);
        foreach (var change in TagDiff)
            hash.Add(change);
        hash.Add(ExpiresAt);
        return hash.ToHashCode();
    }
}

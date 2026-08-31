using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// Plans one of the Library Gardener's three file actions — retag, rename, move (SPEC F154.1,
/// F154.3; STORY-379; PLAN T379/T381 review N4, gh-#529) — and enforces the jail (canonicalise,
/// symlink-resolve, root-prefix, exempt roots, never-overwrite) before any write — or any file
/// READ — is even considered. No database access, framework-free (L1); no I/O of its own beyond the
/// implementation's own filesystem probe and — for a retag, and only once the subject has already
/// passed its own destination gate — a read of the file's CURRENT tags via
/// <see cref="IFileTagReader"/> (T381 review N4: the caller no longer supplies these; a refused
/// subject is never opened at all). A plan is either produced or refused, in one synchronous call.
/// </summary>
public interface IFileActionPlanner
{
    /// <summary>
    /// Plans <paramref name="verb"/> against <paramref name="subject"/>, or refuses it and names why.
    /// </summary>
    /// <param name="subject">The catalog row's own snapshot.</param>
    /// <param name="verb">Which of the three actions to plan.</param>
    /// <param name="target">
    /// For <see cref="FileActionVerb.Rename"/>: an optional operator-supplied file NAME (no
    /// directory separators, same extension as the source) — <see langword="null"/> uses the
    /// <c>{artist} - {title}.{ext}</c> template. For <see cref="FileActionVerb.Move"/>: the
    /// destination DIRECTORY — MUST be an absolute path (SPEC F154.3's own "absolute only, simpler
    /// jail" ruling, T379 review B1). A non-rooted move target is refused
    /// (<see cref="FileActionRule.OutsideRoot"/>) before any path resolution happens at all, so the
    /// process's own current working directory never participates in the jail. Ignored for
    /// <see cref="FileActionVerb.Retag"/>.
    /// </param>
    /// <param name="now">The instant the plan is built from — <see cref="FileActionPlan.ExpiresAt"/>
    /// is <paramref name="now"/> plus the 10-minute plan TTL (F154.5).</param>
    FileActionPlanResult Plan(FileActionSubject subject, FileActionVerb verb, string? target, DateTimeOffset now);
}

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Options;
using Microsoft.Extensions.Options;

namespace GenWave.MediaLibrary.Garden.FileActions;

/// <summary>
/// The Library Gardener's file-action jail (SPEC F154.1, F154.3, F154.4, F154.5; STORY-379; PLAN
/// T379, gh-#529) — pure computation over <see cref="FileActionSubject"/> plus whatever
/// <see cref="IFileSystemProbe"/> answers; no Postgres, no direct file I/O of its own.
///
/// <para>
/// <b>Root mapping (SPEC F154.3's own amendment ruling):</b> this codebase scans exactly one
/// library — <c>ScanService.ScannedLibraryId</c> = 1, root = <see cref="LibraryOptions.MediaRoot"/>
/// (<c>library.library</c> carries no per-library root) — so the jail root IS
/// <see cref="LibraryOptions.MediaRoot"/>; a subject must be that one scanned library's row, under
/// it; "another library's root" means any path not under it; exempt roots are
/// <see cref="ScanOptions.QuarantineExemptRoots"/>.
/// </para>
///
/// <para>
/// <b>Two root forms, never mixed:</b> <c>canonicalRoot</c> is <c>Path.GetFullPath(MediaRoot)</c>,
/// nothing more — this is what an ordinary containment check compares against, so a
/// <c>MediaRoot</c> that is ITSELF configured as a symlink still recognises a subject/target that
/// carries the SAME symlink prefix a real scan would have discovered it under. <c>resolvedRoot</c>
/// is <c>canonicalRoot</c> link-resolved ONCE (never re-resolved per comparison — "a root that is
/// itself a symlink resolves once", STORY-379's own fixture fact) — this is the boundary a symlink
/// escape is measured against. Either form can be <see langword="null"/> when
/// <see cref="IFileSystemProbe.ResolveLinks"/> itself cannot vouch for it (a cycle) — a null root
/// or resolved path never satisfies containment, the same fail-closed posture <see cref="IsUnderRoot"/>
/// enforces throughout.
/// </para>
///
/// <para>
/// <b>ONE destination gate (T379 review B2/B3):</b> <see cref="CheckDestinationJail"/> is the single
/// exempt-root/root-containment/symlink-escape check every verb's own destination passes through —
/// the subject's own path for ALL three verbs (a retag's destination IS its source, since it never
/// moves the file), plus the computed target for rename/move. It checks the exempt-root rule against
/// BOTH the canonical and the link-resolved form of the path (a symlink inside the root that points
/// at an exempt directory still counts, T379 review B3), before either of the generic
/// outside-root/symlink-escape checks — so a path that is both outside the root AND under a known
/// exempt root always reports the more specific <see cref="FileActionRule.ExemptRoot"/>.
/// </para>
///
/// <para>
/// <b>The ordered rule list, refusing at the first failure and BEFORE any filesystem probe call at
/// all for the raw-string rules (T379 review N7):</b>
/// (1) <see cref="FileActionRule.NotScannedLibrary"/> — subject not in the one scanned library, no
/// I/O; (2) <see cref="FileActionRule.SubjectOutsideRoot"/> — the subject path is null/empty (T379
/// review round 2 item 4: <c>Path.GetFullPath("")</c> throws — refused here instead, before that
/// call is ever reached), no I/O; (3) <see cref="FileActionRule.Traversal"/> — the raw target string
/// (rename/move) contains a literal <c>..</c> segment, no I/O; (4)
/// <see cref="FileActionRule.InvalidName"/> — an operator-supplied rename name that isn't a bare,
/// valid, same-extension, non-hidden file name, no I/O; (5)
/// <see cref="FileActionRule.MissingTarget"/> — a move with no destination directory at all (T379
/// review round 2 item 4), no I/O; (6) <see cref="FileActionRule.OutsideRoot"/> — a move target that
/// was never an absolute path to begin with, refused before <c>Path.GetFullPath</c> would otherwise
/// resolve it against the process's own working directory (T379 review B1), no I/O; (7) the
/// SUBJECT's own destination gate (first I/O: <see cref="IFileSystemProbe.ResolveLinks"/>) —
/// <see cref="FileActionRule.ExemptRoot"/> or <see cref="FileActionRule.SubjectOutsideRoot"/>; (8)
/// for rename/move, the COMPUTED TARGET's own destination gate —
/// <see cref="FileActionRule.ExemptRoot"/>, <see cref="FileActionRule.OutsideRoot"/>, or
/// <see cref="FileActionRule.SymlinkEscape"/>; (9) <see cref="FileActionRule.SameAsSource"/> —
/// rename/move only; (10) <see cref="FileActionRule.TargetNotADirectory"/> — move only, a
/// destination directory that isn't already a real directory (missing OR a file — T379 review round
/// 2 item 3: the planner never implies an mkdir), the ONE point <see cref="IFileSystemProbe.Kind"/>
/// is first consulted; (11) <see cref="FileActionRule.TargetExists"/> — rename/move, <c>Kind</c>
/// again; (12) <see cref="FileActionRule.NothingToRetag"/> — retag only, right after step 7 passes
/// (retag has no steps 8-11 — its destination IS its source).
/// </para>
/// </summary>
sealed class FileActionPlanner(
    IOptionsMonitor<LibraryOptions> libraryOptions,
    IOptionsMonitor<ScanOptions> scanOptions,
    IFileSystemProbe fileSystemProbe) : IFileActionPlanner
{
    /// <summary>The one scanned library (SPEC F154.3's amendment ruling) — mirrors
    /// <c>Scan.ScanService.ScannedLibraryId</c> exactly; kept as its own constant here rather than a
    /// shared reference because that field is private to its own type (Garden's own L2 narrowing
    /// keeps non-repository types, this one included, from reaching into Scan/Catalog internals).
    /// </summary>
    const long ScannedLibraryId = 1;

    /// <summary>The plan token's own 10-minute horizon (SPEC F154.5).</summary>
    internal static readonly TimeSpan PlanTtl = TimeSpan.FromMinutes(10);

    static readonly char[] PathSeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    public FileActionPlanResult Plan(FileActionSubject subject, FileActionVerb verb, string? target, DateTimeOffset now)
    {
        if (subject.LibraryId != ScannedLibraryId)
            return FileActionPlanResult.Refused(FileActionRule.NotScannedLibrary);

        // An empty subject path (T379 review round 2 item 4) would otherwise reach
        // Path.GetFullPath("") inside CheckDestinationJail below, which THROWS ArgumentException —
        // the planner must never throw on any input shape the endpoint could pass. There is no
        // dedicated rule for this shape; it is exactly what SubjectOutsideRoot already means.
        if (string.IsNullOrEmpty(subject.Path))
            return FileActionPlanResult.Refused(FileActionRule.SubjectOutsideRoot);

        // Raw-string, zero-I/O rules first (T379 review N7) — a Traversal/InvalidName/non-rooted
        // refusal must be reachable without this method ever having called the filesystem probe.
        if (verb is FileActionVerb.Rename or FileActionVerb.Move && target is not null && ContainsTraversalSegment(target))
            return FileActionPlanResult.Refused(FileActionRule.Traversal);

        if (verb == FileActionVerb.Rename && target is not null
            && (!RenameNaming.IsValidRenameName(target) || !RenameNaming.HasSourceExtension(target, subject.Path)))
            return FileActionPlanResult.Refused(FileActionRule.InvalidName);

        if (verb == FileActionVerb.Move)
        {
            // A null/empty move target (T379 review round 2 item 4) is a caller-shape problem, not a
            // programming error — refused, never thrown; Path.GetFullPath would otherwise throw on
            // an empty string a few lines further down this same method.
            if (string.IsNullOrEmpty(target))
                return FileActionPlanResult.Refused(FileActionRule.MissingTarget);
            if (!Path.IsPathRooted(target))
                return FileActionPlanResult.Refused(FileActionRule.OutsideRoot);
        }

        var canonicalRoot = NormalizePath(Path.GetFullPath(libraryOptions.CurrentValue.MediaRoot));
        var resolvedRoot = Resolve(canonicalRoot);

        var (subjectRefusal, _) = CheckDestinationJail(
            subject.Path, canonicalRoot, resolvedRoot, FileActionRule.SubjectOutsideRoot, FileActionRule.SubjectOutsideRoot);
        if (subjectRefusal is { } subjectRule)
            return FileActionPlanResult.Refused(subjectRule);

        // A tuple pattern match (not a plain switch on verb) so PlanMove's own parameter can stay
        // non-nullable: the { } pattern below both proves and binds target's non-null-ness to the
        // compiler at the one call site that needs it, rather than reaching for `!` (T379 review
        // round 2 item 4 — target was already refused above for Move when null/empty, so the final
        // arm is unreachable in practice; it exists only so this method can never throw regardless).
        return (verb, target) switch
        {
            (FileActionVerb.Retag, _) => PlanRetag(subject, now),
            (FileActionVerb.Rename, _) => PlanRename(subject, target, canonicalRoot, resolvedRoot, now),
            (FileActionVerb.Move, { } rootedTarget) => PlanMove(subject, rootedTarget, canonicalRoot, resolvedRoot, now),
            (FileActionVerb.Move, null) => FileActionPlanResult.Refused(FileActionRule.MissingTarget),
            _ => throw new ArgumentOutOfRangeException(nameof(verb), verb, "Unknown file action verb."),
        };
    }

    static FileActionPlanResult PlanRetag(FileActionSubject subject, DateTimeOffset now)
    {
        var diff = TagDiffCalculator.Compute(subject);
        if (diff.Count == 0)
            return FileActionPlanResult.Refused(FileActionRule.NothingToRetag);

        return FileActionPlanResult.Planned(new FileActionPlan(
            subject.MediaId, subject.Xmin, FileActionVerb.Retag, subject.Path, subject.Path, diff, now + PlanTtl));
    }

    FileActionPlanResult PlanRename(
        FileActionSubject subject, string? target, string canonicalRoot, string? resolvedRoot, DateTimeOffset now)
    {
        var sourceDir = Path.GetDirectoryName(subject.Path);
        if (sourceDir is null)
            return FileActionPlanResult.Refused(FileActionRule.SubjectOutsideRoot);

        var fileName = target ?? RenameNaming.BuildTemplateName(subject);

        // The template's own output is re-validated here (T379 review round 2 item 2) — an enforced
        // postcondition, not trusted arithmetic. An operator-supplied name was already validated
        // above in Plan(); re-checking it here would be redundant, not wrong, but this scopes the
        // check to exactly the case that needs it.
        if (target is null && !RenameNaming.IsValidRenameName(fileName))
            return FileActionPlanResult.Refused(FileActionRule.InvalidName);

        var to = Path.Combine(sourceDir, fileName);

        return EvaluateTarget(subject, FileActionVerb.Rename, to, moveTargetDirectory: null, canonicalRoot, resolvedRoot, now);
    }

    FileActionPlanResult PlanMove(
        FileActionSubject subject, string target, string canonicalRoot, string? resolvedRoot, DateTimeOffset now)
    {
        var fileName = Path.GetFileName(subject.Path);
        var to = Path.Combine(target, fileName);

        return EvaluateTarget(subject, FileActionVerb.Move, to, moveTargetDirectory: target, canonicalRoot, resolvedRoot, now);
    }

    FileActionPlanResult EvaluateTarget(
        FileActionSubject subject, FileActionVerb verb, string to, string? moveTargetDirectory,
        string canonicalRoot, string? resolvedRoot, DateTimeOffset now)
    {
        var (refusal, canonicalTo) = CheckDestinationJail(
            to, canonicalRoot, resolvedRoot, FileActionRule.OutsideRoot, FileActionRule.SymlinkEscape);
        if (refusal is { } targetRule)
            return FileActionPlanResult.Refused(targetRule);

        var canonicalFrom = Path.GetFullPath(subject.Path);
        if (string.Equals(canonicalTo, canonicalFrom, StringComparison.Ordinal))
            return FileActionPlanResult.Refused(FileActionRule.SameAsSource);

        // A move's destination directory must already EXIST as a directory (T379 review round 2 item
        // 3, ruling) — Missing refuses exactly like File does; the planner never implies an mkdir
        // under the jail.
        if (moveTargetDirectory is not null
            && fileSystemProbe.Kind(Path.GetFullPath(moveTargetDirectory)) != FileSystemEntryKind.Directory)
            return FileActionPlanResult.Refused(FileActionRule.TargetNotADirectory);

        if (fileSystemProbe.Kind(canonicalTo) != FileSystemEntryKind.Missing)
            return FileActionPlanResult.Refused(FileActionRule.TargetExists);

        return FileActionPlanResult.Planned(new FileActionPlan(
            subject.MediaId, subject.Xmin, verb, subject.Path, canonicalTo, [], now + PlanTtl));
    }

    /// <summary>The one destination gate every verb's own destination passes through (see this
    /// class's own remarks) — exempt-root (canonical AND resolved form), then root-containment, then
    /// symlink-escape, refusing at the first that fails. <paramref name="outsideRootRule"/>/
    /// <paramref name="symlinkEscapeRule"/> let the SAME method serve both the subject (which folds
    /// both failure shapes into <see cref="FileActionRule.SubjectOutsideRoot"/> — there is no
    /// separate "subject symlink escape" rule) and a computed target (which keeps them distinct).
    /// Returns the refusal (or <see langword="null"/> when the path passes) alongside the path's own
    /// canonical form, which every caller needs next regardless of outcome.</summary>
    (FileActionRule? Refusal, string CanonicalPath) CheckDestinationJail(
        string rawPath, string canonicalRoot, string? resolvedRoot,
        FileActionRule outsideRootRule, FileActionRule symlinkEscapeRule)
    {
        var canonicalPath = NormalizePath(Path.GetFullPath(rawPath));
        var resolvedPath = Resolve(canonicalPath);

        if (IsUnderAnyExemptRoot(canonicalPath, resolvedPath))
            return (FileActionRule.ExemptRoot, canonicalPath);

        if (!IsUnderRoot(canonicalPath, canonicalRoot))
            return (outsideRootRule, canonicalPath);

        if (!IsUnderRoot(resolvedPath, resolvedRoot))
            return (symlinkEscapeRule, canonicalPath);

        return (null, canonicalPath);
    }

    string? Resolve(string canonicalPath) =>
        fileSystemProbe.ResolveLinks(canonicalPath) is { } resolved ? NormalizePath(resolved) : null;

    bool IsUnderAnyExemptRoot(string canonicalPath, string? resolvedPath)
    {
        foreach (var raw in scanOptions.CurrentValue.QuarantineExemptRoots)
        {
            var exemptRoot = NormalizePath(Path.GetFullPath(raw));
            if (IsUnderRoot(canonicalPath, exemptRoot) || IsUnderRoot(resolvedPath, exemptRoot))
                return true;
        }

        return false;
    }

    static bool IsUnderRoot(string? canonicalPath, string? canonicalRoot) =>
        canonicalPath is not null && canonicalRoot is not null
        && (string.Equals(canonicalPath, canonicalRoot, StringComparison.Ordinal)
            || canonicalPath.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal));

    static bool ContainsTraversalSegment(string rawTarget) =>
        rawTarget.Split(PathSeparators).Any(segment => segment == "..");

    static string NormalizePath(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar);
        return trimmed.Length == 0 ? Path.DirectorySeparatorChar.ToString() : trimmed;
    }
}

namespace GenWave.Core.Domain;

/// <summary>
/// The closed set of reasons an <see cref="Abstractions.IFileActionPlanner"/> refuses a plan (SPEC
/// F154.3; STORY-379; PLAN T379, gh-#529) — <see cref="FileActionRefusal"/> carries exactly one of
/// these and NOTHING else: no path, no operator input, ever (F154.3's "path not echoed" rule). Named
/// so a caller (the dry-run endpoint, T381) can map each rule to a 400/409 shape and a fixed,
/// non-leaking message.
/// </summary>
public enum FileActionRule
{
    /// <summary>The subject's <c>library_id</c> is not the one scanned library
    /// (<c>ScanService.ScannedLibraryId</c> = 1) — there is no real root to jail it against.</summary>
    NotScannedLibrary,

    /// <summary>The subject's own path — a retag's own destination too, since retag never moves the
    /// file (T379 review B2) — canonicalised and symlink-resolved, does not start under the library
    /// root — a row this action must never have been asked to touch.</summary>
    SubjectOutsideRoot,

    /// <summary>The raw target string (an operator-supplied rename name, or a move destination
    /// directory) contains a literal <c>..</c> path segment — refused before <c>Path.GetFullPath</c>
    /// ever gets a chance to collapse it away, and before any filesystem probe call at all.</summary>
    Traversal,

    /// <summary>A move was requested with no destination directory at all — <see langword="null"/> or
    /// empty (T379 review round 2, item 4) — refused before <c>Path.GetFullPath</c> would otherwise
    /// throw on an empty string.</summary>
    MissingTarget,

    /// <summary>An operator-supplied rename name is empty, starts with <c>.</c> (T379 review round 2
    /// item 1 — <c>EnumerationOptions</c>' own default skips Hidden entries on Unix, and a leading
    /// dot IS Hidden there, so the renamed file would vanish from the very next scan tick, F154.6),
    /// contains a directory separator or any control character, exceeds 255 UTF-8 bytes, or names a
    /// different extension than the source file's own (T379 review B5/N9a) — also the answer when
    /// the TEMPLATE's own generated name still fails this same check after truncation (T379 review
    /// round 2 item 2: an enforced postcondition, never a silently-invalid plan).</summary>
    InvalidName,

    /// <summary>The computed target, canonicalised, does not start under the library root — also the
    /// answer for a move target that was never rooted to begin with (T379 review B1: refused before
    /// <c>Path.GetFullPath</c> would otherwise resolve it against the process's own working
    /// directory).</summary>
    OutsideRoot,

    /// <summary>The computed target (or the subject's own path) sits under the library root on its
    /// face, but a symlinked ancestor resolves it to somewhere outside the root.</summary>
    SymlinkEscape,

    /// <summary>The subject's own path, or the computed target — canonical form OR symlink-resolved
    /// form (T379 review B3: a symlink inside the root that points at an exempt directory still
    /// counts) — lies under one of <c>Library:Scan:QuarantineExemptRoots</c>: authored space the
    /// gardener must never write into, checked BEFORE the generic root-prefix rules above so this
    /// more specific reason wins.</summary>
    ExemptRoot,

    /// <summary>The computed target is textually identical to the subject's current path — a
    /// rename/move that would not actually move anything.</summary>
    SameAsSource,

    /// <summary>A move's destination directory does not already exist as a real directory — missing
    /// entirely, or occupied by a FILE instead (T379 review N9b, ruled further at round 2 item 3: the
    /// planner never implies creating the directory — a not-yet-existing target is refused exactly
    /// like one occupied by a file, never treated as an implicit mkdir).</summary>
    TargetNotADirectory,

    /// <summary>Something already occupies the computed target (F154.4 — never overwrite).</summary>
    TargetExists,

    /// <summary>A retag was requested but the catalog and the file's own tags already agree on every
    /// field the catalog has an opinion on — there is nothing to write.</summary>
    NothingToRetag,
}

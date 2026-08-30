namespace GenWave.Core.Domain;

/// <summary>
/// The catalog row an <see cref="Abstractions.IFileActionPlanner"/> plans against (SPEC F154.1,
/// F154.3, F154.5; STORY-379; PLAN T379, gh-#529) — a caller-assembled snapshot: the row itself
/// (<see cref="MediaId"/>/<see cref="Xmin"/>/<see cref="Path"/>/<see cref="LibraryId"/>), the
/// catalog's own tag fields, and (for a retag) the file's current tags as the caller last read them.
/// The planner never queries the database or opens the file itself — every fact it reasons about
/// arrives on this record.
/// </summary>
/// <param name="MediaId">The <c>library.media</c> row id — carried onto the resulting plan and, once
/// minted, onto the plan token (F154.5's <c>(media id, xmin, from, to)</c> binding).</param>
/// <param name="Xmin">The row's Postgres <c>xmin</c> concurrency token, as a string (the
/// <c>MediaRow.Xmin</c>/<c>AdminMediaDto.Version</c> convention this codebase already uses
/// everywhere else) — bound onto the plan token so a PATCH that lands between dry-run and confirm is
/// caught (F154.5, STORY-379 AC7).</param>
/// <param name="Path">The row's current <c>library.media.path</c> — the jail's subject and, for
/// every verb, the plan's own <see cref="FileActionPlan.From"/>.</param>
/// <param name="LibraryId">The row's current <c>library.media.library_id</c>. This codebase scans
/// exactly one library (<c>ScanService.ScannedLibraryId</c> = 1, root = <c>Library:MediaRoot</c>);
/// <c>library.library</c> carries no per-library root, so a subject whose <see cref="LibraryId"/>
/// is not that one library can never be jailed against a real root and is refused outright
/// (<c>FileActionRule.NotScannedLibrary</c>, SPEC F154.3's amendment ruling).</param>
/// <param name="Artist">The catalog's own artist value, or <see langword="null"/>.</param>
/// <param name="Title">The catalog's own title value, or <see langword="null"/>.</param>
/// <param name="Album">The catalog's own album value, or <see langword="null"/>.</param>
/// <param name="Year">The catalog's own year value, or <see langword="null"/>. <see cref="int"/> —
/// the catalog-side <c>library.media.year</c> column's own width, unlike
/// <see cref="FileTags.Year"/>'s file-tag-side <see cref="uint"/>.</param>
/// <param name="Genre">The catalog's own genre value, or <see langword="null"/>.</param>
/// <param name="CurrentFileTags">The file's own tags as the caller last read them (retag's diff
/// input), or <see langword="null"/> when the caller has no reading (rename/move never need one).
/// </param>
public sealed record FileActionSubject(
    long MediaId,
    string Xmin,
    string Path,
    long LibraryId,
    string? Artist,
    string? Title,
    string? Album,
    int? Year,
    string? Genre,
    FileTags? CurrentFileTags);

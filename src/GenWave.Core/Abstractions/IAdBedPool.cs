namespace GenWave.Core.Abstractions;

/// <summary>
/// Reads the installed background-music pool a rendering ad spot can pick from (SPEC F168.1;
/// STORY-403; PLAN T416) — the <see cref="ILibraryRepository"/> placement precedent applied to a
/// narrower, ads-specific query: a Core-level port a MediaLibrary repository implements directly,
/// never widening the published <c>GenWave.Abstractions</c> NuGet surface (an ads-only read seam has
/// no reason to leave this repo).
///
/// <para>
/// <b>The pool is <c>library.media</c> rows T414's jingle-pack install already wrote</b> —
/// <c>imaging_kind='jingle'</c>, <c>jingle_role='bed'</c> — never a separate table. This port never
/// installs, edits, or deletes a row; it only lists the ones currently eligible to be picked.
/// </para>
/// </summary>
public interface IAdBedPool
{
    /// <summary>
    /// Every currently airable background-music row installed into <paramref name="libraryId"/> —
    /// <c>state='ready'</c>, <c>unavailable_since is null</c>, <c>jingle_role='bed'</c> — ordered by
    /// id, so a caller indexing into the result deterministically (<c>GenWave.Ads.AdBedPicker</c>'s
    /// own seeded pick — plain text, not a <c>cref</c>: GenWave.Core never references GenWave.Ads,
    /// L10) sees the SAME ordering on every call. An empty list is a normal, legal answer (no pack
    /// installed yet) — never an error.
    /// </summary>
    Task<IReadOnlyList<long>> ListReadyBedIdsAsync(long libraryId, CancellationToken ct);
}

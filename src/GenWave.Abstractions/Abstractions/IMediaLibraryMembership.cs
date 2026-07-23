using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// gh-#99 — the one question the taste-thumb and booth-log surfaces need answered across the
/// station/library schema boundary: which of these media ids live in the given libraries?
/// <c>station.booth_log</c> rows are read by the station role, which deliberately has no grant on
/// <c>library.media</c> — so safe-content membership is resolved through this seam on the library
/// connection instead of a cross-schema SQL join, and the Host composes the two.
///
/// Deliberately its own narrow interface rather than a new <see cref="IMediaCatalog"/> member:
/// every existing catalog fake keeps compiling, and this seam's one consumer concern (exclusion
/// checks) never grows read-amplification temptations.
/// </summary>
public interface IMediaLibraryMembership
{
    /// <summary>
    /// Returns the subset of <paramref name="mediaIds"/> whose row's <c>library_id</c> falls in
    /// <paramref name="libraries"/>. Unknown ids are simply absent from the result — never an error.
    /// An empty <paramref name="libraries"/> scope returns the empty set without touching the
    /// database.
    /// </summary>
    Task<IReadOnlySet<long>> FilterToLibrariesAsync(
        IReadOnlyCollection<long> mediaIds, LibraryScope libraries, CancellationToken ct);
}

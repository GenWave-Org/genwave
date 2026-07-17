using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// Resolves the effective <see cref="LibraryScope"/> for a browse or filter operation.
///
/// Rule (F23.2 / STORY-064):
///   • Named library-id    → that single library IS the effective scope, regardless of whether it
///                           falls inside the station's rotation scope. <c>OutOfScope = true</c>
///                           when it is outside, signalling the UI to surface a banner.
///   • No library-id given → station scope as normal; never out-of-scope.
///
/// Scope is a curation boundary, not a trust boundary (F23.6, single-operator deployment).
/// </summary>
internal static class EffectiveScope
{
    /// <summary>
    /// Returns the scope the query should use and whether it sits outside the station's rotation.
    /// </summary>
    /// <param name="stationScope">The station's configured rotation scope.</param>
    /// <param name="namedLibraryId">
    ///   The library-id the caller explicitly named, or <c>null</c> for an unfiltered browse.
    /// </param>
    public static (LibraryScope Scope, bool OutOfScope) Resolve(
        LibraryScope stationScope,
        long? namedLibraryId)
    {
        if (namedLibraryId is null)
            return (stationScope, false);

        var scope      = new LibraryScope([namedLibraryId.Value]);
        var outOfScope = !stationScope.LibraryIds.Contains(namedLibraryId.Value);
        return (scope, outOfScope);
    }
}

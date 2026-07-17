using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// Checks whether a destination library is outside the station scope and, if so, sets the
/// <c>X-Out-Of-Scope: true</c> response header (STORY-048, Epic J).
///
/// Used by both single-row PATCH (<see cref="MediaController"/>) and the future bulk reassign
/// endpoint (L4 / STORY-049) so the header + scope check live in exactly one place.
///
/// Design (F43): scope is a curation signal, not a gate. GET/PATCH/reenrich accept any existing
/// row regardless of whether its subject row or destination library sits inside
/// <c>Station:Scope:LibraryIds</c> — there is no 403 path. This helper only stamps the warning
/// header so the UI can surface a confirmation prompt when the operator is parking or moving a
/// track outside the rotating scope.
/// </summary>
internal static class OutOfScopeWarning
{
    /// <summary>
    /// If <paramref name="destinationLibraryId"/> is NOT in <paramref name="scope"/>, sets
    /// <c>X-Out-Of-Scope: true</c> on <paramref name="response"/> and returns <c>true</c>.
    /// Returns <c>false</c> (and sets no header) when the destination is in scope.
    /// </summary>
    public static bool ApplyIfOutOfScope(
        Microsoft.AspNetCore.Http.HttpResponse response,
        long destinationLibraryId,
        LibraryScope scope)
    {
        if (scope.LibraryIds.Contains(destinationLibraryId))
            return false;

        response.Headers["X-Out-Of-Scope"] = "true";
        return true;
    }
}

namespace GenWave.Core.Domain;

/// <summary>
/// Identifies the set of libraries a catalog query is permitted to read. Default-deny: an empty scope
/// (see <see cref="None"/>) produces no results without touching the database. T009 replaces the
/// transitional hard-coded sentinel with the real config-bound <c>StationContext.Scope</c>.
/// </summary>
public sealed record LibraryScope(IReadOnlyCollection<long> LibraryIds)
{
    /// <summary>Empty sentinel — no libraries are in scope; all catalog methods short-circuit to null.</summary>
    public static readonly LibraryScope None = new([]);

    /// <summary>True when no library ids are present; catalog methods will not issue any SQL.</summary>
    public bool IsEmpty => LibraryIds.Count == 0;
}

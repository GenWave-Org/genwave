namespace GenWave.Core.Domain;

/// <summary>
/// Discriminated union expressing every outcome of an <c>IAdminLibraryWrite</c> operation (STORY-046, Epic J).
/// Cases that carry data (<see cref="Created"/>, <see cref="HasDependents"/>) are sealed records with
/// positional properties. Singleton cases (<see cref="Renamed"/>, <see cref="Deleted"/>,
/// <see cref="NotFound"/>, <see cref="NameConflict"/>) are sealed records with no payload.
/// The private constructor on the abstract base closes the hierarchy so callers can write
/// exhaustive pattern-match switches without a discard arm.
/// </summary>
public abstract record LibraryWriteResult
{
    private LibraryWriteResult() { }

    /// <summary>The library was created; <see cref="Id"/> is the new row's identity.</summary>
    public sealed record Created(long Id) : LibraryWriteResult;

    /// <summary>The library was successfully renamed.</summary>
    public sealed record Renamed : LibraryWriteResult;

    /// <summary>The library was successfully deleted.</summary>
    public sealed record Deleted : LibraryWriteResult;

    /// <summary>No library with the requested id exists.</summary>
    public sealed record NotFound : LibraryWriteResult;

    /// <summary>Another library already holds the requested name.</summary>
    public sealed record NameConflict : LibraryWriteResult;

    /// <summary>The library cannot be deleted because media rows still reference it.</summary>
    public sealed record HasDependents(int DependentMediaCount) : LibraryWriteResult;
}

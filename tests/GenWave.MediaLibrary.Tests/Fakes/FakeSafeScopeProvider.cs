using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Tests.Fakes;

/// <summary>
/// gh-#99 — fixed-value <see cref="ISafeScopeProvider"/> for repository specs. Defaults to the empty
/// scope (nothing is safe content — the pre-#99 behavior every pre-existing spec assumes); exclusion
/// specs construct it with the library ids they seed as safe.
/// </summary>
public sealed class FakeSafeScopeProvider(params long[] libraryIds) : ISafeScopeProvider
{
    public LibraryScope Current { get; } = new(libraryIds);
}

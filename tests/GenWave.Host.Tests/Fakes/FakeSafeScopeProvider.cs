using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// gh-#99 — fixed-value <see cref="ISafeScopeProvider"/> for controller specs. Defaults to the
/// empty scope (nothing is safe content — the pre-#99 behavior every pre-existing spec assumes);
/// exclusion specs construct it with the library ids they treat as safe.
/// </summary>
sealed class FakeSafeScopeProvider(params long[] libraryIds) : ISafeScopeProvider
{
    public LibraryScope Current { get; } = new(libraryIds);
}

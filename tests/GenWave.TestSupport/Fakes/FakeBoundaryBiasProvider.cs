using GenWave.Core.Abstractions;

namespace GenWave.TestSupport.Fakes;

/// <summary>
/// Mutable <see cref="IBoundaryBiasProvider"/> double (SPEC F74.3, mirrors
/// <see cref="FakeRenderBudgetProvider"/> one seam over). Set <see cref="Lookahead"/> between
/// calls to simulate a config-provider reload without standing up a real options stack in a unit
/// test.
/// </summary>
public sealed class FakeBoundaryBiasProvider(TimeSpan lookahead) : IBoundaryBiasProvider
{
    /// <summary>The lookahead window a spec can mutate between calls.</summary>
    public TimeSpan Lookahead { get; set; } = lookahead;

    /// <inheritdoc/>
    public TimeSpan Current => Lookahead;
}

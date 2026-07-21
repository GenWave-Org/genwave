using GenWave.Core.Abstractions;

namespace GenWave.Host.Tests;

/// <summary>
/// Mutable <see cref="IBoundaryBiasProvider"/> double (SPEC F74.3, mirrors
/// <see cref="FakeRenderBudgetProvider"/> one seam over). Set <see cref="Lookahead"/> between
/// calls to simulate a config-provider reload without standing up a real options stack in a unit
/// test.
/// </summary>
sealed class FakeBoundaryBiasProvider(TimeSpan lookahead) : IBoundaryBiasProvider
{
    public TimeSpan Lookahead { get; set; } = lookahead;

    public TimeSpan Current => Lookahead;
}

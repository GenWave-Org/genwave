namespace GenWave.Architecture.Tests.Fixtures.L10Probe.SliceA;

/// <summary>L10 probe: one half of a genuine two-slice namespace cycle — a real compiled property
/// reference into <see cref="SliceB.TypeB"/>, which references back to this type in turn. Proves
/// <c>NamespaceCycleFence</c> reaches an actual ArchUnitNET slice cycle, not a lookalike, and reports
/// it tagged <see cref="GenWave.Architecture.Tests.Support.LawId.L10"/>.</summary>
public sealed class TypeA
{
    public SliceB.TypeB? Other { get; init; }
}

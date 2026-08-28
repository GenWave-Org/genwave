namespace GenWave.Architecture.Tests.Fixtures.L10Probe.SliceB;

/// <summary>L10 probe: the other half of the genuine cycle — references back to
/// <see cref="SliceA.TypeA"/>, closing the loop <see cref="SliceA.TypeA"/>'s own remarks describe.</summary>
public sealed class TypeB
{
    public SliceA.TypeA? Other { get; init; }
}

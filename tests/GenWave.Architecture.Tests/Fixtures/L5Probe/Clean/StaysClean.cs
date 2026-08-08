// Fixture type for STORY-292 AC2's self-exercising negative probe (Story292_HostTripwire.cs).
// Never wired into any DI container or call path.

namespace GenWave.Architecture.Tests.Fixtures.L5Probe.Clean;

/// <summary>Outside the probe's reserved stand-in namespace — proves the rule doesn't fail
/// indiscriminately (the L2/L3 "clean elsewhere fixture" precedent).</summary>
public sealed class StaysClean
{
    public int Value => 1;
}

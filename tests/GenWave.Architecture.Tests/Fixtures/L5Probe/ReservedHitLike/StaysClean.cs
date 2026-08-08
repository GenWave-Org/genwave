// Fixture type for STORY-292 AC2's self-exercising negative probe (Story292_HostTripwire.cs).
// Never wired into any DI container or call path — the F1 review's same-prefix-lookalike proof: this
// namespace's text literally STARTS WITH "...L5Probe.ReservedHit" (no dot boundary), the exact shape a
// bare StartsWith match on the reservation would wrongly catch.

namespace GenWave.Architecture.Tests.Fixtures.L5Probe.ReservedHitLike;

/// <summary>Outside the probe's reserved namespace despite the same-prefix namespace text — proves
/// <see cref="GenWave.Architecture.Tests.Support.HostNamespaceTripwire"/>'s segment-boundary matching
/// (via <see cref="GenWave.Architecture.Tests.Support.AssemblyReferenceScan.HasFamilyPrefix"/>) is
/// real, not a bare <c>StartsWith</c>.</summary>
public sealed class StaysClean
{
    public int Value => 1;
}

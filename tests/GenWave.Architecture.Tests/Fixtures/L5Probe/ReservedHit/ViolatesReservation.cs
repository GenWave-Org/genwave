// Fixture type for STORY-292 AC2's self-exercising negative probe (Story292_HostTripwire.cs).
// Never wired into any DI container or call path — stands in for a subsystem's logic landing under a
// reserved namespace (the exact shape the graduation rule forbids: e.g. IContextProvider's logic
// landing under GenWave.Host.Context, gh-#378).

namespace GenWave.Architecture.Tests.Fixtures.L5Probe.ReservedHit;

/// <summary>An ordinary, hand-written type under the probe's reserved stand-in namespace — the plain
/// case <see cref="GenWave.Architecture.Tests.Support.HostNamespaceTripwire"/> must catch and
/// attribute directly (no compiler-generated nesting involved).</summary>
public sealed class ViolatesReservation
{
}

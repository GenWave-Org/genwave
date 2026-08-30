// Fixture type for STORY-380 AC3's self-exercising negative probe (Story380_GardenerNamespaceAndDisjointness.cs).
// Never wired into any DI container or call path — namespaced GenWave.Host.Gardener (the REAL
// production namespace text HostReservedNamespaces.Entries reserves as of T357, not a probe-local
// stand-in) so the fact proves the ACTUAL seeded reservation, not merely that the detector mechanism
// works in the abstract (Story292_HostTripwire.cs already proves the mechanism generically).

namespace GenWave.Host.Gardener;

/// <summary>An ordinary, hand-written type under the Gardener's reserved namespace — stands in for
/// gardener logic (SPEC F155.2, e.g. a GardenerService or a rot-finding pass) accidentally landing in
/// GenWave.Host instead of GenWave.MediaLibrary/Garden.</summary>
public sealed class ViolatesReservation
{
}

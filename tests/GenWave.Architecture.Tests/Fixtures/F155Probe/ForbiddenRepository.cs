// Fixture type for STORY-380 AC4's self-exercising negative probe (Story380_GardenerNamespaceAndDisjointness.cs,
// PLAN T367 review MED-3). Never wired into any DI container or call path — stands in for the real
// production forbidden types (MediaRatingRepository/PersonaTasteAccrualRepository) so this probe stays
// fully decoupled from GenWave.MediaLibrary's own type graph (the Story292_HostTripwire.cs "probe-local
// reservation list" precedent, one law over).

namespace GenWave.Architecture.Tests.Fixtures.F155Probe;

/// <summary>The probe's own stand-in "forbidden" repository — a plain, self-contained type
/// <see cref="ProbeAction"/> reaches through an async lambda and an async local function, proving
/// <see cref="GenWave.Architecture.Tests.Support.GardenerThumbDisjointnessScan"/> follows BOTH shapes
/// (T367 review HIGH-1) rather than only Roslyn's plain-named-method state-machine naming.</summary>
public sealed class ForbiddenRepository
{
    public void Touch()
    {
    }
}

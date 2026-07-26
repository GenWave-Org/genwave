// STORY-238 — The shelf cannot touch the air (SPEC F90.8, PLAN T106)
//
// BDD specification — xUnit, pending. Structural isolation pins: the catalog surface is
// admin-plane only, and its absence/failure is invisible everywhere else. Entry-point
// discipline: spectator surface probed on the public listener, catalog endpoints on the
// admin surface, both via WebApplicationFactory<Program>.

namespace GenWave.Host.Tests.Specs;

public static class FeatureShelfCannotTouchAir
{
    public sealed class ScenarioByteIdenticalWithoutTheCatalog
    {
        // Given the catalog disabled (empty URL) and separately an unreachable origin.

        [Fact(Skip = "Pending (T106)")]
        public void SpectatorDisclosurePayloadsGainNoNewFields() { }

        [Fact(Skip = "Pending (T106)")]
        public void PublicListenerExposesNoCatalogRoute() { }

        [Fact(Skip = "Pending (T106)")]
        public void UnreachableCatalogLeavesPlayoutTicksUntouched() { }
    }

    public sealed class ScenarioPolicyParityWithTheImportEndpoint
    {
        // Given unauthenticated and under-privileged callers.

        [Fact(Skip = "Pending (T106)")]
        public void UnauthenticatedCatalogCallMatchesImportEndpointResponse() { }

        [Fact(Skip = "Pending (T106)")]
        public void UnderPrivilegedCatalogCallMatchesImportEndpointResponse() { }
    }
}

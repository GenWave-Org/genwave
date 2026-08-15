// STORY-337 — Icon packs swap the chrome (SPEC F130.1–.5 · PLAN T302 model + T303 endpoints)
//
// BDD specification — xUnit. Backend halves: definition validation (T302) and
// install/activation plumbing (T303). The renderer, per-name fallback, currentColor
// discipline, and the dangling-setting notice (AC2/AC3/AC4/AC6 UI halves) live in
// admin-ui jest (icon-pack-renderer.spec.tsx) + the T306 wire.

namespace GenWave.Host.Tests.Specs;

public static class FeatureIconPacksSwapTheChrome
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the definition model (T302)
    // ---------------------------------------------------------------------

    public sealed class ScenarioAValidDefinitionPasses
    {
        [Fact(Skip = "Pending T302 — see docs/PLAN.md")]
        public void WhitelistPrimitivesWithNumericAttrsValidate()
        {
            // path/rect/circle/ellipse/line/polyline/polygon; d matches the grammar;
            // fills/strokes only none|currentColor; ≤256 KiB.
            Assert.Fail("pending T302");
        }

        [Fact(Skip = "Pending T302 — see docs/PLAN.md")]
        public void NamesOutsideTheContractAreIgnoredWithOneWarn()
        {
            Assert.Fail("pending T302");
        }

        [Fact(Skip = "Pending T302 — see docs/PLAN.md")]
        public void TheIconNameContractMatchesTheHouseIconExports()
        {
            // The app constant and icons.tsx's export set cannot drift (parity pin,
            // the T68 golden-table idiom).
            Assert.Fail("pending T302");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — install + activation (T303)
    // ---------------------------------------------------------------------

    public sealed class ScenarioInstallAndActivate
    {
        [Fact(Skip = "Pending T303 — see docs/PLAN.md")]
        public void InstallStoresTheDefinitionKeyedBySlug()
        {
            Assert.Fail("pending T303");
        }

        [Fact(Skip = "Pending T303 — see docs/PLAN.md")]
        public void StationIconPackIsAnAllowlistedLiveSetting()
        {
            // Default "" = house icons; dropdown control fed by installed packs.
            Assert.Fail("pending T303");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — hostile definitions and the fail-open uninstall
    // ---------------------------------------------------------------------

    public sealed class ScenarioAHostileDefinitionCannotLand
    {
        [Fact(Skip = "Pending T302 — see docs/PLAN.md")]
        public void AnUnknownTagRejectsNamingTheRule()
        {
            Assert.Fail("pending T302");
        }

        [Fact(Skip = "Pending T302 — see docs/PLAN.md")]
        public void ANonNumericGeometryAttrRejects()
        {
            Assert.Fail("pending T302");
        }

        [Fact(Skip = "Pending T302 — see docs/PLAN.md")]
        public void ALiteralColorRejects()
        {
            // Only none|currentColor are expressible — hue stays token-bound.
            Assert.Fail("pending T302");
        }

        [Fact(Skip = "Pending T302 — see docs/PLAN.md")]
        public void AnOversizeDefinitionRejects()
        {
            Assert.Fail("pending T302");
        }
    }

    public sealed class ScenarioUninstallingTheActivePackFailsOpen
    {
        [Fact(Skip = "Pending T303 — see docs/PLAN.md")]
        public void TheActiveResolutionAnswersTheHouseSetAfterUninstall()
        {
            // No cross-store write from the DELETE; the resolver answers "house icons"
            // for a dangling Station:IconPack value.
            Assert.Fail("pending T303");
        }
    }
}

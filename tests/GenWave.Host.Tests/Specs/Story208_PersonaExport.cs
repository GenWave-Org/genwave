// STORY-208 — Card export carries character, never memory
//
// BDD specification — xUnit (SPEC F79.1, F79.2). PLAN T66 wires the export endpoint. The
// deployed-entry-point scenarios drive the real admin route (WebApplicationFactory, Story181
// idiom) so "zero accrued rows" is proven at the production surface, not an internal method.

namespace GenWave.Host.Tests.Specs;

public static class FeaturePersonaCardExport
{
    public static class ScenarioExportOfALivingPersona
    {
        // Arrange (T66): persona seeded with card fields + authored lore + authored taste
        // + ACCRUED memory and ACCRUED taste; GET the export via the admin API entry point.

        [Fact(Skip = "Pending T66 — see docs/PLAN.md")]
        public static void ExportContainsSchemaVersionAndCardFields()
        {
            // Assert.Equal(1, export.SchemaVersion.Major);  // F79.1 shape
            Assert.Fail("pending T66");
        }

        [Fact(Skip = "Pending T66 — see docs/PLAN.md")]
        public static void ExportContainsAuthoredLore()
        {
            Assert.Fail("pending T66");
        }

        [Fact(Skip = "Pending T66 — see docs/PLAN.md")]
        public static void ExportContainsAuthoredTasteRules()
        {
            Assert.Fail("pending T66");
        }

        [Fact(Skip = "Pending T66 — see docs/PLAN.md")]
        public static void ExportContainsZeroAccruedRowsOfAnyKind()
        {
            // the seeded persona HOLDS accrued memory and accrued taste; the export must hold none (F79.1)
            Assert.Fail("pending T66");
        }

        [Fact(Skip = "Pending T66 — see docs/PLAN.md")]
        public static void ExportFileNameIsSlugPersonaJson()
        {
            // Content-Disposition: <slug>.persona.json (F79.1)
            Assert.Fail("pending T66");
        }
    }

    public static class SadPathUnknownSlug
    {
        [Fact(Skip = "Pending T66 — see docs/PLAN.md")]
        public static void ExportOfUnknownSlugReturns404()
        {
            Assert.Fail("pending T66");
        }
    }
}

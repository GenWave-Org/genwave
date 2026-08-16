// STORY-334 — Faces arrive with adoption (SPEC F128.7/.8 · PLAN T297)
//
// BDD specification — xUnit. Backend halves only: the trust modal's face render
// (AC1) is admin-ui jest (adoption-shows-the-face.spec.tsx). Skip-pinned until T297.

namespace GenWave.Host.Tests.Specs;

public static class FeatureFacesArriveWithAdoption
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioConfirmedImportInstallsTheFace
    {
        [Fact(Skip = "Pending T297 — see docs/PLAN.md")]
        public void TheImportedPersonaWearsTheEntrysFace()
        {
            // Catalog persona entry with an avatar asset → import → persona_avatar row
            // (source='catalog', imported_from = the entry slug), token minted.
            Assert.Fail("pending T297");
        }

        [Fact(Skip = "Pending T297 — see docs/PLAN.md")]
        public void AFacelessEntryImportsExactlyAsBefore()
        {
            Assert.Fail("pending T297");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the byte-stability fences
    // ---------------------------------------------------------------------

    public sealed class ScenarioFileImportAndExportAreUntouched
    {
        [Fact(Skip = "Pending T297 — see docs/PLAN.md")]
        public void FileUploadImportAcceptsCardJsonOnly()
        {
            // No image side-channel exists on the file path.
            Assert.Fail("pending T297");
        }

        [Fact(Skip = "Pending T297 — see docs/PLAN.md")]
        public void ExportBytesAreIdenticalToThePreF128Shape()
        {
            // A faced persona exports the same bytes as an unfaced one with the same card
            // (the F79 golden-card pin extended, not changed).
            Assert.Fail("pending T297");
        }
    }
}

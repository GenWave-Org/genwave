// STORY-333 — The worn face (SPEC F128.5/.6/.9 · PLAN T291 pipeline + T295 endpoints)
//
// BDD specification — xUnit. The normalize pipeline's own gates pin to T291; the
// endpoints pin to T295. The Personas-page render/placeholder/offer UI (AC2/AC4's UI
// halves) lives in admin-ui jest (persona-faces.spec.tsx) + the T301 wire.

namespace GenWave.Host.Tests.Specs;

public static class FeatureTheWornFace
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the pipeline (T291)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheNormalizePipelineProducesACleanFace
    {
        [Fact(Skip = "Pending T291 — see docs/PLAN.md")]
        public void OutputIsAFresh512SquarePng()
        {
            // Any accepted input (PNG or JPEG, any plausible dims) → 512×512 PNG bytes.
            Assert.Fail("pending T291");
        }

        [Fact(Skip = "Pending T291 — see docs/PLAN.md")]
        public void NonSquareInputIsCenterCropped()
        {
            Assert.Fail("pending T291");
        }

        [Fact(Skip = "Pending T291 — see docs/PLAN.md")]
        public void MetadataIsStructurallyAbsentFromTheOutput()
        {
            // EXIF/GPS/text chunks in the input do not survive the re-encode.
            Assert.Fail("pending T291");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the write paths (T295)
    // ---------------------------------------------------------------------

    public sealed class ScenarioWearingAPackFaceCopiesIt
    {
        [Fact(Skip = "Pending T295 — see docs/PLAN.md")]
        public void TheFaceRowIsACopyWithCatalogProvenance()
        {
            // from-pack → persona_avatar(source='catalog', imported_from=pack slug).
            Assert.Fail("pending T295");
        }

        [Fact(Skip = "Pending T295 — see docs/PLAN.md")]
        public void TheTokenRotatesOnTheWrite()
        {
            Assert.Fail("pending T295");
        }
    }

    public sealed class ScenarioOwnerUploadWearsAndRemoves
    {
        [Fact(Skip = "Pending T295 — see docs/PLAN.md")]
        public void PutStoresTheNormalizedFaceWithSourceUpload()
        {
            Assert.Fail("pending T295");
        }

        [Fact(Skip = "Pending T295 — see docs/PLAN.md")]
        public void DeleteRemovesTheRow()
        {
            Assert.Fail("pending T295");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — hostile input dies at the gate (T291/T295)
    // ---------------------------------------------------------------------

    public sealed class ScenarioHostileUploadsDieQuietlyAtTheGates
    {
        [Fact(Skip = "Pending T291 — see docs/PLAN.md")]
        public void ANonImageBodyFailsTheMagicGateBeforeAnyDecoderRuns()
        {
            Assert.Fail("pending T291");
        }

        [Fact(Skip = "Pending T291 — see docs/PLAN.md")]
        public void OversizeDimensionsFailTheHeaderGateBeforeFfmpeg()
        {
            // >4096px per IHDR/SOF (decompression-bomb class) and <256px both 400.
            Assert.Fail("pending T291");
        }

        [Fact(Skip = "Pending T295 — see docs/PLAN.md")]
        public void ADecodeFailureLeavesThePreviousFaceUnchanged()
        {
            Assert.Fail("pending T295");
        }
    }
}

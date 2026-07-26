// STORY-235 — One click, eyes open: informed catalog import (SPEC F90.5–F90.6, PLAN T103)
//
// BDD specification — xUnit, pending. The review-modal gating (no request without confirm)
// is T103 browser acceptance; these facts pin the server-side parity contract: a card fetched
// from the catalog and the same card hand-uploaded MUST land identically through the one F79
// import endpoint (entry-point discipline: both flows driven via WebApplicationFactory).

namespace GenWave.Host.Tests.Specs;

public static class FeatureInformedCatalogImport
{
    public sealed class ScenarioCatalogImportEqualsFileImport
    {
        // Given the same valid card obtained via /api/catalog/entries/{slug} and as a raw
        // file body, When both are POSTed to the F79 import endpoint.

        [Fact(Skip = "Pending (T103)")]
        public void BothImportsSucceedWithIdenticalResponses() { }

        [Fact(Skip = "Pending (T103)")]
        public void UnresolvableVoiceWarnsIdenticallyOnBothPaths() { }

        [Fact(Skip = "Pending (T103)")]
        public void AccruedRowsSurviveBothReImportPaths() { }
    }

    public sealed class ScenarioRejectingInvalidCards
    {
        // Sad path — F79.6 caps and validation hold identically for catalog-sourced bytes.

        [Fact(Skip = "Pending (T103)")]
        public void OversizeCatalogCardIsRejectedTransactionally() { }

        [Fact(Skip = "Pending (T103)")]
        public void NewerSchemaMajorFromCatalogIsRejectedNamingBothVersions() { }
    }
}

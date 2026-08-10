// STORY-315 — Hire a show from the shelf (F118.2, F118.3, F118.5)
//
// BDD specification — xUnit, PENDING scaffold (planned 2026-08-10). Comment-bodied on
// purpose: the import endpoint lands at T254 through the F79 shell (caps, schema-major
// reject, transactional no-partial upsert, provenance). The shelf/modal UI half is jest
// (catalog-show-shelf.spec.tsx). Cross-repo golden parity follows the T107 precedent.

namespace GenWave.Host.Tests.Specs;

using Xunit;

public static class FeatureShowImport
{
    public sealed class ScenarioImportThroughTheShell
    {
        [Fact(Skip = "Pending (T254)")]
        public void ImportUpsertsTransactionallyWithProvenance()
        {
            // Given a valid show card fetched by catalogSlug
            // When  POST /api/shows/{slug}/import runs
            // Then  the show lands whole with imported_from = catalogSlug, imported_at set
        }

        [Fact(Skip = "Pending (T254)")]
        public void FileUploadStampsFile()
        {
            // Given a direct file upload of a show manifest
            // When  the import runs
            // Then  imported_from = "file" (the F103.6 provenance triple)
        }

        [Fact(Skip = "Pending (T254)")]
        public void GoldenParityPinsTheCrossRepoContract()
        {
            // Given fixtures/golden.show.json (the catalog repo pins the same bytes)
            // When  the manifest parser consumes it
            // Then  it round-trips — the T107/T193 two-repo drift guard extended to shows
        }
    }

    public sealed class ScenarioSoftSuggestion
    {
        [Fact(Skip = "Pending (T254)")]
        public void ImportSucceedsWithSuggestionAbsentUnknownOrHired()
        {
            // Given a card whose suggestedPersona is missing, unknown, or already hired
            // When  the show imports
            // Then  it succeeds with no offer and no error (soft means soft — F118.3)
        }
    }

    public sealed class ScenarioRejectingBadImports
    {
        [Fact(Skip = "Pending (T254)")]
        public void SchemaMajorAndSizeCapRejectAsTheShellDoes()
        {
            // Given a newer-major manifest (or an over-cap body / >2× flavor)
            // When  the import runs
            // Then  it fails closed naming both versions / the cap — no partial write
        }

        [Fact(Skip = "Pending (T254)")]
        public void SpectatorIsByteIdenticalWithCatalogUnreachable()
        {
            // Given the catalog disabled or unreachable
            // When  spectator payloads are read
            // Then  byte-identical (F103.12 inherited verbatim — F118.5)
        }
    }
}

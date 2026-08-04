// STORY-272 — Importing a theme (SPEC F103.6)
//
// BDD specification — xUnit. POST /api/themes/{slug}/import reuses the F79 persona-import shell:
// AdminSurface + Settings auth, a size-capped bounded body read, deserialization-as-validation via
// ThemeManifestParser, a schema-major reject naming both versions, ?catalogSlug/'file'/null
// provenance, and a transactional, no-partial upsert into station.theme.
//
// PENDING T184 (wire) — a wire task, so the happy-path Scenario drives the real endpoint through
// WebApplicationFactory<Program> (production pipeline, fake store adapter). One assertion per Fact;
// the sad path (413/400/409) is its own block.

using Xunit;

namespace GenWave.Host.Tests.Specs;

public static class FeatureThemeImport
{
    const string Pending = "pending T184 — POST /api/themes/{slug}/import (F79 shell)";

    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioACatalogThemeImports
    {
        [Fact(Skip = Pending)]
        public void ItRespondsSuccessAndStoresWithCatalogProvenance()
        {
            // Given a valid theme and a catalogSlug,
            // When POST /api/themes/{slug}/import is called (the real endpoint),
            // Then it responds success, the theme is stored, and imported_from is the catalog slug (AC1).
            Assert.Fail(Pending);
        }
    }

    public sealed class ScenarioAFileUploadImports
    {
        [Fact(Skip = Pending)]
        public void ItIsStoredWithFileProvenance()
        {
            // Given a valid theme manifest uploaded directly (no catalogSlug),
            // When the import runs,
            // Then it is stored with imported_from "file" (AC2).
            Assert.Fail(Pending);
        }
    }

    public sealed class ScenarioTheWriteIsTransactional
    {
        [Fact(Skip = Pending)]
        public void NoPartialThemeRowRemainsOnFailure()
        {
            // Given an import that fails partway,
            // When it errors,
            // Then no partial theme row remains (AC3) — no-partial-writes.
            Assert.Fail(Pending);
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public sealed class ScenarioRejectingBadImports
    {
        [Fact(Skip = Pending)]
        public void AnOversizeBodyIsRefusedWith413()
        {
            // Given an import body over the size cap,
            // When it is posted,
            // Then it responds 413 and nothing is stored (AC4).
            Assert.Fail(Pending);
        }

        [Fact(Skip = Pending)]
        public void AnInvalidManifestIsRefusedWith400()
        {
            // Given a body that does not deserialize to a ThemeManifest,
            // When it is posted,
            // Then it responds 400 and nothing is stored (AC5) — deserialization-as-validation.
            Assert.Fail(Pending);
        }

        [Fact(Skip = Pending)]
        public void ANewerMajorManifestIsRefusedNamingBothVersions()
        {
            // Given a manifest whose schema major exceeds the app's,
            // When it is posted,
            // Then it responds 400 naming both versions (AC6).
            Assert.Fail(Pending);
        }
    }
}

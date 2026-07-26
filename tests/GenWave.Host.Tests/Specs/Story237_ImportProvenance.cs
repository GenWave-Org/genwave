// STORY-237 — Where did this DJ come from? (SPEC F90.7, PLAN T98, T105)
//
// BDD specification — xUnit, pending. Provenance stamping through the real import endpoint
// (db/24: persona.imported_from / imported_at); the Personas-page badge itself is T105
// browser acceptance — these facts pin the columns, stamps, and projection.

namespace GenWave.Host.Tests.Specs;

public static class FeatureImportProvenance
{
    public sealed class ScenarioStampsOnImport
    {
        // Given one catalog import (entry slug known), one file import, one authored-in-place
        // persona, When each commits.

        [Fact(Skip = "Pending (T98)")]
        public void CatalogImportStampsTheEntrySlug() { }

        [Fact(Skip = "Pending (T98)")]
        public void FileImportStampsFile() { }

        [Fact(Skip = "Pending (T98)")]
        public void BothImportPathsStampImportedAt() { }

        [Fact(Skip = "Pending (T98)")]
        public void AuthoredPersonaKeepsNullProvenance() { }

        [Fact(Skip = "Pending (T98)")]
        public void PersonaProjectionExposesProvenanceFields() { }
    }

    public sealed class ScenarioReImport
    {
        // Given an already-imported slug, When the same card is imported again.

        [Fact(Skip = "Pending (T98)")]
        public void ReImportRefreshesTheStamp() { }
    }
}

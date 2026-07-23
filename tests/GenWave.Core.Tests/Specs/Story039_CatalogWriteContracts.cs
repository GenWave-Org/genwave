// STORY-039 — Catalog write contract + schema (the Core contract half)
//
// BDD specification — xUnit. Pure reflection over Core (no I/O).
// Specs Skip-pinned until W1 (the batched contract + schema) lands. See docs/PLAN.md Epic I.
// The schema half of STORY-039 lives in GenWave.MediaLibrary.Tests.

using System.Reflection;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Core.Tests.Specs;

public static class FeatureCatalogWriteContracts
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioIAdminMediaWriteContractLivesInCore
    {
        [Fact]
        public void TypeIsInCoreAbstractions()
        {
            Assert.Equal("GenWave.Core.Abstractions", typeof(IAdminMediaWrite).Namespace);
        }

        [Fact]
        public void IsAnInterface()
        {
            Assert.True(typeof(IAdminMediaWrite).IsInterface);
        }

        [Fact]
        public void UpdateReturningVersionAsyncReturnsMediaUpdateOutcomeTask()
        {
            // gh-#4 re-pinned the contract from the legacy UpdateAsync to the ETag-returning path.
            var m = typeof(IAdminMediaWrite).GetMethod("UpdateReturningVersionAsync")!;
            Assert.Equal(typeof(Task<MediaUpdateOutcome>), m.ReturnType);
        }

        [Fact]
        public void UpdateReturningVersionAsyncTakesIdPatchExpectedVersionScopeAndCancellationToken()
        {
            // params: (string id, MediaPatch patch, string expectedVersion, LibraryScope scope, CancellationToken ct)
            var m = typeof(IAdminMediaWrite).GetMethod("UpdateReturningVersionAsync")!;
            var p = m.GetParameters();
            Assert.Equal(5, p.Length);
            Assert.Equal(typeof(string), p[0].ParameterType);
            Assert.Equal(typeof(MediaPatch), p[1].ParameterType);
            Assert.Equal(typeof(string), p[2].ParameterType);
            Assert.Equal(typeof(LibraryScope), p[3].ParameterType);
            Assert.Equal(typeof(CancellationToken), p[4].ParameterType);
        }

        [Fact]
        public void TheLegacyUpdateAsyncStaysRetired()
        {
            // gh-#4 — zero production callers; a reintroduction would be dead contract surface.
            Assert.Null(typeof(IAdminMediaWrite).GetMethod("UpdateAsync"));
        }
    }

    public sealed class ScenarioMediaPatchValueType
    {
        [Fact]
        public void MediaPatchIsASealedRecord()
        {
            Assert.True(typeof(MediaPatch).IsSealed);
        }

        [Fact]
        public void HasNullableStringTagFields()
        {
            // MediaPatch carries nullable Title, Artist, Album, Genre (string?) — only-present fields applied
            Assert.Equal(typeof(string), typeof(MediaPatch).GetProperty("Title")!.PropertyType);
            Assert.Equal(typeof(string), typeof(MediaPatch).GetProperty("Artist")!.PropertyType);
            Assert.Equal(typeof(string), typeof(MediaPatch).GetProperty("Album")!.PropertyType);
            Assert.Equal(typeof(string), typeof(MediaPatch).GetProperty("Genre")!.PropertyType);
        }

        [Fact]
        public void HasNullableYear()
        {
            Assert.Equal(typeof(int?), typeof(MediaPatch).GetProperty("Year")!.PropertyType);
        }

        [Fact]
        public void HasNullableEligible()
        {
            Assert.Equal(typeof(bool?), typeof(MediaPatch).GetProperty("Eligible")!.PropertyType);
        }
    }

    public sealed class ScenarioMediaWriteResultExpressesEachOutcome
    {
        // SPEC F43.2 (Epic V, closes gitea-#203) supersedes this fact: OutOfScope was retired from the
        // single-row write path (scope is a curation filter, not an access gate) and, being fully
        // unused, was deleted from the enum outright rather than kept as a dead member.
        [Fact]
        public void DistinguishesUpdatedConflictNotFoundUnknownLibraryId()
        {
            // MediaWriteResult can represent each of: Updated, Conflict, NotFound, UnknownLibraryId
            var values = Enum.GetValues<MediaWriteResult>();
            Assert.Contains(MediaWriteResult.Updated, values);
            Assert.Contains(MediaWriteResult.Conflict, values);
            Assert.Contains(MediaWriteResult.NotFound, values);
            Assert.Contains(MediaWriteResult.UnknownLibraryId, values);
        }
    }
}

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

    /// <summary>
    /// T378 review MED-1 — <see cref="IAdminMediaWrite.SetEligibilityAsync(MediaQuery,IReadOnlyList{long}?,bool,LibraryScope,CancellationToken)"/>'s
    /// own default body must fail LOUD on a double that has not overridden it, never silently widen
    /// the write by falling through to the four-parameter overload with a non-empty id list ignored.
    /// </summary>
    public sealed class ScenarioIdScopedEligibilityDefaultFailsLoudNotOpen
    {
        /// <summary>A fake implementing ONLY the interface's REQUIRED members (the historic
        /// four-parameter <c>SetEligibilityAsync</c> included) — it never overrides the id-scoped
        /// overload, exactly the shape every one of this codebase's 13 existing test doubles has.</summary>
        sealed class FakeWriteWithoutIdOverride : IAdminMediaWrite
        {
            public Task<MediaUpdateOutcome> UpdateReturningVersionAsync(
                string id, MediaPatch patch, string expectedVersion, LibraryScope scope, CancellationToken ct) =>
                throw new NotImplementedException();

            public Task<int> SetEligibilityAsync(MediaQuery filter, bool eligible, LibraryScope scope, CancellationToken ct) =>
                Task.FromResult(0);

            public Task<int?> BulkReassignAsync(MediaQuery filter, long toLibraryId, LibraryScope scope, CancellationToken ct) =>
                throw new NotImplementedException();
        }

        [Fact]
        public async Task NonEmptyMediaIdsThrowsNotSupportedRatherThanWideningTheWrite()
        {
            IAdminMediaWrite fake = new FakeWriteWithoutIdOverride();

            await Assert.ThrowsAsync<NotSupportedException>(() =>
                fake.SetEligibilityAsync(new MediaQuery(), [1], true, LibraryScope.None, CancellationToken.None));
        }

        [Fact]
        public async Task NullMediaIdsStillFallsThroughToTheFourParameterOverload()
        {
            IAdminMediaWrite fake = new FakeWriteWithoutIdOverride();

            var affected = await fake.SetEligibilityAsync(new MediaQuery(), null, true, LibraryScope.None, CancellationToken.None);

            Assert.Equal(0, affected);
        }

        /// <summary>T378 review LOW-A — <see cref="IAdminMediaWrite.SetEligibilityAsync(MediaQuery,IReadOnlyList{long}?,bool,LibraryScope,CancellationToken)"/>'s
        /// own doc promises "null OR empty" applies no id constraint; the fact above only pins the
        /// null half — an EMPTY list must fall through exactly like null does, never throw.</summary>
        [Fact]
        public async Task EmptyMediaIdsAlsoFallsThroughToTheFourParameterOverload()
        {
            IAdminMediaWrite fake = new FakeWriteWithoutIdOverride();

            var affected = await fake.SetEligibilityAsync(new MediaQuery(), [], true, LibraryScope.None, CancellationToken.None);

            Assert.Equal(0, affected);
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

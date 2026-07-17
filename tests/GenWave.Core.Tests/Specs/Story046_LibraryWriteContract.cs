// STORY-046 — Library CRUD contract + schema (the Core contract half)
//
// BDD specification — xUnit. Pure reflection over Core (no I/O).
// The schema half of STORY-046 lives in GenWave.MediaLibrary.Tests (Skip-pinned, DB required).
// See docs/PLAN.md Epic J.

using System.Reflection;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Core.Tests.Specs;

public static class FeatureLibraryWriteContract
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioIAdminLibraryWriteContractLivesInCore
    {
        [Fact]
        public void TypeIsInCoreAbstractions()
        {
            Assert.Equal("GenWave.Core.Abstractions", typeof(IAdminLibraryWrite).Namespace);
        }

        [Fact]
        public void IsAnInterface()
        {
            Assert.True(typeof(IAdminLibraryWrite).IsInterface);
        }

        [Fact]
        public void CreateAsyncReturnsLibraryWriteResultTask()
        {
            var m = typeof(IAdminLibraryWrite).GetMethod("CreateAsync")!;
            Assert.Equal(typeof(Task<LibraryWriteResult>), m.ReturnType);
        }

        [Fact]
        public void RenameAsyncTakesIdNameAndCancellationToken()
        {
            var m = typeof(IAdminLibraryWrite).GetMethod("RenameAsync")!;
            var p = m.GetParameters();
            Assert.Equal(3, p.Length);
            Assert.Equal(typeof(long), p[0].ParameterType);
            Assert.Equal(typeof(string), p[1].ParameterType);
            Assert.Equal(typeof(CancellationToken), p[2].ParameterType);
        }

        [Fact]
        public void DeleteAsyncTakesIdAndCancellationToken()
        {
            var m = typeof(IAdminLibraryWrite).GetMethod("DeleteAsync")!;
            var p = m.GetParameters();
            Assert.Equal(2, p.Length);
            Assert.Equal(typeof(long), p[0].ParameterType);
            Assert.Equal(typeof(CancellationToken), p[1].ParameterType);
        }
    }

    public sealed class ScenarioLibraryWriteResultDiscriminatesOutcomes
    {
        [Fact]
        public void HasCreatedWithId()
        {
            var createdType = typeof(LibraryWriteResult).GetNestedType("Created");
            Assert.NotNull(createdType);
            var idProp = createdType!.GetProperty("Id");
            Assert.NotNull(idProp);
            Assert.Equal(typeof(long), idProp!.PropertyType);
        }

        [Fact]
        public void HasRenamedDeletedNotFoundAndNameConflict()
        {
            var nestedNames = typeof(LibraryWriteResult)
                .GetNestedTypes()
                .Select(t => t.Name)
                .ToHashSet();
            Assert.Contains("Renamed", nestedNames);
            Assert.Contains("Deleted", nestedNames);
            Assert.Contains("NotFound", nestedNames);
            Assert.Contains("NameConflict", nestedNames);
        }

        [Fact]
        public void HasHasDependentsCarryingDependentMediaCount()
        {
            var hasDependentsType = typeof(LibraryWriteResult).GetNestedType("HasDependents");
            Assert.NotNull(hasDependentsType);
            var countProp = hasDependentsType!.GetProperty("DependentMediaCount");
            Assert.NotNull(countProp);
            Assert.Equal(typeof(int), countProp!.PropertyType);
        }
    }
}

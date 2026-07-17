// STORY-001 — Scoped catalog reads with default-deny
//
// BDD specification — xUnit. Specs are Skip-pinned until the gating PLAN task
// lands (see docs/PLAN.md). Bodies document the intended Arrange/Act/Assert in
// comments; /build-loop fills them in and removes the Skip.

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureScopedCatalogReadsWithDefaultDeny
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioLibraryScopeRecord
    {
        [Fact(Skip = "Pending T001 — see docs/PLAN.md")]
        public void ExistsAsASealedRecord()
        {
            // var type = typeof(GenWave.Core.Domain.LibraryScope);
            // Assert.True(type.IsSealed && type.IsClass);
            Assert.Fail("pending T001");
        }

        [Fact(Skip = "Pending T001 — see docs/PLAN.md")]
        public void ExposesLibraryIdsAsReadOnlyCollectionOfLong()
        {
            // var scope = new LibraryScope(new long[] { 1 });
            // Assert.IsAssignableFrom<IReadOnlyCollection<long>>(scope.LibraryIds);
            Assert.Fail("pending T001");
        }

        [Fact(Skip = "Pending T001 — see docs/PLAN.md")]
        public void HasStaticNoneSentinelThatIsEmpty()
        {
            // Assert.True(LibraryScope.None.IsEmpty);
            Assert.Fail("pending T001");
        }
    }

    public sealed class ScenarioCatalogByIdReadWithScope
    {
        [Fact(Skip = "Pending T001/T003 — see docs/PLAN.md")]
        public void ReturnsMediaReferenceWhoseIdEqualsRequested()
        {
            // var scope = new LibraryScope(new long[] { 1 });
            // var result = await catalog.GetByIdAsync(scope, "m1", ct);
            // Assert.Equal("m1", result!.MediaId);
            Assert.Fail("pending T001/T003");
        }
    }

    public sealed class ScenarioCatalogRandomReadyReadWithScope
    {
        [Fact(Skip = "Pending T001/T003 — see docs/PLAN.md")]
        public void ReturnsANonNullMediaReference()
        {
            // var scope = new LibraryScope(new long[] { 1 });
            // var result = await catalog.GetRandomReadyAsync(scope, [], ct);
            // Assert.NotNull(result);
            Assert.Fail("pending T001/T003");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — default-deny and architectural enforcement
    // ---------------------------------------------------------------------

    public sealed class ScenarioEmptyScopeReturnsNullOnByIdEvenWhenRowExists
    {
        [Fact(Skip = "Pending T001/T003 — see docs/PLAN.md")]
        public void ReturnsNull()
        {
            // var result = await catalog.GetByIdAsync(LibraryScope.None, "m1", ct);
            // Assert.Null(result);
            Assert.Fail("pending T001/T003");
        }
    }

    public sealed class ScenarioEmptyScopeReturnsNullOnRandomReadyEvenWhenReadyRowsExist
    {
        [Fact(Skip = "Pending T001/T003 — see docs/PLAN.md")]
        public void ReturnsNull()
        {
            // var result = await catalog.GetRandomReadyAsync(LibraryScope.None, [], ct);
            // Assert.Null(result);
            Assert.Fail("pending T001/T003");
        }
    }

    public sealed class ScenarioNoUnscopedOverloadExistsOnInterface
    {
        [Fact(Skip = "Pending T001 — see docs/PLAN.md")]
        public void EveryMethodHasLibraryScopeParameter()
        {
            // foreach (var m in typeof(IMediaCatalog).GetMethods())
            //     Assert.Contains(m.GetParameters(), p => p.ParameterType == typeof(LibraryScope));
            Assert.Fail("pending T001");
        }
    }

    public sealed class ScenarioEmptyScopeShortCircuitsBeforeAnySql
    {
        [Fact(Skip = "Pending T001/T003 — see docs/PLAN.md")]
        public void IssuesZeroSqlCommandsForEmptyScope()
        {
            // Use an Npgsql command-counter wrapper around the catalog:
            //   await catalog.GetRandomReadyAsync(LibraryScope.None, [], ct);
            //   Assert.Equal(0, counter.CommandsIssued);
            Assert.Fail("pending T001/T003");
        }
    }
}

// STORY-002 — library_id column + backfill on library.media

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureLibraryIdColumnAndBackfill
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioSchemaAddsLibraryIdColumnOnMedia
    {
        [Fact(Skip = "Pending T002 — see docs/PLAN.md")]
        public void ColumnNamedLibraryIdExistsOnMedia()
        {
            // Query information_schema.columns:
            //   SELECT 1 FROM information_schema.columns
            //   WHERE table_schema='library' AND table_name='media' AND column_name='library_id'
            // Assert.True(rowExists);
            Assert.Fail("pending T002");
        }

        [Fact(Skip = "Pending T002 — see docs/PLAN.md")]
        public void ColumnLibraryIdIsBigintNotNull()
        {
            // SELECT data_type, is_nullable FROM information_schema.columns ...
            // Assert.Equal(("bigint","NO"), (dataType,isNullable));
            Assert.Fail("pending T002");
        }
    }

    public sealed class ScenarioDefaultLibraryRowExists
    {
        [Fact(Skip = "Pending T002 — see docs/PLAN.md")]
        public void ExactlyOneDefaultLibraryRowExists()
        {
            // var count = await db.QuerySingleAsync<long>("SELECT count(*) FROM library.library");
            // Assert.Equal(1, count);
            Assert.Fail("pending T002");
        }
    }

    public sealed class ScenarioExistingMediaRowsAreBackfilled
    {
        [Fact(Skip = "Pending T002 — see docs/PLAN.md")]
        public void EveryMediaRowHasNonNullLibraryId()
        {
            // var nullCount = await db.QuerySingleAsync<long>(
            //     "SELECT count(*) FROM library.media WHERE library_id IS NULL");
            // Assert.Equal(0, nullCount);
            Assert.Fail("pending T002");
        }
    }

    public sealed class ScenarioCoveringIndexForScopeFilteredRandomReady
    {
        [Fact(Skip = "Pending T002 — see docs/PLAN.md")]
        public void IndexCoveringLibraryIdExists()
        {
            // SELECT 1 FROM pg_indexes WHERE schemaname='library' AND tablename='media'
            //   AND indexdef ILIKE '%library_id%'
            // Assert.True(rowExists);
            Assert.Fail("pending T002");
        }
    }

    public sealed class ScenarioScopeFilterAppliedInSql
    {
        [Fact(Skip = "Pending T002/T003 — see docs/PLAN.md")]
        public void OnlyRowsInScopedLibrariesAreReturned()
        {
            // Seed rows in libraries 1 and 2, all 'ready':
            //   var r = await catalog.GetRandomReadyAsync(new LibraryScope([1]), [], ct);
            //   Assert.Equal(1L, r!.LibraryId);   // (or look up the row by id)
            Assert.Fail("pending T002/T003");
        }
    }

    public sealed class ScenarioMultiIdScopeIsOrOfLibraries
    {
        [Fact(Skip = "Pending T002/T003 — see docs/PLAN.md")]
        public void ReturnsARowWhoseLibraryIdIsInTheScopeSet()
        {
            // var r = await catalog.GetRandomReadyAsync(new LibraryScope([1,2]), [], ct);
            // Assert.Contains(r!.LibraryId, new long[] { 1, 2 });
            Assert.Fail("pending T002/T003");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioScopeReferencingNonexistentLibraryReturnsNull
    {
        [Fact(Skip = "Pending T002/T003 — see docs/PLAN.md")]
        public void ReturnsNullWhenNoRowsMatchTheScope()
        {
            // var r = await catalog.GetRandomReadyAsync(new LibraryScope([999]), [], ct);
            // Assert.Null(r);
            Assert.Fail("pending T002/T003");
        }
    }
}

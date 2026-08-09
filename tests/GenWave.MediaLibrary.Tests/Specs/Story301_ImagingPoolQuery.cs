// STORY-301 — Top-of-hour idents from the imaging pool: the pool query (F110.2, T231)
//
// BDD specification — xUnit, Postgres-backed (Category=Integration) via DatabaseCollection — the
// kind + ready predicate is selection SQL, provable only against the real planner (mirrors
// Story134_RotationNeverDrainsCatalogQuery's own posture).

using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureImagingPoolQuery
{
    // ---------------------------------------------------------------------
    // Helpers (spec-local, the Story242/Gh149 convention)
    // ---------------------------------------------------------------------

    static readonly LibraryScope DefaultScope = new([1L]);

    /// <summary>Authors a ready row of <paramref name="kind"/> at <paramref name="path"/> (unique
    /// path per row — <c>library.media.path</c> is unique).</summary>
    static async Task<long> InsertReadyAsync(DatabaseFixture db, string path, ImagingKind kind) =>
        await ((IAuthoredCatalogWriter)Harness.Repo(db)).InsertAuthoredAsync(
            Harness.AuthoredInsert(path: path, kind: kind), CancellationToken.None);

    /// <summary>Seeds a bare discovered (not-yet-enriched) row and stamps <paramref name="kind"/>
    /// directly — InsertDiscoveredAsync itself has no kind parameter (only the authored-insert seam
    /// does), so this mirrors Gh149_ImagingKindAuthoredRows' raw-SQL stamp for the shape an
    /// in-flight/never-enriched authored row would be in.</summary>
    static async Task<long> InsertNotReadyAsync(DatabaseFixture db, string path, ImagingKind kind)
    {
        var repo = Harness.Repo(db);
        var id = await repo.InsertDiscoveredAsync(path, "wav", 1, Harness.Mtime, CancellationToken.None);
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "update library.media set imaging_kind = @kind where id = @id",
            new { id, kind = ImagingKindTokens.ToToken(kind) });
        return id;
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRandomReadyByImagingKind(DatabaseFixture db)
    {
        [Fact]
        public async Task ReturnsOnlyRowsOfTheRequestedKind()
        {
            // Seed liner + two station_id + jingle rows; query kind=station_id repeatedly ⇒ every
            // returned row is one of the two station_id ids (never the liner or jingle one — that
            // exclusion is fully implied by Contains against a set that names only the two).
            await db.ResetAsync();
            await InsertReadyAsync(db, "/imaging/liner.wav", ImagingKind.Liner);
            var idA = await InsertReadyAsync(db, "/imaging/station-a.wav", ImagingKind.StationId);
            var idB = await InsertReadyAsync(db, "/imaging/station-b.wav", ImagingKind.StationId);
            await InsertReadyAsync(db, "/imaging/jingle.wav", ImagingKind.Jingle);

            var catalog = (IMediaCatalog)Harness.Repo(db);
            var stationIds = new[] { idA.ToString(), idB.ToString() };

            for (var i = 0; i < 10; i++)
            {
                var result = await catalog.GetRandomReadyByImagingKindAsync(DefaultScope, ImagingKind.StationId, CancellationToken.None);
                Assert.NotNull(result);
                Assert.Contains(result.MediaId, stationIds);
            }
        }

        [Fact]
        public async Task DrawsFromBothMatchingRowsRatherThanAlwaysTheSameOne()
        {
            // Pins order by random() rather than a deterministic order (e.g. order by id, which
            // would always return the same row and pass every OTHER fact here undetected): with two
            // station_id rows and enough draws, more than one distinct id must appear.
            await db.ResetAsync();
            var idA = await InsertReadyAsync(db, "/imaging/random-a.wav", ImagingKind.StationId);
            var idB = await InsertReadyAsync(db, "/imaging/random-b.wav", ImagingKind.StationId);

            var catalog = (IMediaCatalog)Harness.Repo(db);
            var seen = new HashSet<string>();
            for (var i = 0; i < 40; i++)
            {
                var result = await catalog.GetRandomReadyByImagingKindAsync(DefaultScope, ImagingKind.StationId, CancellationToken.None);
                Assert.NotNull(result);
                seen.Add(result.MediaId);
            }

            Assert.True(seen.Count > 1, $"expected draws from both {idA} and {idB}, only ever saw {string.Join(',', seen)}");
        }

        [Fact]
        public async Task ReturnsOnlyReadyRows()
        {
            // A station_id row that is not ready (unenriched) never returns, even across repeated
            // draws — only the ready sibling is ever selectable (the not-ready exclusion is fully
            // implied by Equal against the one ready id).
            await db.ResetAsync();
            var readyId = await InsertReadyAsync(db, "/imaging/ready.wav", ImagingKind.StationId);
            await InsertNotReadyAsync(db, "/imaging/not-ready.wav", ImagingKind.StationId);

            var catalog = (IMediaCatalog)Harness.Repo(db);

            for (var i = 0; i < 10; i++)
            {
                var result = await catalog.GetRandomReadyByImagingKindAsync(DefaultScope, ImagingKind.StationId, CancellationToken.None);
                Assert.NotNull(result);
                Assert.Equal(readyId.ToString(), result.MediaId);
            }
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioEmptyPool(DatabaseFixture db)
    {
        [Fact]
        public async Task NoMatchingRowsReturnsNullNotAnError()
        {
            // Zero station_id rows (a ready liner exists, proving the kind filter — not just an
            // empty catalog — is what drives the null) ⇒ null result, the drain's template-fallback
            // signal (SPEC F110.2).
            await db.ResetAsync();
            await InsertReadyAsync(db, "/imaging/only-liner.wav", ImagingKind.Liner);

            var catalog = (IMediaCatalog)Harness.Repo(db);
            var result = await catalog.GetRandomReadyByImagingKindAsync(DefaultScope, ImagingKind.StationId, CancellationToken.None);

            Assert.Null(result);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioEmptyScope(DatabaseFixture db)
    {
        [Fact]
        public async Task AnEmptyScopeReturnsNullEvenWhenAReadyMatchingRowExists()
        {
            // Default-deny (the authz control every IMediaCatalog read shares): a ready station_id
            // row exists, but LibraryScope.None must still short-circuit to null rather than reach it.
            await db.ResetAsync();
            await InsertReadyAsync(db, "/imaging/empty-scope.wav", ImagingKind.StationId);

            var catalog = (IMediaCatalog)Harness.Repo(db);
            var result = await catalog.GetRandomReadyByImagingKindAsync(LibraryScope.None, ImagingKind.StationId, CancellationToken.None);

            Assert.Null(result);
        }
    }
}

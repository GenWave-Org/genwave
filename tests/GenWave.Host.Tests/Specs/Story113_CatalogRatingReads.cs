// STORY-113 — Catalog reads carry rating state, with a never-play filter
//
// BDD specification — xUnit. Drives GET /api/media and GET /api/media/{id} through the
// controller directly with in-process fakes — the browse-SQL LEFT JOIN + COALESCE itself is
// proven against real Postgres in MediaLibrary.Tests/Specs/StoryF3_BulkEligibilityByFilter.cs
// (Story113 rating-reads section, S5). These specs pin the WIRING: score/neverPlay ride the
// existing browse/detail payloads unmodified, the never-play=true filter reaches the repository
// as MediaQuery.NeverPlay, and it composes with the F23.3 named-library scope swap. See
// docs/PLAN.md Epic S.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes (file-scoped: this spec owns its own doubles) ──────────────────────────────

/// <summary>
/// Records the scope/query each call receives and applies the never-play filter itself over a
/// caller-supplied row set — mirroring the repository's documented decision (true = filter to
/// flagged; absent/false = no filter) without needing a live database. Real predicate/SQL
/// correctness is proven in MediaLibrary.Tests.
/// </summary>
file sealed class FakeRatingAwareAdminQuery : IAdminMediaQuery
{
    public required IReadOnlyList<AdminMediaDto> Rows { get; init; }
    public LibraryScope? LastScope { get; private set; }
    public MediaQuery? LastQuery { get; private set; }

    public Task<PagedResult<AdminMediaDto>> ListAdminAsync(LibraryScope scope, MediaQuery query, CancellationToken ct)
    {
        LastScope = scope;
        LastQuery = query;

        var items = query.NeverPlay is true
            ? Rows.Where(r => r.NeverPlay).ToList()
            : Rows.ToList();

        return Task.FromResult(new PagedResult<AdminMediaDto>(items, items.Count, 1));
    }
}

/// <summary>Returns a configured detail row (or null) for GET /api/media/{id}.</summary>
file sealed class FakeRatingAwareAdminLookup : IAdminMediaLookup
{
    public (AdminMediaDto Row, long LibraryId)? Result { get; set; }

    public Task<(AdminMediaDto Row, long LibraryId)?> GetByIdWithLibraryAsync(long id, CancellationToken ct)
        => Task.FromResult(Result);
}

/// <summary>Unused by these read-only specs — every member throws if ever invoked.</summary>
file sealed class ThrowingAdminWrite : IAdminMediaWrite
{
    public Task<MediaWriteResult> UpdateAsync(string id, MediaPatch patch, string expectedVersion, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-113.");

    public Task<MediaUpdateOutcome> UpdateReturningVersionAsync(string id, MediaPatch patch, string expectedVersion, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-113.");

    public Task<int> SetEligibilityAsync(MediaQuery filter, bool eligible, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-113.");

    public Task<int?> BulkReassignAsync(MediaQuery filter, long toLibraryId, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-113.");
}

file static class CatalogRatingReadsHarness
{
    /// <summary>Station scope [1L] unless overridden.</summary>
    public static (MediaController Controller, FakeRatingAwareAdminQuery Query, FakeRatingAwareAdminLookup Lookup, DefaultHttpContext Http)
        Build(IReadOnlyList<AdminMediaDto>? rows = null, params long[] scopeLibraryIds)
    {
        var query = new FakeRatingAwareAdminQuery { Rows = rows ?? [] };
        var lookup = new FakeRatingAwareAdminLookup();
        var http = new DefaultHttpContext();
        var scope = new LibraryScope(scopeLibraryIds.Length == 0 ? [1L] : scopeLibraryIds);

        var controller = new MediaController(
            query,
            lookup,
            new ThrowingAdminWrite(),
            new FakeStationScopeProvider(scope),
            NullLogger<MediaController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };

        return (controller, query, lookup, http);
    }

    public static AdminMediaDto Row(string mediaId, int score, bool neverPlay, string version = "1") => new(
        MediaId: mediaId,
        Locator: $"/media/{mediaId}.flac",
        Format: "flac",
        State: "ready",
        DurationMs: 180_000,
        Title: "Title",
        Artist: "Artist",
        Album: null,
        Genre: null,
        Year: null,
        IntegratedLufs: -14.0,
        TruePeakDbtp: -1.0,
        Measurable: true,
        CueInSec: null,
        CueOutSec: null,
        Eligible: true,
        Version: version,
        Score: score,
        NeverPlay: neverPlay);
}

public static class FeatureCatalogRatingReads
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — rating state rides existing reads
    // ---------------------------------------------------------------------

    public sealed class ScenarioBrowseCarriesRatingState
    {
        readonly IReadOnlyList<AdminMediaDto> body;

        public ScenarioBrowseCarriesRatingState()
        {
            var rows = new[]
            {
                CatalogRatingReadsHarness.Row("1", score: 80, neverPlay: false),
                CatalogRatingReadsHarness.Row("2", score: 50, neverPlay: false),
            };
            var (controller, _, _, _) = CatalogRatingReadsHarness.Build(rows);

            var result = controller
                .List(state: null, artist: null, genre: null, libraryId: null, q: null, eligible: null, neverPlay: null)
                .GetAwaiter().GetResult();

            body = Assert.IsAssignableFrom<IReadOnlyList<AdminMediaDto>>(Assert.IsType<OkObjectResult>(result).Value);
        }

        [Fact]
        public void EveryBrowseRowCarriesScoreAndNeverPlay()
        {
            // GET /api/media → every row has camelCase score + neverPlay (F33.10).
            Assert.Equal(2, body.Count);
            Assert.Contains(body, r => r.MediaId == "1" && r.Score == 80 && !r.NeverPlay);
        }

        [Fact]
        public void UnratedRowsReadTheDefaults()
        {
            // A row with no rating row reads score 50 / neverPlay false (F33.2) — the query fake
            // returns the ledger default exactly as the repository would for an unrated row.
            Assert.Contains(body, r => r.MediaId == "2" && r.Score == 50 && !r.NeverPlay);
        }
    }

    public sealed class ScenarioDetailCarriesRatingState
    {
        [Fact]
        public async Task TheDetailPayloadIncludesScoreAndNeverPlay()
        {
            // GET /api/media/{id} includes both fields (F33.10).
            var (controller, _, lookup, _) = CatalogRatingReadsHarness.Build(scopeLibraryIds: [1L]);
            lookup.Result = (CatalogRatingReadsHarness.Row("7", score: 73, neverPlay: true, version: "9"), 1L);

            var result = await controller.GetById(7L, CancellationToken.None);

            var body = Assert.IsType<AdminMediaDto>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(73, body.Score);
            Assert.True(body.NeverPlay);
        }

        [Fact]
        public async Task TheDetailETagRemainsTheMediaRowVersion()
        {
            // The ETag is still W/"<xmin>" of library.media — rating writes never move it (F33.1).
            // Two reads carry the SAME version but DIFFERENT scores (as a vote would produce without
            // ever bumping xmin) — the ETag tracks Version only, never Score.
            var (controller, _, lookup, http) = CatalogRatingReadsHarness.Build(scopeLibraryIds: [1L]);

            lookup.Result = (CatalogRatingReadsHarness.Row("7", score: 50, neverPlay: false, version: "7"), 1L);
            await controller.GetById(7L, CancellationToken.None);
            var firstEtag = http.Response.Headers.ETag.ToString();

            lookup.Result = (CatalogRatingReadsHarness.Row("7", score: 51, neverPlay: false, version: "7"), 1L);
            await controller.GetById(7L, CancellationToken.None);
            var secondEtag = http.Response.Headers.ETag.ToString();

            Assert.Equal("W/\"7\"", firstEtag);
            Assert.Equal(firstEtag, secondEtag);
        }
    }

    public sealed class ScenarioFilteringToFlaggedRows
    {
        [Fact]
        public async Task NeverPlayTrueReturnsExactlyTheFlaggedRows()
        {
            // GET /api/media?never-play=true over three rows (one flagged) → exactly one row (F33.10).
            var rows = new[]
            {
                CatalogRatingReadsHarness.Row("1", score: 50, neverPlay: false),
                CatalogRatingReadsHarness.Row("2", score: 50, neverPlay: true),
                CatalogRatingReadsHarness.Row("3", score: 50, neverPlay: false),
            };
            var (controller, query, _, _) = CatalogRatingReadsHarness.Build(rows);

            var result = await controller
                .List(state: null, artist: null, genre: null, libraryId: null, q: null, eligible: null, neverPlay: true);

            var body = Assert.IsAssignableFrom<IReadOnlyList<AdminMediaDto>>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(["2"], body.Select(r => r.MediaId));
            Assert.True(query.LastQuery?.NeverPlay);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — filter composition
    // ---------------------------------------------------------------------

    public sealed class ScenarioFilterComposesWithExistingParams
    {
        [Fact]
        public async Task NeverPlayComposesWithTheNamedLibraryFilter()
        {
            // GET /api/media?never-play=true&library-id=<x> → the F23.3 named-library scope swap
            // still names the effective scope, and never-play=true still reaches the repository
            // alongside it — both bits of information travel together to ListAdminAsync, which is
            // where the real intersection with library_id happens (proven against Postgres).
            var rows = new[] { CatalogRatingReadsHarness.Row("9", score: 50, neverPlay: true) };
            var (controller, query, _, _) = CatalogRatingReadsHarness.Build(rows, scopeLibraryIds: [1L]);

            var result = await controller
                .List(state: null, artist: null, genre: null, libraryId: 2, q: null, eligible: null, neverPlay: true);

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal([2L], query.LastScope?.LibraryIds);
            Assert.True(query.LastQuery?.NeverPlay);
        }
    }
}

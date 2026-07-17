// STORY-145 — The catalog shows and filters the new signals (Epic X / SPEC F49.1–F49.2,
// closes gitea-#190, gitea-#208) — API half. The column-toggle UI half lives in
// admin-ui/__specs__/catalog-signal-columns.spec.tsx.
//
// BDD specification — xUnit. Drives the controller directly with in-process fakes, mirroring
// Story113_CatalogRatingReads.cs's harness idiom exactly: the real WHERE-clause composition
// (year = @year / year BETWEEN.. / year IS NULL) is proven against Postgres in
// MediaLibrary.Tests/Specs/Story145_YearDecadeFilterSql.cs; these specs pin the wiring — the
// three query params reach MediaQuery.Year/Decade/YearMissing, the conflict/alignment 400s fire
// before the repository is ever called, and AdminMediaDto.Bpm/TrackEnergy ride the existing
// browse payload unmodified.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes (file-scoped: this spec owns its own doubles) ──────────────────────────────

/// <summary>
/// Records the scope/query each call receives and applies the year/decade/year-missing filters
/// itself over a caller-supplied row set — mirroring the repository's documented WHERE semantics
/// (exact match / BETWEEN start AND start+9 / IS NULL) without needing a live database. Also
/// applies the pre-existing artist filter so the "composes with existing filters" fact has
/// something real to narrow. Real predicate/SQL correctness is proven in MediaLibrary.Tests.
/// </summary>
file sealed class FakeYearAwareAdminQuery : IAdminMediaQuery
{
    public required IReadOnlyList<AdminMediaDto> Rows { get; init; }
    public MediaQuery? LastQuery { get; private set; }

    public Task<PagedResult<AdminMediaDto>> ListAdminAsync(LibraryScope scope, MediaQuery query, CancellationToken ct)
    {
        LastQuery = query;

        var items = Rows.AsEnumerable();

        if (query.Artist is not null)
            items = items.Where(r => r.Artist is not null &&
                r.Artist.Contains(query.Artist, StringComparison.OrdinalIgnoreCase));

        if (query.Year.HasValue)
            items = items.Where(r => r.Year == query.Year.Value);

        if (query.Decade.HasValue)
            items = items.Where(r => r.Year.HasValue &&
                r.Year.Value >= query.Decade.Value && r.Year.Value <= query.Decade.Value + 9);

        if (query.YearMissing is true)
            items = items.Where(r => r.Year is null);

        var list = items.ToList();
        return Task.FromResult(new PagedResult<AdminMediaDto>(list, list.Count, 1));
    }
}

/// <summary>Unused by these specs — every member throws if ever invoked.</summary>
file sealed class ThrowingAdminLookup : IAdminMediaLookup
{
    public Task<(AdminMediaDto Row, long LibraryId)?> GetByIdWithLibraryAsync(long id, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-145.");
}

/// <summary>Unused by these read-only specs — every member throws if ever invoked.</summary>
file sealed class ThrowingAdminWrite : IAdminMediaWrite
{
    public Task<MediaWriteResult> UpdateAsync(string id, MediaPatch patch, string expectedVersion, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-145.");

    public Task<MediaUpdateOutcome> UpdateReturningVersionAsync(string id, MediaPatch patch, string expectedVersion, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-145.");

    public Task<int> SetEligibilityAsync(MediaQuery filter, bool eligible, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-145.");

    public Task<int?> BulkReassignAsync(MediaQuery filter, long toLibraryId, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-145.");
}

file static class YearDecadeFiltersHarness
{
    /// <summary>Station scope [1L] — fixed; these specs never exercise scope resolution.</summary>
    public static (MediaController Controller, FakeYearAwareAdminQuery Query) Build(IReadOnlyList<AdminMediaDto> rows)
    {
        var query = new FakeYearAwareAdminQuery { Rows = rows };
        var http = new DefaultHttpContext();

        var controller = new MediaController(
            query,
            new ThrowingAdminLookup(),
            new ThrowingAdminWrite(),
            new FakeStationScopeProvider(new LibraryScope([1L])),
            NullLogger<MediaController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };

        return (controller, query);
    }

    public static AdminMediaDto Row(
        string mediaId, int? year, string? artist = "Artist", double? bpm = null, double? trackEnergy = null) => new(
        MediaId: mediaId,
        Locator: $"/media/{mediaId}.flac",
        Format: "flac",
        State: "ready",
        DurationMs: 180_000,
        Title: "Title",
        Artist: artist,
        Album: null,
        Genre: null,
        Year: year,
        IntegratedLufs: -14.0,
        TruePeakDbtp: -1.0,
        Measurable: true,
        CueInSec: null,
        CueOutSec: null,
        Eligible: true,
        Version: "1",
        Bpm: bpm,
        TrackEnergy: trackEnergy);

    /// <summary>Calls List with every filter defaulted to absent except the ones under test.</summary>
    public static Task<IActionResult> CallList(
        MediaController controller,
        string? artist = null,
        int? year = null,
        int? decade = null,
        bool? yearMissing = null) =>
        controller.List(
            state: null, artist: artist, genre: null, libraryId: null, q: null, eligible: null,
            neverPlay: null, year: year, decade: decade, yearMissing: yearMissing);
}

public static class FeatureYearDecadeFiltersAndSignalDto
{
    public sealed class ScenarioYearAndDecadeFiltersNarrowTheBrowse
    {
        [Fact]
        public async Task AnExactYearFilterReturnsOnlyThatYear()
        {
            // GET /api/media?year=1975 → only the row tagged 1975 (F49.1).
            var rows = new[]
            {
                YearDecadeFiltersHarness.Row("1", year: 1975),
                YearDecadeFiltersHarness.Row("2", year: 1980),
                YearDecadeFiltersHarness.Row("3", year: null),
            };
            var (controller, query) = YearDecadeFiltersHarness.Build(rows);

            var result = await YearDecadeFiltersHarness.CallList(controller, year: 1975);

            var body = Assert.IsAssignableFrom<IReadOnlyList<AdminMediaDto>>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(["1"], body.Select(r => r.MediaId));
            Assert.Equal(1975, query.LastQuery?.Year);
        }

        [Fact]
        public async Task ADecadeFilterReturnsTheTenYearSpan()
        {
            // GET /api/media?decade=1970 → year BETWEEN 1970 AND 1979 (F49.1): 1970 and 1979 are
            // in range; 1969 and 1980 are just outside it.
            var rows = new[]
            {
                YearDecadeFiltersHarness.Row("1969", year: 1969),
                YearDecadeFiltersHarness.Row("1970", year: 1970),
                YearDecadeFiltersHarness.Row("1979", year: 1979),
                YearDecadeFiltersHarness.Row("1980", year: 1980),
            };
            var (controller, query) = YearDecadeFiltersHarness.Build(rows);

            var result = await YearDecadeFiltersHarness.CallList(controller, decade: 1970);

            var body = Assert.IsAssignableFrom<IReadOnlyList<AdminMediaDto>>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(["1970", "1979"], body.Select(r => r.MediaId).OrderBy(x => x));
            Assert.Equal(1970, query.LastQuery?.Decade);
        }

        [Fact]
        public async Task YearMissingReturnsOnlyUnfilledRows()
        {
            // GET /api/media?year-missing=true → year IS NULL (F49.1, the F48 tail findable).
            var rows = new[]
            {
                YearDecadeFiltersHarness.Row("1", year: null),
                YearDecadeFiltersHarness.Row("2", year: 1999),
            };
            var (controller, query) = YearDecadeFiltersHarness.Build(rows);

            var result = await YearDecadeFiltersHarness.CallList(controller, yearMissing: true);

            var body = Assert.IsAssignableFrom<IReadOnlyList<AdminMediaDto>>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(["1"], body.Select(r => r.MediaId));
            Assert.True(query.LastQuery?.YearMissing);
        }

        [Fact]
        public async Task ADecadeFilterComposesWithTheExistingFilters()
        {
            // GET /api/media?decade=1970&artist=Alpha — both predicates apply together (F49.1):
            // an Alpha row from 1975 matches; a Beta row from 1975 and an Alpha row from 1985 don't.
            var rows = new[]
            {
                YearDecadeFiltersHarness.Row("match", year: 1975, artist: "Alpha"),
                YearDecadeFiltersHarness.Row("wrong-artist", year: 1975, artist: "Beta"),
                YearDecadeFiltersHarness.Row("wrong-decade", year: 1985, artist: "Alpha"),
            };
            var (controller, query) = YearDecadeFiltersHarness.Build(rows);

            var result = await YearDecadeFiltersHarness.CallList(controller, artist: "Alpha", decade: 1970);

            var body = Assert.IsAssignableFrom<IReadOnlyList<AdminMediaDto>>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(["match"], body.Select(r => r.MediaId));
            Assert.Equal("Alpha", query.LastQuery?.Artist);
            Assert.Equal(1970, query.LastQuery?.Decade);
        }
    }

    public sealed class ScenarioTheDtoCarriesTheNewSignals
    {
        [Fact]
        public async Task ABrowseRowCarriesBpmAndTrackEnergy()
        {
            // AdminMediaDto.Bpm/TrackEnergy ride the browse payload unmodified (F49.2).
            var rows = new[] { YearDecadeFiltersHarness.Row("1", year: 2000, bpm: 128.4, trackEnergy: 0.73) };
            var (controller, _) = YearDecadeFiltersHarness.Build(rows);

            var result = await YearDecadeFiltersHarness.CallList(controller);

            var body = Assert.IsAssignableFrom<IReadOnlyList<AdminMediaDto>>(Assert.IsType<OkObjectResult>(result).Value);
            var row = Assert.Single(body);
            Assert.Equal(128.4, row.Bpm);
            Assert.Equal(0.73, row.TrackEnergy);
        }

        [Fact]
        public async Task UnmeasuredRowsCarryNullsNotZeros()
        {
            // An unanalyzed/unmeasured row keeps null bpm/trackEnergy — never coerced to 0 (F49.2).
            var rows = new[] { YearDecadeFiltersHarness.Row("1", year: 2000, bpm: null, trackEnergy: null) };
            var (controller, _) = YearDecadeFiltersHarness.Build(rows);

            var result = await YearDecadeFiltersHarness.CallList(controller);

            var body = Assert.IsAssignableFrom<IReadOnlyList<AdminMediaDto>>(Assert.IsType<OkObjectResult>(result).Value);
            var row = Assert.Single(body);
            Assert.Null(row.Bpm);
            Assert.Null(row.TrackEnergy);
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioConflictingOrMalformedYearFiltersFourHundred
    {
        [Fact]
        public async Task NamingYearAndDecadeTogetherIsRejected()
        {
            // GET /api/media?year=1975&decade=1970 → 400; the repository is never called (F49.1).
            var (controller, query) = YearDecadeFiltersHarness.Build([]);

            var result = await YearDecadeFiltersHarness.CallList(controller, year: 1975, decade: 1970);

            var problem = Assert.IsType<ProblemDetails>(Assert.IsType<BadRequestObjectResult>(result).Value);
            Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
            Assert.Null(query.LastQuery);
        }

        [Fact]
        public async Task ANonAlignedDecadeValueIsRejected()
        {
            // GET /api/media?decade=1975 → 400; 1975 is not divisible by 10 (F49.1).
            var (controller, query) = YearDecadeFiltersHarness.Build([]);

            var result = await YearDecadeFiltersHarness.CallList(controller, decade: 1975);

            var problem = Assert.IsType<ProblemDetails>(Assert.IsType<BadRequestObjectResult>(result).Value);
            Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
            Assert.Null(query.LastQuery);
        }
    }
}

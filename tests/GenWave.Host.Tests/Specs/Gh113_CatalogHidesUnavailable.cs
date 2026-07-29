// gh-#113 — Hide unavailable rows from the catalog view (commit 1: the endpoint half).
//
// BDD specification — xUnit. Drives MediaController directly with in-process fakes, mirroring
// Story145_YearDecadeFiltersAndSignalDto.cs's harness idiom exactly: the real SQL hiding predicate
// and hidden-row count are proven against Postgres in
// MediaLibrary.Tests/Specs/Gh113_UnavailableHiddenAndStamped.cs; these specs pin the WIRING — the
// include-unavailable query param reaches MediaQuery.IncludeUnavailable, and the
// X-Unavailable-Hidden header rides exactly the responses whose page hid rows (never a revealed
// or state-filtered browse).

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes (file-scoped: this spec owns its own doubles) ──────────────────────────────

/// <summary>
/// Applies MediaQuery.HidesUnavailable over a caller-supplied row set — mirroring the
/// repository's documented browse semantics (state &lt;&gt; 'unavailable' by default, revealed by
/// IncludeUnavailable=true or an explicit state filter) without a live database. Records the
/// query each call receives, and whether the hidden-count read ever fired.
/// </summary>
file sealed class FakeHidingAdminQuery : IAdminMediaQuery
{
    public required IReadOnlyList<AdminMediaDto> Rows { get; init; }
    public int UnavailableCount { get; init; }
    public MediaQuery? LastQuery { get; private set; }
    public bool CountWasCalled { get; private set; }

    public Task<PagedResult<AdminMediaDto>> ListAdminAsync(LibraryScope scope, MediaQuery query, CancellationToken ct)
    {
        LastQuery = query;

        var items = Rows.AsEnumerable();

        if (query.State is not null)
            items = items.Where(r => r.State == query.State);
        else if (query.HidesUnavailable)
            items = items.Where(r => r.State != "unavailable");

        var list = items.ToList();
        return Task.FromResult(new PagedResult<AdminMediaDto>(list, list.Count, 1));
    }

    public Task<int> CountUnavailableAsync(LibraryScope scope, MediaQuery query, CancellationToken ct)
    {
        CountWasCalled = true;
        return Task.FromResult(UnavailableCount);
    }
}

/// <summary>Unused by these specs — every member throws if ever invoked.</summary>
file sealed class Gh113ThrowingAdminLookup : IAdminMediaLookup
{
    public Task<(AdminMediaDto Row, long LibraryId)?> GetByIdWithLibraryAsync(long id, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by gh-#113.");
}

/// <summary>Unused by these read-only specs — every member throws if ever invoked.</summary>
file sealed class Gh113ThrowingAdminWrite : IAdminMediaWrite
{
    public Task<MediaUpdateOutcome> UpdateReturningVersionAsync(string id, MediaPatch patch, string expectedVersion, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by gh-#113.");

    public Task<int> SetEligibilityAsync(MediaQuery filter, bool eligible, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by gh-#113.");

    public Task<int?> BulkReassignAsync(MediaQuery filter, long toLibraryId, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by gh-#113.");
}

file static class HidesUnavailableHarness
{
    public static (MediaController Controller, FakeHidingAdminQuery Query, HttpContext Http) Build(
        IReadOnlyList<AdminMediaDto> rows, int unavailableCount)
    {
        var query = new FakeHidingAdminQuery { Rows = rows, UnavailableCount = unavailableCount };
        var http = new DefaultHttpContext();

        var controller = new MediaController(
            query,
            new Gh113ThrowingAdminLookup(),
            new Gh113ThrowingAdminWrite(),
            new FakeStationScopeProvider(new LibraryScope([1L])),
            NullLogger<MediaController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };

        return (controller, query, http);
    }

    public static AdminMediaDto Row(string mediaId, string state) => new(
        MediaId: mediaId,
        Locator: $"/media/{mediaId}.flac",
        Format: "flac",
        State: state,
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
        Version: "1");

    /// <summary>Calls List with every filter defaulted to absent except the ones under test.</summary>
    public static Task<IActionResult> CallList(
        MediaController controller,
        string? state = null,
        bool? includeUnavailable = null) =>
        controller.List(
            state: state, artist: null, genre: null, libraryId: null, q: null, eligible: null,
            includeUnavailable: includeUnavailable);
}

public static class FeatureCatalogHidesUnavailable
{
    static readonly IReadOnlyList<AdminMediaDto> MixedRows =
    [
        HidesUnavailableHarness.Row("1", "ready"),
        HidesUnavailableHarness.Row("2", "ready"),
        HidesUnavailableHarness.Row("3", "unavailable"),
    ];

    public sealed class ScenarioDefaultBrowseHidesUnavailable
    {
        [Fact]
        public async Task The_page_excludes_unavailable_rows_and_the_header_names_the_hidden_count()
        {
            var (controller, _, http) = HidesUnavailableHarness.Build(MixedRows, unavailableCount: 7);

            var result = await HidesUnavailableHarness.CallList(controller);

            var ok = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<IReadOnlyList<AdminMediaDto>>(ok.Value);
            Assert.Equal(2, items.Count);
            Assert.DoesNotContain(items, r => r.State == "unavailable");
            Assert.Equal("7", http.Response.Headers["X-Unavailable-Hidden"]);
        }

        [Fact]
        public async Task The_param_defaults_to_absent_on_the_repository_query()
        {
            var (controller, query, _) = HidesUnavailableHarness.Build(MixedRows, unavailableCount: 0);

            await HidesUnavailableHarness.CallList(controller);

            Assert.NotNull(query.LastQuery);
            Assert.Null(query.LastQuery.IncludeUnavailable);
            Assert.True(query.LastQuery.HidesUnavailable);
        }
    }

    public sealed class ScenarioIncludeUnavailableReveals
    {
        [Fact]
        public async Task Include_unavailable_true_reaches_the_query_and_suppresses_the_header()
        {
            var (controller, query, http) = HidesUnavailableHarness.Build(MixedRows, unavailableCount: 7);

            var result = await HidesUnavailableHarness.CallList(controller, includeUnavailable: true);

            var ok = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<IReadOnlyList<AdminMediaDto>>(ok.Value);
            Assert.Equal(3, items.Count);
            Assert.True(query.LastQuery?.IncludeUnavailable);
            Assert.False(http.Response.Headers.ContainsKey("X-Unavailable-Hidden"));
            Assert.False(query.CountWasCalled);
        }
    }

    public sealed class ScenarioExplicitStateFilterDisablesHiding
    {
        [Fact]
        public async Task A_state_filtered_browse_never_carries_the_hidden_header()
        {
            // state=unavailable must return its rows; any explicit state filter already narrows
            // by state, so a hidden-count alongside it would be noise (and always zero for
            // non-unavailable states) — the header stays off, per MediaQuery.HidesUnavailable.
            var (controller, query, http) = HidesUnavailableHarness.Build(MixedRows, unavailableCount: 7);

            var result = await HidesUnavailableHarness.CallList(controller, state: "unavailable");

            var ok = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<IReadOnlyList<AdminMediaDto>>(ok.Value);
            var row = Assert.Single(items);
            Assert.Equal("unavailable", row.State);
            Assert.False(http.Response.Headers.ContainsKey("X-Unavailable-Hidden"));
            Assert.False(query.CountWasCalled);
        }
    }
}

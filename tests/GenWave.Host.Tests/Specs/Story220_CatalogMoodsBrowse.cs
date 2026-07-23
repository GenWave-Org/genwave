// STORY-220 — The catalog shows and filters by mood (SPEC F86.8)
//
// BDD specification — xUnit. Drives the controller directly with in-process fakes, mirroring
// Story145's ScenarioYearAndDecadeFiltersNarrowTheBrowse harness idiom exactly: the real
// case-insensitive any-match SQL predicate (EXISTS (SELECT 1 FROM unnest(moods)...)) is proven
// against Postgres in MediaLibrary.Tests/Specs/Story220_MoodExactFilterSql.cs; these specs pin the
// wiring — moods rides the browse DTO, the repeatable ?mood-exact= param reaches
// MediaQuery.MoodsExact, and it ANDs with the other exact filters through the one shared
// MediaQuery/BuildAdminWhere seam (F52 idiom).

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes (file-scoped: this spec owns its own doubles) ──────────────────────────────

/// <summary>
/// Records the scope/query each call receives and applies the mood-exact/artist-exact filters
/// itself over a caller-supplied row set — mirroring the repository's documented WHERE semantics
/// (case-insensitive any-match against a row's moods, OR'd across occurrences, AND'd with other
/// exact filters) without needing a live database. Real predicate/SQL correctness is proven in
/// MediaLibrary.Tests.
/// </summary>
file sealed class FakeMoodAwareAdminQuery : IAdminMediaQuery
{
    public required IReadOnlyList<AdminMediaDto> Rows { get; init; }
    public MediaQuery? LastQuery { get; private set; }

    public Task<PagedResult<AdminMediaDto>> ListAdminAsync(LibraryScope scope, MediaQuery query, CancellationToken ct)
    {
        LastQuery = query;

        var items = Rows.AsEnumerable();

        if (query.ArtistExact is not null)
            items = items.Where(r => r.Artist is not null &&
                string.Equals(r.Artist, query.ArtistExact, StringComparison.OrdinalIgnoreCase));

        if (query.MoodsExact is { Count: > 0 } moodsExact)
            items = items.Where(r => r.Moods is not null &&
                r.Moods.Any(rowMood => moodsExact.Any(term => string.Equals(rowMood, term, StringComparison.OrdinalIgnoreCase))));

        var list = items.ToList();
        return Task.FromResult(new PagedResult<AdminMediaDto>(list, list.Count, 1));
    }
}

/// <summary>Unused by these specs — every member throws if ever invoked.</summary>
file sealed class ThrowingAdminLookup : IAdminMediaLookup
{
    public Task<(AdminMediaDto Row, long LibraryId)?> GetByIdWithLibraryAsync(long id, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-220.");
}

/// <summary>Unused by these read-only specs — every member throws if ever invoked.</summary>
file sealed class ThrowingAdminWrite : IAdminMediaWrite
{
    public Task<MediaWriteResult> UpdateAsync(string id, MediaPatch patch, string expectedVersion, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-220.");

    public Task<MediaUpdateOutcome> UpdateReturningVersionAsync(string id, MediaPatch patch, string expectedVersion, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-220.");

    public Task<int> SetEligibilityAsync(MediaQuery filter, bool eligible, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-220.");

    public Task<int?> BulkReassignAsync(MediaQuery filter, long toLibraryId, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-220.");
}

file static class MoodsBrowseHarness
{
    /// <summary>Station scope [1L] — fixed; these specs never exercise scope resolution.</summary>
    public static (MediaController Controller, FakeMoodAwareAdminQuery Query) Build(IReadOnlyList<AdminMediaDto> rows)
    {
        var query = new FakeMoodAwareAdminQuery { Rows = rows };
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

    public static AdminMediaDto Row(string mediaId, string[]? moods = null, string? artist = "Artist") => new(
        MediaId: mediaId,
        Locator: $"/media/{mediaId}.flac",
        Format: "flac",
        State: "ready",
        DurationMs: 180_000,
        Title: "Title",
        Artist: artist,
        Album: null,
        Genre: null,
        Year: null,
        IntegratedLufs: -14.0,
        TruePeakDbtp: -1.0,
        Measurable: true,
        CueInSec: null,
        CueOutSec: null,
        Eligible: true,
        Version: "1",
        Moods: moods);

    /// <summary>Calls List with every filter defaulted to absent except the ones under test.</summary>
    public static Task<IActionResult> CallList(
        MediaController controller,
        string? artistExact = null,
        string[]? moodExact = null) =>
        controller.List(
            state: null, artist: null, genre: null, libraryId: null, q: null, eligible: null,
            neverPlay: null, year: null, decade: null, yearMissing: null,
            artistExact: artistExact, albumExact: null, genreExact: null, moodExact: moodExact);
}

public static class FeatureCatalogMoodsBrowse
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioBrowseRowsCarryMoods
    {
        [Fact]
        public async Task TaggedRowsReturnTheirMoods()
        {
            // A row tagged ["dreamy","warm"] carries exactly those moods in the browse DTO (F86.8).
            var rows = new[] { MoodsBrowseHarness.Row("1", moods: ["dreamy", "warm"]) };
            var (controller, _) = MoodsBrowseHarness.Build(rows);

            var result = await MoodsBrowseHarness.CallList(controller);

            var body = Assert.IsAssignableFrom<IReadOnlyList<AdminMediaDto>>(Assert.IsType<OkObjectResult>(result).Value);
            var row = Assert.Single(body);
            Assert.Equal(["dreamy", "warm"], row.Moods);
        }
    }

    public sealed class ScenarioMoodExactFilter
    {
        // Arrange (when built): rows tagged ["dreamy"], ["DREAMY","driving"], ["driving"],
        // and an untagged row.
        static AdminMediaDto[] Rows() =>
        [
            MoodsBrowseHarness.Row("dreamy-only", moods: ["dreamy"]),
            MoodsBrowseHarness.Row("dreamy-driving", moods: ["DREAMY", "driving"], artist: "Vantage"),
            MoodsBrowseHarness.Row("driving-only", moods: ["driving"]),
            MoodsBrowseHarness.Row("untagged", moods: null),
        ];

        [Fact]
        public async Task SingleMoodExactMatchesAnyOfARowsMoodsCaseInsensitively()
        {
            // ?mood-exact=dreamy returns both the "dreamy" and "DREAMY,driving" rows (F86.8).
            var (controller, _) = MoodsBrowseHarness.Build(Rows());

            var result = await MoodsBrowseHarness.CallList(controller, moodExact: ["dreamy"]);

            var body = Assert.IsAssignableFrom<IReadOnlyList<AdminMediaDto>>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(["dreamy-driving", "dreamy-only"], body.Select(r => r.MediaId).OrderBy(x => x));
        }

        [Fact]
        public async Task RepeatedMoodExactValuesOrTogether()
        {
            // ?mood-exact=dreamy&mood-exact=driving returns the union of the three tagged rows.
            var (controller, _) = MoodsBrowseHarness.Build(Rows());

            var result = await MoodsBrowseHarness.CallList(controller, moodExact: ["dreamy", "driving"]);

            var body = Assert.IsAssignableFrom<IReadOnlyList<AdminMediaDto>>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(
                ["dreamy-driving", "dreamy-only", "driving-only"],
                body.Select(r => r.MediaId).OrderBy(x => x));
        }

        [Fact]
        public async Task MoodExactAndsWithOtherExactFiltersThroughTheSharedBuilder()
        {
            // ?mood-exact=driving&artist-exact=Vantage returns only rows satisfying BOTH,
            // composed by the one shared WHERE builder (F52 idiom, F86.8).
            var (controller, query) = MoodsBrowseHarness.Build(Rows());

            var result = await MoodsBrowseHarness.CallList(controller, artistExact: "Vantage", moodExact: ["driving"]);

            var body = Assert.IsAssignableFrom<IReadOnlyList<AdminMediaDto>>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(["dreamy-driving"], body.Select(r => r.MediaId));
            Assert.Equal("Vantage", query.LastQuery?.ArtistExact);
            Assert.Equal(["driving"], query.LastQuery?.MoodsExact);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — untagged rows, unknown terms
    // ---------------------------------------------------------------------

    public sealed class ScenarioUntaggedRows
    {
        [Fact]
        public async Task UntaggedRowsCarryNoMoodsAndSurviveUnfilteredBrowse()
        {
            // A null-moods row browses normally with an empty/absent moods field (F86.8).
            var rows = new[] { MoodsBrowseHarness.Row("1", moods: null) };
            var (controller, _) = MoodsBrowseHarness.Build(rows);

            var result = await MoodsBrowseHarness.CallList(controller);

            var body = Assert.IsAssignableFrom<IReadOnlyList<AdminMediaDto>>(Assert.IsType<OkObjectResult>(result).Value);
            var row = Assert.Single(body);
            Assert.Null(row.Moods);
        }

        [Fact]
        public async Task AnActiveMoodFilterExcludesUntaggedRows()
        {
            // Under any ?mood-exact= filter, null-moods rows never match (F86.8).
            var rows = new[]
            {
                MoodsBrowseHarness.Row("tagged", moods: ["warm"]),
                MoodsBrowseHarness.Row("untagged", moods: null),
            };
            var (controller, _) = MoodsBrowseHarness.Build(rows);

            var result = await MoodsBrowseHarness.CallList(controller, moodExact: ["warm"]);

            var body = Assert.IsAssignableFrom<IReadOnlyList<AdminMediaDto>>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(["tagged"], body.Select(r => r.MediaId));
        }
    }

    public sealed class ScenarioUnknownMoodTerm
    {
        [Fact]
        public async Task OutOfVocabularyTermReturnsAnEmptySetWithoutError()
        {
            // ?mood-exact=sparkly (not in MoodVocabulary) returns 200 with zero rows (F86.8).
            var rows = new[] { MoodsBrowseHarness.Row("1", moods: ["warm"]) };
            var (controller, _) = MoodsBrowseHarness.Build(rows);

            var result = await MoodsBrowseHarness.CallList(controller, moodExact: ["sparkly"]);

            var body = Assert.IsAssignableFrom<IReadOnlyList<AdminMediaDto>>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Empty(body);
        }
    }
}

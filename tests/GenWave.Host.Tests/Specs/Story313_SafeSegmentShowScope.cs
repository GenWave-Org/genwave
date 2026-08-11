// STORY-313 — Show scope rides the imaging authoring endpoint (F117.1, PLAN T246 — wire half).
//
// BDD specification — xUnit, in-process. POST /api/safe-segments grows an optional `showId` field:
// absent/null stays station-wide (today's only behavior), a known id flows into
// SafeSegmentRequest.ShowId unchanged, and an unknown id is a 400 with nothing rendered (F27.3's
// validate-first discipline — the same posture libraryId/bedMediaId already have). Mirrors
// Gh149_ImagingKindEndpoint.cs's own construct-the-controller-with-fakes pattern for the other
// collaborators (file-scoped minimal copies — the BulkRatingController duplicate-helper precedent);
// IShowStore is the one exception — it uses the shared Fakes.FakeShowStore, since seeding a known
// show is a one-liner (`new FakeShowStore([new Show(...)])`) that five call sites across this
// project already share.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Options;
using GenWave.Host.Tests.Fakes;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes (minimal Story313 copies of Story079's file-scoped fakes) ───────────────────

file sealed class FakeOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

file sealed class FakeSafeSegmentAuthor : ISafeSegmentAuthor
{
    public SafeSegmentAuthorResult? Result { get; set; }
    public SafeSegmentRequest? LastRequest { get; private set; }
    public int CallCount { get; private set; }

    public Task<SafeSegmentAuthorResult> AuthorAsync(SafeSegmentRequest request, CancellationToken ct)
    {
        CallCount++;
        LastRequest = request;
        return Task.FromResult(Result ?? throw new InvalidOperationException("Result not set"));
    }
}

file sealed class FakeLibraryRepository(params long[] knownIds) : ILibraryRepository
{
    public Task<IReadOnlyList<LibraryInfo>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LibraryInfo>>(
            ids.Where(knownIds.Contains)
               .Select(id => new LibraryInfo(id, $"library-{id}"))
               .ToList());

    public Task<IReadOnlyList<LibraryAdminInfo>> GetAllWithMediaCountAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LibraryAdminInfo>>([]);
}

file sealed class FakeAdminMediaLookup : IAdminMediaLookup
{
    readonly Dictionary<long, (AdminMediaDto Row, long LibraryId)> rows = [];

    public void Add(long id, AdminMediaDto row, long libraryId) => rows[id] = (row, libraryId);

    public Task<(AdminMediaDto Row, long LibraryId)?> GetByIdWithLibraryAsync(long id, CancellationToken ct) =>
        Task.FromResult(rows.TryGetValue(id, out var found)
            ? found
            : ((AdminMediaDto Row, long LibraryId)?)null);
}

file static class Story313ControllerFactory
{
    public static (SafeSegmentsController Controller, FakeSafeSegmentAuthor Author) Build(IShowStore? showStore = null)
    {
        var author = new FakeSafeSegmentAuthor { Result = SafeSegmentAuthorResult.Success(42) };
        var lookup = new FakeAdminMediaLookup();
        lookup.Add(42, SampleRow(42), libraryId: 1);

        var controller = new SafeSegmentsController(
            author,
            new FakeLibraryRepository(1),
            lookup,
            showStore ?? new FakeShowStore(),
            new FakeOptionsMonitor<StationOptions>(new StationOptions
            {
                Id    = "test",
                Name  = "Test Station",
                Voice = "af_heart",
                Safe  = new StationSafeOptions
                {
                    AuthoredRoot  = "/authored",
                    BedDuckDb     = -12.0,
                    BedPadSeconds = 1.5,
                },
            }),
            new FakeOptionsMonitor<TtsOptions>(new TtsOptions()),
            NullLogger<SafeSegmentsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        return (controller, author);
    }

    static AdminMediaDto SampleRow(long id) => new(
        MediaId:        id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Locator:        $"/authored/{id}.wav",
        Format:         "wav",
        State:          "ready",
        DurationMs:     5000,
        Title:          "Please Stand By",
        Artist:         "Test Station",
        Album:          null,
        Genre:          null,
        Year:           null,
        IntegratedLufs: -16.0,
        TruePeakDbtp:   -1.5,
        Measurable:     true,
        CueInSec:       null,
        CueOutSec:      null,
        Eligible:       true,
        Version:        "12345",
        ShowId:         7);
}

// ── Specs ────────────────────────────────────────────────────────────────────────────────────────

public static class FeatureSafeSegmentShowScope
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — a known showId flows into the authoring request
    // ---------------------------------------------------------------------

    public sealed class ScenarioAKnownShowIdFlowsIntoTheAuthoringRequest
    {
        [Fact]
        public async Task AKnownShowIdIsPassedThrough()
        {
            var show = new Show(7, "Show 7", "show-7", null, null, null, null, DateTime.UtcNow, DateTime.UtcNow);
            var (controller, author) = Story313ControllerFactory.Build(new FakeShowStore([show]));

            var result = await controller.Create(
                new SafeSegmentCreateRequest("Text.", 1, ShowId: 7), CancellationToken.None);

            Assert.IsType<CreatedResult>(result);
            Assert.Equal(7, author.LastRequest!.ShowId);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — an absent showId stays station-wide (today's behavior)
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnAbsentShowIdDefaultsToStationWide
    {
        [Fact]
        public async Task ARequestWithoutAShowIdAuthorsAStationWideRow()
        {
            var (controller, author) = Story313ControllerFactory.Build();

            var result = await controller.Create(
                new SafeSegmentCreateRequest("Text.", 1), CancellationToken.None);

            Assert.IsType<CreatedResult>(result);
            Assert.Null(author.LastRequest!.ShowId);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — an unknown showId is a 400 with nothing rendered
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnUnknownShowIdIsRejectedBeforeAnyRender
    {
        [Fact]
        public async Task AnUnknownIdReturns400AndNeverReachesTheAuthor()
        {
            var (controller, author) = Story313ControllerFactory.Build(new FakeShowStore());

            var result = await controller.Create(
                new SafeSegmentCreateRequest("Text.", 1, ShowId: 999), CancellationToken.None);

            var bad = Assert.IsType<BadRequestObjectResult>(result);
            var problem = Assert.IsType<ProblemDetails>(bad.Value);
            Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
            Assert.Contains("999", problem.Detail);
            Assert.Equal(0, author.CallCount);
        }
    }
}

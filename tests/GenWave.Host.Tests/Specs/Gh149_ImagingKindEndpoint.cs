// gh-#149 — Authored segments carry a Station Imaging content kind (wire half).
//
// BDD specification — xUnit, in-process. POST /api/safe-segments grows an optional `kind` token
// (liner | station_id | jingle | promo): absent defaults to liner (today's behavior), a known
// token flows into SafeSegmentRequest.Kind unchanged, and an unknown token is a 400 with nothing
// rendered (F27.3's validate-first discipline). Kinds are METADATA-ONLY — the render pipeline is
// untouched by them. Mirrors Story079's construct-the-controller-with-fakes pattern (the fakes
// there are file-scoped, so this file carries its own minimal copies — the BulkRatingController
// duplicate-helper precedent); IShowStore is the one exception — no scenario here names a showId,
// so the shared Fakes.FakeShowStore's empty-roster default is enough.

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

// ── In-process fakes (minimal Gh149 copies of Story079's file-scoped fakes) ──────────────────────

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

    public Task<LibraryAdminInfo?> GetByNameAsync(string name, CancellationToken ct) =>
        Task.FromResult<LibraryAdminInfo?>(null);
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

file static class Gh149ControllerFactory
{
    public static (SafeSegmentsController Controller, FakeSafeSegmentAuthor Author) Build()
    {
        var author = new FakeSafeSegmentAuthor { Result = SafeSegmentAuthorResult.Success(42) };
        var lookup = new FakeAdminMediaLookup();
        lookup.Add(42, SampleRow(42), libraryId: 1);

        var controller = new SafeSegmentsController(
            author,
            new FakeLibraryRepository(1),
            lookup,
            new FakeShowStore(),
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
        ImagingKind:    "jingle");
}

// ── Specs ────────────────────────────────────────────────────────────────────────────────────────

public static class FeatureImagingKindEndpoint
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — a known kind token flows into the authoring request
    // ---------------------------------------------------------------------

    public sealed class ScenarioAKnownKindFlowsIntoTheAuthoringRequest
    {
        [Fact]
        public async Task StationIdIsParsedAndPassedThrough()
        {
            var (controller, author) = Gh149ControllerFactory.Build();

            var result = await controller.Create(
                new SafeSegmentCreateRequest("Text.", 1, Kind: "station_id"), CancellationToken.None);

            Assert.IsType<CreatedResult>(result);
            Assert.Equal(ImagingKind.StationId, author.LastRequest!.Kind);
        }

        [Fact]
        public async Task TheEnumSpellingIsAcceptedCaseInsensitively()
        {
            var (controller, author) = Gh149ControllerFactory.Build();

            var result = await controller.Create(
                new SafeSegmentCreateRequest("Text.", 1, Kind: "StationId"), CancellationToken.None);

            Assert.IsType<CreatedResult>(result);
            Assert.Equal(ImagingKind.StationId, author.LastRequest!.Kind);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — an absent kind defaults to Liner (today's behavior)
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnAbsentKindDefaultsToLiner
    {
        [Fact]
        public async Task ARequestWithoutAKindAuthorsALiner()
        {
            var (controller, author) = Gh149ControllerFactory.Build();

            var result = await controller.Create(
                new SafeSegmentCreateRequest("Text.", 1), CancellationToken.None);

            Assert.IsType<CreatedResult>(result);
            Assert.Equal(ImagingKind.Liner, author.LastRequest!.Kind);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — an unknown kind is a 400 with nothing rendered
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnUnknownKindIsRejectedBeforeAnyRender
    {
        [Fact]
        public async Task AnUnknownTokenReturns400AndNeverReachesTheAuthor()
        {
            var (controller, author) = Gh149ControllerFactory.Build();

            var result = await controller.Create(
                new SafeSegmentCreateRequest("Text.", 1, Kind: "sweeper-of-doom"), CancellationToken.None);

            var bad = Assert.IsType<BadRequestObjectResult>(result);
            var problem = Assert.IsType<ProblemDetails>(bad.Value);
            Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
            Assert.Contains("liner, station_id, jingle, promo", problem.Detail);
            Assert.Equal(0, author.CallCount);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — "ad" parses generically but is refused HERE (SPEC F158.5/F161.3, PLAN T395
    // review finding-4, RULED): ads are born only through the F161 authored ad-spot tail, never
    // through this generic Station Imaging endpoint.
    // ---------------------------------------------------------------------

    public sealed class ScenarioAdKindIsRejectedEvenThoughItParsesGenerically
    {
        [Fact]
        public async Task AdReturns400AndNeverReachesTheAuthor()
        {
            var (controller, author) = Gh149ControllerFactory.Build();

            var result = await controller.Create(
                new SafeSegmentCreateRequest("Text.", 1, Kind: "ad"), CancellationToken.None);

            var bad = Assert.IsType<BadRequestObjectResult>(result);
            var problem = Assert.IsType<ProblemDetails>(bad.Value);
            Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
            Assert.Contains("liner, station_id, jingle, promo", problem.Detail);
            Assert.Equal(0, author.CallCount);
        }
    }
}

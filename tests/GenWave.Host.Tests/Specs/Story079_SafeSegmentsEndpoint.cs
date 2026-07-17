// STORY-079 — Operator generates a safe segment via the API (WIRE)
//
// BDD specification — xUnit. SPEC F27.3. POST /api/safe-segments: cookie-auth,
// deny-by-default, Content-Type: application/json (F18.7 posture); body
// { text, libraryId, title?, voice?, bedMediaId? }; synchronous render within
// Tts:RenderBudgetSeconds; 201 + created row (GET /api/media/{id} shape,
// Location, ETag); 400 validation / 502 synthesis-mix failure, nothing
// persisted. WIRE: the live proof is a real POST producing a row the very
// next GET /internal/safe-track can select (P6, operator-gated bits in P9).
//
// In-process tests (FeatureSafeSegmentsEndpointInProcess): construct
// SafeSegmentsController directly with fakes (ISafeSegmentAuthor, ILibraryRepository,
// IAdminMediaLookup) — no live stack required. Mirrors the Story047/Story049 pattern.
//
// Operator-gated integration scenarios (FeatureSafeSegmentsEndpoint): remain Skip-pinned —
// real ffmpeg mixing, real Postgres selectability, and the cookie/Content-Type pipeline all
// need the live stack.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Options;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes ──────────────────────────────────────────────────────────────────────────────

/// <summary>Minimal <see cref="IOptionsMonitor{T}"/> that always returns the given value.</summary>
file sealed class FakeOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>
/// Scriptable <see cref="ISafeSegmentAuthor"/>: configure <see cref="Result"/> (or
/// <see cref="ThrowOperationCanceled"/> to simulate the render-budget timeout) before calling the
/// controller. Records every call so tests can assert whether a render was even attempted —
/// the in-process proxy for "nothing persisted" (AC4: <see cref="SafeSegmentAuthor"/> owns the actual
/// all-or-nothing file/row cleanup; STORY-078 covers that directly).
/// </summary>
file sealed class FakeSafeSegmentAuthor : ISafeSegmentAuthor
{
    public SafeSegmentAuthorResult? Result { get; set; }
    public bool ThrowOperationCanceled { get; set; }
    public SafeSegmentRequest? LastRequest { get; private set; }
    public int CallCount { get; private set; }

    public Task<SafeSegmentAuthorResult> AuthorAsync(SafeSegmentRequest request, CancellationToken ct)
    {
        CallCount++;
        LastRequest = request;

        if (ThrowOperationCanceled)
            throw new OperationCanceledException(ct);

        return Task.FromResult(Result ?? throw new InvalidOperationException("Result not set"));
    }
}

/// <summary>In-memory <see cref="ILibraryRepository"/> that only knows the given ids.</summary>
file sealed class FakeSafeSegmentsLibraryRepository(params long[] knownIds) : ILibraryRepository
{
    public Task<IReadOnlyList<LibraryInfo>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LibraryInfo>>(
            ids.Where(knownIds.Contains)
               .Select(id => new LibraryInfo(id, $"library-{id}"))
               .ToList());

    public Task<IReadOnlyList<LibraryAdminInfo>> GetAllWithMediaCountAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LibraryAdminInfo>>([]);
}

/// <summary>
/// Scriptable <see cref="IAdminMediaLookup"/>: register rows by id via <see cref="Add"/>. Serves both
/// roles the controller needs it for — resolving a <c>bedMediaId</c> before render, and re-fetching
/// the newly-inserted row after a successful render.
/// </summary>
file sealed class FakeSafeSegmentsAdminMediaLookup : IAdminMediaLookup
{
    readonly Dictionary<long, (AdminMediaDto Row, long LibraryId)> rows = [];

    public void Add(long id, AdminMediaDto row, long libraryId) => rows[id] = (row, libraryId);

    public Task<(AdminMediaDto Row, long LibraryId)?> GetByIdWithLibraryAsync(long id, CancellationToken ct) =>
        Task.FromResult(rows.TryGetValue(id, out var found)
            ? found
            : ((AdminMediaDto Row, long LibraryId)?)null);
}

/// <summary>Builds a <see cref="SafeSegmentsController"/> wired to the given fakes.</summary>
file static class SafeSegmentsControllerFactory
{
    public static SafeSegmentsController Build(
        ISafeSegmentAuthor author,
        ILibraryRepository libraryRepository,
        IAdminMediaLookup adminLookup,
        StationOptions? stationOptions = null,
        TtsOptions? ttsOptions = null) =>
        new(
            author,
            libraryRepository,
            adminLookup,
            new FakeOptionsMonitor<StationOptions>(stationOptions ?? DefaultStationOptions()),
            new FakeOptionsMonitor<TtsOptions>(ttsOptions ?? new TtsOptions()),
            NullLogger<SafeSegmentsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    public static StationOptions DefaultStationOptions() => new()
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
    };

    public static AdminMediaDto SampleRow(long id, string title = "Please Stand By") => new(
        MediaId:        id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Locator:        $"/authored/{id}.wav",
        Format:         "wav",
        State:          "ready",
        DurationMs:     5000,
        Title:          title,
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
        Version:        "12345");

    public static AdminMediaDto SampleBedRow(long id, double? cueInSec = 1.0, double? cueOutSec = 25.0) => new(
        MediaId:        id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Locator:        $"/media/bed-{id}.mp3",
        Format:         "mp3",
        State:          "ready",
        DurationMs:     30000,
        Title:          "Bed Track",
        Artist:         "Someone",
        Album:          null,
        Genre:          null,
        Year:           null,
        IntegratedLufs: -14.0,
        TruePeakDbtp:   -1.0,
        Measurable:     true,
        CueInSec:       cueInSec,
        CueOutSec:      cueOutSec,
        Eligible:       true,
        Version:        "999");
}

// ── In-process tests ──────────────────────────────────────────────────────────────────────────────

public static class FeatureSafeSegmentsEndpointInProcess
{
    // ── AC1 — valid { text, libraryId } → 201 + created row + Location + ETag ───────────────────

    public sealed class ScenarioGenerateReturns201WithTheRow
    {
        [Fact]
        public async Task AValidRequestReturns201()
        {
            var author = new FakeSafeSegmentAuthor { Result = SafeSegmentAuthorResult.Success(42) };
            var lookup = new FakeSafeSegmentsAdminMediaLookup();
            lookup.Add(42, SafeSegmentsControllerFactory.SampleRow(42), libraryId: 1);
            var controller = SafeSegmentsControllerFactory.Build(
                author, new FakeSafeSegmentsLibraryRepository(1), lookup);

            var result = await controller.Create(
                new SafeSegmentCreateRequest("You're listening to Test Station.", 1), CancellationToken.None);

            var created = Assert.IsType<CreatedResult>(result);
            Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        }

        [Fact]
        public async Task TheResponseBodyIsTheCreatedRowInGetByIdShape()
        {
            var author = new FakeSafeSegmentAuthor { Result = SafeSegmentAuthorResult.Success(42) };
            var lookup = new FakeSafeSegmentsAdminMediaLookup();
            var row    = SafeSegmentsControllerFactory.SampleRow(42);
            lookup.Add(42, row, libraryId: 1);
            var controller = SafeSegmentsControllerFactory.Build(
                author, new FakeSafeSegmentsLibraryRepository(1), lookup);

            var result = await controller.Create(
                new SafeSegmentCreateRequest("Text", 1), CancellationToken.None);

            var created = Assert.IsType<CreatedResult>(result);
            Assert.Equal(row, Assert.IsType<AdminMediaDto>(created.Value));
            Assert.Equal($"W/\"{row.Version}\"", controller.Response.Headers.ETag.ToString());
        }

        [Fact]
        public async Task ALocationHeaderPointsAtTheCreatedRow()
        {
            var author = new FakeSafeSegmentAuthor { Result = SafeSegmentAuthorResult.Success(42) };
            var lookup = new FakeSafeSegmentsAdminMediaLookup();
            lookup.Add(42, SafeSegmentsControllerFactory.SampleRow(42), libraryId: 1);
            var controller = SafeSegmentsControllerFactory.Build(
                author, new FakeSafeSegmentsLibraryRepository(1), lookup);

            var result = await controller.Create(
                new SafeSegmentCreateRequest("Text", 1), CancellationToken.None);

            var created = Assert.IsType<CreatedResult>(result);
            Assert.Equal("/api/media/42", created.Location);
        }

        [Fact]
        public async Task StationNameAndVoiceDefaultsFlowIntoTheAuthorRequest()
        {
            // F27.3: title defaults to "Please Stand By" (owned by SafeSegmentAuthor); voice defaults
            // to Station:Voice; artist is always Station:Name — all resolved here, not in the request.
            var author = new FakeSafeSegmentAuthor { Result = SafeSegmentAuthorResult.Success(42) };
            var lookup = new FakeSafeSegmentsAdminMediaLookup();
            lookup.Add(42, SafeSegmentsControllerFactory.SampleRow(42), libraryId: 1);
            var controller = SafeSegmentsControllerFactory.Build(
                author, new FakeSafeSegmentsLibraryRepository(1), lookup);

            await controller.Create(new SafeSegmentCreateRequest("Text", 1), CancellationToken.None);

            Assert.NotNull(author.LastRequest);
            Assert.Equal("Test Station", author.LastRequest.StationName);
            Assert.Equal("af_heart", author.LastRequest.DefaultVoice);
            Assert.Equal("/authored", author.LastRequest.AuthoredRoot);
            Assert.Equal(-12.0, author.LastRequest.BedDuckDb);
            Assert.Equal(1.5, author.LastRequest.BedPadSeconds);
        }
    }

    // ── AC2 (partial, in-process) — bedMediaId resolves to a row-built BedSpec, never a raw path ──

    public sealed class ScenarioBedMediaIdResolution
    {
        [Fact]
        public async Task AValidBedMediaIdBuildsBedSpecFromTheRowsPathAndCuePoints()
        {
            var author  = new FakeSafeSegmentAuthor { Result = SafeSegmentAuthorResult.Success(42) };
            var lookup  = new FakeSafeSegmentsAdminMediaLookup();
            var bedRow  = SafeSegmentsControllerFactory.SampleBedRow(7);
            lookup.Add(7, bedRow, libraryId: 1);
            lookup.Add(42, SafeSegmentsControllerFactory.SampleRow(42), libraryId: 1);
            var controller = SafeSegmentsControllerFactory.Build(
                author, new FakeSafeSegmentsLibraryRepository(1), lookup);

            await controller.Create(
                new SafeSegmentCreateRequest("Text", 1, BedMediaId: 7), CancellationToken.None);

            var request = author.LastRequest;
            Assert.NotNull(request);
            var bed = request.Bed;
            Assert.NotNull(bed);
            Assert.Equal(bedRow.Locator, bed.Path);
            Assert.Equal(bedRow.CueInSec, bed.CueInSec);
            Assert.Equal(bedRow.CueOutSec, bed.CueOutSec);
        }

        [Fact]
        public async Task InvertedBedCuePointsDegradeToNoCueInsteadOfThrowing()
        {
            // P6 reviewer follow-up: a bed row with CueInSec > CueOutSec used to throw ArgumentException
            // constructing BedSpec (→ 500). Mirrors MediaRow.ResolveCue's inverted-cue discipline
            // (MediaLibrary project): treat as no-cue with a WARN, never throw.
            var author = new FakeSafeSegmentAuthor { Result = SafeSegmentAuthorResult.Success(42) };
            var lookup = new FakeSafeSegmentsAdminMediaLookup();
            var bedRow = SafeSegmentsControllerFactory.SampleBedRow(7, cueInSec: 20.0, cueOutSec: 5.0);
            lookup.Add(7, bedRow, libraryId: 1);
            lookup.Add(42, SafeSegmentsControllerFactory.SampleRow(42), libraryId: 1);
            var controller = SafeSegmentsControllerFactory.Build(
                author, new FakeSafeSegmentsLibraryRepository(1), lookup);

            var result = await controller.Create(
                new SafeSegmentCreateRequest("Text", 1, BedMediaId: 7), CancellationToken.None);

            Assert.IsType<CreatedResult>(result);
            var request = author.LastRequest;
            Assert.NotNull(request);
            var bed = request.Bed;
            Assert.NotNull(bed);
            Assert.Equal(bedRow.Locator, bed.Path);
            Assert.Null(bed.CueInSec);
            Assert.Null(bed.CueOutSec);
        }

        [Fact]
        public async Task NoBedMediaIdRendersVoiceOnly()
        {
            var author = new FakeSafeSegmentAuthor { Result = SafeSegmentAuthorResult.Success(42) };
            var lookup = new FakeSafeSegmentsAdminMediaLookup();
            lookup.Add(42, SafeSegmentsControllerFactory.SampleRow(42), libraryId: 1);
            var controller = SafeSegmentsControllerFactory.Build(
                author, new FakeSafeSegmentsLibraryRepository(1), lookup);

            await controller.Create(new SafeSegmentCreateRequest("Text", 1), CancellationToken.None);

            Assert.NotNull(author.LastRequest);
            Assert.Null(author.LastRequest.Bed);
        }
    }

    // ── AC4 — validation failures return 400, and never even attempt a render ───────────────────

    public sealed class ScenarioValidationFailuresReturn400
    {
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public async Task BlankTextReturns400ProblemDetails(string? text)
        {
            var author = new FakeSafeSegmentAuthor();
            var controller = SafeSegmentsControllerFactory.Build(
                author, new FakeSafeSegmentsLibraryRepository(1), new FakeSafeSegmentsAdminMediaLookup());

            var result = await controller.Create(
                new SafeSegmentCreateRequest(text, 1), CancellationToken.None);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.IsType<ProblemDetails>(badRequest.Value);
            Assert.Equal(0, author.CallCount);
        }

        [Fact]
        public async Task MissingLibraryIdReturns400ProblemDetails()
        {
            var author = new FakeSafeSegmentAuthor();
            var controller = SafeSegmentsControllerFactory.Build(
                author, new FakeSafeSegmentsLibraryRepository(1), new FakeSafeSegmentsAdminMediaLookup());

            var result = await controller.Create(
                new SafeSegmentCreateRequest("Text", null), CancellationToken.None);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.IsType<ProblemDetails>(badRequest.Value);
            Assert.Equal(0, author.CallCount);
        }

        [Fact]
        public async Task UnknownLibraryIdReturns400ProblemDetails()
        {
            var author = new FakeSafeSegmentAuthor();
            var controller = SafeSegmentsControllerFactory.Build(
                author, new FakeSafeSegmentsLibraryRepository(1), new FakeSafeSegmentsAdminMediaLookup());

            var result = await controller.Create(
                new SafeSegmentCreateRequest("Text", 9999), CancellationToken.None);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.IsType<ProblemDetails>(badRequest.Value);
            Assert.Equal(0, author.CallCount);
        }

        [Fact]
        public async Task UnknownBedMediaIdReturns400ProblemDetails()
        {
            var author = new FakeSafeSegmentAuthor();
            var controller = SafeSegmentsControllerFactory.Build(
                author, new FakeSafeSegmentsLibraryRepository(1), new FakeSafeSegmentsAdminMediaLookup());

            var result = await controller.Create(
                new SafeSegmentCreateRequest("Text", 1, BedMediaId: 9999), CancellationToken.None);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.IsType<ProblemDetails>(badRequest.Value);
            Assert.Equal(0, author.CallCount);
        }
    }

    // ── AC5 — a reported authoring failure returns 502 with no internals leaked ─────────────────

    public sealed class ScenarioSynthesisFailureReturns502
    {
        [Theory]
        [InlineData(SafeSegmentFailureReason.SynthesisFailed)]
        [InlineData(SafeSegmentFailureReason.MixFailed)]
        [InlineData(SafeSegmentFailureReason.MeasurementFailed)]
        [InlineData(SafeSegmentFailureReason.InsertFailed)]
        public async Task AnAuthoringFailureReturns502ProblemDetails(SafeSegmentFailureReason reason)
        {
            var author = new FakeSafeSegmentAuthor
            {
                Result = SafeSegmentAuthorResult.Failure(reason, "internal ffmpeg stderr, a stack trace, etc."),
            };
            var controller = SafeSegmentsControllerFactory.Build(
                author, new FakeSafeSegmentsLibraryRepository(1), new FakeSafeSegmentsAdminMediaLookup());

            var result = await controller.Create(
                new SafeSegmentCreateRequest("Text", 1), CancellationToken.None);

            var problem = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status502BadGateway, problem.StatusCode);
            Assert.IsType<ProblemDetails>(problem.Value);
        }

        [Fact]
        public async Task A502DoesNotLeakTheUnderlyingFailureDetail()
        {
            const string sensitiveDetail = "Postgres error 23503 at 10.0.0.4:5432, connection string ...";
            var author = new FakeSafeSegmentAuthor
            {
                Result = SafeSegmentAuthorResult.Failure(SafeSegmentFailureReason.InsertFailed, sensitiveDetail),
            };
            var controller = SafeSegmentsControllerFactory.Build(
                author, new FakeSafeSegmentsLibraryRepository(1), new FakeSafeSegmentsAdminMediaLookup());

            var result = await controller.Create(
                new SafeSegmentCreateRequest("Text", 1), CancellationToken.None);

            var problem = Assert.IsType<ObjectResult>(result);
            var body    = Assert.IsType<ProblemDetails>(problem.Value);
            Assert.DoesNotContain(sensitiveDetail, body.Detail);
        }

        [Fact]
        public async Task ARenderExceedingTheBudgetMapsTo502NotAnUnhandledException()
        {
            // Simulates the linked-CTS budget firing: AuthorAsync observes cancellation on the token
            // it was given, while the caller's own request token (CancellationToken.None) was never
            // cancelled — the controller must distinguish "budget elapsed" from "client disconnected".
            var author = new FakeSafeSegmentAuthor { ThrowOperationCanceled = true };
            var controller = SafeSegmentsControllerFactory.Build(
                author, new FakeSafeSegmentsLibraryRepository(1), new FakeSafeSegmentsAdminMediaLookup());

            var result = await controller.Create(
                new SafeSegmentCreateRequest("Text", 1), CancellationToken.None);

            var problem = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status502BadGateway, problem.StatusCode);
            Assert.IsType<ProblemDetails>(problem.Value);
        }
    }
}

// ── Operator-gated (live stack) ───────────────────────────────────────────────────────────────────

public static class FeatureSafeSegmentsEndpoint
{
    const string Pending = "Pending P6 live wiring — operator-gated, see docs/PLAN.md Epic P";

    // ---------------------------------------------------------------------
    // HAPPY PATH — bed variant + safe-track selectability
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioBedVariantAndSafeTrackSelection
    {
        [Fact(Skip = Pending)]
        public void ABedMediaIdRequestProducesAMixedArtifactUnderAuthored()
        {
            // AC2 — 201; referenced /authored file contains the mixed audio
            Assert.Fail("pending live verification");
        }

        [Fact(Skip = Pending)]
        public void TheNewRowIsSelectableByTheSafeTrackEndpoint()
        {
            // AC3 — with its library in SafeScope, GET /internal/safe-track can
            //       return an annotate line carrying the row's REAL brand tags (F21.4)
            Assert.Fail("pending live verification");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — real Kokoro-down, real auth/content-type pipeline
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioSynthesisFailureReturns502
    {
        [Fact(Skip = Pending)]
        public void KokoroDownReturns502ProblemDetails()
        {
            Assert.Fail("pending live verification");
        }

        [Fact(Skip = Pending)]
        public void A502PersistsNoRowAndNoOrphanFile()
        {
            // AC5 — all-or-nothing surfaces through the endpoint
            Assert.Fail("pending live verification");
        }
    }

    [Trait("Category", "Integration")]
    public sealed class ScenarioDenyByDefaultAndJsonContentTypeEnforced
    {
        [Fact(Skip = Pending)]
        public void AnUncookiedRequestIsRejectedWhenAdminPasswordIsSet()
        {
            // AC6 — 401/403 per the shipped write posture
            Assert.Fail("pending live verification");
        }

        [Fact(Skip = Pending)]
        public void ANonJsonContentTypeIsRejected()
        {
            // AC6 — CSRF posture: Content-Type: application/json required
            Assert.Fail("pending live verification");
        }
    }
}

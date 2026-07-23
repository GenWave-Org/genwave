// STORY-056 — Safe-track endpoint (WIRE)
//
// BDD specification — xUnit. GET /internal/safe-track — anonymous, core network only,
// reads SafeScope via IOptionsMonitor, calls the shipped IMediaCatalog.GetRandomReadyAsync,
// stamps annotations via LiquidsoapAnnotationBuilder (STORY-055), 204 on empty.
// Same trust class as /internal/engine-config (SPEC F21.2).

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Playout;
using GenWave.Host.Api;
using GenWave.Host.Engine;
using GenWave.Host.Options;
// Alias to avoid clash with the GenWave.Loudness namespace (FfmpegLoudnessAnalyzer project).
using TrackLoudness = GenWave.Core.Domain.Loudness;

namespace GenWave.Host.Tests.Specs;

// ── File-scoped fakes ────────────────────────────────────────────────────────

/// <summary>
/// Minimal <see cref="IOptionsMonitor{T}"/> that returns <see cref="CurrentValue"/> on every read.
/// Call <see cref="Update"/> between handler invocations to simulate a live config reload (AC8).
/// </summary>
file sealed class FakeOptionsMonitor<T> : IOptionsMonitor<T>
{
    T current;
    internal FakeOptionsMonitor(T initial) => current = initial;
    internal void Update(T value) => current = value;
    public T CurrentValue => current;
    public T Get(string? name) => current;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>
/// Catalog fake that records every scope and exclude-ID list passed to
/// <see cref="GetRandomReadyAsync"/> so tests can assert what the endpoint sends.
/// Always returns the configured <paramref name="readyTrack"/> regardless of scope.
/// </summary>
file sealed class FakeScopeCaptureCatalog(MediaReference? readyTrack) : IMediaCatalog
{
    readonly List<LibraryScope> capturedScopes = [];
    readonly List<IReadOnlyList<string>> capturedExcludes = [];

    internal IReadOnlyList<LibraryScope> CapturedScopes => capturedScopes;
    internal IReadOnlyList<IReadOnlyList<string>> CapturedExcludes => capturedExcludes;

    public Task<MediaReference?> GetByIdAsync(LibraryScope scope, string mediaId, CancellationToken ct)
        => Task.FromResult<MediaReference?>(null);

    public Task<MediaReference?> GetByIdUnscopedAsync(string mediaId, CancellationToken ct)
        => Task.FromResult<MediaReference?>(null);

    public Task<MediaReference?> GetRandomReadyAsync(LibraryScope scope, IReadOnlyList<string> excludeIds, CancellationToken ct)
    {
        capturedScopes.Add(scope);
        capturedExcludes.Add(excludeIds);
        return Task.FromResult(readyTrack);
    }

    public Task<RotationCandidate?> GetRotationCandidateAsync(
        LibraryScope scope, IReadOnlyList<string> orderedRecentIds, int artistSeparation, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-056's safe-track scenarios — only /internal/safe-track's GetRandomReadyAsync path.");

    public Task<PagedResult<MediaReference>> ListAsync(LibraryScope scope, MediaQuery query, CancellationToken ct)
        => Task.FromResult(new PagedResult<MediaReference>([], 0, 0));

    public Task<CatalogStatusCounts> GetStatusCountsAsync(LibraryScope safeScope, CancellationToken ct)
        => Task.FromResult(new CatalogStatusCounts(0, 0, 0, 0, 0));

    // Not exercised by STORY-056's safe-track scenarios — facets are a curation-console concern (SPEC F52.1).
    public Task<IReadOnlyList<FacetValue>> GetFacetsAsync(FacetField field, LibraryScope scope, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<FacetValue>>([]);
}

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for AC1 only. Sets <c>Admin:Password</c>
/// so the deny-by-default fallback policy is active, removes hosted services that would
/// attempt real Liquidsoap / DB connections, and replaces <see cref="IMediaCatalog"/> with a
/// controllable fake. The safe-track endpoint's <c>AllowAnonymous</c> must bypass that policy.
/// </summary>
/// <remarks>
/// Configuration strategy: both settings are injected per-instance via <c>ConfigureWebHost</c>'s
/// <c>UseSetting</c> (colon-form keys) rather than process environment variables — this reaches
/// <c>AddMediaLibrary</c>'s composition-time <c>GetConnectionString("Library")</c> read in
/// Program.cs (verified empirically), so no shared process state is mutated and no other test
/// class can race with it. A non-reachable host is fine: <see cref="IMediaCatalog"/> is replaced
/// with a fake in <c>ConfigureTestServices</c>, so the real <c>NpgsqlDataSource</c> is never
/// resolved during this test.
/// </remarks>
file sealed class SafeTrackWebFactory(IMediaCatalog catalog) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development config includes Station:Id/Name/Voice/Scope and Tts:Endpoint —
        // all the values required to pass ValidateOnStart().
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "s3cret");

        builder.ConfigureTestServices(services =>
        {
            // Remove ALL hosted services (ScanService, EnrichmentService, PlayoutSupervisor)
            // so the test host does not attempt Liquidsoap socket connections or DB scans.
            services.RemoveAll<IHostedService>();

            // Replace IMediaCatalog with the controllable fake (the real MediaRepository
            // requires a live Postgres and must not be resolved during this test).
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton(catalog);
        });
    }
}

// ── Shared test helpers ──────────────────────────────────────────────────────

public static class FeatureSafeTrackEndpoint
{
    const string OperatorGated = "Operator-gated — requires live engine container calling the endpoint over core; see docs/PLAN.md Epic K";

    /// <summary>
    /// STORY-223/PLAN T85 wired <see cref="ArtworkUrlResolver"/> onto this endpoint's signature —
    /// none of STORY-056's own facts are about artwork, so every call site shares one resolver
    /// backed by an empty <c>Station:PublicBaseUrl</c> (the F88.5 default), which never touches
    /// <see cref="FakeArtworkTokenStore"/>.
    /// </summary>
    static ArtworkUrlResolver NoArtworkResolver() =>
        new(new FakeOptionsMonitor<StationOptions>(new StationOptions()), new FakeArtworkTokenStore());

    /// <summary>
    /// Builds a ready, measurable <see cref="MediaReference"/> for use in tests.
    /// Loudness is set to -20 LUFS / -2 dBTP so gain computation produces a non-zero value.
    /// </summary>
    static MediaReference BuildReadyTrack(string mediaId = "track-001", string locator = "/media/track-001.mp3") => new(
        MediaId: mediaId,
        Locator: locator,
        Title: "Safe Track",
        Loudness: new TrackLoudness(-20.0, -2.0, Measurable: true),
        DurationMs: 180_000,
        SampleRate: 44100,
        Channels: 2,
        BitrateKbps: 320,
        Artist: "Test Artist",
        Album: null,
        Genre: null,
        Year: null);

    /// <summary>
    /// Builds a <see cref="StationOptions"/> with <paramref name="safeScope"/> library IDs.
    /// The main Scope always contains library 1 (so the startup validator is satisfied if it runs).
    /// </summary>
    static StationOptions BuildStationOptions(IList<long>? safeScope = null) => new()
    {
        Id = "test-station",
        Name = "Test Station",
        Voice = "en-us",
        Scope = new StationScopeOptions { LibraryIds = [1L] },
        SafeScope = new StationScopeOptions { LibraryIds = safeScope ?? [1L] },
    };

    /// <summary>
    /// Invokes <see cref="InternalEndpoints.HandleSafeTrackAsync"/> in-process with fake
    /// dependencies. Returns the HTTP status code, the response body (empty for 204), and the
    /// <see cref="DefaultHttpContext"/> so callers can inspect response headers.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="IStatusCodeHttpResult"/> and <see cref="IValueHttpResult{T}"/> to
    /// inspect the returned <see cref="IResult"/> without calling <c>ExecuteAsync</c>, which
    /// requires a non-null <c>HttpContext.RequestServices</c> in .NET 10.
    /// </remarks>
    static async Task<(int StatusCode, string Body, DefaultHttpContext Ctx)> InvokeHandlerAsync(
        IMediaCatalog catalog,
        StationOptions stationOpts,
        LoudnessOptions? loudnessOpts = null)
    {
        var ctx = new DefaultHttpContext();

        var result = await InternalEndpoints.HandleSafeTrackAsync(
            catalog,
            new FakeOptionsMonitor<StationOptions>(stationOpts),
            new FakeOptionsMonitor<LoudnessOptions>(loudnessOpts ?? new LoudnessOptions()),
            NoArtworkResolver(),
            NullLogger.Instance,
            ctx.Response,
            CancellationToken.None);

        // Results.NoContent() → StatusCode = 204; Results.Text() → StatusCode = null (= 200).
        var statusCode = result is IStatusCodeHttpResult s ? s.StatusCode ?? 200 : 200;

        // In .NET 10, ContentHttpResult does not implement IValueHttpResult<string>.
        // Read the body directly from ContentHttpResult.ResponseContent instead.
        var body = result is ContentHttpResult text ? text.ResponseContent ?? string.Empty : string.Empty;

        return (statusCode, body, ctx);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioEndpointIsReachableAnonymously
    {
        [Fact]
        public async Task AnonymousGetReturnsSuccessEvenWhenAdminPasswordIsSet()
        {
            // AC1 — with Admin:Password set, GET /internal/safe-track WITHOUT the genwave-auth
            //       cookie returns 200 or 204 — never 401 or 403 (SPEC F21.2, [AllowAnonymous]
            //       explicit, prevents future middleware regression).
            //
            // Uses WebApplicationFactory so the real auth middleware runs (deny-by-default
            // FallbackPolicy active) and [AllowAnonymous] on the /internal group is exercised.
            var track = BuildReadyTrack();
            await using var factory = new SafeTrackWebFactory(new FakeMediaCatalog(track));
            var client = factory.CreateClient();

            var response = await client.GetAsync("/internal/safe-track");

            Assert.True(
                response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent,
                $"Expected 200 or 204 but got {(int)response.StatusCode} {response.StatusCode}. " +
                "The [AllowAnonymous] group attribute may have been removed.");
        }
    }

    public sealed class ScenarioEndpointReturnsOneAnnotateLine
    {
        [Fact]
        public async Task ResponseBodyIsOneAnnotateLine()
        {
            // AC2 — with SafeScope=[1] and one ready+measurable+eligible row in library 1, the
            //       response body is a single line of Liquidsoap annotate:...:/media/... shape.
            var track = BuildReadyTrack();
            var catalog = new FakeMediaCatalog(track);
            var stationOpts = BuildStationOptions(safeScope: [1L]);

            var (statusCode, body, _) = await InvokeHandlerAsync(catalog, stationOpts);

            Assert.Equal(200, statusCode);
            Assert.StartsWith("annotate:", body, StringComparison.Ordinal);
            Assert.Contains(track.Locator, body, StringComparison.Ordinal);
            // Single-line: the body must not contain a newline character.
            Assert.DoesNotContain('\n', body);
        }

        [Fact]
        public async Task AnnotationIsByteIdenticalToBuildAnnotation()
        {
            // AC3 — the response body byte-equals LiquidsoapAnnotationBuilder.Build(item, gainDb)
            //       for the same MediaItem and gain (SPEC F21.4 shared code path with main pushes).
            var track = BuildReadyTrack();
            var catalog = new FakeMediaCatalog(track);
            var stationOpts = BuildStationOptions(safeScope: [1L]);
            var loudnessOpts = new LoudnessOptions { TargetLufs = -16.0, CeilingDbtp = -1.0 };

            var (_, body, _) = await InvokeHandlerAsync(catalog, stationOpts, loudnessOpts);

            var item = new MediaItem(track.MediaId, track.Locator, track.Title, track.Loudness,
                track.Artist, track.Cue, track.IntroEnergy, track.OutroEnergy);
            var gainDb = Gain.NormGainDb(track.Loudness, loudnessOpts.TargetLufs, loudnessOpts.CeilingDbtp);
            var expected = LiquidsoapAnnotationBuilder.Build(item, gainDb, stationOpts.Id, stationOpts.Name);

            Assert.Equal(expected, body);
        }

        [Fact]
        public async Task SelectionOnlyDrawsFromSafeScopeLibraries()
        {
            // AC4 — with SafeScope=[7,8] and rows across libraries [1,7,8], every returned row's
            //       library_id ∈ {7, 8} — only rows in SafeScope are eligible.
            //       Verified by asserting the scope passed to GetRandomReadyAsync is exactly {7,8}.
            var track = BuildReadyTrack();
            var catalog = new FakeScopeCaptureCatalog(track);
            var stationOpts = BuildStationOptions(safeScope: [7L, 8L]);

            await InternalEndpoints.HandleSafeTrackAsync(
                catalog,
                new FakeOptionsMonitor<StationOptions>(stationOpts),
                new FakeOptionsMonitor<LoudnessOptions>(new LoudnessOptions()),
                NoArtworkResolver(),
                NullLogger.Instance,
                new DefaultHttpContext().Response,
                CancellationToken.None);

            var capturedScope = Assert.Single(catalog.CapturedScopes);
            Assert.Equal(
                new long[] { 7L, 8L }.OrderBy(x => x),
                capturedScope.LibraryIds.OrderBy(x => x));
        }

        [Fact]
        public async Task SelectionRespectsReadyMeasurableAndEligible()
        {
            // AC5 — with a mix of ready / discovered / non-measurable / ineligible rows in the
            //       SafeScope libraries, every returned row satisfies
            //       state='ready' AND measurable AND eligible.
            //       Verified by asserting the endpoint calls GetRandomReadyAsync (which filters
            //       to ready+measurable+eligible by contract) — not GetByIdAsync or ListAsync.
            var track = BuildReadyTrack();
            var catalog = new FakeScopeCaptureCatalog(track);
            var stationOpts = BuildStationOptions(safeScope: [1L]);

            await InternalEndpoints.HandleSafeTrackAsync(
                catalog,
                new FakeOptionsMonitor<StationOptions>(stationOpts),
                new FakeOptionsMonitor<LoudnessOptions>(new LoudnessOptions()),
                NoArtworkResolver(),
                NullLogger.Instance,
                new DefaultHttpContext().Response,
                CancellationToken.None);

            // GetRandomReadyAsync was called exactly once (the method that enforces
            // ready+measurable+eligible filtering in the real catalog implementation).
            Assert.Single(catalog.CapturedScopes);
        }

        [Fact]
        public async Task NoExcludeRecentIsApplied()
        {
            // AC6 — repeated calls against a small safe library are allowed to repeat rows
            //       (SPEC F21.11 — excludeRecent=[]).
            var track = BuildReadyTrack();
            var catalog = new FakeMediaCatalog(track);
            var stationOpts = BuildStationOptions(safeScope: [1L]);

            await InvokeHandlerAsync(catalog, stationOpts);
            await InvokeHandlerAsync(catalog, stationOpts);

            // Both calls recorded an empty exclude list.
            Assert.Equal(2, catalog.RandomCalls.Count);
            Assert.All(catalog.RandomCalls, excludeIds => Assert.Empty(excludeIds));
        }
    }

    public sealed class ScenarioResponseIsNotCacheable
    {
        [Fact]
        public async Task ResponseCarriesCacheControlNoStore()
        {
            // AC7 — the response header contains Cache-Control: no-store so a live SafeScope
            //       edit reflects on the very next engine call (SPEC F21.6).
            var track = BuildReadyTrack();
            var catalog = new FakeMediaCatalog(track);
            var stationOpts = BuildStationOptions(safeScope: [1L]);

            var ctx = new DefaultHttpContext();
            ctx.Response.Body = new MemoryStream();

            await InternalEndpoints.HandleSafeTrackAsync(
                catalog,
                new FakeOptionsMonitor<StationOptions>(stationOpts),
                new FakeOptionsMonitor<LoudnessOptions>(new LoudnessOptions()),
                NoArtworkResolver(),
                NullLogger.Instance,
                ctx.Response,
                CancellationToken.None);

            Assert.Equal("no-store", ctx.Response.Headers.CacheControl.ToString());
        }
    }

    public sealed class ScenarioLiveSafeScopeChangesTakeEffectImmediately
    {
        [Fact]
        public async Task NextCallAfterOptionsMonitorReloadReflectsTheNewScope()
        {
            // AC8 — after serving rows for SafeScope=[1], reloading StationOptions to
            //       SafeScope=[2] makes the very next GET use scope=[2] —
            //       no api restart. (endpoint reads IOptionsMonitor.CurrentValue per call.)
            var track = BuildReadyTrack();
            var catalog = new FakeScopeCaptureCatalog(track);

            var stationMonitor = new FakeOptionsMonitor<StationOptions>(
                BuildStationOptions(safeScope: [1L]));
            var loudnessMonitor = new FakeOptionsMonitor<LoudnessOptions>(new LoudnessOptions());

            // First call: SafeScope=[1]
            await InternalEndpoints.HandleSafeTrackAsync(
                catalog, stationMonitor, loudnessMonitor,
                NoArtworkResolver(),
                NullLogger.Instance,
                new DefaultHttpContext().Response, CancellationToken.None);

            // Simulate live reload: SafeScope changes to [2] (e.g. via K4 settings endpoint).
            stationMonitor.Update(BuildStationOptions(safeScope: [2L]));

            // Second call: SafeScope=[2] — must use the new scope, not the old one.
            await InternalEndpoints.HandleSafeTrackAsync(
                catalog, stationMonitor, loudnessMonitor,
                NoArtworkResolver(),
                NullLogger.Instance,
                new DefaultHttpContext().Response, CancellationToken.None);

            Assert.Equal(2, catalog.CapturedScopes.Count);
            Assert.Equal([1L], catalog.CapturedScopes[0].LibraryIds.ToArray());
            Assert.Equal([2L], catalog.CapturedScopes[1].LibraryIds.ToArray());
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — empty scope + WIRE
    // ---------------------------------------------------------------------

    public sealed class ScenarioEmptyScopeReturnsNoContent
    {
        [Fact]
        public async Task EmptySafeScopeReturns204()
        {
            // AC9 — with SafeScope=[] (or SafeScope=[99] pointing at a library with zero rows),
            //       GET /internal/safe-track returns 204 with an empty body (SPEC F21.5).
            var track = BuildReadyTrack();
            var catalog = new FakeMediaCatalog(track);
            // Empty SafeScope — LibraryScope.IsEmpty will be true.
            var stationOpts = BuildStationOptions(safeScope: []);

            var (statusCode, body, _) = await InvokeHandlerAsync(catalog, stationOpts);

            Assert.Equal(204, statusCode);
            Assert.Empty(body);
        }

        [Fact]
        public async Task NoReadyRowsInScopeReturns204()
        {
            // AC10 — with SafeScope=[1] and every row in library 1 either discovered, ineligible,
            //        or non-measurable, GET /internal/safe-track returns 204.
            //        Modelled by a catalog that returns null from GetRandomReadyAsync.
            var catalog = new FakeMediaCatalog(ready: null);
            var stationOpts = BuildStationOptions(safeScope: [1L]);

            var (statusCode, body, _) = await InvokeHandlerAsync(catalog, stationOpts);

            Assert.Equal(204, statusCode);
            Assert.Empty(body);
        }
    }

    public sealed class ScenarioWireVerification
    {
        [Fact(Skip = OperatorGated), Trait("Category", "Integration")]
        public void RealCurlFromTheEngineContainerReturnsAValidUri()
        {
            // AC11 — WIRE — a real `curl http://api:8080/internal/safe-track` from the engine
            //        container over the core network returns a valid annotate line that
            //        Liquidsoap's request.create accepts as a URI (verified with `liquidsoap
            //        --check` or an equivalent parse harness). Not unit-tests-pass — this is
            //        the production binary producing the spec's side effect.
        }
    }
}

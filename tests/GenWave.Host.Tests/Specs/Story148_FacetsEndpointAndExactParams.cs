// STORY-148 — Eligibility curation by exact artist, album, and genre (Epic Y / SPEC F52.1–F52.4,
// closes gitea-#189) — API half. The SQL half lives in
// MediaLibrary.Tests/Specs/Story148_FacetsAndExactFilterSql.cs; the UI half in
// admin-ui/__specs__/catalog-facet-pickers.spec.tsx.
//
// BDD specification — xUnit. Drives the deployed entry points through the real HTTP pipeline
// (WebApplicationFactory<Program> + app.MapControllers()) — mirrors Story137's
// ScopeNeverBlocksWebFactory pattern: real routing/auth/model-binding, IMediaCatalog/
// IAdminMediaQuery/IAdminMediaWrite/IAdminMediaReenrichment/IStationScopeProvider replaced by
// scriptable fakes, no live Postgres. GET /api/media/facets is served by the standalone
// FacetsController (decoupled from MediaController the same way ReenrichController is), so its
// specs script IMediaCatalog directly; the browse -exact-param and bulk-DTO specs script the
// existing admin seams MediaController/ReenrichController already depend on.

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Scriptable <see cref="IMediaCatalog"/> — the seam GET /api/media/facets reads. Every member
/// beyond <see cref="GetFacetsAsync"/> throws: no fact in this file exercises the playout surface.
/// </summary>
file sealed class FakeMediaCatalog : IMediaCatalog
{
    public IReadOnlyList<FacetValue> FacetsResult { get; set; } = [];
    public FacetField? LastField { get; private set; }
    public LibraryScope LastScope { get; private set; } = LibraryScope.None;

    public Task<MediaReference?> GetByIdAsync(LibraryScope scope, string mediaId, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-148.");

    public Task<MediaReference?> GetByIdUnscopedAsync(string mediaId, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-148.");

    public Task<MediaReference?> GetRandomReadyAsync(LibraryScope scope, IReadOnlyList<string> excludeIds, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-148.");

    public Task<RotationCandidate?> GetRotationCandidateAsync(
        LibraryScope scope, IReadOnlyList<string> orderedRecentIds, int artistSeparation, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-148.");

    public Task<PagedResult<MediaReference>> ListAsync(LibraryScope scope, MediaQuery query, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-148.");

    public Task<CatalogStatusCounts> GetStatusCountsAsync(LibraryScope safeScope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-148.");

    public Task<IReadOnlyList<FacetValue>> GetFacetsAsync(FacetField field, LibraryScope scope, CancellationToken ct)
    {
        LastField = field;
        LastScope = scope;
        return Task.FromResult(FacetsResult);
    }
}

/// <summary>Unused by these specs — every member throws if ever invoked.</summary>
file sealed class ThrowingAdminMediaLookup : IAdminMediaLookup
{
    public Task<(AdminMediaDto Row, long LibraryId)?> GetByIdWithLibraryAsync(long id, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-148.");
}

/// <summary>
/// Scriptable <see cref="IAdminMediaQuery"/>. Applies the exact-match/substring/eligible predicates
/// itself over a caller-supplied row set — mirroring the repository's documented WHERE semantics
/// (equality/OR/substring) without needing a live database, the same idiom as Story145's
/// FakeYearAwareAdminQuery. Real predicate/SQL correctness is proven in MediaLibrary.Tests.
/// </summary>
file sealed class FakeExactAwareAdminQuery : IAdminMediaQuery
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

        if (query.AlbumExact is not null)
            items = items.Where(r => r.Album is not null &&
                string.Equals(r.Album, query.AlbumExact, StringComparison.OrdinalIgnoreCase));

        if (query.GenresExact is { Count: > 0 } genresExact)
            items = items.Where(r => r.Genre is not null &&
                genresExact.Any(g => string.Equals(r.Genre, g, StringComparison.OrdinalIgnoreCase)));

        if (query.Eligible.HasValue)
            items = items.Where(r => r.Eligible == query.Eligible.Value);

        var filtered = items.ToList();
        var paged = filtered.Skip((query.Page - 1) * query.Limit).Take(query.Limit).ToList();
        var pages = (int)Math.Ceiling(filtered.Count / (double)query.Limit);
        return Task.FromResult(new PagedResult<AdminMediaDto>(paged, filtered.Count, pages));
    }
}

/// <summary>
/// Scriptable <see cref="IAdminMediaWrite"/>. Records the <see cref="MediaQuery"/> the bulk
/// eligibility and reassign routes pass through, so the exact-field wiring facts can assert on it
/// without needing a filtered dataset (mirrors Story137's FakeAdminMediaWrite).
/// </summary>
file sealed class FakeAdminMediaWrite : IAdminMediaWrite
{
    public MediaQuery? LastEligibilityFilter { get; private set; }
    public int EligibilityAffected { get; set; }

    public MediaQuery? LastReassignFilter { get; private set; }
    public int? ReassignUpdated { get; set; } = 1;

    public Task<MediaWriteResult> UpdateAsync(string id, MediaPatch patch, string expectedVersion, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-148.");

    public Task<MediaUpdateOutcome> UpdateReturningVersionAsync(string id, MediaPatch patch, string expectedVersion, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-148.");

    public Task<int> SetEligibilityAsync(MediaQuery filter, bool eligible, LibraryScope scope, CancellationToken ct)
    {
        LastEligibilityFilter = filter;
        return Task.FromResult(EligibilityAffected);
    }

    public Task<int?> BulkReassignAsync(MediaQuery filter, long toLibraryId, LibraryScope scope, CancellationToken ct)
    {
        LastReassignFilter = filter;
        return Task.FromResult(ReassignUpdated);
    }
}

/// <summary>
/// Scriptable <see cref="IAdminMediaReenrichment"/>. Records the <see cref="MediaQuery"/> the bulk
/// reenrich route passes through.
/// </summary>
file sealed class FakeAdminMediaReenrichment : IAdminMediaReenrichment
{
    public MediaQuery? LastBulkFilter { get; private set; }
    public int BulkScheduled { get; set; }

    public Task<ReenrichResult> ScheduleAsync(string id, ReenrichFields fields, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-148.");

    public Task<int> ScheduleBulkAsync(MediaQuery filter, ReenrichFields fields, LibraryScope scope, CancellationToken ct)
    {
        LastBulkFilter = filter;
        return Task.FromResult(BulkScheduled);
    }
}

// ── WebApplicationFactory ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> that brings up the real HTTP pipeline while
/// replacing every admin-media seam with a scriptable fake (or a sensible empty default) — mirrors
/// Story137's <c>ScopeNeverBlocksWebFactory</c>. Callers pass only the fakes their fact scripts;
/// everything else defaults to an inert double.
///
/// Admin:Password is always configured (STORY-164/T02: the admin plane fail-closes without one) —
/// every fact that expects success logs in first via <see cref="FeatureFacetsEndpointAndExactParams.LoggedInClientAsync"/>.
/// </summary>
file sealed class FacetsAndExactParamsWebFactory(
    IMediaCatalog? catalog = null,
    IAdminMediaQuery? adminQuery = null,
    IAdminMediaWrite? adminWrite = null,
    IAdminMediaReenrichment? adminReenrichment = null,
    LibraryScope? stationScope = null) : WebApplicationFactory<Program>
{
    const string LibraryConnVar = "ConnectionStrings__Library";
    internal const string Password = "test-password-x7z";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development config provides Station:Id/Name/Voice/Scope/SafeScope and Tts:Endpoint
        // so ValidateOnStart() is satisfied without injecting them manually.
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            // Remove ALL hosted services — no Liquidsoap or DB connections during this test.
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton(catalog ?? new FakeMediaCatalog());

            services.RemoveAll<IAdminMediaLookup>();
            services.AddSingleton<IAdminMediaLookup>(new ThrowingAdminMediaLookup());

            services.RemoveAll<IAdminMediaQuery>();
            services.AddSingleton(adminQuery ?? new FakeExactAwareAdminQuery { Rows = [] });

            services.RemoveAll<IAdminMediaWrite>();
            services.AddSingleton(adminWrite ?? new FakeAdminMediaWrite());

            services.RemoveAll<IAdminMediaReenrichment>();
            services.AddSingleton(adminReenrichment ?? new FakeAdminMediaReenrichment());

            services.RemoveAll<IStationScopeProvider>();
            services.AddSingleton<IStationScopeProvider>(new FakeStationScopeProvider(stationScope ?? new LibraryScope([1L])));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // AddMediaLibrary reads Library connection string early in Program.cs (before
        // ConfigureTestServices runs), so it is injected via the environment. A non-reachable
        // host is fine: every seam that would touch it is replaced with a fake above.
        var prevLib = Environment.GetEnvironmentVariable(LibraryConnVar);
        var prevAdmin = Environment.GetEnvironmentVariable("Admin__Password");
        Environment.SetEnvironmentVariable(LibraryConnVar, "Host=nowhere;Database=test");
        Environment.SetEnvironmentVariable("Admin__Password", Password);
        try
        {
            return base.CreateHost(builder);
        }
        finally
        {
            Environment.SetEnvironmentVariable(LibraryConnVar, prevLib);
            Environment.SetEnvironmentVariable("Admin__Password", prevAdmin);
        }
    }
}

// ── Specs ──────────────────────────────────────────────────────────────────────────────────────────

public static class FeatureFacetsEndpointAndExactParams
{
    /// <summary>Logs in with the factory's fixed test password and returns the now-authenticated client.</summary>
    static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = FacetsAndExactParamsWebFactory.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    static AdminMediaDto Row(string mediaId, string? artist = null, string? album = null, string? genre = null, bool eligible = true) => new(
        MediaId: mediaId,
        Locator: $"/media/{mediaId}.flac",
        Format: "flac",
        State: "ready",
        DurationMs: 180_000,
        Title: "Title",
        Artist: artist,
        Album: album,
        Genre: genre,
        Year: null,
        IntegratedLufs: -14.0,
        TruePeakDbtp: -1.0,
        Measurable: true,
        CueInSec: null,
        CueOutSec: null,
        Eligible: eligible,
        Version: "1");

    // ─────────────────────────────────────────────────────────────────────────
    // HAPPY PATH — the facets endpoint and exact params through the API
    // ─────────────────────────────────────────────────────────────────────────

    // ScopeNeverBlocksWebFactory.CreateHost/this file's factory mutate the ConnectionStrings__Library
    // process env var for the boot window — shared with Story056's/Story058's/Story112's/Story137's
    // factories, so every scenario class below opts into the serializing collection (see
    // EnvVarMutatingWebFactoryCollection) rather than racing them under xUnit's default parallelism.
    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioFacetsEndpointServesTheThreeFields
    {
        [Fact]
        public async Task FieldArtistReturnsValueCountPairs()
        {
            var catalog = new FakeMediaCatalog { FacetsResult = [new FacetValue("Queen", 3), new FacetValue("Rush", 1)] };
            await using var factory = new FacetsAndExactParamsWebFactory(catalog: catalog);
            var client = await LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/media/facets?field=artist");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<List<FacetValue>>();
            Assert.Equal([new FacetValue("Queen", 3), new FacetValue("Rush", 1)], body);
            Assert.Equal(FacetField.Artist, catalog.LastField);
        }

        [Theory]
        [InlineData("album")]
        [InlineData("genre")]
        public async Task FieldAlbumAndGenreServeIdentically(string field)
        {
            var catalog = new FakeMediaCatalog { FacetsResult = [new FacetValue("A Night at the Opera", 1)] };
            await using var factory = new FacetsAndExactParamsWebFactory(catalog: catalog);
            var client = await LoggedInClientAsync(factory);

            var response = await client.GetAsync($"/api/media/facets?field={field}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<List<FacetValue>>();
            Assert.Equal([new FacetValue("A Night at the Opera", 1)], body);
        }

        [Fact]
        public async Task ANamedLibraryIdBecomesTheEffectiveScope()
        {
            // Station scope is [1]; naming library-id=2 makes library 2 the effective scope
            // (F23.3/F52.2), regardless of it sitting outside the station's rotation.
            var catalog = new FakeMediaCatalog { FacetsResult = [] };
            await using var factory = new FacetsAndExactParamsWebFactory(
                catalog: catalog, stationScope: new LibraryScope([1L]));
            var client = await LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/media/facets?field=artist&library-id=2");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal([2L], catalog.LastScope.LibraryIds);
        }

        [Fact]
        public async Task TheEndpointRequiresTheAuthCookie()
        {
            await using var factory = new FacetsAndExactParamsWebFactory();
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.GetAsync("/api/media/facets?field=artist");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioBrowseAcceptsTheExactParams
    {
        [Fact]
        public async Task ArtistExactNarrowsTheBrowse()
        {
            // GET /api/media?artist-exact=Queen against Queen + the Queensrÿche lookalike (F52.3):
            // only the exact case-insensitive equality match returns.
            var rows = new[] { Row("1", artist: "Queen"), Row("2", artist: "Queensrÿche") };
            var query = new FakeExactAwareAdminQuery { Rows = rows };
            await using var factory = new FacetsAndExactParamsWebFactory(adminQuery: query);
            var client = await LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/media?artist-exact=Queen");

            var body = await response.Content.ReadFromJsonAsync<List<AdminMediaDto>>();
            Assert.Equal(["1"], body!.Select(r => r.MediaId));
            Assert.Equal("Queen", query.LastQuery?.ArtistExact);
        }

        [Fact]
        public async Task AnEmptySubstringParamAlongsideExactBindsNullAndSucceeds()
        {
            // GET /api/media?artist=&artist-exact=Queen — the catalog UI's facet picks rely on this
            // binding: a native GET form serializes the empty sibling input too, so picking the
            // artist facet emits `artist=` alongside `artist-exact=Queen`. ASP.NET Core's default
            // [FromQuery] string? binding (ConvertEmptyStringToNull) folds that empty substring
            // param to null before the mutual-exclusion check runs, so no 400 here (contrast
            // ArtistAndArtistExactTogetherReturn400, where the substring is genuinely non-empty).
            var rows = new[] { Row("1", artist: "Queen") };
            var query = new FakeExactAwareAdminQuery { Rows = rows };
            await using var factory = new FacetsAndExactParamsWebFactory(adminQuery: query);
            var client = await LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/media?artist=&artist-exact=Queen");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Queen", query.LastQuery?.ArtistExact);
        }

        [Fact]
        public async Task RepeatedGenreExactParamsOrMatch()
        {
            // GET /api/media?genre-exact=Rock&genre-exact=Metal — either genre matches (F52.3).
            var rows = new[] { Row("1", genre: "Rock"), Row("2", genre: "Metal"), Row("3", genre: "Jazz") };
            var query = new FakeExactAwareAdminQuery { Rows = rows };
            await using var factory = new FacetsAndExactParamsWebFactory(adminQuery: query);
            var client = await LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/media?genre-exact=Rock&genre-exact=Metal");

            var body = await response.Content.ReadFromJsonAsync<List<AdminMediaDto>>();
            Assert.Equal(["1", "2"], body!.Select(r => r.MediaId).OrderBy(x => x));
            Assert.Equal(["Rock", "Metal"], query.LastQuery?.GenresExact);
        }

        [Fact]
        public async Task ExactParamsCombineWithEligibleAndPagination()
        {
            // GET /api/media?artist-exact=Queen&eligible=true&page=1&limit=10 — the exact param
            // composes with the existing eligible/pagination params, not just the other filters.
            var rows = new[]
            {
                Row("1", artist: "Queen", eligible: true),
                Row("2", artist: "Queen", eligible: false),
            };
            var query = new FakeExactAwareAdminQuery { Rows = rows };
            await using var factory = new FacetsAndExactParamsWebFactory(adminQuery: query);
            var client = await LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/media?artist-exact=Queen&eligible=true&page=1&limit=10");

            var body = await response.Content.ReadFromJsonAsync<List<AdminMediaDto>>();
            Assert.Equal(["1"], body!.Select(r => r.MediaId));
            Assert.Equal("Queen", query.LastQuery?.ArtistExact);
            Assert.True(query.LastQuery?.Eligible);
        }
    }

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioBulkFiltersCarryTheExactFields
    {
        [Fact]
        public async Task BulkEligibilityAcceptsArtistExact()
        {
            var write = new FakeAdminMediaWrite();
            await using var factory = new FacetsAndExactParamsWebFactory(adminWrite: write);
            var client = await LoggedInClientAsync(factory);

            var response = await client.PostAsJsonAsync("/api/media/eligibility", new
            {
                eligible = false,
                filter = new { artistExact = "Queen" },
            });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Queen", write.LastEligibilityFilter?.ArtistExact);
        }

        [Fact]
        public async Task BulkReassignAndReenrichAcceptGenresExact()
        {
            var write = new FakeAdminMediaWrite();
            var reenrichment = new FakeAdminMediaReenrichment();
            await using var factory = new FacetsAndExactParamsWebFactory(adminWrite: write, adminReenrichment: reenrichment);
            var client = await LoggedInClientAsync(factory);

            var reassignResponse = await client.PostAsJsonAsync("/api/media/bulk/reassign", new
            {
                toLibraryId = 1,
                filter = new { genresExact = new[] { "Rock", "Metal" } },
            });
            var reenrichResponse = await client.PostAsJsonAsync("/api/media/bulk/reenrich", new
            {
                filter = new { genresExact = new[] { "Rock", "Metal" } },
            });

            Assert.Equal(HttpStatusCode.OK, reassignResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, reenrichResponse.StatusCode);
            Assert.Equal(["Rock", "Metal"], write.LastReassignFilter?.GenresExact);
            Assert.Equal(["Rock", "Metal"], reenrichment.LastBulkFilter?.GenresExact);
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioRejectingInvalidRequests
    {
        [Fact]
        public async Task AMissingFacetFieldReturns400()
        {
            await using var factory = new FacetsAndExactParamsWebFactory();
            var client = await LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/media/facets");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task AnUnknownFacetFieldReturns400()
        {
            await using var factory = new FacetsAndExactParamsWebFactory();
            var client = await LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/media/facets?field=year");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task ArtistAndArtistExactTogetherReturn400()
        {
            var query = new FakeExactAwareAdminQuery { Rows = [] };
            await using var factory = new FacetsAndExactParamsWebFactory(adminQuery: query);
            var client = await LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/media?artist=que&artist-exact=Queen");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Null(query.LastQuery);
        }

        [Fact]
        public async Task GenreAndGenreExactTogetherReturn400()
        {
            var query = new FakeExactAwareAdminQuery { Rows = [] };
            await using var factory = new FacetsAndExactParamsWebFactory(adminQuery: query);
            var client = await LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/media?genre=roc&genre-exact=Rock");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Null(query.LastQuery);
        }
    }
}

// STORY-137 — Scope never blocks direct row access (Epic V / SPEC F43, closes gitea-#203).
// The UI badge half lives in admin-ui/__specs__/out-of-rotation-badge.spec.tsx.
//
// BDD specification — xUnit. GET/PATCH/reenrich facts drive the deployed entry points through the
// real HTTP pipeline (WebApplicationFactory<Program> + app.MapControllers()) with
// IAdminMediaLookup/IAdminMediaWrite/IAdminMediaReenrichment/IStationScopeProvider replaced by
// scriptable fakes — the wire requirement this story calls out (mirrors Story056's
// SafeTrackWebFactory pattern: real routing/auth/model-binding, no live Postgres). The one negative
// fact whose "unnamed bulk filter stays bounded" claim reduces to a single shared helper
// (EffectiveScope.Resolve) also drives the real /api/media/bulk/reassign route, recording the scope
// argument the fake write receives.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

/// <summary>Scriptable <see cref="IAdminMediaLookup"/> — the seam GET /api/media/{id} reads.</summary>
file sealed class FakeAdminMediaLookup : IAdminMediaLookup
{
    public (AdminMediaDto Row, long LibraryId)? Result { get; set; }

    public Task<(AdminMediaDto Row, long LibraryId)?> GetByIdWithLibraryAsync(long id, CancellationToken ct)
        => Task.FromResult(Result);
}

/// <summary>No-op query — none of this story's routes call into the list/browse surface.</summary>
file sealed class NoOpAdminMediaQuery : IAdminMediaQuery
{
    public Task<PagedResult<AdminMediaDto>> ListAdminAsync(LibraryScope scope, MediaQuery query, CancellationToken ct)
        => Task.FromResult(new PagedResult<AdminMediaDto>([], 0, 0));
}

/// <summary>
/// Scriptable <see cref="IAdminMediaWrite"/>. Records the <see cref="LibraryScope"/> the bulk-reassign
/// route passes through, so the "unnamed filter stays bounded" negative fact can assert on it without
/// needing a real filtered dataset.
/// </summary>
file sealed class FakeAdminMediaWrite : IAdminMediaWrite
{
    public MediaUpdateOutcome UpdateOutcome { get; set; } = new(MediaWriteResult.Updated, "1");
    public int? BulkReassignResult { get; set; } = 0;
    public LibraryScope LastBulkReassignScope { get; private set; } = LibraryScope.None;

    public Task<MediaWriteResult> UpdateAsync(
        string id, MediaPatch patch, string expectedVersion, LibraryScope scope, CancellationToken ct)
        => Task.FromResult(UpdateOutcome.Result);

    public Task<MediaUpdateOutcome> UpdateReturningVersionAsync(
        string id, MediaPatch patch, string expectedVersion, LibraryScope scope, CancellationToken ct)
        => Task.FromResult(UpdateOutcome);

    public Task<int> SetEligibilityAsync(MediaQuery filter, bool eligible, LibraryScope scope, CancellationToken ct)
        => Task.FromResult(0);

    public Task<int?> BulkReassignAsync(MediaQuery filter, long toLibraryId, LibraryScope scope, CancellationToken ct)
    {
        LastBulkReassignScope = scope;
        return Task.FromResult(BulkReassignResult);
    }
}

/// <summary>Scriptable <see cref="IAdminMediaReenrichment"/> — the seam POST .../reenrich reads.</summary>
file sealed class FakeAdminMediaReenrichment : IAdminMediaReenrichment
{
    public ReenrichResult ScheduleResult { get; set; } = ReenrichResult.Scheduled;

    public Task<ReenrichResult> ScheduleAsync(string id, ReenrichFields fields, LibraryScope scope, CancellationToken ct)
        => Task.FromResult(ScheduleResult);

    public Task<int> ScheduleBulkAsync(MediaQuery filter, ReenrichFields fields, LibraryScope scope, CancellationToken ct)
        => Task.FromResult(0);
}

// ── WebApplicationFactory ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> that brings up the real HTTP pipeline (routing,
/// auth, model binding, [Consumes] negotiation) while removing hosted services and replacing the
/// admin-media seams with scriptable fakes — mirrors Story056's <c>SafeTrackWebFactory</c>.
///
/// Admin:Password is always configured (STORY-164/T02: the admin plane fail-closes without one) —
/// every fact that expects success logs in first via <see cref="FeatureScopeNeverBlocksRowAccess.LoggedInClientAsync"/>.
/// </summary>
file sealed class ScopeNeverBlocksWebFactory(
    IAdminMediaLookup lookup,
    IAdminMediaWrite write,
    IAdminMediaReenrichment reenrichment,
    LibraryScope stationScope) : WebApplicationFactory<Program>
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

            // Replace the admin-media seams with the controllable fakes above (the real
            // MediaRepository requires a live Postgres and must not be resolved here).
            services.RemoveAll<IAdminMediaLookup>();
            services.AddSingleton(lookup);

            services.RemoveAll<IAdminMediaQuery>();
            services.AddSingleton<IAdminMediaQuery>(new NoOpAdminMediaQuery());

            services.RemoveAll<IAdminMediaWrite>();
            services.AddSingleton(write);

            services.RemoveAll<IAdminMediaReenrichment>();
            services.AddSingleton(reenrichment);

            services.RemoveAll<IStationScopeProvider>();
            services.AddSingleton<IStationScopeProvider>(new FakeStationScopeProvider(stationScope));
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

public static class FeatureScopeNeverBlocksRowAccess
{
    /// <summary>Logs in with the factory's fixed test password and returns the now-authenticated client.</summary>
    static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = ScopeNeverBlocksWebFactory.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    /// <summary>Builds an <see cref="AdminMediaDto"/> row usable by any GET/PATCH fact below.</summary>
    static AdminMediaDto BuildRow(string mediaId, string version) => new(
        MediaId: mediaId,
        Locator: $"/media/{mediaId}.flac",
        Format: "flac",
        State: "ready",
        DurationMs: 180_000,
        Title: "Track",
        Artist: "Artist",
        Album: null,
        Genre: null,
        Year: null,
        IntegratedLufs: -16.0,
        TruePeakDbtp: -1.0,
        Measurable: true,
        CueInSec: null,
        CueOutSec: null,
        Eligible: true,
        Version: version);

    // ScopeNeverBlocksWebFactory.CreateHost mutates the ConnectionStrings__Library process env var
    // for the boot window — shared with Story056's/Story058's/Story112's factories, so every
    // scenario class below opts into the serializing collection (see
    // EnvVarMutatingWebFactoryCollection) rather than racing them under xUnit's default parallelism.
    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioOutOfScopeRowsAreReachableWithTheSignal
    {
        static readonly LibraryScope StationScope = new([1L]);

        [Fact]
        public async Task GetByIdOnAnOutOfScopeRowReturns200()
        {
            var lookup = new FakeAdminMediaLookup { Result = (BuildRow("99", "42"), LibraryId: 2L) };
            await using var factory = new ScopeNeverBlocksWebFactory(
                lookup, new FakeAdminMediaWrite(), new FakeAdminMediaReenrichment(), StationScope);
            var client = await LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/media/99");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task GetByIdOnAnOutOfScopeRowCarriesTheOutOfScopeHeader()
        {
            var lookup = new FakeAdminMediaLookup { Result = (BuildRow("99", "42"), LibraryId: 2L) };
            await using var factory = new ScopeNeverBlocksWebFactory(
                lookup, new FakeAdminMediaWrite(), new FakeAdminMediaReenrichment(), StationScope);
            var client = await LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/media/99");

            Assert.True(response.Headers.Contains("X-Out-Of-Scope"));
            Assert.Equal("true", response.Headers.GetValues("X-Out-Of-Scope").Single());
        }

        [Fact]
        public async Task GetByIdOnAnOutOfScopeRowStillCarriesItsETag()
        {
            var lookup = new FakeAdminMediaLookup { Result = (BuildRow("99", "42"), LibraryId: 2L) };
            await using var factory = new ScopeNeverBlocksWebFactory(
                lookup, new FakeAdminMediaWrite(), new FakeAdminMediaReenrichment(), StationScope);
            var client = await LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/media/99");

            Assert.Equal("W/\"42\"", response.Headers.ETag?.ToString());
        }

        [Fact]
        public async Task PatchOnAnOutOfScopeRowWithAValidIfMatchSucceeds()
        {
            // The row's current library (2) is not in StationScope ([1]) — no libraryId in the
            // patch body, so this is a plain tag edit on an out-of-scope row.
            var write = new FakeAdminMediaWrite
            {
                UpdateOutcome = new MediaUpdateOutcome(MediaWriteResult.Updated, "43", LibraryId: 2L),
            };
            await using var factory = new ScopeNeverBlocksWebFactory(
                new FakeAdminMediaLookup(), write, new FakeAdminMediaReenrichment(), StationScope);
            var client = await LoggedInClientAsync(factory);

            using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/media/99")
            {
                Content = JsonContent.Create(new { title = "New Title" }),
            };
            request.Headers.TryAddWithoutValidation("If-Match", "W/\"42\"");

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [Fact]
        public async Task ReenrichOnAnOutOfScopeRowReturns202()
        {
            var reenrichment = new FakeAdminMediaReenrichment { ScheduleResult = ReenrichResult.Scheduled };
            await using var factory = new ScopeNeverBlocksWebFactory(
                new FakeAdminMediaLookup(), new FakeAdminMediaWrite(), reenrichment, StationScope);
            var client = await LoggedInClientAsync(factory);

            var response = await client.PostAsync("/api/media/99/reenrich", content: null);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        [Fact]
        public async Task AnInScopeRowCarriesNoOutOfScopeHeader()
        {
            var lookup = new FakeAdminMediaLookup { Result = (BuildRow("1", "1"), LibraryId: 1L) };
            await using var factory = new ScopeNeverBlocksWebFactory(
                lookup, new FakeAdminMediaWrite(), new FakeAdminMediaReenrichment(), StationScope);
            var client = await LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/media/1");

            Assert.False(response.Headers.Contains("X-Out-Of-Scope"));
        }

        [Fact]
        public async Task DestinationOutOfScopeSignalingOnReassignIsUnchanged()
        {
            // F20.6 is unrelated to F43's source-row repeal: reassigning INTO library 2 (out of
            // StationScope [1]) still succeeds with the warning header + outOfScope body field.
            var write = new FakeAdminMediaWrite
            {
                UpdateOutcome = new MediaUpdateOutcome(MediaWriteResult.Updated, "44", LibraryId: 1L),
            };
            await using var factory = new ScopeNeverBlocksWebFactory(
                new FakeAdminMediaLookup(), write, new FakeAdminMediaReenrichment(), StationScope);
            var client = await LoggedInClientAsync(factory);

            using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/media/99")
            {
                Content = JsonContent.Create(new { libraryId = 2 }),
            };
            request.Headers.TryAddWithoutValidation("If-Match", "W/\"42\"");

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("true", response.Headers.GetValues("X-Out-Of-Scope").Single());

            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(body.GetProperty("outOfScope").GetBoolean());
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioTheRemainingWallsStand
    {
        static readonly LibraryScope StationScope = new([1L]);

        [Fact]
        public async Task AnUnknownIdIsStill404()
        {
            var lookup = new FakeAdminMediaLookup { Result = null };
            await using var factory = new ScopeNeverBlocksWebFactory(
                lookup, new FakeAdminMediaWrite(), new FakeAdminMediaReenrichment(), StationScope);
            var client = await LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/media/999999");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task AnUnauthenticatedRequestIsRejectedExactlyAsBefore()
        {
            var lookup = new FakeAdminMediaLookup { Result = (BuildRow("99", "42"), LibraryId: 2L) };
            await using var factory = new ScopeNeverBlocksWebFactory(
                lookup, new FakeAdminMediaWrite(), new FakeAdminMediaReenrichment(), StationScope);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.GetAsync("/api/media/99");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task ABulkFilterNamingNoLibraryCannotTouchOutOfScopeRows()
        {
            // F43.4's negative case: an unnamed bulk filter stays bounded by the station scope —
            // the scope handed to the repo is exactly StationScope, never widened.
            var write = new FakeAdminMediaWrite { BulkReassignResult = 0 };
            await using var factory = new ScopeNeverBlocksWebFactory(
                new FakeAdminMediaLookup(), write, new FakeAdminMediaReenrichment(), StationScope);
            var client = await LoggedInClientAsync(factory);

            var response = await client.PostAsJsonAsync("/api/media/bulk/reassign", new { toLibraryId = 9 });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                StationScope.LibraryIds.OrderBy(x => x),
                write.LastBulkReassignScope.LibraryIds.OrderBy(x => x));
        }
    }
}

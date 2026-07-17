// STORY-084 — Status endpoint: one cheap aggregate for the dashboard (WIRE, Epic Q / SPEC F28.6)
//
// BDD specification — xUnit. GET /api/status (cookie-auth) returns
//   { startedAt, catalog: { ready, enriching, failed, unavailable }, safeScope: { libraryIds, playable } }
// camelCase (F18.1), catalog counts + safeScope.playable from IMediaCatalog.GetStatusCountsAsync
// (one grouped query; playable repeats /internal/safe-track's ready+measurable+eligible predicate),
// SafeScope read through IOptionsMonitor (live PUT visible without an api restart — the P9
// stale-snapshot finding this endpoint must not repeat).
//
// In-process scenarios construct StatusController directly with FakeMediaCatalog + a mutable
// FakeOptionsMonitor<StationOptions> (mirrors Story056/Story079's pattern) — no live stack
// required. ScenarioDeployedEndpoint and ScenarioDenyByDefault drive the real HTTP pipeline via
// WebApplicationFactory<Program> (mirrors Story056's SafeTrackWebFactory / Story058's
// SettingsApiWebFactory) so routing, cookie auth, and the deny-by-default fallback policy are all
// exercised for real. The SQL correctness of GetStatusCountsAsync against a live Postgres and the
// true no-restart live-overlay round trip are Q12's acceptance-gate job (E10/W7/.../P9 pattern) —
// not repeated here.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Options;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes ──────────────────────────────────────────────────────────

/// <summary>
/// Minimal <see cref="IOptionsMonitor{T}"/> that returns <see cref="CurrentValue"/> on every read.
/// Call <see cref="Update"/> between calls to simulate a live settings-overlay reload (AC3).
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
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for the deployed-pipeline scenarios. Sets
/// <c>Admin:Password</c> so the deny-by-default fallback policy is active, removes hosted services
/// that would attempt real Liquidsoap/DB connections, and replaces <see cref="IMediaCatalog"/> with
/// a controllable fake — mirrors Story056's <c>SafeTrackWebFactory</c> exactly (both settings go
/// through the environment, not <c>UseSetting</c>: Program.cs reads <c>Admin:Password</c> to decide
/// the fallback policy, and <c>AddMediaLibrary</c> reads the Library connection string, both before
/// <c>builder.Build()</c> — earlier than a <c>ConfigureWebHost</c>-registered override is visible).
/// </summary>
file sealed class StatusApiWebFactory(IMediaCatalog catalog) : WebApplicationFactory<Program>
{
    const string LibraryConnVar = "ConnectionStrings__Library";
    const string AdminPasswordVar = "Admin__Password";
    internal const string Password = "test-password-x7z";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development config provides Station:Id/Name/Voice/Scope/SafeScope and Tts:Endpoint so
        // ValidateOnStart() is satisfied without injecting them manually (Station:SafeScope:LibraryIds
        // defaults to [1] there).
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            // Remove ALL hosted services — no Liquidsoap or DB connections during this test.
            services.RemoveAll<IHostedService>();

            // Replace IMediaCatalog with the controllable fake (the real MediaRepository requires
            // a live Postgres and must not be resolved during this test).
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton(catalog);

            // T9 (SPEC F34.8): StatusController now also resolves IActivePersonaAccessor. The real
            // ActivePersonaAccessor takes IPersonaStore as a constructor dependency, and DI resolves
            // constructor dependencies eagerly — so leaving the real accessor wired would force a
            // real NpgsqlDataSource to build against Station's (unset in this test) connection
            // string the moment the controller is constructed, never mind that Persona:ActiveId is 0
            // and no method on the store would ever actually be called. Replaced with the fake, same
            // as IMediaCatalog above.
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var prevLibrary = Environment.GetEnvironmentVariable(LibraryConnVar);
        var prevAdmin   = Environment.GetEnvironmentVariable(AdminPasswordVar);
        Environment.SetEnvironmentVariable(LibraryConnVar, "Host=nowhere;Database=test");
        Environment.SetEnvironmentVariable(AdminPasswordVar, Password);
        try
        {
            return base.CreateHost(builder);
        }
        finally
        {
            Environment.SetEnvironmentVariable(LibraryConnVar, prevLibrary);
            Environment.SetEnvironmentVariable(AdminPasswordVar, prevAdmin);
        }
    }
}

// ── Shared helpers ──────────────────────────────────────────────────────────

public static class FeatureStatusEndpoint
{
    static StationOptions BuildStationOptions(IList<long>? safeScope = null) => new()
    {
        Id    = "test-station",
        Name  = "Test Station",
        Voice = "en-us",
        Scope = new StationScopeOptions { LibraryIds = [1L] },
        SafeScope = new StationScopeOptions { LibraryIds = safeScope ?? [1L] },
    };

    // Takes the monitor as a parameter (rather than constructing and handing one back) because the
    // concrete FakeOptionsMonitor<T> is file-scoped: a file-scoped type cannot appear in a member
    // signature of this non-file-scoped class. Callers that need to simulate a live reload build
    // their own FakeOptionsMonitor<StationOptions> and call .Update on it directly (AC3).
    //
    // The llm aggregate itself (SPEC F34.8, STORY-125) is exercised in Story125_LlmStatus.cs — every
    // scenario here defaults to a disabled writer + no persona + no attempt, so the catalog/safeScope
    // assertions this file makes are unaffected by T9's addition.
    static StatusController BuildController(
        IMediaCatalog catalog,
        IOptionsMonitor<StationOptions> stationMonitor,
        DateTimeOffset? startedAt = null) =>
        new(
            catalog,
            stationMonitor,
            new FakeOptionsMonitor<LlmOptions>(new LlmOptions()),
            new LlmCopyStatusHolder(),
            new FakeActivePersonaAccessor(),
            new ProcessStartTime(startedAt ?? new DateTimeOffset(2026, 7, 11, 9, 30, 0, TimeSpan.Zero)))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    /// <summary>Serializes an <see cref="OkObjectResult"/>'s value and parses it back as JSON — the
    /// same shape the wire would carry, without spinning up a full HTTP pipeline.</summary>
    static JsonElement AsJson(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return JsonDocument.Parse(JsonSerializer.Serialize(ok.Value)).RootElement;
    }

    static long[] LibraryIds(JsonElement safeScope) =>
        safeScope.GetProperty("libraryIds").EnumerateArray().Select(e => e.GetInt64()).ToArray();

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioAggregateShape
    {
        [Fact]
        public async Task ResponseIsOk()
        {
            var controller = BuildController(
                new FakeMediaCatalog(ready: null), new FakeOptionsMonitor<StationOptions>(BuildStationOptions()));

            var result = await controller.Get(CancellationToken.None);

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task BodyCarriesStartedAt()
        {
            var startedAt = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
            var controller = BuildController(
                new FakeMediaCatalog(ready: null),
                new FakeOptionsMonitor<StationOptions>(BuildStationOptions()),
                startedAt);

            var result = await controller.Get(CancellationToken.None);

            Assert.Equal(startedAt, AsJson(result).GetProperty("startedAt").GetDateTimeOffset());
        }

        [Fact]
        public async Task CatalogCountsMatchTheRowsByState()
        {
            var counts = new CatalogStatusCounts(Ready: 5, Enriching: 3, Failed: 2, Unavailable: 1, Playable: 4);
            var controller = BuildController(
                new FakeMediaCatalog(ready: null, statusCounts: counts),
                new FakeOptionsMonitor<StationOptions>(BuildStationOptions()));

            var result = await controller.Get(CancellationToken.None);

            var body = AsJson(result).GetProperty("catalog");
            Assert.Equal(5, body.GetProperty("ready").GetInt32());
            Assert.Equal(3, body.GetProperty("enriching").GetInt32());
            Assert.Equal(2, body.GetProperty("failed").GetInt32());
            Assert.Equal(1, body.GetProperty("unavailable").GetInt32());
        }

        [Fact]
        public async Task PropertyNamesAreCamelCase()
        {
            var controller = BuildController(
                new FakeMediaCatalog(ready: null), new FakeOptionsMonitor<StationOptions>(BuildStationOptions()));

            var result = await controller.Get(CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var json = JsonSerializer.Serialize(ok.Value);

            foreach (var key in new[]
                     {
                         "startedAt", "catalog", "ready", "enriching", "failed", "unavailable",
                         "safeScope", "libraryIds", "playable",
                     })
            {
                Assert.Contains($"\"{key}\":", json, StringComparison.Ordinal);
            }

            // Guard against a future PascalCase regression (F18.1) rather than just checking presence.
            Assert.DoesNotContain("\"StartedAt\":", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"SafeScope\":", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"LibraryIds\":", json, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioSafeScopePlayablePredicate
    {
        // Arrange: SafeScope libraries hold 2 ready+measurable+eligible rows and 1 ineligible row.
        // The predicate itself (ready + measurable + eligible) is enforced by MediaRepository's
        // grouped SQL, proven identical to GetRandomReadyAsync's WHERE clause by construction; this
        // spec proves the controller passes the count straight through and scopes the call to
        // SafeScope (not the rotation Scope).

        [Fact]
        public async Task PlayableCountsOnlySafeTrackSelectableRows()
        {
            var counts = new CatalogStatusCounts(Ready: 3, Enriching: 0, Failed: 0, Unavailable: 0, Playable: 2);
            var catalog = new FakeMediaCatalog(ready: null, statusCounts: counts);
            var controller = BuildController(
                catalog, new FakeOptionsMonitor<StationOptions>(BuildStationOptions(safeScope: [1L])));

            var result = await controller.Get(CancellationToken.None);

            Assert.Equal(2, AsJson(result).GetProperty("safeScope").GetProperty("playable").GetInt32());
            var scope = Assert.Single(catalog.StatusCountsCalls);
            Assert.Equal([1L], scope.LibraryIds.ToArray());
        }

        [Fact]
        public async Task LibraryIdsEchoTheEffectiveScope()
        {
            var catalog = new FakeMediaCatalog(ready: null);
            var controller = BuildController(
                catalog, new FakeOptionsMonitor<StationOptions>(BuildStationOptions(safeScope: [7L, 8L])));

            var result = await controller.Get(CancellationToken.None);

            Assert.Equal(
                new long[] { 7L, 8L },
                LibraryIds(AsJson(result).GetProperty("safeScope")));
        }
    }

    public sealed class ScenarioLiveScopeNotBootSnapshot
    {
        [Fact]
        public async Task SafeScopeReflectsThePostBootValue()
        {
            // Arrange: SafeScope starts at [1] — mirrors a PUT /api/settings edit (STORY-058)
            // landing between the two GETs, with no controller/api restart in between.
            var catalog = new FakeMediaCatalog(ready: null);
            var stationMonitor = new FakeOptionsMonitor<StationOptions>(BuildStationOptions(safeScope: [1L]));
            var controller = BuildController(catalog, stationMonitor);

            var first = await controller.Get(CancellationToken.None);
            Assert.Equal(new long[] { 1L }, LibraryIds(AsJson(first).GetProperty("safeScope")));

            stationMonitor.Update(BuildStationOptions(safeScope: [2L]));

            var second = await controller.Get(CancellationToken.None);
            Assert.Equal(new long[] { 2L }, LibraryIds(AsJson(second).GetProperty("safeScope")));

            Assert.Equal(2, catalog.StatusCountsCalls.Count);
            Assert.Equal([1L], catalog.StatusCountsCalls[0].LibraryIds.ToArray());
            Assert.Equal([2L], catalog.StatusCountsCalls[1].LibraryIds.ToArray());
        }
    }

    // StatusApiWebFactory.CreateHost mutates the Admin__Password / ConnectionStrings__Library
    // process env vars for the boot window — shared with Story056/Story058's factories, so this
    // class (and ScenarioDenyByDefault below) opts into the serializing collection (see
    // EnvVarMutatingWebFactoryCollection) rather than racing them under xUnit's default parallelism.
    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioDeployedEndpoint
    {
        [Fact]
        public async Task RealRequestReturnsTheAggregate()
        {
            // Drives GET /api/status through the production HTTP pipeline (routing, cookie auth,
            // the deny-by-default fallback policy) with a valid cookie obtained via a real
            // POST /api/auth/login round trip.
            var counts = new CatalogStatusCounts(Ready: 5, Enriching: 3, Failed: 2, Unavailable: 1, Playable: 4);
            var catalog = new FakeMediaCatalog(ready: null, statusCounts: counts);
            await using var factory = new StatusApiWebFactory(catalog);
            var client = factory.CreateClient();

            var login = await client.PostAsJsonAsync("/api/auth/login", new { password = StatusApiWebFactory.Password });
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

            var response = await client.GetAsync("/api/status");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

            Assert.True(body.TryGetProperty("startedAt", out _));

            var catalogJson = body.GetProperty("catalog");
            Assert.Equal(5, catalogJson.GetProperty("ready").GetInt32());
            Assert.Equal(3, catalogJson.GetProperty("enriching").GetInt32());
            Assert.Equal(2, catalogJson.GetProperty("failed").GetInt32());
            Assert.Equal(1, catalogJson.GetProperty("unavailable").GetInt32());

            var safeScopeJson = body.GetProperty("safeScope");
            Assert.Equal(4, safeScopeJson.GetProperty("playable").GetInt32());
            // appsettings.Development.json seeds Station:SafeScope:LibraryIds = [1].
            Assert.Equal(new long[] { 1L }, LibraryIds(safeScopeJson));
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioDenyByDefault
    {
        [Fact]
        public async Task MissingCookieIsUnauthorized()
        {
            var catalog = new FakeMediaCatalog(ready: null);
            await using var factory = new StatusApiWebFactory(catalog);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.GetAsync("/api/status");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}

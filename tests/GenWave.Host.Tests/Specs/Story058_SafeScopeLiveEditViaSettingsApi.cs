// STORY-058 — SafeScope live-edit via F19 settings API (WIRE)
//
// BDD specification — xUnit. GET/PUT /api/settings — no new endpoint (W5 shipped). This
// story extends the AllowedSetting registry wiring so Station:SafeScope:LibraryIds
// round-trips through the settings API and a PUT reflects on the next GET /internal/safe-track
// without an api restart (via IOptionsMonitor). Same CSRF posture as the rest of F18/F19.

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Host.Api;
using GenWave.Host.Configuration;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes ─────────────────────────────────────────────────────────

file sealed class SafeScopeFakeSettingsStore : IStationSettingsStore
{
    readonly Dictionary<string, string> overrides = new(StringComparer.OrdinalIgnoreCase);

    public int WriteCallCount { get; private set; }

    public Task WriteAsync(string key, object value, CancellationToken cancellationToken = default)
    {
        if (!StationSettingsAllowlist.ByKey.ContainsKey(key))
            throw new ArgumentException($"Key '{key}' is not allowlisted.", nameof(key));
        overrides[key] = value.ToString() ?? string.Empty;
        WriteCallCount++;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, string> result =
            new Dictionary<string, string>(overrides, StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(result);
    }
}

// ── WebApplicationFactory for auth/content-type AC tests ─────────────────────

/// <summary>
/// Minimal <see cref="WebApplicationFactory{TEntryPoint}"/> that brings up the real HTTP
/// pipeline (routing, auth, content-type negotiation) while removing background services
/// that need live infrastructure.
///
/// <paramref name="withAdminPassword"/> controls whether a deny-by-default fallback
/// authorization policy is active (AC6 needs it; AC7 does not).
/// </summary>
file sealed class SettingsApiWebFactory(bool withAdminPassword) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development config provides Station:Id/Name/Voice/Scope/SafeScope and Tts:Endpoint
        // so ValidateOnStart() is satisfied without injecting them manually.
        builder.UseEnvironment("Development");

        // AddMediaLibrary reads the Library connection string at composition time in Program.cs —
        // UseSetting (colon-form) reaches that read (verified empirically), so no process env var
        // is needed and no other test class can race with this per-instance value.
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");

        if (withAdminPassword)
        {
            builder.UseSetting("Admin:Password", "test-password-x7z");
        }

        builder.ConfigureTestServices(services =>
        {
            // Remove ALL hosted services — no Liquidsoap or DB connections during this test.
            services.RemoveAll<IHostedService>();
        });
    }
}

// ── Shared helpers ─────────────────────────────────────────────────────────────

public static class FeatureSafeScopeLiveEditViaSettingsApi
{
    const string OperatorGated =
        "Operator-gated — requires running api + Postgres for the live-apply round-trip; see docs/PLAN.md Epic K";

    // Mirrors AllDefaults in Story043; includes Station:SafeScope:LibraryIds as indexed keys
    // (the form ASP.NET Core AddInMemoryCollection requires for arrays).
    static IEnumerable<KeyValuePair<string, string?>> AllDefaults() =>
    [
        new("Loudness:TargetLufs",                            "-16"),
        new("Loudness:CeilingDbtp",                           "-1"),
        new("Station:Cadence:LeadInBeforeEachTrack",          "true"),
        new("Station:Cadence:BackAnnounceAfterEachTrack",     "true"),
        new("Station:Cadence:StationIdEveryNUnits",           "4"),
        // NumberList: stored as colon-indexed keys so GetSection.GetChildren works.
        new("Station:SafeScope:LibraryIds:0",                 "1"),
        new("GW_XFADE_MIN",                                   "3"),
        new("GW_XFADE_MAX",                                   "8"),
    ];

    static IConfiguration BuildConfig(IEnumerable<KeyValuePair<string, string?>> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    static SettingsController BuildController(IConfiguration config, IStationSettingsStore store) =>
        new(
            config,
            store,
            new SettingValidator(config),
            NullLogger<SettingsController>.Instance)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext(),
            },
        };

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioGetSettingsReturnsSafeScope
    {
        [Fact]
        public async Task SafeScopeAppearsInGetSettingsWithLiveApplyMode()
        {
            // AC1 — GET /api/settings returns an entry for Station:SafeScope:LibraryIds with
            //       the current effective value, source ("default" or "override"),
            //       applyMode="live", and kind="number-list".
            var config     = BuildConfig(AllDefaults());
            var store      = new SafeScopeFakeSettingsStore();
            var controller = BuildController(config, store);

            var result = await controller.Get(CancellationToken.None);

            var ok    = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value).ToList();

            var safeScopeItem = items.SingleOrDefault(i =>
                string.Equals(i.Key, "Station:SafeScope:LibraryIds", StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(safeScopeItem);
            Assert.Equal("live",        safeScopeItem.ApplyMode);
            Assert.Equal("number-list", safeScopeItem.Kind);
            // Default from AllDefaults() seeds library id 1.
            Assert.Equal("[1]",         safeScopeItem.Value);
            Assert.Equal("default",     safeScopeItem.Source);
        }
    }

    public sealed class ScenarioPutSettingsPersistsAndAppliesLive
    {
        [Fact]
        public async Task PutPersistsSafeScopeToStationSettings()
        {
            // AC2 — a valid PUT /api/settings body with Station:SafeScope:LibraryIds=[2]
            //       returns 200 and station.settings has the row persisted.
            var config     = BuildConfig(AllDefaults());
            var store      = new SafeScopeFakeSettingsStore();
            var controller = BuildController(config, store);

            var updates = new List<SettingUpdateRequest>
            {
                new("Station:SafeScope:LibraryIds", "[2]"),
            };

            var result = await controller.Put(updates, CancellationToken.None);

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(1, store.WriteCallCount);
        }

        [Fact]
        public async Task PutResponseContainsCorrectMetadataForSafeScope()
        {
            // Supplementary — PUT response entry carries applyMode="live" and kind="number-list".
            var config     = BuildConfig(AllDefaults());
            var store      = new SafeScopeFakeSettingsStore();
            var controller = BuildController(config, store);

            var updates = new List<SettingUpdateRequest>
            {
                new("Station:SafeScope:LibraryIds", "[2]"),
            };

            var result = await controller.Put(updates, CancellationToken.None);

            var ok    = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value).ToList();

            var item = items.Single(i =>
                string.Equals(i.Key, "Station:SafeScope:LibraryIds", StringComparison.OrdinalIgnoreCase));

            Assert.Equal("live",        item.ApplyMode);
            Assert.Equal("number-list", item.Kind);
        }

        [Fact(Skip = OperatorGated), Trait("Category", "Integration")]
        public void LivePutIsObservableByTheSafeTrackEndpointWithoutApiRestart()
        {
            // AC3 — WIRE — SafeScope=[1] serving rows from library 1; PUT /api/settings sets
            //       SafeScope=[2] and completes; the very next GET /internal/safe-track returns
            //       a row from library 2 — IOptionsMonitor re-bound, no api restart.
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — validation, secret-list, CSRF
    // ---------------------------------------------------------------------

    public sealed class ScenarioInvalidPutIsRejectedAndNotPersisted
    {
        [Theory]
        [InlineData("not-an-array",     "non-JSON string")]
        [InlineData("[-1]",             "negative id")]
        [InlineData("[0]",              "zero id (not positive)")]
        // "[]" removed by N1 (STORY-068): empty SafeScope is now valid — degraded-mode, F4.4.
        [InlineData("{\"a\":1}",        "JSON object instead of array")]
        [InlineData("\"[1]\"",          "array encoded as a JSON string")]
        public async Task MalformedSafeScopeReturnsFourHundredAndIsNotPersisted(string badValue, string _)
        {
            // AC4 — invalid values for Station:SafeScope:LibraryIds return 400 ProblemDetails;
            //       station.settings is unchanged. Note: "[]" is NOT invalid (N1/STORY-068).
            var config     = BuildConfig(AllDefaults());
            var store      = new SafeScopeFakeSettingsStore();
            var controller = BuildController(config, store);

            var updates = new List<SettingUpdateRequest>
            {
                new("Station:SafeScope:LibraryIds", badValue),
            };

            var result = await controller.Put(updates, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(0, store.WriteCallCount);
        }
    }

    public sealed class ScenarioSafeScopeIsPublicConfigNotASecret
    {
        [Fact]
        public void SafeScopeIsAbsentFromTheSecretsDenyList()
        {
            // AC5 — Station:SafeScope:LibraryIds is on the allowlist (public, live-editable).
            //       Shipped secret keys are NOT on the allowlist.
            Assert.True(
                StationSettingsAllowlist.ByKey.ContainsKey("Station:SafeScope:LibraryIds"),
                "Station:SafeScope:LibraryIds must be on the operator-editable allowlist.");

            string[] secretKeys =
            [
                "Admin:Password",
                "ConnectionStrings:Station",
                "ConnectionStrings:Library",
                "ICECAST_SOURCE_PASSWORD",
                "POSTGRES_PASSWORD",
            ];

            foreach (var secretKey in secretKeys)
            {
                Assert.False(
                    StationSettingsAllowlist.ByKey.ContainsKey(secretKey),
                    $"Secret key '{secretKey}' must NOT be on the allowlist.");
            }
        }
    }

    public sealed class ScenarioAuthAndContentTypeAreRequired
    {
        [Fact]
        public async Task UnauthenticatedPutIsRejected()
        {
            // AC6 — with Admin:Password set, PUT /api/settings WITHOUT the genwave-auth
            //       cookie returns 401.
            await using var factory = new SettingsApiWebFactory(withAdminPassword: true);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });

            var body = JsonContent.Create(new[]
            {
                new { key = "Loudness:TargetLufs", value = "-14" },
            });
            var response = await client.PutAsync("/api/settings", body);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task NonJsonPutIsRejected()
        {
            // AC7 — a PUT with Content-Type: application/x-www-form-urlencoded is rejected
            //       (415) — mirrors the F18.7 CSRF posture.
            //       No Admin:Password set — the factory opens the API so we test content-type
            //       negotiation only without needing a valid cookie.
            await using var factory = new SettingsApiWebFactory(withAdminPassword: false);
            var client = factory.CreateClient();

            var body = new StringContent(
                "Loudness%3ATargetLufs=-14",
                System.Text.Encoding.UTF8,
                "application/x-www-form-urlencoded");
            var response = await client.PutAsync("/api/settings", body);

            // [Consumes("application/json")] returns 415 Unsupported Media Type.
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        }
    }
}

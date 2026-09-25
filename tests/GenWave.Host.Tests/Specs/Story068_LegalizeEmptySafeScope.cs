// STORY-068 — Legalize empty SafeScope at both validators (WIRE)
//
// BDD specification — xUnit. SPEC F25.1/F25.2/F25.6: the boot-time
// StationOptionsValidator and the PUT-time SettingValidator both accept an empty
// Station:SafeScope:LibraryIds, while non-positive ids still fail and main scope
// (Station:Scope:LibraryIds) stays reject-empty.
//
// T507: the boot and PUT WIRE facts below are proven live — no container needed. Boot uses a real
// WebApplicationFactory<Program> whose config deliberately carries no Station:SafeScope section at
// all (StationScopeOptions.LibraryIds defaults to []), so StationOptionsValidator's real F25.1 branch
// runs for real. PUT reuses the Story058-pattern direct SettingsController call. Only the live-reload
// half of the round trip (the NEXT GET observing the write without an api restart) needs the real
// Postgres-backed settings overlay — same "manual: requires running api + Postgres" situation
// Story058's own LivePutIsObservableByTheSafeTrackEndpointWithoutApiRestart is already pinned on.

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Host.Api;
using GenWave.Host.Configuration;
using GenWave.Host.Options;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureLegalizeEmptySafeScope
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — SettingValidator accepts empty SafeScope on PUT (F25.2)
    // ---------------------------------------------------------------------

    public sealed class ScenarioSettingValidatorAcceptsEmptySafeScope
    {
        readonly SettingValidator validator = new(new ConfigurationBuilder().Build());

        [Fact]
        public void AnEmptyJsonArrayReturnsNullFromValidate()
        {
            // Validate returns null on success; a non-null string on rejection.
            Assert.Null(validator.Validate("Station:SafeScope:LibraryIds", "[]"));
        }

        [Fact]
        public void ASingleElementArrayStillReturnsNull()
        {
            // Regression: relaxing empty must not break the non-empty happy path.
            Assert.Null(validator.Validate("Station:SafeScope:LibraryIds", "[1]"));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — per-element positivity survives the empty-allowed relaxation
    // ---------------------------------------------------------------------

    public sealed class ScenarioNonPositiveSafeScopeIdsStillFail
    {
        readonly SettingValidator validator = new(new ConfigurationBuilder().Build());

        [Fact]
        public void ZeroAsAnIdReturnsARejectionMessage()
        {
            Assert.NotNull(validator.Validate("Station:SafeScope:LibraryIds", "[0]"));
        }

        [Fact]
        public void ANegativeIdReturnsARejectionMessage()
        {
            Assert.NotNull(validator.Validate("Station:SafeScope:LibraryIds", "[-1]"));
        }

        [Fact]
        public void ANonNumericElementReturnsARejectionMessage()
        {
            Assert.NotNull(validator.Validate("Station:SafeScope:LibraryIds", "[\"nope\"]"));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — main scope stays reject-empty (F25.6 with F23.1)
    // ---------------------------------------------------------------------

    public sealed class ScenarioMainScopeStaysRejectEmpty
    {
        readonly SettingValidator validator = new(new ConfigurationBuilder().Build());

        [Fact]
        public void AnEmptyJsonArrayForMainScopeReturnsARejectionMessage()
        {
            // Empty main scope = silent station, not degraded mode — must stay a 400.
            Assert.NotNull(validator.Validate("Station:Scope:LibraryIds", "[]"));
        }

        [Fact]
        public void ANonEmptyMainScopeStillReturnsNull()
        {
            Assert.Null(validator.Validate("Station:Scope:LibraryIds", "[1]"));
        }
    }

    // ---------------------------------------------------------------------
    // WIRE — boot behavior: StationOptionsValidator accepts empty + WARN (T507: live, no container)
    // ---------------------------------------------------------------------

    public sealed class ScenarioBootWithEmptySafeScopeSucceedsAndWarns
    {
        [Fact]
        public async Task TheHostReachesReadyWithAnEmptySafeScopeInAppsettings()
        {
            await using var factory = new EmptySafeScopeWebFactory();
            var client = factory.CreateClient();

            var response = await client.GetAsync("/health");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public void AWarnLogLineNamesTheF44DegradedMode()
        {
            using var factory = new EmptySafeScopeWebFactory();

            // Forces the host to build, then resolves StationOptions for real — the same trigger
            // IValidateOptions<StationOptions> runs under on ANY .Value read, ValidateOnStart's own
            // eager hosted-service trigger having been removed along with every other IHostedService.
            _ = factory.Services.GetRequiredService<IOptions<StationOptions>>().Value;

            Assert.Contains(
                factory.Logs.Messages,
                m => m.Contains("SafeScope empty — drain events play mksafe silence (F4.4 degraded mode)", StringComparison.Ordinal));
        }
    }

    // ---------------------------------------------------------------------
    // WIRE — PUT round-trip: [] returns 200 and logs (F25.2) (T507: live, no container)
    // ---------------------------------------------------------------------

    public sealed class ScenarioPutSafeScopeEmptyRoundTripSucceedsAndWarns
    {
        // Non-empty starting SafeScope: SettingsController.Put's own operator-origin WARN only
        // fires on a genuine non-empty→empty transition (its own F25.2 guard reads the CURRENT
        // configuration section before deciding).
        static IConfiguration BuildConfig() => new ConfigurationBuilder()
            .AddInMemoryCollection([new("Station:SafeScope:LibraryIds:0", "1")])
            .Build();

        static SettingsController BuildController(IConfiguration config, IStationSettingsStore store, ILogger<SettingsController> logger) =>
            new(config, store, new SettingValidator(config), logger, TestSettingCopy.Real(), TestSettingChoiceResolver.Default())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext(),
                },
            };

        [Fact]
        public async Task ThePutReturns200AndPersistsTheOverlay()
        {
            var config = BuildConfig();
            var store = new SafeScopeEmptyFakeSettingsStore();
            using var loggerFactory = LoggerFactory.Create(_ => { });
            var controller = BuildController(config, store, loggerFactory.CreateLogger<SettingsController>());

            var result = await controller.Put(
                [new SettingUpdateRequest("Station:SafeScope:LibraryIds", "[]")], CancellationToken.None);

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(1, store.WriteCallCount);
        }

        [Fact]
        public async Task AWarnLogLineNamesTheOperatorOriginAndF44DegradedMode()
        {
            var config = BuildConfig();
            var store = new SafeScopeEmptyFakeSettingsStore();
            var logs = new CapturingLoggerProvider();
            using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(logs));
            var controller = BuildController(config, store, loggerFactory.CreateLogger<SettingsController>());

            await controller.Put([new SettingUpdateRequest("Station:SafeScope:LibraryIds", "[]")], CancellationToken.None);

            Assert.Contains(
                logs.Messages,
                m => m.Contains("SafeScope emptied by operator — drain events play mksafe silence (F4.4 degraded mode)", StringComparison.Ordinal));
        }

        // The very next GET observing the emptied key without an api restart needs the real
        // Postgres-backed settings overlay + IOptionsMonitor reload this in-process controller call
        // never exercises (Get() reads straight off IConfiguration, which only the real overlay
        // provider re-binds on a write) — the same live-apply situation Story058's own
        // LivePutIsObservableByTheSafeTrackEndpointWithoutApiRestart is pinned on.
        [Fact(Skip = "manual: requires running api + Postgres for the live-apply round-trip; see docs/PLAN.md Epic K"), Trait("Category", "Integration")]
        public void TheKeyIsReadBackAsAnEmptyListOnTheNextGet() { }
    }

    /// <summary>In-process fake mirroring Story058's own <c>SafeScopeFakeSettingsStore</c>.</summary>
    sealed class SafeScopeEmptyFakeSettingsStore : IStationSettingsStore
    {
        readonly Dictionary<string, string> overrides = new(StringComparer.OrdinalIgnoreCase);

        public int WriteCallCount { get; private set; }

        public Task WriteAsync(string key, object value, CancellationToken cancellationToken = default)
        {
            overrides[key] = value.ToString() ?? string.Empty;
            WriteCallCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<string, string> result = new Dictionary<string, string>(overrides, StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// <see cref="WebApplicationFactory{TEntryPoint}"/> whose config carries Station:Id/Name/Voice
    /// and a non-empty main Scope, but no Station:SafeScope section anywhere in the merged
    /// configuration (base appsettings.json has none, and this factory deliberately does not select
    /// the Development environment, so appsettings.Development.json's own non-empty SafeScope never
    /// enters the mix). <c>StationScopeOptions.LibraryIds</c> for SafeScope therefore binds to its
    /// own empty-list default — a genuine F25.1 empty-SafeScope boot, not a value an override could
    /// fake — while the main scope stays non-empty per F25.6/F23.1.
    /// </summary>
    sealed class EmptySafeScopeWebFactory : WebApplicationFactory<Program>
    {
        internal CapturingLoggerProvider Logs { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");

            // GenWave.MediaLibrary.AddMediaLibrary reads ConnectionStrings:Library EAGERLY (at
            // service-registration time, before Program.cs ever calls builder.Build()) — UseSetting
            // is the only override mechanism that lands early enough for that read to see it.
            builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");

            // Base appsettings.json itself hardcodes a non-empty Station:SafeScope:LibraryIds
            // ([1]) — Microsoft.Extensions.Configuration has no way to shrink an array-shaped
            // section via a later override (GetChildren() unions index keys across every
            // provider, so an earlier provider's index 0 survives even when a later one never
            // mentions it). Proving a genuine F25.1 empty-SafeScope boot therefore means replacing
            // configuration wholesale (ConfigureAppConfiguration runs at builder.Build() time —
            // late enough that it never disturbs AddMediaLibrary's eager read above, and the only
            // hook late enough to actually rebuild the source list instead of merely overriding it).
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.Sources.Clear();
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Station:Id"] = "test-station",
                    ["Station:Name"] = "Test Station",
                    ["Station:Voice"] = "af_heart",
                    // Main scope stays non-empty (F25.6/F23.1) — SafeScope is left unmentioned, so
                    // its own StationScopeOptions.LibraryIds default ([]) is what
                    // StationOptionsValidator sees.
                    ["Station:Scope:LibraryIds:0"] = "1",
                    ["ConnectionStrings:Library"] = "Host=nowhere;Database=test",
                    [StationSettingsHostingExtensions.ExpectNoStoreKey] = "true",
                });
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddSingleton<ILoggerProvider>(Logs);
            });
        }
    }
}

// STORY-043 — Edit station settings via the API (WIRE, live-apply)
//
// BDD specification — xUnit.
//
// Runnable scenarios: construct SettingsController directly with fakes/in-memory config so they
// run in-process without a live stack or DB.
//
// Operator-gated scenarios (genuinely require the running stack):
//   • HTTP auth/CSRF enforcement (requires cookie + real auth middleware)
//   • Loudness target reflected in the running stream without an api restart
//   • Override surviving an api restart (requires the DB store to be durable)
//
// Operator-gated tests carry Skip = OperatorGated + Trait("Category","Integration"),
// mirroring Story038 / Story040.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Host.Api;
using GenWave.Host.Configuration;

namespace GenWave.Host.Tests.Specs;

public static class FeatureStationSettingsApi
{
    const string OperatorGated =
        "Operator-verified live (W5); see docs/PLAN.md Epic I";

    // ── In-memory fakes ────────────────────────────────────────────────────

    /// <summary>
    /// In-memory <see cref="IStationSettingsStore"/> that never touches a DB.
    /// WriteAsync stores values in a dictionary; ReadAllAsync returns them.
    /// </summary>
    sealed class FakeSettingsStore : IStationSettingsStore
    {
        readonly Dictionary<string, string> overrides =
            new(StringComparer.OrdinalIgnoreCase);

        public int WriteCallCount { get; private set; }

        public Task WriteAsync(string key, object value, CancellationToken cancellationToken = default)
        {
            if (!StationSettingsAllowlist.ByKey.ContainsKey(key))
                throw new ArgumentException($"Key '{key}' is not allowlisted.", nameof(key));
            overrides[key] = value?.ToString() ?? string.Empty;
            WriteCallCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, string>> ReadAllAsync(
            CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<string, string> result =
                new Dictionary<string, string>(overrides, StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// Builds an <see cref="IConfiguration"/> from a flat set of key/value pairs.
    /// </summary>
    static IConfiguration BuildConfig(IEnumerable<KeyValuePair<string, string?>> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    /// <summary>
    /// Creates a <see cref="SettingsController"/> with the given configuration and store.
    /// </summary>
    static SettingsController BuildController(IConfiguration config, IStationSettingsStore store) =>
        new(
            config,
            store,
            new SettingValidator(config),
            NullLogger<SettingsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

    // ── Default config values for all allowlisted keys ─────────────────────

    static IEnumerable<KeyValuePair<string, string?>> AllDefaults() =>
    [
        new("Loudness:TargetLufs",                            "-16"),
        new("Loudness:CeilingDbtp",                           "-1"),
        new("Station:Cadence:LeadInBeforeEachTrack",          "true"),
        new("Station:Cadence:BackAnnounceAfterEachTrack",     "true"),
        new("Station:Cadence:StationIdEveryNUnits",           "4"),
        new("GW_XFADE_MIN",                                   "3"),
        new("GW_XFADE_MAX",                                   "8"),
    ];

    // =========================================================================
    // HAPPY PATH
    // =========================================================================

    public sealed class ScenarioGetSettings
    {
        [Fact]
        public async Task GetReturnsOneItemPerAllowlistedKey()
        {
            var config     = BuildConfig(AllDefaults());
            var store      = new FakeSettingsStore();
            var controller = BuildController(config, store);

            var result = await controller.Get(CancellationToken.None);

            var ok    = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value);
            Assert.Equal(StationSettingsAllowlist.All.Count, items.Count());
        }

        [Fact]
        public async Task GetSourceIsDefaultWhenNoOverrideExists()
        {
            var config     = BuildConfig(AllDefaults());
            var store      = new FakeSettingsStore();
            var controller = BuildController(config, store);

            var result = await controller.Get(CancellationToken.None);

            var ok    = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value).ToList();
            Assert.All(items, item => Assert.Equal("default", item.Source));
        }

        [Fact]
        public async Task GetSourceIsOverrideWhenStoreHasTheKey()
        {
            var config = BuildConfig(AllDefaults());
            var store  = new FakeSettingsStore();
            // Pre-seed the store with one override
            await store.WriteAsync("Loudness:TargetLufs", "-14");
            var controller = BuildController(config, store);

            var result = await controller.Get(CancellationToken.None);

            var ok    = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value).ToList();

            var lufs = items.Single(i => string.Equals(
                i.Key, "Loudness:TargetLufs", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("override", lufs.Source);

            // All others must remain "default"
            Assert.All(
                items.Where(i => !string.Equals(
                    i.Key, "Loudness:TargetLufs", StringComparison.OrdinalIgnoreCase)),
                item => Assert.Equal("default", item.Source));
        }

        [Fact]
        public async Task GetReturnsCorrectApplyModeForEachKey()
        {
            var config     = BuildConfig(AllDefaults());
            var store      = new FakeSettingsStore();
            var controller = BuildController(config, store);

            var result = await controller.Get(CancellationToken.None);

            var ok    = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value).ToList();

            // Live keys
            Assert.Equal("live", items.Single(i => i.Key == "Loudness:TargetLufs").ApplyMode);
            Assert.Equal("live", items.Single(i => i.Key == "Loudness:CeilingDbtp").ApplyMode);
            Assert.Equal("live", items.Single(i => i.Key == "Station:Cadence:LeadInBeforeEachTrack").ApplyMode);

            // Engine-restart keys
            Assert.Equal("engine-restart", items.Single(i => i.Key == "GW_XFADE_MIN").ApplyMode);
            Assert.Equal("engine-restart", items.Single(i => i.Key == "GW_XFADE_MAX").ApplyMode);
        }

        [Fact]
        public async Task GetNeverReturnsSecretKeys()
        {
            var config = BuildConfig(AllDefaults());
            var store  = new FakeSettingsStore();
            var controller = BuildController(config, store);

            var result = await controller.Get(CancellationToken.None);

            var ok    = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value).ToList();

            var keys = items.Select(i => i.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("Admin:Password",              keys);
            Assert.DoesNotContain("ConnectionStrings:Station",   keys);
            Assert.DoesNotContain("ConnectionStrings:Library",   keys);
            Assert.DoesNotContain("ICECAST_SOURCE_PASSWORD",     keys);
            Assert.DoesNotContain("POSTGRES_PASSWORD",           keys);
        }

        [Fact]
        public async Task GetReturnsKindAndUnitForEachKey()
        {
            // SettingDto now carries kind ("boolean" | "number") and unit ("LUFS", "dBTP", etc.)
            // so the admin UI can render the correct input control with a hint.
            var config     = BuildConfig(AllDefaults());
            var store      = new FakeSettingsStore();
            var controller = BuildController(config, store);

            var result = await controller.Get(CancellationToken.None);

            var ok    = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value).ToList();

            // Number keys carry a non-empty unit
            var lufs    = items.Single(i => i.Key == "Loudness:TargetLufs");
            Assert.Equal("number", lufs.Kind);
            Assert.Equal("LUFS", lufs.Unit);

            var ceiling = items.Single(i => i.Key == "Loudness:CeilingDbtp");
            Assert.Equal("number", ceiling.Kind);
            Assert.Equal("dBTP", ceiling.Unit);

            var stationId = items.Single(i => i.Key == "Station:Cadence:StationIdEveryNUnits");
            Assert.Equal("number", stationId.Kind);
            Assert.Equal("count", stationId.Unit);

            var xfadeMin = items.Single(i => i.Key == "GW_XFADE_MIN");
            Assert.Equal("number", xfadeMin.Kind);
            Assert.Equal("seconds", xfadeMin.Unit);

            var xfadeMax = items.Single(i => i.Key == "GW_XFADE_MAX");
            Assert.Equal("number", xfadeMax.Kind);
            Assert.Equal("seconds", xfadeMax.Unit);

            // Boolean keys have empty unit
            var leadIn = items.Single(i => i.Key == "Station:Cadence:LeadInBeforeEachTrack");
            Assert.Equal("boolean", leadIn.Kind);
            Assert.Equal(string.Empty, leadIn.Unit);

            var backAnnounce = items.Single(i => i.Key == "Station:Cadence:BackAnnounceAfterEachTrack");
            Assert.Equal("boolean", backAnnounce.Kind);
            Assert.Equal(string.Empty, backAnnounce.Unit);
        }

        [Fact]
        public async Task GetReturnsNonEmptyValueForCadenceDefaultsFromAppsettings()
        {
            // Root cause of F2 bug: GET returned "" for cadence/xfade keys because those defaults
            // were not present in appsettings.json. With defaults present, configuration[key] is
            // always populated and the value is never empty.
            var config     = BuildConfig(AllDefaults());
            var store      = new FakeSettingsStore();
            var controller = BuildController(config, store);

            var result = await controller.Get(CancellationToken.None);

            var ok    = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value).ToList();

            // These keys had no default before F2 — they must now return the appsettings default.
            var leadIn = items.Single(i => i.Key == "Station:Cadence:LeadInBeforeEachTrack");
            Assert.False(string.IsNullOrEmpty(leadIn.Value), "LeadInBeforeEachTrack must not be empty.");

            var backAnnounce = items.Single(i => i.Key == "Station:Cadence:BackAnnounceAfterEachTrack");
            Assert.False(string.IsNullOrEmpty(backAnnounce.Value), "BackAnnounceAfterEachTrack must not be empty.");

            var stationId = items.Single(i => i.Key == "Station:Cadence:StationIdEveryNUnits");
            Assert.False(string.IsNullOrEmpty(stationId.Value), "StationIdEveryNUnits must not be empty.");

            var xfadeMin = items.Single(i => i.Key == "GW_XFADE_MIN");
            Assert.False(string.IsNullOrEmpty(xfadeMin.Value), "GW_XFADE_MIN must not be empty.");

            var xfadeMax = items.Single(i => i.Key == "GW_XFADE_MAX");
            Assert.False(string.IsNullOrEmpty(xfadeMax.Value), "GW_XFADE_MAX must not be empty.");
        }
    }

    public sealed class ScenarioPutSettings
    {
        [Fact]
        public async Task PutValidValueCallsWriteAsyncAndReturns200()
        {
            var config     = BuildConfig(AllDefaults());
            var store      = new FakeSettingsStore();
            var controller = BuildController(config, store);

            var updates = new List<SettingUpdateRequest>
            {
                new("Loudness:TargetLufs", "-14"),
            };

            var result = await controller.Put(updates, CancellationToken.None);

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(1, store.WriteCallCount);
        }

        [Fact]
        public async Task PutResponseContainsApplyModeKindAndUnitForEachWrittenKey()
        {
            var config     = BuildConfig(AllDefaults());
            var store      = new FakeSettingsStore();
            var controller = BuildController(config, store);

            var updates = new List<SettingUpdateRequest>
            {
                new("Loudness:TargetLufs", "-14"),
                new("GW_XFADE_MAX",        "10"),
            };

            var result = await controller.Put(updates, CancellationToken.None);

            var ok    = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value).ToList();

            var lufs = items.Single(i => i.Key == "Loudness:TargetLufs");
            Assert.Equal("live",   lufs.ApplyMode);
            Assert.Equal("number", lufs.Kind);
            Assert.Equal("LUFS",   lufs.Unit);

            var xfadeMax = items.Single(i => i.Key == "GW_XFADE_MAX");
            Assert.Equal("engine-restart", xfadeMax.ApplyMode);
            Assert.Equal("number",         xfadeMax.Kind);
            Assert.Equal("seconds",        xfadeMax.Unit);
        }

        [Fact]
        public async Task PutMultipleValidValuesWritesAll()
        {
            var config     = BuildConfig(AllDefaults());
            var store      = new FakeSettingsStore();
            var controller = BuildController(config, store);

            var updates = new List<SettingUpdateRequest>
            {
                new("Loudness:TargetLufs",  "-14"),
                new("Loudness:CeilingDbtp", "-0.5"),
                new("GW_XFADE_MIN",         "2"),
            };

            await controller.Put(updates, CancellationToken.None);

            Assert.Equal(3, store.WriteCallCount);
        }
    }

    // =========================================================================
    // SAD PATH — runnable in-process
    // =========================================================================

    public sealed class ScenarioInvalidPutValues
    {
        [Fact]
        public async Task OutOfTypeValueIsRejectedWith400AndWriteAsyncIsNotCalled()
        {
            var config     = BuildConfig(AllDefaults());
            var store      = new FakeSettingsStore();
            var controller = BuildController(config, store);

            // "notanumber" cannot bind to a double LoudnessOptions.TargetLufs
            var updates = new List<SettingUpdateRequest>
            {
                new("Loudness:TargetLufs", "notanumber"),
            };

            var result = await controller.Put(updates, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(0, store.WriteCallCount);
        }

        [Fact]
        public async Task NonAllowlistedKeyIsRejectedWith400AndWriteAsyncIsNotCalled()
        {
            var config     = BuildConfig(AllDefaults());
            var store      = new FakeSettingsStore();
            var controller = BuildController(config, store);

            var updates = new List<SettingUpdateRequest>
            {
                new("Admin:Password", "newpassword"),
            };

            var result = await controller.Put(updates, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(0, store.WriteCallCount);
        }

        [Fact]
        public async Task SecretKeyIsRejectedWith400AndWriteAsyncIsNotCalled()
        {
            var config     = BuildConfig(AllDefaults());
            var store      = new FakeSettingsStore();
            var controller = BuildController(config, store);

            var secretKeys = new[]
            {
                "Admin:Password",
                "ConnectionStrings:Station",
                "ConnectionStrings:Library",
                "ICECAST_SOURCE_PASSWORD",
                "POSTGRES_PASSWORD",
            };

            foreach (var secret in secretKeys)
            {
                var writesBefore = store.WriteCallCount;
                var updates = new List<SettingUpdateRequest> { new(secret, "anything") };

                var result = await controller.Put(updates, CancellationToken.None);

                Assert.IsType<BadRequestObjectResult>(result);
                Assert.Equal(writesBefore, store.WriteCallCount);
            }
        }

        [Fact]
        public async Task WhenAnyEntryIsInvalidNothingIsPersisted()
        {
            // All-or-nothing: one invalid entry in a multi-entry batch rejects the whole batch.
            var config     = BuildConfig(AllDefaults());
            var store      = new FakeSettingsStore();
            var controller = BuildController(config, store);

            var updates = new List<SettingUpdateRequest>
            {
                new("Loudness:TargetLufs",  "-14"),   // valid
                new("Loudness:CeilingDbtp", "oops"),  // invalid — not a double
            };

            var result = await controller.Put(updates, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(0, store.WriteCallCount);
        }

        [Fact]
        public async Task EmptyUpdateListReturns400()
        {
            var config     = BuildConfig(AllDefaults());
            var store      = new FakeSettingsStore();
            var controller = BuildController(config, store);

            var result = await controller.Put([], CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        // ── Range-validation specs (AC5 engine-safety invariant) ──────────────

        [Fact]
        public async Task TargetLufsAboveZeroIsRejectedWith400AndNothingPersisted()
        {
            // +50 LUFS is physically absurd as a loudness target — must be rejected.
            var config     = BuildConfig(AllDefaults());
            var store      = new FakeSettingsStore();
            var controller = BuildController(config, store);

            var updates = new List<SettingUpdateRequest>
            {
                new("Loudness:TargetLufs", "50"),
            };

            var result = await controller.Put(updates, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(0, store.WriteCallCount);
        }

        [Fact]
        public async Task TargetLufsInRangeIsAcceptedAndPersisted()
        {
            // -14 LUFS is within the sane range — must pass and persist.
            var config     = BuildConfig(AllDefaults());
            var store      = new FakeSettingsStore();
            var controller = BuildController(config, store);

            var updates = new List<SettingUpdateRequest>
            {
                new("Loudness:TargetLufs", "-14"),
            };

            var result = await controller.Put(updates, CancellationToken.None);

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(1, store.WriteCallCount);
        }

        [Fact]
        public async Task NegativeXfadeMinIsRejectedWith400AndNothingPersisted()
        {
            // GW_XFADE_MIN must be > 0; negative seconds are nonsense.
            var config     = BuildConfig(AllDefaults());
            var store      = new FakeSettingsStore();
            var controller = BuildController(config, store);

            var updates = new List<SettingUpdateRequest>
            {
                new("GW_XFADE_MIN", "-5"),
            };

            var result = await controller.Put(updates, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(0, store.WriteCallCount);
        }

        [Fact]
        public async Task XfadeMinGreaterThanXfadeMaxInSameBatchIsRejectedWith400AndNothingPersisted()
        {
            // MIN=9, MAX=8 in the same PUT batch violates the MIN ≤ MAX invariant.
            var config     = BuildConfig(AllDefaults());
            var store      = new FakeSettingsStore();
            var controller = BuildController(config, store);

            var updates = new List<SettingUpdateRequest>
            {
                new("GW_XFADE_MIN", "9"),
                new("GW_XFADE_MAX", "8"),
            };

            var result = await controller.Put(updates, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(0, store.WriteCallCount);
        }

        [Fact]
        public async Task XfadeMinGreaterThanCurrentConfigMaxIsRejectedWith400AndNothingPersisted()
        {
            // Single-key PUT: MIN=9 when current config MAX=8 must also be rejected.
            var configValues = AllDefaults().ToList();
            // Override GW_XFADE_MAX in the current config to 8 (already the default, but explicit).
            var config     = BuildConfig(configValues);
            var store      = new FakeSettingsStore();
            var controller = BuildController(config, store);

            // With GW_XFADE_MAX=8 (from AllDefaults), proposing MIN=9 inverts the pair.
            var updates = new List<SettingUpdateRequest>
            {
                new("GW_XFADE_MIN", "9"),
            };

            var result = await controller.Put(updates, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(0, store.WriteCallCount);
        }

        [Fact]
        public async Task InvalidBoolForCadenceKeyReturns400()
        {
            var config     = BuildConfig(AllDefaults());
            var store      = new FakeSettingsStore();
            var controller = BuildController(config, store);

            var updates = new List<SettingUpdateRequest>
            {
                new("Station:Cadence:LeadInBeforeEachTrack", "maybe"),
            };

            var result = await controller.Put(updates, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(0, store.WriteCallCount);
        }

        [Fact]
        public async Task NegativeIntForStationIdEveryNUnitsReturns400()
        {
            // Superseded by STORY-136 (SPEC F42.2, closes gitea-#216): 0 is now a legal value (it
            // disables station IDs entirely — see FeatureStationIdCadenceValidation in
            // Story136_StationIdCadenceValidation.cs), so this sad-path case moves to a genuinely
            // still-invalid negative value.
            var config     = BuildConfig(AllDefaults());
            var store      = new FakeSettingsStore();
            var controller = BuildController(config, store);

            var updates = new List<SettingUpdateRequest>
            {
                new("Station:Cadence:StationIdEveryNUnits", "-1"),
            };

            var result = await controller.Put(updates, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(0, store.WriteCallCount);
        }
    }

    // =========================================================================
    // OPERATOR-GATED (live stack required — skip in CI)
    // =========================================================================

    public sealed class ScenarioLiveStackVerification
    {
        [Fact(Skip = OperatorGated), Trait("Category", "Integration")]
        public void GetSettingsReturnsEachNonSecretTunableWithSourceAndApplyMode()
        {
            // GET /api/settings → each item is { key, value, source(default|override), applyMode(live|engine-restart) }.
            // Verify against the running stack that the response shape matches StationSettingsAllowlist.All.
            Assert.Fail(OperatorGated);
        }

        [Fact(Skip = OperatorGated), Trait("Category", "Integration")]
        public void ChangingLoudnessTargetReflectsOnTheNextPushWithoutAnApiRestart()
        {
            // PUT Loudness:TargetLufs to a new value.
            // Observe the next feeder push annotation carries the updated gain without restarting the api container.
            // (Verified by checking the Liquidsoap telnet metadata for the encoded gain annotation.)
            Assert.Fail(OperatorGated);
        }

        [Fact(Skip = OperatorGated), Trait("Category", "Integration")]
        public void EngineSideKnobReportsApplyModeEngineRestart()
        {
            // GET /api/settings returns applyMode = "engine-restart" for GW_XFADE_MIN / GW_XFADE_MAX.
            // PUT GW_XFADE_MAX persists successfully; the running stream is NOT affected until the engine restarts.
            Assert.Fail(OperatorGated);
        }

        [Fact(Skip = OperatorGated), Trait("Category", "Integration")]
        public void AnOverrideSurvivesAnApiRestart()
        {
            // PUT an override; restart the api container; GET /api/settings still shows source=override and the stored value.
            Assert.Fail(OperatorGated);
        }

        [Fact(Skip = OperatorGated), Trait("Category", "Integration")]
        public void WriteWithoutCookieOrWithNonJsonContentTypeIsRejected()
        {
            // With Admin:Password set: no cookie → 401; non-JSON Content-Type → 415.
            Assert.Fail(OperatorGated);
        }
    }
}

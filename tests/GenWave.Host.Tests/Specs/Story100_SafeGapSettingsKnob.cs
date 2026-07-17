// STORY-100 — Gap knob in the settings surface (Epic R / SPEC F29.8, gitea-#182)
//
// BDD specification — xUnit, un-pinned at R5.
// GW_SAFE_GAP_SECONDS joins the F19 allowlist as an engine-restart key, mirroring GW_XFADE_*
// (Story058/Story091's registry + validator seams). Same in-process SettingsController pattern
// as Story043 — no live stack or DB required.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Host.Api;
using GenWave.Host.Configuration;

namespace GenWave.Host.Tests.Specs;

public static class FeatureSafeGapSettingsKnob
{
    // ── In-memory fakes (mirrors Story043's FakeSettingsStore) ─────────────

    sealed class FakeSettingsStore : IStationSettingsStore
    {
        readonly Dictionary<string, string> overrides = new(StringComparer.OrdinalIgnoreCase);

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

    static IConfiguration BuildConfig(IEnumerable<KeyValuePair<string, string?>> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

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

    // Mirrors Story043's AllDefaults() — GW_SAFE_GAP_SECONDS added at its appsettings default.
    static IEnumerable<KeyValuePair<string, string?>> AllDefaults() =>
    [
        new("Loudness:TargetLufs",                            "-16"),
        new("Loudness:CeilingDbtp",                           "-1"),
        new("Station:Cadence:LeadInBeforeEachTrack",          "true"),
        new("Station:Cadence:BackAnnounceAfterEachTrack",     "true"),
        new("Station:Cadence:StationIdEveryNUnits",           "4"),
        new("GW_XFADE_MIN",                                   "3"),
        new("GW_XFADE_MAX",                                   "8"),
        new("GW_SAFE_GAP_SECONDS",                            "7"),
    ];

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioKeyInTheAllowlist
    {
        [Fact]
        public async Task GetSettingsListsTheKeyWithEngineRestartApplyMode()
        {
            // GET /api/settings → entry { key: GW_SAFE_GAP_SECONDS, applyMode: "engine-restart" }
            // — exact key naming follows the shipped GW_XFADE_* allowlist rows.
            var config     = BuildConfig(AllDefaults());
            var store      = new FakeSettingsStore();
            var controller = BuildController(config, store);

            var result = await controller.Get(CancellationToken.None);

            var ok    = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value).ToList();

            var gap = items.SingleOrDefault(i =>
                string.Equals(i.Key, "GW_SAFE_GAP_SECONDS", StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(gap);
            Assert.Equal("engine-restart", gap.ApplyMode);
            Assert.Equal("number",         gap.Kind);
            Assert.Equal("seconds",        gap.Unit);
            Assert.Equal("7",              gap.Value);
        }

        [Fact]
        public async Task PutPersistsToTheOverlayLikeItsXfadeSiblings()
        {
            // PUT a valid value → 200; settings store holds the override; source flips to
            // "override" on the next GET.
            var config     = BuildConfig(AllDefaults());
            var store      = new FakeSettingsStore();
            var controller = BuildController(config, store);

            var updates = new List<SettingUpdateRequest>
            {
                new("GW_SAFE_GAP_SECONDS", "10"),
            };

            var putResult = await controller.Put(updates, CancellationToken.None);

            Assert.IsType<OkObjectResult>(putResult);
            Assert.Equal(1, store.WriteCallCount);

            var getResult = await controller.Get(CancellationToken.None);
            var ok    = Assert.IsType<OkObjectResult>(getResult);
            var items = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value).ToList();

            var gap = items.Single(i =>
                string.Equals(i.Key, "GW_SAFE_GAP_SECONDS", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("override", gap.Source);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioInvalidValueRejected
    {
        [Fact]
        public async Task NegativeGapIsBadRequestAndNothingPersists()
        {
            // PUT -1 → 400 ProblemDetails; store unchanged (F19.5).
            var config     = BuildConfig(AllDefaults());
            var store      = new FakeSettingsStore();
            var controller = BuildController(config, store);

            var updates = new List<SettingUpdateRequest>
            {
                new("GW_SAFE_GAP_SECONDS", "-1"),
            };

            var result = await controller.Put(updates, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(0, store.WriteCallCount);
        }
    }
}

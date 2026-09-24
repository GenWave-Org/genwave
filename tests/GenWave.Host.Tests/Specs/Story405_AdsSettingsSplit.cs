// STORY-405 — The settings split holds: Ads:* Live, Packs:* env/compose-only (SPEC F170.1/F170.2 · PLAN T417)
//
// Runnable in-process against the real SettingsController.Put, the Story043_StationSettingsApi
// pattern: an in-memory IConfiguration + a fake IStationSettingsStore, no live stack or DB.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Host.Api;
using GenWave.Host.Configuration;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAdsSettingsAreLiveAndPacksSettingsAreEnvOnly
{
    // ── In-memory fakes (mirrors Story043_StationSettingsApi's own FakeSettingsStore/BuildController) ──

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
            NullLogger<SettingsController>.Instance,
            new FakeIconPackStore(),
            TestSettingCopy.Real())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

    static IEnumerable<KeyValuePair<string, string?>> AllDefaults() =>
    [
        new("Station:Ads:AnnouncerVoice", ""),
        new("Station:Ads:CastVoices",     "af_nova,am_michael,bf_alice,am_onyx"),
        new("Station:Ads:BedFadeMs",      "300"),
        new("Station:Ads:BedDuckDb",      "-12"),
    ];

    /// <summary>
    /// The BuildConfig/FakeSettingsStore/BuildController triple every fact below needs, seeded with
    /// <see cref="AllDefaults"/> — extracted because five facts repeated it verbatim. Returns the
    /// store alongside the controller since every fact also asserts against
    /// <see cref="FakeSettingsStore.WriteCallCount"/>/<see cref="FakeSettingsStore.ReadAllAsync"/>.
    /// </summary>
    static (SettingsController Controller, FakeSettingsStore Store) NewController()
    {
        var config = BuildConfig(AllDefaults());
        var store = new FakeSettingsStore();
        var controller = BuildController(config, store);
        return (controller, store);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — live keys accept via allowlist
    // ---------------------------------------------------------------------

    public sealed class ScenarioLiveAllowlistAcceptsAdsKeys
    {
        [Fact]
        public async Task AdsAnnouncerVoiceIsAcceptedByThePutSettingsEndpoint()
        {
            var (controller, store) = NewController();

            var updates = new List<SettingUpdateRequest> { new("Station:Ads:AnnouncerVoice", "am_adam") };

            var result = await controller.Put(updates, CancellationToken.None);

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(1, store.WriteCallCount);
            var overrides = await store.ReadAllAsync(CancellationToken.None);
            Assert.Equal("am_adam", overrides["Station:Ads:AnnouncerVoice"]);
        }

        [Fact]
        public async Task AdsCastVoicesIsAcceptedByThePutSettingsEndpoint()
        {
            var (controller, store) = NewController();

            const string value = "af_nova,am_michael,bf_alice,am_onyx";
            var updates = new List<SettingUpdateRequest> { new("Station:Ads:CastVoices", value) };

            var result = await controller.Put(updates, CancellationToken.None);

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(1, store.WriteCallCount);
            var overrides = await store.ReadAllAsync(CancellationToken.None);
            Assert.Equal(value, overrides["Station:Ads:CastVoices"]);
        }

        [Fact]
        public async Task AdsBedFadeMsIsAcceptedByThePutSettingsEndpoint()
        {
            var (controller, store) = NewController();

            var updates = new List<SettingUpdateRequest> { new("Station:Ads:BedFadeMs", "500") };

            var result = await controller.Put(updates, CancellationToken.None);

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(1, store.WriteCallCount);
            var overrides = await store.ReadAllAsync(CancellationToken.None);
            Assert.Equal("500", overrides["Station:Ads:BedFadeMs"]);
        }

        [Fact]
        public async Task AdsBedDuckDbIsAcceptedByThePutSettingsEndpointAndRefusedOutsideMinusSixtyToZero()
        {
            // gh-#746 — the duck moved off the env-only Ads:* section so an operator can tune it by
            // ear on a running station; boundaries pass, one past each refuses with nothing persisted.
            var (controller, store) = NewController();

            foreach (var accepted in new[] { "-60", "0", "-18.5" })
            {
                var okResult = await controller.Put(
                    new List<SettingUpdateRequest> { new("Station:Ads:BedDuckDb", accepted) },
                    CancellationToken.None);
                Assert.IsType<OkObjectResult>(okResult);
            }
            var overrides = await store.ReadAllAsync(CancellationToken.None);
            Assert.Equal("-18.5", overrides["Station:Ads:BedDuckDb"]);

            var writesBefore = store.WriteCallCount;
            foreach (var outOfRange in new[] { "-60.5", "0.5", "loud" })
            {
                var result = await controller.Put(
                    new List<SettingUpdateRequest> { new("Station:Ads:BedDuckDb", outOfRange) },
                    CancellationToken.None);
                Assert.IsNotType<OkObjectResult>(result);
            }
            Assert.Equal(writesBefore, store.WriteCallCount);
        }

        [Fact]
        public async Task AdsBedFadeMsIsValidatedInTheHundredToOneThousandRange()
        {
            var (controller, store) = NewController();

            // Boundaries pass.
            foreach (var boundary in new[] { "100", "1000" })
            {
                var okResult = await controller.Put(
                    new List<SettingUpdateRequest> { new("Station:Ads:BedFadeMs", boundary) },
                    CancellationToken.None);
                Assert.IsType<OkObjectResult>(okResult);
            }

            // One past each boundary refuses, nothing persisted.
            foreach (var outOfRange in new[] { "99", "1001" })
            {
                var writesBefore = store.WriteCallCount;
                var badResult = await controller.Put(
                    new List<SettingUpdateRequest> { new("Station:Ads:BedFadeMs", outOfRange) },
                    CancellationToken.None);

                Assert.IsType<BadRequestObjectResult>(badResult);
                Assert.Equal(writesBefore, store.WriteCallCount);
            }
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — env-only keys refuse via allowlist
    // ---------------------------------------------------------------------

    public sealed class ScenarioEnvOnlyKeysRefuseViaAllowlist
    {
        static async Task AssertRefusedByAllowlist(string key)
        {
            var (controller, store) = NewController();

            var updates = new List<SettingUpdateRequest> { new(key, "anything") };

            var result = await controller.Put(updates, CancellationToken.None);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
            Assert.Contains(
                problem.Errors.Values.SelectMany(m => m),
                message => message.Contains("is not an operator-editable setting", StringComparison.Ordinal));
            Assert.Equal(0, store.WriteCallCount);
        }

        [Fact]
        public Task PacksJingleRootIsRefusedByThePutSettingsEndpoint() =>
            AssertRefusedByAllowlist("Packs:JingleRoot");

        [Fact]
        public Task PacksVoicesRootIsRefusedByThePutSettingsEndpoint() =>
            AssertRefusedByAllowlist("Packs:VoicesRoot");

        [Fact]
        public Task PacksPreviewMaxBytesIsRefusedByThePutSettingsEndpoint() =>
            AssertRefusedByAllowlist("Packs:PreviewMaxBytes");

        [Fact]
        public Task PacksJingleAssetMaxBytesIsRefusedByThePutSettingsEndpoint() =>
            AssertRefusedByAllowlist("Packs:JingleAssetMaxBytes");
    }
}

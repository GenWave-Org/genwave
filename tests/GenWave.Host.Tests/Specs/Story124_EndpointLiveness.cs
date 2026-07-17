// STORY-124 — TTS/LLM endpoints are live-editable, location-agnostic URLs (WIRE)
//
// BDD specification — xUnit. The Kokoro client + voice listers drop boot-frozen
// IOptions/BaseAddress for IOptionsMonitor + absolute URIs per call; the four keys join
// the F19 live allowlist; Llm:ApiKey never crosses the settings surface (F19.3).
// Zero compose changes ship (F36.3, operator ruling 2026-07-13).
//
// Same in-process SettingsController pattern as Story100/Story120 — no live stack or DB
// required. The live-repoint behavior (a PUT mid-render actually reaching a new Kokoro/LLM
// endpoint, and the voices-cache invalidating on repoint) is exercised against the REAL
// KokoroTtsSynthesizer/KokoroVoiceLister/LlmCopyWriter in GenWave.Tts.Tests
// (Story124_EndpointLiveRepoint.cs) — this file owns only the allowlist surface and the
// Llm:ApiKey secrecy contract.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Host.Api;
using GenWave.Host.Configuration;

namespace GenWave.Host.Tests.Specs;

public static class FeatureEndpointLiveness
{
    // ── In-memory fakes (mirrors Story100's/Story120's FakeSettingsStore) ─────────

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

    // Effective config includes Llm:ApiKey (env-only, F19.3) so the secrecy facts below prove
    // it is withheld even though a real value is present in the underlying IConfiguration.
    static IEnumerable<KeyValuePair<string, string?>> AllDefaults() =>
    [
        new("Tts:Endpoint",           "http://kokoro:8880"),
        new("Llm:Endpoint",           "http://llm-gateway:9000"),
        new("Llm:Model",              "test-model"),
        new("Llm:TimeoutSeconds",     "10"),
        new("Llm:ApiKey",             "super-secret-token"),
    ];

    // ---------------------------------------------------------------------
    // HAPPY PATH — the four keys join the F19 live allowlist
    // ---------------------------------------------------------------------

    public sealed class ScenarioAllowlistCarriesTheKeys
    {
        [Fact]
        public async Task TtsEndpointAppearsWithLiveApplyMode()
        {
            // GET /api/settings (F36.2, AC1).
            var config = BuildConfig(AllDefaults());
            var controller = BuildController(config, new FakeSettingsStore());

            var result = await controller.Get(CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value).ToList();
            var entry = items.Single(i =>
                string.Equals(i.Key, "Tts:Endpoint", StringComparison.OrdinalIgnoreCase));

            Assert.True(
                entry.ApplyMode == "live" && entry.Kind == "string" && entry.Value == "http://kokoro:8880");
        }

        [Fact]
        public async Task LlmEndpointModelAndTimeoutAppearWithLiveApplyMode()
        {
            // Llm:Endpoint, Llm:Model, Llm:TimeoutSeconds (F36.2, AC1).
            var config = BuildConfig(AllDefaults());
            var controller = BuildController(config, new FakeSettingsStore());

            var result = await controller.Get(CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value).ToList();

            var endpoint = items.Single(i => string.Equals(i.Key, "Llm:Endpoint", StringComparison.OrdinalIgnoreCase));
            var model    = items.Single(i => string.Equals(i.Key, "Llm:Model",    StringComparison.OrdinalIgnoreCase));
            var timeout  = items.Single(i => string.Equals(i.Key, "Llm:TimeoutSeconds", StringComparison.OrdinalIgnoreCase));

            Assert.True(
                endpoint.ApplyMode == "live" && endpoint.Kind == "string" && endpoint.Value == "http://llm-gateway:9000" &&
                model.ApplyMode == "live" && model.Kind == "string" && model.Value == "test-model" &&
                timeout.ApplyMode == "live" && timeout.Kind == "number" && timeout.Value == "10");
        }
    }

    public sealed class ScenarioLivePutRoundTrips
    {
        [Fact]
        public async Task PutPersistsTtsEndpointToTheOverlay()
        {
            var config = BuildConfig(AllDefaults());
            var store = new FakeSettingsStore();
            var controller = BuildController(config, store);

            var putResult = await controller.Put(
                [new SettingUpdateRequest("Tts:Endpoint", "http://kokoro-2:8880")], CancellationToken.None);

            Assert.IsType<OkObjectResult>(putResult);
            Assert.Equal(1, store.WriteCallCount);
        }

        [Fact]
        public async Task PutRejectsANonAbsoluteTtsEndpoint()
        {
            // Tts:Endpoint has no "disabled" state (unlike Llm:Endpoint) — non-empty and absolute
            // http/https is required (F36.1).
            var config = BuildConfig(AllDefaults());
            var store = new FakeSettingsStore();
            var controller = BuildController(config, store);

            var result = await controller.Put(
                [new SettingUpdateRequest("Tts:Endpoint", "not-a-url")], CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(0, store.WriteCallCount);
        }

        [Fact]
        public async Task PutAcceptsAnEmptyLlmEndpointAsTheDisabledState()
        {
            // Empty Llm:Endpoint legally disables LLM-authored copy (F34.2) — unlike Tts:Endpoint.
            var config = BuildConfig(AllDefaults());
            var store = new FakeSettingsStore();
            var controller = BuildController(config, store);

            var result = await controller.Put(
                [new SettingUpdateRequest("Llm:Endpoint", "")], CancellationToken.None);

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(1, store.WriteCallCount);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the secret stays env-only
    // ---------------------------------------------------------------------

    public sealed class ScenarioApiKeyNeverCrossesTheSettingsSurface
    {
        [Fact]
        public async Task LlmApiKeyNeverAppearsInSettingsResponses()
        {
            // (F19.3, F34.3, AC5). Llm:ApiKey carries a real value in the underlying
            // IConfiguration (AllDefaults) — proving the omission is deliberate, not incidental.
            var config = BuildConfig(AllDefaults());
            var controller = BuildController(config, new FakeSettingsStore());

            var result = await controller.Get(CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value).ToList();

            Assert.DoesNotContain(items, i => string.Equals(i.Key, "Llm:ApiKey", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task PutNamingLlmApiKeyIsRejected()
        {
            // 400 — secrets are env-only (F19.3, AC5). Llm:ApiKey is absent from
            // StationSettingsAllowlist, so it fails the same "not an operator-editable setting"
            // check every unlisted key does — nothing is persisted.
            var config = BuildConfig(AllDefaults());
            var store = new FakeSettingsStore();
            var controller = BuildController(config, store);

            var result = await controller.Put(
                [new SettingUpdateRequest("Llm:ApiKey", "attempted-override")], CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(0, store.WriteCallCount);
        }
    }
}

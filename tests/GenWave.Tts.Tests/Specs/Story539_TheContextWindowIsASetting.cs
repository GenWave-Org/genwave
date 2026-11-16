// STORY-539 — Llm:ContextTokens is a live station setting (PLAN T719 · SPEC F218.21)
//
// Entry point: the settings-bound IOptionsMonitor<LlmOptions> the health cycle's LlmContextWindow reads.

namespace GenWave.Tts.Tests.Specs;

using GenWave.Tts.Tests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

public static class FeatureTheContextWindowIsASetting
{
    const string Endpoint = "http://fake.test:11434";

    static ConfigurationRoot Settings(string? contextTokens) =>
        (ConfigurationRoot)new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:Endpoint"] = Endpoint,
                ["Llm:Model"] = "m",
                ["Llm:ContextTokens"] = contextTokens,
            })
            .Build();

    static ServiceProvider Services(IConfiguration settings) =>
        new ServiceCollection()
            .AddOptions<LlmOptions>()
            .Bind(settings.GetSection(LlmOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart()
            .Services
            .BuildServiceProvider();

    static LlmContextWindow WindowOver(IOptionsMonitor<LlmOptions> options) =>
        new(new OllamaContextClient(new SingleHandlerHttpClientFactory(new CannedChatHandler(_ => "", processListBody: null))),
            options, NullLogger<LlmContextWindow>.Instance);

    public sealed class ScenarioABlankSettingIsUnset : IDisposable
    {
        readonly ServiceProvider services = Services(Settings(""));

        [Fact]
        public void TheOptionsBindToNull() =>
            Assert.Null(services.GetRequiredService<IOptionsMonitor<LlmOptions>>().CurrentValue.ContextTokens);

        public void Dispose() => services.Dispose();
    }

    public sealed class ScenarioAPutReachesTheNextHealthCycle : IAsyncLifetime
    {
        public ScenarioAPutReachesTheNextHealthCycle()
        {
            settings = Settings(null);
            services = Services(settings);
        }

        readonly ConfigurationRoot settings;
        readonly ServiceProvider services;
        int? beforeTheSetting;
        int? afterTheSetting;
        int? afterTheSettingIsCleared;

        public async Task InitializeAsync()
        {
            var window = WindowOver(services.GetRequiredService<IOptionsMonitor<LlmOptions>>());

            await window.RefreshAsync(CancellationToken.None);
            beforeTheSetting = window.Tokens;

            settings["Llm:ContextTokens"] = "16384";
            settings.Reload();
            await window.RefreshAsync(CancellationToken.None);
            afterTheSetting = window.Tokens;

            settings["Llm:ContextTokens"] = "";
            settings.Reload();
            await window.RefreshAsync(CancellationToken.None);
            afterTheSettingIsCleared = window.Tokens;
        }

        public Task DisposeAsync()
        {
            services.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public void BeforeThePutTheWindowIsUnknown() => Assert.Null(beforeTheSetting);

        [Fact]
        public void TheNextRefreshReadsTheNewValueWithNoOllama() => Assert.Equal(16384, afterTheSetting);

        [Fact]
        public void ClearingItReturnsTheWindowToUnknown() => Assert.Null(afterTheSettingIsCleared);
    }
}

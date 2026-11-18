// STORY-539 — Llm:ContextTokens is a live station setting (PLAN T719 · SPEC F218.21)
//
// Entry point: SettingsController.Put, the same door the admin Settings page uses.

namespace GenWave.Host.Tests.Specs;

using GenWave.Host.Api;
using GenWave.Host.Configuration;
using GenWave.Host.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

public static class FeatureContextTokensIsASetting
{
    const string Key = "Llm:ContextTokens";

    sealed class MemoryStore : IStationSettingsStore
    {
        public Dictionary<string, string> Written { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task WriteAsync(string key, object value, CancellationToken cancellationToken = default)
        {
            Written[key] = value.ToString() ?? string.Empty;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(Written, StringComparer.OrdinalIgnoreCase));
    }

    static async Task<(IActionResult Result, MemoryStore Store)> PutAsync(string value)
    {
        var config = new ConfigurationBuilder().Build();
        var store = new MemoryStore();
        var controller = new SettingsController(
            config, store, new SettingValidator(config, TestVoiceEngines.KokoroAndPiper()), NullLogger<SettingsController>.Instance,
            TestSettingCopy.Real(), TestSettingChoiceResolver.Default())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        var result = await controller.Put([new SettingUpdateRequest(Key, value)], CancellationToken.None);
        return (result, store);
    }

    public sealed class ScenarioAValidWindowIsPersisted : IAsyncLifetime
    {
        MemoryStore store = new();

        public async Task InitializeAsync() => (_, store) = await PutAsync("16384");

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheValueIsStored() => Assert.Equal("16384", store.Written[Key]);
    }

    public sealed class ScenarioABlankWindowIsAcceptedAsUnset : IAsyncLifetime
    {
        MemoryStore store = new();
        IActionResult result = new OkResult();

        public async Task InitializeAsync() => (result, store) = await PutAsync("");

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItIsNotRejected() => Assert.IsNotType<BadRequestObjectResult>(result);

        [Fact]
        public void TheBlankIsStored() => Assert.Equal("", store.Written[Key]);
    }

    public sealed class ScenarioZeroIsRejected : IAsyncLifetime
    {
        IActionResult result = new OkResult();
        MemoryStore store = new();

        public async Task InitializeAsync() => (result, store) = await PutAsync("0");

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItIs400() => Assert.IsType<BadRequestObjectResult>(result);

        [Fact]
        public void NothingIsStored() => Assert.Empty(store.Written);
    }

    public sealed class ScenarioANegativeIsRejected : IAsyncLifetime
    {
        IActionResult result = new OkResult();

        public async Task InitializeAsync() => (result, _) = await PutAsync("-5");

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItIs400() => Assert.IsType<BadRequestObjectResult>(result);
    }

    public sealed class ScenarioANonIntegerIsRejected : IAsyncLifetime
    {
        IActionResult result = new OkResult();

        public async Task InitializeAsync() => (result, _) = await PutAsync("lots");

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItIs400() => Assert.IsType<BadRequestObjectResult>(result);
    }
}

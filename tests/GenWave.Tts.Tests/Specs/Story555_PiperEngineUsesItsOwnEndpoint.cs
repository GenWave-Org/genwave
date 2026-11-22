// STORY-555 — A piper-profile engine speaks to its own registry endpoint (SPEC F219.1, F219.3 · PLAN T749)
//
// The composed Tts services, a real Piper stub, and a Kokoro-style stub that must stay untouched.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GenWave.Tts.Tests.Specs;

public static class FeaturePiperEngineUsesItsOwnEndpoint
{
    static ServiceProvider Compose(Dictionary<string, string?> config, string cacheRoot)
    {
        config["Tts:Format"] = "wav";
        config["Tts:CacheRoot"] = cacheRoot;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(config).Build();
        return new ServiceCollection().AddLogging().AddGenWaveTts(configuration).BuildServiceProvider();
    }

    // Tts:Engines:piper is set, Tts:PiperPrimaryEndpoint is not
    public sealed class ScenarioAPiperEngineWithNoLegacyEndpoint : IAsyncLifetime
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        PiperStubServer? piper;

        public async Task InitializeAsync()
        {
            piper = await PiperStubServer.StartAsync();
            using var provider = Compose(new()
            {
                ["Tts:Endpoint"] = "http://kokoro:8880",
                ["Tts:Engine"] = "piper",
                ["Tts:Engines:piper:Endpoint"] = piper.BaseUri.ToString(),
                ["Tts:Engines:piper:Profile"] = "piper",
            }, cacheRoot);

            await provider.GetRequiredService<FallbackTtsSynthesizer>()
                .SynthesizeAsync("Coming up next", "af_heart", CancellationToken.None);
        }

        public async Task DisposeAsync()
        {
            if (piper is not null) await piper.DisposeAsync();
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
        }

        [Fact]
        public void TheRenderReachesThePiperEnginesEndpoint() => Assert.Equal(1, piper?.CallCount);
    }

    // Tts:PiperPrimaryEndpoint points elsewhere; the engine named piper owns its own endpoint
    public sealed class ScenarioAPiperEngineWhoseEndpointDiffersFromTheLegacyKey : IAsyncLifetime
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        PiperStubServer? engineStub;
        PiperStubServer? legacyStub;

        public async Task InitializeAsync()
        {
            engineStub = await PiperStubServer.StartAsync();
            legacyStub = await PiperStubServer.StartAsync();
            using var provider = Compose(new()
            {
                ["Tts:Endpoint"] = "http://kokoro:8880",
                ["Tts:Engine"] = "piper",
                ["Tts:PiperPrimaryEndpoint"] = legacyStub.BaseUri.ToString(),
                ["Tts:Engines:piper:Endpoint"] = engineStub.BaseUri.ToString(),
                ["Tts:Engines:piper:Profile"] = "piper",
            }, cacheRoot);

            await provider.GetRequiredService<FallbackTtsSynthesizer>()
                .SynthesizeAsync("Coming up next", "af_heart", CancellationToken.None);
        }

        public async Task DisposeAsync()
        {
            if (engineStub is not null) await engineStub.DisposeAsync();
            if (legacyStub is not null) await legacyStub.DisposeAsync();
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
        }

        [Fact]
        public void TheEnginesOwnEndpointGetsTheRequest() => Assert.Equal(1, engineStub?.CallCount);

        [Fact]
        public void TheLegacyEndpointGetsNothing() => Assert.Equal(0, legacyStub?.CallCount);
    }

    // The stored engine is gone from config: the render fails (nothing to fall back to), no crash
    public sealed class ScenarioAStoredEngineMissingFromConfig
    {
        [Fact]
        public async Task TheRenderFailsAsAnOrdinaryPrimaryFailure()
        {
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            using var provider = Compose(new()
            {
                ["Tts:Endpoint"] = "http://kokoro:8880",
                ["Tts:Engine"] = "chatterbox",
            }, cacheRoot);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                provider.GetRequiredService<FallbackTtsSynthesizer>()
                    .SynthesizeAsync("Coming up next", "af_heart", CancellationToken.None));

            Assert.Contains("chatterbox", ex.Message, StringComparison.Ordinal);
        }
    }
}

// STORY-560 — Engine down means music (SPEC F219 · PLAN F219)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Host.Tests.Specs;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Tts;

/// <summary>
/// Stands in for every engine's HTTP endpoint, by host. The chatterbox endpoint either answers audio or refuses the
/// connection; every other host (kokoro on localhost:8880 in Development config, a piper or kokoro fallback hop) answers
/// audio and is counted. Installed under every typed client, so a fallback hop's own client cannot reach a real port.
/// </summary>
file sealed class EnginesByHostHandler : HttpMessageHandler
{
    int otherEngineRequests;

    public bool ChatterboxDown { get; init; }

    /// <summary>Requests that reached any engine other than chatterbox.</summary>
    public int OtherEngineRequests => Volatile.Read(ref otherEngineRequests);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.Host == "chatterbox")
        {
            return ChatterboxDown
                ? throw new HttpRequestException("Connection refused (chatterbox:8004)")
                : Task.FromResult(Audio());
        }

        Interlocked.Increment(ref otherEngineRequests);
        return Task.FromResult(Audio());
    }

    static HttpResponseMessage Audio() => new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) };
}

/// <summary>The real host with chatterbox as the active engine; only the engines' HTTP transport and the Postgres edges are faked.</summary>
file sealed class ActiveEngineWebFactory(EnginesByHostHandler engines, IReadOnlyDictionary<string, string?>? extraSettings = null)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-t766";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Llm:Endpoint", "http://llm.invalid");
        builder.UseSetting("Tts:Engine", "chatterbox");
        builder.UseSetting("Tts:Engines:chatterbox:Endpoint", "http://chatterbox:8004");
        builder.UseSetting("Tts:Engines:chatterbox:Profile", "chatterbox");
        builder.UseSetting("Tts:CacheRoot", Path.Combine(Path.GetTempPath(), "genwave-t766-" + Guid.NewGuid().ToString("N")));
        builder.UseSetting("Station:Voice", "{\"kokoro\":\"af_heart\",\"chatterbox\":\"reader-1\"}");

        foreach (var (key, value) in extraSettings ?? new Dictionary<string, string?>())
            builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            // No background tick: the spec drives the warm-up cycle itself.
            services.RemoveAll<IHostedService>();
            // Every typed client (the speech client AND the fallback hops' own renderers) goes through the counter.
            services.ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() => engines));

            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IMediaRotationSink>();
            services.AddSingleton<IMediaRotationSink>(new FakeMediaRotationSink());
            services.RemoveAll<IRotFindingStore>();
            services.AddSingleton<IRotFindingStore>(new FakeRotFindingStore());
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
        });
    }
}

/// <summary>Boots the host with chatterbox active, runs one warm-up cycle, and reads GET /api/status.</summary>
file static class ActiveEngineStatus
{
    static readonly DependencyProbeCadence Cadence = new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10), UnhealthyThreshold: 2);

    public static async Task<JsonElement> AfterWarmUpAsync(WebApplicationFactory<Program> factory, bool runWarmUp = true)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = ActiveEngineWebFactory.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        if (runWarmUp)
        {
            await factory.Services.GetRequiredService<VoiceEngineWarmUp>()
                .RunCycleAsync(Cadence, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(10));
        }

        var response = await client.GetAsync("/api/status").WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("voice");
    }
}

public static class FeatureEngineDownMeansMusic
{
    // AC1
    public sealed class ScenarioWarmUpThenGreen : IAsyncLifetime
    {
        readonly WebApplicationFactory<Program> factory = new ActiveEngineWebFactory(new EnginesByHostHandler());
        JsonElement voice;

        public async Task InitializeAsync() => voice = await ActiveEngineStatus.AfterWarmUpAsync(factory);

        public async Task DisposeAsync() => await factory.DisposeAsync();

        [Fact]
        public void TheTileIsGreen() =>
            Assert.False(voice.GetProperty("degraded").GetBoolean());

        [Fact]
        public void TheTileNamesTheEngine() =>
            Assert.Equal("chatterbox", voice.GetProperty("engine").GetString());
    }

    // AC2, AC3 — a render past the engine's budget
    public sealed class ScenarioLateTalkIsLogged
    {
        [Fact(Skip = "pending: T767")]
        public void ATalkDroppedLateRowIsWritten() { }

        [Fact(Skip = "pending: T767")]
        public void TheStatusResponseCarriesTheSevenDayCount() { }

        [Fact(Skip = "pending: T767")]
        public void ARowEightDaysOldIsNotCounted() { }
    }

    // Before the warm-up line has rendered: not green, and not "down" either
    public sealed class ScenarioBeforeTheWarmUpLine : IAsyncLifetime
    {
        readonly WebApplicationFactory<Program> factory = new ActiveEngineWebFactory(new EnginesByHostHandler());
        JsonElement voice;

        public async Task InitializeAsync() => voice = await ActiveEngineStatus.AfterWarmUpAsync(factory, runWarmUp: false);

        public async Task DisposeAsync() => await factory.DisposeAsync();

        [Fact]
        public void TheTileIsWarming() =>
            Assert.True(voice.GetProperty("warming").GetBoolean());

        [Fact]
        public void TheTileIsNotGreen() =>
            Assert.True(voice.GetProperty("degraded").GetBoolean());
    }

    // Sad path
    // AC4, AC5 — connections refused, empty fallback chain
    public sealed class ScenarioTheEngineIsDown : IAsyncLifetime
    {
        readonly Func<int> otherEngineRequests;
        readonly WebApplicationFactory<Program> factory;
        JsonElement voice;
        Exception? renderFailure;

        public ScenarioTheEngineIsDown()
        {
            var engines = new EnginesByHostHandler { ChatterboxDown = true };
            otherEngineRequests = () => engines.OtherEngineRequests;
            factory = new ActiveEngineWebFactory(engines, FallbackSettings);
        }

        /// <summary>The shipped default: an empty fallback chain. Setting <c>["Tts:Fallback:0"] = "kokoro"</c> here turns <c>NoOtherEngineIsCalled</c> red (checked by hand in T766).</summary>
        static IReadOnlyDictionary<string, string?> FallbackSettings => new Dictionary<string, string?>();

        public async Task InitializeAsync()
        {
            voice = await ActiveEngineStatus.AfterWarmUpAsync(factory);

            // A real render through the production chain (normalizer, fallback decorator, active engine, speech client):
            // with Tts:Fallback empty it must fail, not borrow another engine.
            renderFailure = await Record.ExceptionAsync(() => factory.Services.GetRequiredService<ITtsSynthesizer>()
                .SynthesizeAsync("Back to the music.", "reader-1", CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(10)));
        }

        public async Task DisposeAsync() => await factory.DisposeAsync();

        [Fact]
        public void TheTileIsRed() =>
            Assert.True(voice.GetProperty("degraded").GetBoolean());

        [Fact]
        public void TheTileGivesTheReason() =>
            Assert.Equal("Connection refused (chatterbox:8004)", voice.GetProperty("reason").GetString());

        [Fact]
        public void TheTileIsNotWarming() =>
            Assert.False(voice.GetProperty("warming").GetBoolean());

        [Fact]
        public void TheRenderFails() =>
            Assert.IsType<HttpRequestException>(renderFailure);

        [Fact]
        public void NoOtherEngineIsCalled() =>
            Assert.Equal(0, otherEngineRequests());
    }
}

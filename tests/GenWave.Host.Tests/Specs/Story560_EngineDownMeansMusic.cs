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
using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Tests.Fakes;
using GenWave.Orchestration;
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
file sealed class ActiveEngineWebFactory(
    EnginesByHostHandler engines,
    IReadOnlyDictionary<string, string?>? extraSettings = null,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<Program>
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
            configureServices?.Invoke(services);
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
        static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

        // A hand-built one-slot break whose render the budget outran, pushed through the host's own BreakDelivery
        // and event sink; the row is read off the booth log's queue, the seam the drain service persists from.
        [Fact]
        public async Task ATalkDroppedLateRowIsWritten()
        {
            await using var factory = new ActiveEngineWebFactory(new EnginesByHostHandler());
            var identity = new StationIdentity("station-1", "GenWave", "reader-1");
            var request = new SegmentRequest(SegmentKind.LeadIn, identity.Voice, identity.Name, null, Now, identity.Id);
            var slot = new PlannedSlot(1, SegmentKind.LeadIn, new RenderSource(request), null, new SilentDrop(), ObserveDuration: true);
            var context = new BreakContext(1, null, null, null, new CadenceConfig(), identity, Now, null, new HashSet<SpeechDeferralKind>(), TimeSpan.Zero);
            var plan = new BreakPlan(1, context, TimeSpan.FromSeconds(5), [slot]);

            factory.Services.GetRequiredService<BreakDelivery>().Deliver(plan, [new TimedOutOutcome()]);

            var row = await factory.Services.GetRequiredService<ChannelReader<BoothLogAppendRequest>>()
                .ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("talk-dropped-late", row.Kind);
        }

        [Fact]
        public async Task TheStatusResponseCarriesTheSevenDayCount() =>
            Assert.Equal(2, await SevenDayCountAsync(Now.AddDays(-1), Now.AddHours(-2)));

        [Fact]
        public async Task ARowEightDaysOldIsNotCounted() =>
            Assert.Equal(0, await SevenDayCountAsync(Now.AddDays(-8)));

        // GET /api/status through the real host, the clock held by a FakeTimeProvider and the booth rows by an in-memory reader.
        static async Task<int> SevenDayCountAsync(params DateTimeOffset[] rowTimes)
        {
            await using var factory = new ActiveEngineWebFactory(
                new EnginesByHostHandler(),
                configureServices: services =>
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
                    services.RemoveAll<ITalkDroppedLateReader>();
                    services.AddSingleton<ITalkDroppedLateReader>(new FakeTalkDroppedLateReader(rowTimes));
                });

            var client = factory.CreateClient();
            var login = await client.PostAsJsonAsync("/api/auth/login", new { password = ActiveEngineWebFactory.Password })
                .WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

            var body = await client.GetFromJsonAsync<JsonElement>("/api/status").WaitAsync(TimeSpan.FromSeconds(10));
            return body.GetProperty("talkDroppedLate7d").GetInt32();
        }
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

    // T768a — the speech client and the warm-up must share ONE render-activity tracker, or the tile never hears about renders.
    public sealed class ScenarioTheCompositionRoot : IAsyncLifetime
    {
        const string Identity = "chatterbox|http://chatterbox:8004";
        static readonly DependencyProbeCadence Cadence = new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10), UnhealthyThreshold: 2);

        readonly WebApplicationFactory<Program> factory = new ActiveEngineWebFactory(new EnginesByHostHandler());

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync() => await factory.DisposeAsync();

        [Fact]
        public async Task TheSpeechClientFeedsTheSharedActivity()
        {
            var engine = factory.Services.GetRequiredService<IActiveVoiceEngine>().Current
                ?? throw new InvalidOperationException("chatterbox is not registered");

            await factory.Services.GetRequiredService<SpeechEngineClient>()
                .RenderOnAsync(engine, new TtsRenderContext("Back to the music.", "reader-1", Kind: null), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(10));

            Assert.NotNull(factory.Services.GetRequiredService<EngineRenderActivity>().Read(Identity).LastSucceededAt);
        }

        [Fact]
        public async Task TheWarmUpReadsTheSharedActivity()
        {
            var warmUp = factory.Services.GetRequiredService<VoiceEngineWarmUp>();
            await warmUp.RunCycleAsync(Cadence, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));   // warmed: green

            var activity = factory.Services.GetRequiredService<EngineRenderActivity>();
            activity.Failed(Identity, "Connection refused (chatterbox:8004)");
            activity.Failed(Identity, "Connection refused (chatterbox:8004)");
            await warmUp.RunCycleAsync(Cadence, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

            var verdict = factory.Services.GetRequiredService<DependencyHealthStore>().GetVerdict(DependencyNames.VoiceEngine("chatterbox"));
            Assert.False(verdict?.Healthy);
        }
    }
}

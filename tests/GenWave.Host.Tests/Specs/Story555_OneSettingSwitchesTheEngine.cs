// STORY-555 — One setting switches the engine (SPEC F219 · PLAN F219)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Host.Tests.Specs;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Host.Configuration;
using GenWave.Tts;

// In-process settings store that drives a real live reload of the app's IConfiguration, so a PUT
// is visible to the next request (the F19 contract) with no Postgres behind it.
file sealed class EngineSettingProvider : ConfigurationProvider
{
    public void SetAndReload(string key, string value)
    {
        Set(key, value);
        OnReload();
    }
}

file sealed class EngineSettingSource(EngineSettingProvider provider) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
}

file sealed class EngineSettingsStore : IStationSettingsStore
{
    readonly EngineSettingProvider provider = new();

    public EngineSettingsStore(IConfiguration configuration)
    {
        ((IConfigurationBuilder)configuration).Add(new EngineSettingSource(provider));
    }

    public Task WriteAsync(string key, object value, CancellationToken cancellationToken = default)
    {
        provider.SetAndReload(key, value?.ToString() ?? string.Empty);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}

/// <summary>Stands in for every engine's endpoint: answers any speech request with a few audio bytes and remembers where it was sent.</summary>
public sealed class EngineEndpointRecorder : HttpMessageHandler
{
    readonly List<Uri> requested = [];
    readonly List<string> bodies = [];

    public IReadOnlyList<Uri> Requested { get { lock (requested) return [.. requested]; } }

    /// <summary>The JSON body of each request, in the same order as <see cref="Requested"/>.</summary>
    public IReadOnlyList<string> Bodies { get { lock (requested) return [.. bodies]; } }

    /// <summary>When set, every request to an engine fails as if the engine were down.</summary>
    public bool Unreachable { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (requested)
        {
            requested.Add(request.RequestUri ?? throw new InvalidOperationException("request without a uri"));
            bodies.Add(body);
        }

        if (Unreachable)
            throw new HttpRequestException("engine is down");

        // A voices listing answers the engines' { "voices": [...] } shape; every other request gets audio bytes.
        return request.RequestUri?.AbsolutePath.EndsWith("/v1/audio/voices", StringComparison.Ordinal) == true
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { voices = new[] { "reader-1", "reader-2" } }) }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) };
    }
}

/// <summary>
/// The real host with chatterbox optionally added to the engine registry (kokoro is seeded). No
/// <c>Tts:PiperPrimaryEndpoint</c> is set either way — a Kokoro + fallback install, where the
/// registry holds no piper.
/// </summary>
file sealed class EngineSettingFactory(bool withChatterbox, EngineEndpointRecorder recorder, Action<IServiceCollection> configure) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-engine";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        if (withChatterbox)
        {
            builder.UseSetting("Tts:Engines:chatterbox:Endpoint", "http://chatterbox:8004");
            builder.UseSetting("Tts:Engines:chatterbox:Profile", "chatterbox");
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            // Transport swap on the one speech client only; the render chain above it stays real DI.
            services.AddHttpClient<SpeechEngineClient>().ConfigurePrimaryHttpMessageHandler(() => recorder);
            services.RemoveAll<IStationSettingsStore>();
            services.AddSingleton<IStationSettingsStore>(sp =>
                new EngineSettingsStore(sp.GetRequiredService<IConfiguration>()));
            configure(services);
        });
    }
}

/// <summary>Boots the host once per fact and logs in; each scenario inherits it.</summary>
public abstract class EngineSettingScenario : IAsyncLifetime
{
    readonly WebApplicationFactory<Program> factory;
    readonly EngineEndpointRecorder recorder = new();

    protected EngineSettingScenario(bool withChatterbox = true)
    {
        factory = new EngineSettingFactory(withChatterbox, recorder, ConfigureServices);
        Client = factory.CreateClient();
    }

    protected HttpClient Client { get; }

    /// <summary>A scenario swaps real seams here (a card source, an event sink); the render chain stays real DI.</summary>
    protected virtual void ConfigureServices(IServiceCollection services) { }

    /// <summary>A service from the booted host's real DI container.</summary>
    protected T Service<T>() where T : notnull => factory.Services.GetRequiredService<T>();

    /// <summary>Takes every engine down (or brings them back).</summary>
    protected bool EnginesUnreachable { set => recorder.Unreachable = value; }

    /// <summary>Every speech request the engines received, in order.</summary>
    protected IReadOnlyList<Uri> EngineRequests => recorder.Requested;

    /// <summary>The body of every speech request the engines received, in order.</summary>
    protected IReadOnlyList<string> EngineRequestBodies => recorder.Bodies;

    public virtual async Task InitializeAsync()
    {
        var login = await Client.PostAsJsonAsync("/api/auth/login", new { password = EngineSettingFactory.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

    public virtual async Task DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();
    }

    protected Task<HttpResponseMessage> PutEngineAsync(string engine) =>
        Client.PutAsJsonAsync("/api/settings", new[] { new { key = "Tts:Engine", value = engine } });
}

file sealed record EngineSettingRow(string Key, string Value);

file sealed record EngineSummaryRow(string Name, string Profile);

file sealed record EnginesBody(string Active, List<EngineSummaryRow> Engines);

public static class FeatureOneSettingSwitchesTheEngine
{
    // AC1, AC3 — chatterbox in the registry; through the settings endpoint
    public sealed class ScenarioSavingTheSetting : EngineSettingScenario
    {
        [Fact]
        public async Task SettingChatterboxReturns200()
        {
            var response = await PutEngineAsync("chatterbox");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task AFreshInstallReadsKokoro()
        {
            var rows = await Client.GetFromJsonAsync<List<EngineSettingRow>>("/api/settings");

            Assert.NotNull(rows);
            Assert.Equal("kokoro", Assert.Single(rows, r => r.Key == "Tts:Engine").Value);
        }

        [Fact]
        public async Task TheEnginesEndpointListsTheRegistry()
        {
            var body = await Client.GetFromJsonAsync<EnginesBody>("/api/voices/engines");

            Assert.NotNull(body);
            Assert.Equal(["chatterbox", "kokoro"], body.Engines.Select(e => e.Name).Order().ToArray());
        }
    }

    // AC2 — the engine changed, then a preview through the test host, no restart
    public sealed class ScenarioTheSwitchIsLive : EngineSettingScenario
    {
        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            var saved = await PutEngineAsync("chatterbox");
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

            var preview = await Client.PostAsJsonAsync("/api/tts/preview", new { text = "Hello there.", voice = "af_heart" });
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        }

        [Fact]
        public void TheNextRenderReachesTheNewEnginesEndpoint() =>
            Assert.Equal("chatterbox", Assert.Single(EngineRequests).Host);

        [Fact]
        public void TheOldEngineGetsNoRequest() =>
            Assert.DoesNotContain(EngineRequests, uri => uri.Host == "kokoro");
    }

    // Sad path
    // AC6
    public sealed class ScenarioAnUnknownEngine : EngineSettingScenario
    {
        [Fact]
        public async Task SettingNopeReturns400()
        {
            var response = await PutEngineAsync("nope");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    // F70.3 — the per-kind map still names wire kinds, not registry entries
    public sealed class ScenarioPiperInAnEngineByKindMapOnAKokoroOnlyRegistry : EngineSettingScenario
    {
        public ScenarioPiperInAnEngineByKindMapOnAKokoroOnlyRegistry() : base(withChatterbox: false) { }

        [Fact]
        public async Task SettingStationIdToPiperReturns200()
        {
            var response = await Client.PutAsJsonAsync(
                "/api/settings",
                new[] { new { key = "Tts:EngineByKind", value = "{\"StationId\":\"piper\"}" } });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}

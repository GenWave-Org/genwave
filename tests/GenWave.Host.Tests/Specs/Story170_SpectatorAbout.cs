// STORY-170 — Public about panel: station identity, version, license, stream URL
//
// BDD specification — xUnit (SPEC F62.8, F65.3). {stationName, version, license, projectUrl,
// streamUrl}; version comes from AssemblyInformationalVersion; streamUrl from the new
// Station:PublicStreamUrl live setting (empty string when unset — the page hides the player).
// Red until PLAN T12. The live PUT round trip is operator-gated (real Postgres overlay).

using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

file sealed class SpectatorAboutWebFactory(string? streamUrl) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Station:SpectatorMode", "true");
        if (streamUrl is not null)
            builder.UseSetting("Station:PublicStreamUrl", streamUrl);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var prevLib = Environment.GetEnvironmentVariable("ConnectionStrings__Library");
        var prevAdmin = Environment.GetEnvironmentVariable("Admin__Password");
        Environment.SetEnvironmentVariable("ConnectionStrings__Library", "Host=nowhere;Database=test");
        Environment.SetEnvironmentVariable("Admin__Password", "test-password-x7z");
        try
        {
            return base.CreateHost(builder);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__Library", prevLib);
            Environment.SetEnvironmentVariable("Admin__Password", prevAdmin);
        }
    }
}

public static class FeatureSpectatorAbout
{
    const string OperatorGated =
        "Live PUT round trip requires the real Postgres settings overlay — proven in the " +
        "operator acceptance gate (mirrors Story058), not under WebApplicationFactory.";

    static async Task<JsonElement> FetchAboutAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/spectator/api/about");
        Assert.True(response.IsSuccessStatusCode, $"/spectator/api/about returned {(int)response.StatusCode}.");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioAboutShape
    {
        [Fact]
        public async Task StationNameIsPresent()
        {
            await using var factory = new SpectatorAboutWebFactory("https://demo.example/stream");
            var body = await FetchAboutAsync(factory);
            Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("stationName").GetString()));
        }

        [Fact]
        public async Task VersionReportsTheStampedInformationalVersion()
        {
            await using var factory = new SpectatorAboutWebFactory("https://demo.example/stream");
            var body = await FetchAboutAsync(factory);
            Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("version").GetString()));
        }

        [Fact]
        public async Task LicenseIsAgpl()
        {
            await using var factory = new SpectatorAboutWebFactory("https://demo.example/stream");
            var body = await FetchAboutAsync(factory);
            Assert.Equal("AGPL-3.0-or-later", body.GetProperty("license").GetString());
        }

        [Fact]
        public async Task ProjectUrlIsPresent()
        {
            await using var factory = new SpectatorAboutWebFactory("https://demo.example/stream");
            var body = await FetchAboutAsync(factory);
            Assert.StartsWith("https://", body.GetProperty("projectUrl").GetString());
        }

        [Fact]
        public async Task StreamUrlReflectsTheConfiguredValue()
        {
            await using var factory = new SpectatorAboutWebFactory("https://demo.example/stream");
            var body = await FetchAboutAsync(factory);
            Assert.Equal("https://demo.example/stream", body.GetProperty("streamUrl").GetString());
        }

        [Fact(Skip = OperatorGated)]
        public Task StreamUrlUpdatesLiveViaSettingsPut() => Task.CompletedTask;
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioUnsetStreamUrl
    {
        [Fact]
        public async Task StreamUrlIsAnEmptyString()
        {
            await using var factory = new SpectatorAboutWebFactory(streamUrl: null);
            var body = await FetchAboutAsync(factory);
            Assert.Equal("", body.GetProperty("streamUrl").GetString());
        }
    }
}

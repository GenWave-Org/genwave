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
using GenWave.Core;
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
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-x7z");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
        });
    }
}

public static class FeatureSpectatorAbout
{
    const string OperatorGated =
        "manual: Live PUT round trip requires the real Postgres settings overlay — proven in the " +
        "operator acceptance gate (mirrors Story058), not under WebApplicationFactory.";

    static async Task<JsonElement> FetchAboutAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/spectator/api/about");
        Assert.True(response.IsSuccessStatusCode, $"/spectator/api/about returned {(int)response.StatusCode}.");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    // ── HAPPY PATH ────────────────────────────────────────────────────────

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

    /// <summary>SPEC F211.2, PLAN T589 — version matches the Host assembly's own build stamp,
    /// independently re-derived by this fact's own oracle (<see cref="AppVersion.FromAssembly"/>
    /// against <c>typeof(Program).Assembly</c>) rather than read back off the SAME DI singleton
    /// the controller itself resolves — a same-instance comparison there would only prove the
    /// controller passes DI's value through unchanged, never that DI holds the right one. Arranged
    /// once (mirrors Story483_OneVersion.cs's own <c>ScenarioSpectatorAboutShowsTheDisplayForm</c>) —
    /// both facts below read the same fetch.</summary>
    public sealed class ScenarioTheVersionReadsLikeARelease : IAsyncLifetime
    {
        // Given: GET /spectator/api/about, once

        string expected = "";
        string version = "";

        public async Task InitializeAsync()
        {
            await using var factory = new SpectatorAboutWebFactory("https://demo.example/stream");
            expected = AppVersion.FromAssembly(typeof(Program).Assembly).Display;
            var body = await FetchAboutAsync(factory);
            version = body.GetProperty("version").GetString() ?? "";
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void VersionReportsTheHostDisplayVersion() => Assert.Equal(expected, version);

        /// <summary>SPEC F211.2 — the served version reads like a release tag: "v" + a SemVer core.</summary>
        [Fact]
        public void VersionReadsLikeARelease() => Assert.Matches(@"^v[0-9]+\.[0-9]+\.[0-9]+", version);
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

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

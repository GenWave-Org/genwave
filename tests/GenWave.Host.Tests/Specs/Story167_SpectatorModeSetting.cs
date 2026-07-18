// STORY-167 — Station:SpectatorMode setting gates the spectator surface
//
// BDD specification — xUnit (SPEC F62.1, F62.2). Allowlisted live boolean, default false; when
// off, spectator routes return 404 (the surface does not exist — never 401). Red until PLAN
// T05/T10. The true live round trip (PUT via the DB-backed overlay, no restart) needs a real
// Postgres — operator-gated, mirroring Story058's acceptance split.

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

namespace GenWave.Host.Tests.Specs;

file sealed class SpectatorSettingWebFactory() : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-x7z";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
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
        Environment.SetEnvironmentVariable("Admin__Password", Password);
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

public static class FeatureSpectatorModeSetting
{
    const string OperatorGated =
        "Live PUT round trip requires the real Postgres settings overlay — proven in the " +
        "operator acceptance gate (mirrors Story058), not under WebApplicationFactory.";

    static async Task<JsonElement?> FindSettingAsync(WebApplicationFactory<Program> factory, string key)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsync("/api/auth/login",
            JsonContent.Create(new { password = SpectatorSettingWebFactory.Password }));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        var body = JsonDocument.Parse(await client.GetStringAsync("/api/settings")).RootElement;
        foreach (var entry in body.EnumerateArray())
        {
            if (string.Equals(entry.GetProperty("key").GetString(), key, StringComparison.OrdinalIgnoreCase))
                return entry;
        }
        return null;
    }

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioAllowlistedLiveSetting
    {
        [Fact]
        public async Task SpectatorModeAppearsInTheSettingsListing()
        {
            await using var factory = new SpectatorSettingWebFactory();

            var entry = await FindSettingAsync(factory, "Station:SpectatorMode");

            Assert.NotNull(entry);
        }

        [Fact]
        public async Task SpectatorModeApplyModeIsLive()
        {
            await using var factory = new SpectatorSettingWebFactory();

            var entry = await FindSettingAsync(factory, "Station:SpectatorMode");

            // Wire convention is lowercase (F18.1 camelCase contract; matches Story043/058/100/120/124/139).
            Assert.Equal("live", entry!.Value.GetProperty("applyMode").GetString());
        }

        [Fact(Skip = OperatorGated)]
        public Task TogglingOnLiveAppliesWithoutRestart() => Task.CompletedTask;
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioOffMeansTheSurfaceDoesNotExist
    {
        [Theory]
        [InlineData("/spectator/api/now-playing")]
        [InlineData("/spectator/api/play-history")]
        [InlineData("/spectator/api/stats")]
        [InlineData("/spectator/api/about")]
        [InlineData("/spectator")]
        public async Task SpectatorRouteReturns404WhenOff(string route)
        {
            // Development config leaves Station:SpectatorMode at its default: false.
            await using var factory = new SpectatorSettingWebFactory();
            var client = factory.CreateClient();

            var response = await client.GetAsync(route);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}

// STORY-180 — Spectator page shares the admin UI favicon
//
// BDD specification — xUnit (SPEC F63.6 addendum; adjacent to GitHub #15). The spectator page
// links /spectator/favicon.ico, served from the same bytes as admin-ui/app/favicon.ico so both
// surfaces present one station identity. Red until PLAN T22.

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

file sealed class FaviconWebFactory(bool spectatorMode = true) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Station:SpectatorMode", spectatorMode ? "true" : "false");
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

public static class FeatureSpectatorFavicon
{
    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent!;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioFaviconServedAndLinked
    {
        [Fact]
        public async Task PageLinksTheSpectatorFavicon()
        {
            await using var factory = new FaviconWebFactory();
            var client = factory.CreateClient();

            var html = await client.GetStringAsync("/spectator");

            Assert.Contains("/spectator/favicon.ico", html, StringComparison.Ordinal);
        }

        [Fact]
        public async Task FaviconServesAsAnIcon()
        {
            await using var factory = new FaviconWebFactory();
            var client = factory.CreateClient();

            var response = await client.GetAsync("/spectator/favicon.ico");

            Assert.Equal(("image/x-icon", true),
                (response.Content.Headers.ContentType?.MediaType, response.IsSuccessStatusCode));
        }

        [Fact]
        public async Task FaviconBytesMatchTheAdminUi()
        {
            await using var factory = new FaviconWebFactory();
            var client = factory.CreateClient();

            var served = await client.GetByteArrayAsync("/spectator/favicon.ico");
            var adminUi = await File.ReadAllBytesAsync(
                Path.Combine(RepoRoot(), "admin-ui", "app", "favicon.ico"));

            Assert.Equal(adminUi, served);
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioFaviconIsSurfaceGated
    {
        [Fact]
        public async Task FaviconIs404WhenSpectatorModeOff()
        {
            await using var factory = new FaviconWebFactory(spectatorMode: false);
            var client = factory.CreateClient();

            var response = await client.GetAsync("/spectator/favicon.ico");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}

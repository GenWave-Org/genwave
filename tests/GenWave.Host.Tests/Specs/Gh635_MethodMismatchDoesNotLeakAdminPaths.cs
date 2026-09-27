// gh-#635 — a method mismatch on a disabled surface's path answers 404, not 405 (SPEC F61.2)
//
// Routing's synthesized 405 carries none of the matched routes' metadata, so the surface gate never
// saw it: with Admin:Enabled=false, `GET /api/announcements/token` (POST/DELETE-only) answered 405
// plus an Allow header — proof the admin path exists.

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

file sealed class MethodMismatchWebFactory(bool adminEnabled) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Admin:Enabled", adminEnabled ? "true" : "false");
        builder.UseSetting("Station:SpectatorMode", "true");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-gh635");
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

public static class FeatureMethodMismatchDoesNotLeakAdminPaths
{
    public sealed class ScenarioAWrongVerbOnAnAdminPath
    {
        [Fact]
        public async Task AdminOffAnswers404WithNoAllowHeader()
        {
            await using var factory = new MethodMismatchWebFactory(adminEnabled: false);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.GetAsync("/api/announcements/token");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Empty(response.Content.Headers.Allow);
        }

        [Fact]
        public async Task AdminOnStillAnswers405()
        {
            await using var factory = new MethodMismatchWebFactory(adminEnabled: true);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.GetAsync("/api/announcements/token");

            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }
    }

    public sealed class ScenarioAWrongVerbOnAnOpenPath
    {
        [Fact]
        public async Task AnOpenSurfaceKeepsTheFrameworks405()
        {
            // The spectator surface is on here: a POST on its GET-only about route keeps the
            // framework's honest 405 even with admin off.
            await using var factory = new MethodMismatchWebFactory(adminEnabled: false);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.PostAsync("/spectator/api/about", content: null);

            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }
    }
}

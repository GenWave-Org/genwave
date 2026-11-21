// STORY-555 — A stored engine that is no longer configured keeps the station on air (SPEC F219.3 · PLAN T749)
//
// ARCHITECTURE: "Engine down = music only". A `Tts:Engine` row that outlived its registry entry
// (a launch flag dropped) must not 500 the status read; the voice tile reports red instead.

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

file sealed class MissingEngineFactory : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-missing-engine";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        // Stored in the settings table, but this boot no longer configures chatterbox.
        builder.UseSetting("Tts:Engine", "chatterbox");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
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

public static class FeatureAStoredEngineMissingFromConfig
{
    public sealed class ScenarioTheStatusRead : IAsyncLifetime
    {
        readonly WebApplicationFactory<Program> factory = new MissingEngineFactory();
        HttpResponseMessage response = new();
        JsonElement voice;

        public async Task InitializeAsync()
        {
            using var client = factory.CreateClient();
            var login = await client.PostAsJsonAsync("/api/auth/login", new { password = MissingEngineFactory.Password });
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

            response = await client.GetAsync("/api/status");
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            voice = doc.RootElement.TryGetProperty("voice", out var v) ? v.Clone() : default;
        }

        public async Task DisposeAsync()
        {
            response.Dispose();
            await factory.DisposeAsync();
        }

        [Fact]
        public void ReturnsOk() => Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        [Fact]
        public void TheVoiceTileIsRed() => Assert.True(voice.GetProperty("degraded").GetBoolean());

        [Fact]
        public void TheReasonNamesTheMissingEngine() =>
            Assert.Contains("chatterbox", voice.GetProperty("reason").GetString(), StringComparison.Ordinal);
    }
}

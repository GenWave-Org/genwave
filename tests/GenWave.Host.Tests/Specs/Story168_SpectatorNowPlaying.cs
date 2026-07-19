// STORY-168 — Public now-playing projection: onAir or standby, never an error
//
// BDD specification — xUnit (SPEC F62.4, F62.5). The spectator projection is public-shaped:
// always 200; feeder-warming and safe-rotation drain both collapse to {state:"standby"}; TTS
// patter is anonymized (kind:"patter", no title/artist — operator content). Red until PLAN T10.

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Host.Playout;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

file sealed class SpectatorNowPlayingWebFactory() : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Station:SpectatorMode", "true");
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

public static class FeatureSpectatorNowPlaying
{
    static readonly DateTimeOffset StartedAt = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    static NowPlayingSnapshot MusicSnapshot() =>
        new(MediaId: "42", Title: "Night Drive", Artist: "The Waveforms",
            GainDb: -2.5, StartedAt: StartedAt, DurationMs: 214000, IsDrain: false);

    static NowPlayingSnapshot PatterSnapshot() =>
        new(MediaId: "tts:abc123", Title: "Generated patter text — operator content", Artist: null,
            GainDb: 0, StartedAt: StartedAt, DurationMs: 12345, IsDrain: false);

    static NowPlayingSnapshot DrainSnapshot() =>
        new(MediaId: null, Title: null, Artist: null,
            GainDb: 0, StartedAt: StartedAt, DurationMs: null, IsDrain: true);

    /// <summary>Publishes a snapshot through the production store, then fetches the public projection.</summary>
    static async Task<(HttpStatusCode Status, JsonElement Body)> FetchAsync(
        WebApplicationFactory<Program> factory, NowPlayingSnapshot? snapshot)
    {
        var client = factory.CreateClient();
        if (snapshot is not null)
        {
            var store = factory.Services.GetRequiredService<NowPlayingService>();
            store.Update("1", snapshot); // SingleStation.IdString
        }

        var response = await client.GetAsync("/spectator/api/now-playing");
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return (response.StatusCode, body);
    }

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioOnAirTrack
    {
        [Fact]
        public async Task StateIsOnAir()
        {
            await using var factory = new SpectatorNowPlayingWebFactory();
            var (_, body) = await FetchAsync(factory, MusicSnapshot());
            Assert.Equal("onAir", body.GetProperty("state").GetString());
        }

        [Fact]
        public async Task KindIsTrack()
        {
            await using var factory = new SpectatorNowPlayingWebFactory();
            var (_, body) = await FetchAsync(factory, MusicSnapshot());
            Assert.Equal("track", body.GetProperty("kind").GetString());
        }

        [Fact]
        public async Task TitleAndArtistAreThePublicMetadata()
        {
            await using var factory = new SpectatorNowPlayingWebFactory();
            var (_, body) = await FetchAsync(factory, MusicSnapshot());
            Assert.Equal(("Night Drive", "The Waveforms"),
                (body.GetProperty("title").GetString(), body.GetProperty("artist").GetString()));
        }

        [Fact]
        public async Task StartedAtIsExposedForElapsedComputation()
        {
            await using var factory = new SpectatorNowPlayingWebFactory();
            var (_, body) = await FetchAsync(factory, MusicSnapshot());
            Assert.Equal(StartedAt, body.GetProperty("startedAt").GetDateTimeOffset());
        }

        [Fact]
        public async Task DurationMsIsCarriedWhenKnown()
        {
            await using var factory = new SpectatorNowPlayingWebFactory();
            var (_, body) = await FetchAsync(factory, MusicSnapshot());
            Assert.Equal(214000, body.GetProperty("durationMs").GetInt32());
        }
    }

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioWarmingCollapsesToStandby
    {
        [Fact]
        public async Task ResponseIs200NotServiceUnavailable()
        {
            await using var factory = new SpectatorNowPlayingWebFactory();
            var (status, _) = await FetchAsync(factory, snapshot: null); // feeder has not ticked
            Assert.Equal(HttpStatusCode.OK, status);
        }

        [Fact]
        public async Task StateIsStandby()
        {
            await using var factory = new SpectatorNowPlayingWebFactory();
            var (_, body) = await FetchAsync(factory, snapshot: null);
            Assert.Equal("standby", body.GetProperty("state").GetString());
        }
    }

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioDrainCollapsesToStandby
    {
        [Fact]
        public async Task StateIsStandby()
        {
            await using var factory = new SpectatorNowPlayingWebFactory();
            var (_, body) = await FetchAsync(factory, DrainSnapshot());
            Assert.Equal("standby", body.GetProperty("state").GetString());
        }

        [Fact]
        public async Task TheWordDrainNeverLeaks()
        {
            await using var factory = new SpectatorNowPlayingWebFactory();
            var (_, body) = await FetchAsync(factory, DrainSnapshot());
            Assert.False(body.TryGetProperty("drain", out _));
        }
    }

    // ── SAD PATH (disclosure) ─────────────────────────────────────────────

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioPatterIsAnonymized
    {
        [Fact]
        public async Task KindIsPatter()
        {
            await using var factory = new SpectatorNowPlayingWebFactory();
            var (_, body) = await FetchAsync(factory, PatterSnapshot());
            Assert.Equal("patter", body.GetProperty("kind").GetString());
        }

        [Fact]
        public async Task NoTitleFieldIsPresent()
        {
            await using var factory = new SpectatorNowPlayingWebFactory();
            var (_, body) = await FetchAsync(factory, PatterSnapshot());
            Assert.False(body.TryGetProperty("title", out _));
        }

        [Fact]
        public async Task NoArtistFieldIsPresent()
        {
            await using var factory = new SpectatorNowPlayingWebFactory();
            var (_, body) = await FetchAsync(factory, PatterSnapshot());
            Assert.False(body.TryGetProperty("artist", out _));
        }
    }
}

// STORY-245 — See the art we already broadcast (gh-#159, SPEC F93.3, PLAN T125/T126)
//
// BDD specification — xUnit. The card render (AC2) and icon fallback render (AC3's
// visual half) are T126 browser acceptance per the T92 precedent — no fake unit tests here.
// These facts pin the wire contract the page consumes.

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

/// <summary>
/// Real Program.cs composition root, credential-free spectator surface (mirrors
/// <c>Story168_SpectatorNowPlaying.cs</c>'s own factory): hosted services and the media catalog are
/// swapped for controllable fakes so no Postgres/Liquidsoap connection is ever attempted.
/// <see cref="GenWave.Orchestration.CachingScheduleResolver"/> is left resolving through the real
/// (unwarmed) DI graph — its <c>TryGetCurrent()</c> answers null throughout, so every fact here sees
/// <c>dj: null</c>/<c>upNext: null</c> alongside whatever <c>artworkUrl</c> it asserts on; STORY-244
/// owns proving those two fields, this file owns <c>artworkUrl</c> alone.
/// </summary>
file sealed class ArtworkOnTheCardWebFactory() : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Station:SpectatorMode", "true");
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

public static class FeatureArtworkOnTheCard
{
    static readonly DateTimeOffset StartedAt = new(2026, 7, 27, 9, 0, 0, TimeSpan.Zero);

    static NowPlayingSnapshot TrackSnapshot(string? artworkUrl) =>
        new(MediaId: "42", Title: "Night Drive", Artist: "The Waveforms", GainDb: -2.5,
            StartedAt: StartedAt, DurationMs: 214_000, IsDrain: false, ArtworkUrl: artworkUrl);

    static NowPlayingSnapshot PatterSnapshot() =>
        new(MediaId: "tts:abc123", Title: "Generated patter text — operator content", Artist: null,
            GainDb: 0, StartedAt: StartedAt, DurationMs: 12_345, IsDrain: false);

    static async Task<JsonElement> FetchNowPlayingAsync(WebApplicationFactory<Program> factory, NowPlayingSnapshot snapshot)
    {
        factory.Services.GetRequiredService<NowPlayingService>().Update("1", snapshot); // SingleStation.IdString
        var client = factory.CreateClient();

        var response = await client.GetAsync("/spectator/api/now-playing");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioArtworkUrlOnTrackState
    {
        // Given a track with art on air and Station:PublicBaseUrl set (F93.3).

        [Fact]
        public async Task TrackStateCarriesTheF88TokenUrl()
        {
            const string token = "https://demo.example/spectator/api/artwork/0123456789abcdef0123456789abcdef";
            await using var factory = new ArtworkOnTheCardWebFactory();

            var body = await FetchNowPlayingAsync(factory, TrackSnapshot(token));

            Assert.Equal(token, body.GetProperty("artworkUrl").GetString());
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioFallbacksAreTheStationIcon
    {
        // Sad path — art-less track and patter (F93.3).

        [Fact]
        public async Task ArtLessTrackCarriesNullArtworkUrl()
        {
            // No art (or no Station:PublicBaseUrl) is a PRESENT key with a null value — the track
            // shape always has the property; only its value is absent — so the page's null-check
            // still finds the key and falls back to the station icon.
            await using var factory = new ArtworkOnTheCardWebFactory();

            var body = await FetchNowPlayingAsync(factory, TrackSnapshot(artworkUrl: null));

            Assert.True(body.TryGetProperty("artworkUrl", out var artworkUrl));
            Assert.Equal(JsonValueKind.Null, artworkUrl.ValueKind);
        }

        [Fact]
        public async Task PatterStateCarriesNoArtworkUrl()
        {
            // F93.3: artworkUrl is a TRACK-only field — by construction, SpectatorPatterNowPlaying
            // has no such property at all, so the page shows the station icon unconditionally for
            // patter (never a null-check — there is nothing to check).
            await using var factory = new ArtworkOnTheCardWebFactory();

            var body = await FetchNowPlayingAsync(factory, PatterSnapshot());

            Assert.False(body.TryGetProperty("artworkUrl", out _));
        }

        [Fact(Skip = "Pending (T126): browser acceptance — card renders art with station-icon loading/fallback")]
        public void CardRenderIsBrowserAcceptance() { }
    }
}

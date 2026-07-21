// STORY-169 — Public history and stats projections
//
// BDD specification — xUnit (SPEC F62.6, F62.7). History: newest-first, at most 20, each entry
// {kind, title?, artist?, airedAt} with tts:* entries anonymized. Stats: exactly
// {ready, enriching, failed} — playable/unavailable reveal scope config and are excluded.
// Red until PLAN T11.

using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Playout;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

file sealed class SpectatorHistoryWebFactory(IMediaCatalog catalog) : WebApplicationFactory<Program>
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
            services.AddSingleton(catalog);
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
        });
    }
}

public static class FeatureSpectatorHistoryAndStats
{
    const string StationId = "1"; // SingleStation.IdString

    static PlayHistoryEntry MusicEntry(int index) =>
        new(StationId, MediaId: $"{100 + index}", Title: $"Track {index}", Artist: $"Artist {index}",
            GainDb: -1.0, StartedAt: new DateTimeOffset(2026, 7, 18, 10, 0, 0, TimeSpan.Zero).AddMinutes(index),
            EndedAt: null, DurationMs: 180000);

    static PlayHistoryEntry PatterEntry(int index) =>
        new(StationId, MediaId: "tts:xyz", Title: "Generated patter text", Artist: null,
            GainDb: 0, StartedAt: new DateTimeOffset(2026, 7, 18, 10, 0, 0, TimeSpan.Zero).AddMinutes(index),
            EndedAt: null, DurationMs: 12000);

    static async Task<JsonElement> FetchAsync(WebApplicationFactory<Program> factory, string route, params PlayHistoryEntry[] entries)
    {
        var client = factory.CreateClient();
        var history = factory.Services.GetRequiredService<PlayHistoryService>();
        foreach (var entry in entries)
            history.Push(entry);

        var response = await client.GetAsync(route);
        Assert.True(response.IsSuccessStatusCode, $"{route} returned {(int)response.StatusCode}.");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioHistoryShape
    {
        [Fact]
        public async Task AtMostTwentyEntriesAreReturned()
        {
            await using var factory = new SpectatorHistoryWebFactory(new FakeMediaCatalog(ready: null));
            var body = await FetchAsync(factory, "/spectator/api/play-history",
                Enumerable.Range(0, 25).Select(MusicEntry).ToArray());

            Assert.True(body.GetProperty("entries").GetArrayLength() <= 20);
        }

        [Fact]
        public async Task NewestEntryComesFirst()
        {
            await using var factory = new SpectatorHistoryWebFactory(new FakeMediaCatalog(ready: null));
            var body = await FetchAsync(factory, "/spectator/api/play-history",
                MusicEntry(0), MusicEntry(1), MusicEntry(2));

            Assert.Equal("Track 2", body.GetProperty("entries")[0].GetProperty("title").GetString());
        }

        [Fact]
        public async Task MusicEntryCarriesKindTitleArtistAndAiredAt()
        {
            await using var factory = new SpectatorHistoryWebFactory(new FakeMediaCatalog(ready: null));
            var body = await FetchAsync(factory, "/spectator/api/play-history", MusicEntry(1));

            var entry = body.GetProperty("entries")[0];
            Assert.Equal(
                ("track", "Track 1", "Artist 1", true),
                (entry.GetProperty("kind").GetString(),
                 entry.GetProperty("title").GetString(),
                 entry.GetProperty("artist").GetString(),
                 entry.TryGetProperty("airedAt", out _)));
        }

        [Fact]
        public async Task PatterEntryIsAnonymized()
        {
            await using var factory = new SpectatorHistoryWebFactory(new FakeMediaCatalog(ready: null));
            var body = await FetchAsync(factory, "/spectator/api/play-history", PatterEntry(1));

            var entry = body.GetProperty("entries")[0];
            Assert.Equal(
                ("patter", false, false),
                (entry.GetProperty("kind").GetString(),
                 entry.TryGetProperty("title", out _),
                 entry.TryGetProperty("artist", out _)));
        }
    }

    public sealed class ScenarioStatsCountsOnly
    {
        static WebApplicationFactory<Program> FactoryWithCounts() =>
            new SpectatorHistoryWebFactory(new FakeMediaCatalog(ready: null,
                statusCounts: new CatalogStatusCounts(Ready: 5, Enriching: 3, Failed: 2, Unavailable: 1, Playable: 4)));

        [Fact]
        public async Task CountsMatchTheCatalog()
        {
            await using var factory = FactoryWithCounts();
            var body = await FetchAsync(factory, "/spectator/api/stats");

            Assert.Equal(
                (5, 3, 2),
                (body.GetProperty("ready").GetInt32(),
                 body.GetProperty("enriching").GetInt32(),
                 body.GetProperty("failed").GetInt32()));
        }

        // ── SAD PATH (disclosure) ─────────────────────────────────────────

        [Fact]
        public async Task UnavailableCountIsNotExposed()
        {
            await using var factory = FactoryWithCounts();
            var body = await FetchAsync(factory, "/spectator/api/stats");

            Assert.False(body.TryGetProperty("unavailable", out _));
        }

        [Fact]
        public async Task PlayableCountIsNotExposed()
        {
            await using var factory = FactoryWithCounts();
            var body = await FetchAsync(factory, "/spectator/api/stats");

            Assert.False(body.TryGetProperty("playable", out _));
        }
    }
}

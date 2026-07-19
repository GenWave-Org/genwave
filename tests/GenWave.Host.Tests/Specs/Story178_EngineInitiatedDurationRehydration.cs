// STORY-178 — Engine-initiated plays rehydrate durationMs from the catalog
//
// BDD specification — xUnit (SPEC F66.2–F66.4). Safe-rotation and restart-survivor plays carry a
// numeric media id but no duration (it never rides the annotate line). The Host rehydrates via
// ONE memoized, unscoped by-id catalog read triggered by snapshot publish (NowPlayingService is
// the seam every publish flows through — PlayoutFeeder itself stays DB-free, F16.6), patching
// the snapshot and the matching history entry. Never throws, never fabricates.
// Observed through the production API surface (/api/now-playing, /api/play-history).
// Red until PLAN T07.

using System.Net.Http.Json;
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

/// <summary>Counts by-id reads and can script a hit, a miss, or a thrown catalog failure.</summary>
file sealed class CountingCatalog(MediaReference? byId, bool throwOnRead = false) : IMediaCatalog
{
    int byIdCalls;
    public int ByIdCalls => Volatile.Read(ref byIdCalls);

    public Task<MediaReference?> GetByIdAsync(LibraryScope scope, string mediaId, CancellationToken ct)
    {
        Interlocked.Increment(ref byIdCalls);
        if (throwOnRead) throw new InvalidOperationException("catalog unavailable");
        return Task.FromResult(byId is not null && byId.MediaId == mediaId ? byId : null);
    }

    public Task<MediaReference?> GetByIdUnscopedAsync(string mediaId, CancellationToken ct)
    {
        Interlocked.Increment(ref byIdCalls);
        if (throwOnRead) throw new InvalidOperationException("catalog unavailable");
        return Task.FromResult(byId is not null && byId.MediaId == mediaId ? byId : null);
    }

    public Task<MediaReference?> GetRandomReadyAsync(LibraryScope scope, IReadOnlyList<string> excludeIds, CancellationToken ct)
        => Task.FromResult<MediaReference?>(null);

    public Task<RotationCandidate?> GetRotationCandidateAsync(
        LibraryScope scope, IReadOnlyList<string> orderedRecentIds, int artistSeparation, CancellationToken ct)
        => Task.FromResult<RotationCandidate?>(null);

    public Task<PagedResult<MediaReference>> ListAsync(LibraryScope scope, MediaQuery query, CancellationToken ct)
        => Task.FromResult(new PagedResult<MediaReference>([], 0, 0));

    public Task<CatalogStatusCounts> GetStatusCountsAsync(LibraryScope safeScope, CancellationToken ct)
        => Task.FromResult(new CatalogStatusCounts(0, 0, 0, 0, 0));

    public Task<IReadOnlyList<FacetValue>> GetFacetsAsync(FacetField field, LibraryScope scope, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<FacetValue>>([]);
}

file sealed class RehydrationWebFactory(IMediaCatalog catalog) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-x7z";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton(catalog);
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

public static class FeatureEngineInitiatedDurationRehydration
{
    const string StationId = "1"; // SingleStation.IdString
    static readonly DateTimeOffset StartedAt = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    static MediaReference CatalogRow(string mediaId, int durationMs) =>
        new(MediaId: mediaId, Locator: "/library/track.mp3", Title: "Night Drive",
            Loudness: new Core.Domain.Loudness(-14.0, -1.0, Measurable: true),
            DurationMs: durationMs, SampleRate: 44100, Channels: 2, BitrateKbps: 192,
            Artist: "The Waveforms", Album: null, Genre: null, Year: null);

    static NowPlayingSnapshot EngineInitiated(string mediaId) =>
        new(mediaId, "Night Drive", "The Waveforms", GainDb: -2.5,
            StartedAt: StartedAt, DurationMs: null, IsDrain: false);

    static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsync("/api/auth/login",
            JsonContent.Create(new { password = RehydrationWebFactory.Password }));
        Assert.True(login.IsSuccessStatusCode, $"login returned {(int)login.StatusCode}");
        return client;
    }

    /// <summary>Polls the production endpoint until durationMs is non-null or the timeout lapses.</summary>
    static async Task<int?> PollNowPlayingDurationAsync(HttpClient client, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        int? duration = null;
        while (DateTime.UtcNow < deadline)
        {
            var body = JsonDocument.Parse(await client.GetStringAsync("/api/now-playing")).RootElement;
            if (body.TryGetProperty("durationMs", out var value) && value.ValueKind is JsonValueKind.Number)
            {
                duration = value.GetInt32();
                break;
            }
            await Task.Delay(50);
        }
        return duration;
    }

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioSnapshotIsPatchedFromTheCatalog
    {
        [Fact]
        public async Task NowPlayingGainsTheCatalogDuration()
        {
            var catalog = new CountingCatalog(CatalogRow("42", 214000));
            await using var factory = new RehydrationWebFactory(catalog);
            var client = await LoggedInClientAsync(factory);

            factory.Services.GetRequiredService<NowPlayingService>().Update(StationId, EngineInitiated("42"));

            Assert.Equal(214000, await PollNowPlayingDurationAsync(client, TimeSpan.FromSeconds(2)));
        }
    }

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioHistoryEntryIsPatched
    {
        [Fact]
        public async Task MatchingHistoryEntryGainsTheCatalogDuration()
        {
            var catalog = new CountingCatalog(CatalogRow("42", 214000));
            await using var factory = new RehydrationWebFactory(catalog);
            var client = await LoggedInClientAsync(factory);

            factory.Services.GetRequiredService<PlayHistoryService>().Push(
                new PlayHistoryEntry(StationId, "42", "Night Drive", "The Waveforms", -2.5,
                    StartedAt, EndedAt: null, DurationMs: null));
            factory.Services.GetRequiredService<NowPlayingService>().Update(StationId, EngineInitiated("42"));

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            int? duration = null;
            while (DateTime.UtcNow < deadline && duration is null)
            {
                var body = JsonDocument.Parse(await client.GetStringAsync("/api/play-history")).RootElement;
                var first = body.EnumerateArray().FirstOrDefault();
                if (first.ValueKind is JsonValueKind.Object &&
                    first.TryGetProperty("durationMs", out var value) &&
                    value.ValueKind is JsonValueKind.Number)
                {
                    duration = value.GetInt32();
                    break;
                }
                await Task.Delay(50);
            }

            Assert.Equal(214000, duration);
        }
    }

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioLookupIsMemoized
    {
        [Fact]
        public async Task RepeatedPublishesOfTheSameAiringCauseOneCatalogRead()
        {
            var catalog = new CountingCatalog(CatalogRow("42", 214000));
            await using var factory = new RehydrationWebFactory(catalog);
            var client = await LoggedInClientAsync(factory);
            var store = factory.Services.GetRequiredService<NowPlayingService>();

            // Same airing observed across three 3s ticks.
            store.Update(StationId, EngineInitiated("42"));
            await PollNowPlayingDurationAsync(client, TimeSpan.FromSeconds(2));
            store.Update(StationId, EngineInitiated("42"));
            store.Update(StationId, EngineInitiated("42"));
            await Task.Delay(200);

            Assert.Equal(1, catalog.ByIdCalls);
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioCatalogMissStaysNull
    {
        [Fact]
        public async Task UnknownIdLeavesDurationNull()
        {
            var catalog = new CountingCatalog(byId: null);
            await using var factory = new RehydrationWebFactory(catalog);
            var client = await LoggedInClientAsync(factory);

            factory.Services.GetRequiredService<NowPlayingService>().Update(StationId, EngineInitiated("999"));
            await Task.Delay(300);

            var body = JsonDocument.Parse(await client.GetStringAsync("/api/now-playing")).RootElement;
            Assert.True(!body.TryGetProperty("durationMs", out var v) || v.ValueKind is JsonValueKind.Null);
        }
    }

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioNonNumericAndDrainAreSkipped
    {
        [Fact]
        public async Task TtsIdCausesNoCatalogRead()
        {
            var catalog = new CountingCatalog(byId: null);
            await using var factory = new RehydrationWebFactory(catalog);
            _ = await LoggedInClientAsync(factory);

            factory.Services.GetRequiredService<NowPlayingService>().Update(StationId,
                EngineInitiated("tts:abc123"));
            await Task.Delay(300);

            Assert.Equal(0, catalog.ByIdCalls);
        }

        [Fact]
        public async Task DrainSnapshotCausesNoCatalogRead()
        {
            var catalog = new CountingCatalog(byId: null);
            await using var factory = new RehydrationWebFactory(catalog);
            _ = await LoggedInClientAsync(factory);

            factory.Services.GetRequiredService<NowPlayingService>().Update(StationId,
                new NowPlayingSnapshot(null, null, null, 0, StartedAt, null, IsDrain: true));
            await Task.Delay(300);

            Assert.Equal(0, catalog.ByIdCalls);
        }
    }

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioCatalogFailureIsHarmless
    {
        [Fact]
        public async Task ThrowingCatalogNeverBreaksTheApi()
        {
            var catalog = new CountingCatalog(byId: null, throwOnRead: true);
            await using var factory = new RehydrationWebFactory(catalog);
            var client = await LoggedInClientAsync(factory);

            factory.Services.GetRequiredService<NowPlayingService>().Update(StationId, EngineInitiated("42"));
            await Task.Delay(300);

            var response = await client.GetAsync("/api/now-playing");
            Assert.True(response.IsSuccessStatusCode,
                $"/api/now-playing returned {(int)response.StatusCode} after a catalog failure (F66.4).");
        }
    }
}

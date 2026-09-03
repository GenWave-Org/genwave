// gh-#380 — GET /media/random calls the fenced repository read (SPEC F158.4 · PLAN T395 review
// finding-8).
//
// BDD specification — xUnit, full WebApplicationFactory<Program> + HttpClient (mirrors
// Story084_StatusEndpoint.cs's own StatusApiWebFactory / Story056_SafeTrackEndpoint.cs's own
// SafeTrackWebFactory precedent exactly): a genuine HTTP GET drives the REAL production
// MediaEndpoints route, over a FakeMediaCatalog whose GetRandomPlayableAsync/GetRandomReadyAsync
// overrides record into two DISTINCT lists (FakeMediaCatalog's own remarks) — the only way to
// prove /media/random calls the fenced method rather than merely falling through IMediaCatalog's
// own DIM default, which would make the two indistinguishable. Reverting MediaEndpoints's
// "/random" route back to GetRandomReadyAsync must turn this fact red.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
// Alias to avoid clash with the GenWave.Loudness namespace (FfmpegLoudnessAnalyzer project).
using TrackLoudness = GenWave.Core.Domain.Loudness;

namespace GenWave.Host.Tests.Specs;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for this file's one scenario — mirrors
/// Story084_StatusEndpoint.cs's own StatusApiWebFactory: removes hosted services that would
/// attempt real Liquidsoap/DB connections and replaces <see cref="IMediaCatalog"/> with the
/// controllable <see cref="FakeMediaCatalog"/>. <c>GET /media/random</c> is <c>AllowAnonymous</c>
/// (<c>MediaEndpoints.MapMediaEndpoints</c>'s own <c>/media</c> group), so no auth cookie is needed.
/// </summary>
file sealed class MediaRandomWebFactory(FakeMediaCatalog catalog) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development config provides Station:Id/Name/Voice/Scope/SafeScope and Tts:Endpoint so
        // ValidateOnStart() is satisfied without injecting them manually (mirrors StatusApiWebFactory).
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-gh380-media-random");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(catalog);
        });
    }
}

public static class FeatureMediaRandomCallsTheFencedMethod
{
    static MediaReference BuildReadyTrack() => new(
        MediaId: "track-fenced-001",
        Locator: "/media/track-fenced-001.mp3",
        Title: "Fenced Track",
        Loudness: new TrackLoudness(-20.0, -2.0, Measurable: true),
        DurationMs: 180_000,
        SampleRate: 44100,
        Channels: 2,
        BitrateKbps: 320,
        Artist: "Test Artist",
        Album: null,
        Genre: null,
        Year: null);

    public sealed class ScenarioTheEndpointCallsTheFencedRead
    {
        [Fact]
        public async Task GetMediaRandomRecordsIntoPlayableCallsNeverRandomCalls()
        {
            var catalog = new FakeMediaCatalog(ready: BuildReadyTrack());
            await using var factory = new MediaRandomWebFactory(catalog);
            var client = factory.CreateClient();

            var response = await client.GetAsync("/media/random");

            response.EnsureSuccessStatusCode();
            Assert.Single(catalog.PlayableCalls);
            Assert.Empty(catalog.RandomCalls);
        }
    }
}

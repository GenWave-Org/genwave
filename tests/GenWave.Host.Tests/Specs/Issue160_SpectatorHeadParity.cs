// gh-#160 — spectator: HEAD returned 404 where GET returned 200. Every spectator action was
// mapped [HttpGet]-only (and the page endpoints MapGet-only), so an unmatched HEAD collapsed to a
// bare routing 404 — an art-preflighting client (cheap HEAD before downloading cover art)
// concluded the station broadcasts no artwork, silently defeating gh-#105 for that client class,
// and ops HEAD probes read as an outage.
//
// Per RFC 9110 §9.3.2 HEAD must answer with GET's status/headers, body suppressed. The fix routes
// HEAD onto every spectator GET route through the SAME surface gate and authorization — these
// facts pin STATUS parity per route (Kestrel suppresses the body in production; TestServer does
// not model that server behavior, so body emptiness is deliberately not asserted here).

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Host.Tests.Fakes;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

/// <summary>
/// PLAN T307 (ladder unification): <see cref="IStationImageStore"/> is ALSO swapped for a
/// seedable-but-unseeded double — the unknown-artwork-token fact below now reads it (through
/// <c>StationImageCache</c>) on its own no-oracle fallback; this project has no Postgres fixture, so
/// an un-swapped store would otherwise attempt a real connection.
/// </summary>
file sealed class SpectatorHeadParityWebFactory : WebApplicationFactory<Program>
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
            services.RemoveAll<IStationImageStore>();
            services.AddSingleton<IStationImageStore>(new FakeStationImageStore());
        });
    }
}

public static class FeatureSpectatorHeadParity
{
    /// <summary>Every spectator route HEAD must answer on — API, artwork, and the page trio.</summary>
    public static TheoryData<string> SpectatorPaths => new(
        "/spectator/api/now-playing",
        "/spectator/api/play-history",
        "/spectator/api/stats",
        "/spectator/api/about",
        "/spectator");

    public sealed class ScenarioHeadMatchesGetStatus
    {
        [Theory]
        [MemberData(nameof(SpectatorPaths), MemberType = typeof(FeatureSpectatorHeadParity))]
        public async Task Head_returns_the_same_status_as_get(string path)
        {
            await using var factory = new SpectatorHeadParityWebFactory();
            var client = factory.CreateClient();

            var get = await client.GetAsync(path);
            var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path));

            Assert.Equal(get.StatusCode, head.StatusCode);
        }

        [Fact]
        public async Task Head_on_an_unknown_artwork_token_matches_gets_station_icon_200()
        {
            // The no-oracle discipline: HEAD must produce the SAME verdict the GET path computes.
            // The artwork route never 404s by design (SPEC F88.3 fail-to-icon: an unknown token
            // serves the station icon), so parity here means BOTH answer 200 — the pre-fix HEAD
            // answered a routing 404 that told preflighting clients the station has no artwork.
            await using var factory = new SpectatorHeadParityWebFactory();
            var client = factory.CreateClient();
            const string Path = "/spectator/api/artwork/no-such-token";

            var get = await client.GetAsync(Path);
            var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, Path));

            Assert.Equal(get.StatusCode, head.StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.OK, head.StatusCode);
        }
    }
}

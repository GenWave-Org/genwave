// STORY-179 — Spectator now-playing shows the live listener count
//
// BDD specification — xUnit (SPEC F62.12 addendum; GitHub #10's IListenerStatsSource seam).
// The api polls Icecast's password-protected admin stats (Icecast:StatsUrl +
// Icecast:AdminPassword, env/compose-only) and surfaces a public `listeners` count on every
// now-playing shape. Unconfigured or unreachable Icecast ⇒ listeners: null — never an error,
// never fabricated. Driven end-to-end through the production pipeline against a stub Icecast.
// Red until PLAN T21.

using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using GenWave.Core.Abstractions;
using GenWave.Host.Playout;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

/// <summary>
/// Minimal Icecast admin-stats stub: serves /admin/stats.xml behind HTTP Basic auth
/// (admin / the given password), or a scripted failure status.
/// <para>
/// Kestrel on port 0, and the bound port is read back from the server AFTER it starts (gh-#329).
/// The previous shape asked the OS for a free port, closed that socket, and only then bound an
/// <see cref="HttpListener"/> to the number — leaving a window in which anything on the machine
/// could take the port. Under full-suite parallel load something did, roughly one run in three,
/// and the spec failed on a connect timeout that read like a logic bug. Binding port 0 on the
/// server that actually serves the requests closes the window by construction rather than making
/// the collision recoverable: there is no interval during which the port is spoken for but
/// unheld.
/// </para>
/// </summary>
file sealed class IcecastStatsStub : IDisposable
{
    readonly WebApplication app;
    int requestCount;

    public string BaseUrl { get; }

    /// <summary>
    /// Interlocked: incremented on Kestrel's request threads and read from the spec's thread
    /// (gh-#329). The unsynchronised <c>++</c> this replaces was a second, quieter data race in
    /// the same fixture — never the cause of the observed flake, but the memo-window facts assert
    /// on exact counts, so a torn read would have been indistinguishable from a real regression.
    /// </summary>
    public int RequestCount => Volatile.Read(ref requestCount);

    public IcecastStatsStub(string adminPassword, int? listeners, HttpStatusCode status = HttpStatusCode.OK)
    {
        // CreateEmptyBuilder + UseKestrelCore: this stub needs a socket and a request delegate and
        // nothing else. The full builder would read the Host's own appsettings.json out of the test
        // output directory, which is a surprising amount of coupling for a fake Icecast.
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.WebHost.UseKestrelCore().ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
        app = builder.Build();

        app.Run(async ctx =>
        {
            Interlocked.Increment(ref requestCount);

            var expected = "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes($"admin:{adminPassword}"));
            if (ctx.Request.Headers.Authorization != expected)
            {
                ctx.Response.StatusCode = 401;
                return;
            }

            ctx.Response.StatusCode = (int)status;
            if (status == HttpStatusCode.OK)
            {
                ctx.Response.ContentType = "text/xml";
                await ctx.Response.WriteAsync($"""
                    <icestats><source mount="/stream"><listeners>{listeners ?? 0}</listeners></source></icestats>
                    """);
            }
        });

        app.Start();

        // Read the port back from the server rather than predicting it — the whole point of the fix.
        BaseUrl = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First()
            .TrimEnd('/');
    }

    public void Dispose()
    {
        app.StopAsync().GetAwaiter().GetResult();
        app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

file sealed class ListenerCountWebFactory(
    string? statsUrl, string? adminPassword, TimeProvider? timeProvider = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Station:SpectatorMode", "true");
        if (statsUrl is not null) builder.UseSetting("Icecast:StatsUrl", statsUrl);
        if (adminPassword is not null) builder.UseSetting("Icecast:AdminPassword", adminPassword);
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-x7z");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());

            // gh-#106: the memo-window facts pin IcecastListenerStatsSource's clock so a slow CI
            // runner can never expire the window mid-fact. Facts that don't care leave the
            // production TimeProvider.System binding untouched.
            if (timeProvider is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(timeProvider);
            }
        });
    }
}

public static class FeatureSpectatorListenerCount
{
    static NowPlayingSnapshot MusicSnapshot() =>
        new("42", "Night Drive", "The Waveforms", -2.5,
            new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero), 214000, IsDrain: false);

    static async Task<JsonElement> FetchNowPlayingAsync(WebApplicationFactory<Program> factory, NowPlayingSnapshot? snapshot)
    {
        var client = factory.CreateClient();
        if (snapshot is not null)
            factory.Services.GetRequiredService<NowPlayingService>().Update("1", snapshot);
        var response = await client.GetAsync("/spectator/api/now-playing");
        Assert.True(response.IsSuccessStatusCode, $"now-playing returned {(int)response.StatusCode}");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioListenersFromIcecastStats
    {
        [Fact]
        public async Task OnAirPayloadCarriesTheListenerCount()
        {
            using var stub = new IcecastStatsStub("ice-admin-pw", listeners: 5);
            await using var factory = new ListenerCountWebFactory(stub.BaseUrl, "ice-admin-pw");

            var body = await FetchNowPlayingAsync(factory, MusicSnapshot());

            Assert.Equal(5, body.GetProperty("listeners").GetInt32());
        }

        [Fact]
        public async Task StandbyPayloadAlsoCarriesTheListenerCount()
        {
            using var stub = new IcecastStatsStub("ice-admin-pw", listeners: 3);
            await using var factory = new ListenerCountWebFactory(stub.BaseUrl, "ice-admin-pw");

            var body = await FetchNowPlayingAsync(factory, snapshot: null); // warming → standby

            Assert.Equal(3, body.GetProperty("listeners").GetInt32());
        }

        [Fact]
        public async Task StatsRequestAuthenticatesAsIcecastAdmin()
        {
            // The stub 401s any request without the exact admin basic-auth header — a non-null
            // listeners value therefore proves credentials were sent.
            using var stub = new IcecastStatsStub("ice-admin-pw", listeners: 9);
            await using var factory = new ListenerCountWebFactory(stub.BaseUrl, "ice-admin-pw");

            var body = await FetchNowPlayingAsync(factory, MusicSnapshot());

            Assert.Equal(9, body.GetProperty("listeners").GetInt32());
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioUnknownCountIsNullNeverAnError
    {
        [Fact]
        public async Task UnconfiguredStatsUrlYieldsNullListeners()
        {
            await using var factory = new ListenerCountWebFactory(statsUrl: null, adminPassword: null);

            var body = await FetchNowPlayingAsync(factory, MusicSnapshot());

            Assert.True(
                body.TryGetProperty("listeners", out var v) && v.ValueKind is JsonValueKind.Null,
                "listeners must be present and null when Icecast stats are unconfigured.");
        }

        [Fact]
        public async Task IcecastFailureYieldsNullListenersAnd200()
        {
            using var stub = new IcecastStatsStub("ice-admin-pw", listeners: null, HttpStatusCode.InternalServerError);
            await using var factory = new ListenerCountWebFactory(stub.BaseUrl, "ice-admin-pw");

            var body = await FetchNowPlayingAsync(factory, MusicSnapshot());

            Assert.True(
                body.TryGetProperty("listeners", out var v) && v.ValueKind is JsonValueKind.Null,
                "listeners must degrade to null when Icecast is unreachable.");
        }

        [Fact]
        public async Task RepeatedRequestsWithinTheMemoWindowPollIcecastOnce()
        {
            // gh-#106: the clock is FROZEN — the memo window cannot expire mid-fact no matter how
            // slow the runner is (the wall-clock version of this fact flaked on a cold CI box).
            var clock = new FakeTimeProvider();
            using var stub = new IcecastStatsStub("ice-admin-pw", listeners: 4);
            await using var factory = new ListenerCountWebFactory(stub.BaseUrl, "ice-admin-pw", clock);
            var client = factory.CreateClient();

            await client.GetAsync("/spectator/api/now-playing");
            var first = stub.RequestCount;
            await client.GetAsync("/spectator/api/now-playing");
            await client.GetAsync("/spectator/api/now-playing");

            Assert.Equal(first, stub.RequestCount);
        }

        [Fact]
        public async Task AdvancingPastTheMemoWindowPollsIcecastExactlyOnceMore()
        {
            // gh-#106 companion: the memo EXPIRES on the same injected clock — advance past the
            // ~10s window and exactly one fresh poll happens, however many requests follow it.
            var clock = new FakeTimeProvider();
            using var stub = new IcecastStatsStub("ice-admin-pw", listeners: 4);
            await using var factory = new ListenerCountWebFactory(stub.BaseUrl, "ice-admin-pw", clock);
            var client = factory.CreateClient();

            await client.GetAsync("/spectator/api/now-playing");
            var first = stub.RequestCount;

            clock.Advance(TimeSpan.FromSeconds(11));

            // The now-playing OUTPUT cache (5s, wall-clock) may still serve the whole response
            // without touching the source — hit the source's own seam directly instead, so this
            // fact pins the MEMO's expiry, not the output cache's.
            var source = factory.Services.GetRequiredService<IListenerStatsSource>();
            await source.GetListenerCountAsync(CancellationToken.None);
            await source.GetListenerCountAsync(CancellationToken.None);

            Assert.Equal(first + 1, stub.RequestCount);
        }
    }

    public sealed class ScenarioTheStubOwnsThePortItReports
    {
        [Fact]
        public async Task ConcurrentStubsNeverCollideAndEveryOneOfThemServes()
        {
            // gh-#329 regression guard. A race is not deterministically testable, so this pins the
            // PROPERTY the fix establishes instead: the stub reports a port it is already holding.
            // The old shape predicted one — bind :0, close, return the number, bind for real later —
            // so two stubs alive at once could be handed the same number and the second would fail
            // to start, the same window that made this file fail ~1 run in 3 under full-suite load.
            // Binding :0 on the socket that actually serves makes distinctness the OS's guarantee
            // rather than a bet on timing.
            var stubs = Enumerable.Range(0, 8)
                .Select(_ => new IcecastStatsStub("ice-admin-pw", listeners: 1))
                .ToList();
            try
            {
                Assert.Equal(stubs.Count, stubs.Select(s => s.BaseUrl).Distinct().Count());

                using var http = new HttpClient();
                http.DefaultRequestHeaders.Authorization =
                    new("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes("admin:ice-admin-pw")));

                foreach (var stub in stubs)
                {
                    var response = await http.GetAsync($"{stub.BaseUrl}/admin/stats.xml");
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                }
            }
            finally
            {
                foreach (var stub in stubs) stub.Dispose();
            }
        }
    }
}

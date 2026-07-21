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
/// Minimal Icecast admin-stats stub: serves /admin/stats.xml behind HTTP Basic auth
/// (admin / the given password), or a scripted failure status.
/// </summary>
file sealed class IcecastStatsStub : IDisposable
{
    readonly HttpListener listener = new();
    public string BaseUrl { get; }
    public int RequestCount { get; private set; }

    public IcecastStatsStub(string adminPassword, int? listeners, HttpStatusCode status = HttpStatusCode.OK)
    {
        var port = FreePort();
        BaseUrl = $"http://127.0.0.1:{port}";
        listener.Prefixes.Add($"{BaseUrl}/");
        listener.Start();
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch { return; }
                RequestCount++;

                var expected = "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes($"admin:{adminPassword}"));
                if (ctx.Request.Headers["Authorization"] != expected)
                {
                    ctx.Response.StatusCode = 401;
                    ctx.Response.Close();
                    continue;
                }

                ctx.Response.StatusCode = (int)status;
                if (status == HttpStatusCode.OK)
                {
                    var xml = $"""
                        <icestats><source mount="/stream"><listeners>{listeners ?? 0}</listeners></source></icestats>
                        """;
                    var bytes = Encoding.UTF8.GetBytes(xml);
                    ctx.Response.ContentType = "text/xml";
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                }
                ctx.Response.Close();
            }
        });
    }

    static int FreePort()
    {
        var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }

    public void Dispose() => listener.Close();
}

file sealed class ListenerCountWebFactory(string? statsUrl, string? adminPassword) : WebApplicationFactory<Program>
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
            using var stub = new IcecastStatsStub("ice-admin-pw", listeners: 4);
            await using var factory = new ListenerCountWebFactory(stub.BaseUrl, "ice-admin-pw");
            var client = factory.CreateClient();

            await client.GetAsync("/spectator/api/now-playing");
            var first = stub.RequestCount;
            await client.GetAsync("/spectator/api/now-playing");
            await client.GetAsync("/spectator/api/now-playing");

            Assert.Equal(first, stub.RequestCount);
        }
    }
}

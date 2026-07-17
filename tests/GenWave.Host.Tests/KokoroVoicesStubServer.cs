using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GenWave.Host.Tests;

/// <summary>
/// Minimal Kestrel-backed stub for Kokoro's <c>GET /v1/audio/voices</c> (SPEC F29.4). Listens on a
/// random loopback port; <see cref="BaseUri"/> is available immediately after
/// <see cref="StartAsync"/>. Mirrors <see cref="KokoroStubServer"/>'s shape but for the voices route
/// (a distinct endpoint, so it stays a separate stub rather than growing that one).
///
/// <see cref="CallCount"/> lets ScenarioShortCache assert the TTL cache suppresses a second
/// upstream round-trip. Dispose to shut down — also doubles as the "Kokoro unreachable" fixture:
/// dispose, then GET against the now-dead <see cref="BaseUri"/> to force a real connection-refused
/// failure (no guessed always-closed port needed).
/// </summary>
sealed class KokoroVoicesStubServer : IAsyncDisposable
{
    readonly WebApplication app;
    int callCount;

    /// <summary>The base URI the stub is listening on (e.g. <c>http://127.0.0.1:12345</c>).</summary>
    public Uri BaseUri { get; }

    /// <summary>Number of <c>GET /v1/audio/voices</c> requests served so far.</summary>
    public int CallCount => callCount;

    KokoroVoicesStubServer(WebApplication app, Uri baseUri)
    {
        this.app = app;
        BaseUri = baseUri;
    }

    /// <summary>
    /// Builds, wires, and starts the stub server, returning canned <paramref name="voiceIds"/> from
    /// every request. Returns once the server is ready to accept connections.
    /// </summary>
    public static async Task<KokoroVoicesStubServer> StartAsync(IReadOnlyList<string> voiceIds)
    {
        var builder = WebApplication.CreateSlimBuilder();

        // Suppress all hosting noise in test output.
        builder.Logging.ClearProviders();

        // Bind to port 0 so the OS assigns a free port.
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();

        // Placeholder — replaced below once we have the server reference (mirrors
        // KokoroStubServer's null-guard idiom rather than a null-forgiving capture).
        KokoroVoicesStubServer? serverRef = null;

        app.MapGet("/v1/audio/voices", (HttpContext ctx) =>
        {
            var server = serverRef;
            if (server is null)
            {
                ctx.Response.StatusCode = 500;
                return Task.CompletedTask;
            }

            Interlocked.Increment(ref server.callCount);
            return ctx.Response.WriteAsJsonAsync(new { voices = voiceIds }, ctx.RequestAborted);
        });

        await app.StartAsync();

        var uri = new Uri(app.Urls.First());
        var server = new KokoroVoicesStubServer(app, uri);
        serverRef = server;
        return server;
    }

    public async ValueTask DisposeAsync() => await app.DisposeAsync();
}

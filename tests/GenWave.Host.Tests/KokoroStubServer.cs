using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GenWave.Host.Tests;

/// <summary>
/// Behaviour modes for the <see cref="KokoroStubServer"/>. Switch at runtime via
/// <see cref="KokoroStubServer.Mode"/> to simulate different failure / recovery states.
/// </summary>
enum KokoroStubMode
{
    /// <summary>Every request returns HTTP 500 Internal Server Error.</summary>
    Fail500,

    /// <summary>Every request sleeps longer than any configured render budget before responding.</summary>
    DelayPastBudget,

    /// <summary>Every request returns a minimal valid WAV file as a 200 response.</summary>
    ServeCannedWav,
}

/// <summary>
/// Minimal Kestrel-backed stub for the Kokoro TTS HTTP API. Listens on a random loopback port;
/// <see cref="BaseUri"/> is available immediately after construction. Dispose to shut down.
/// Implements <see cref="IAsyncDisposable"/> so tests can use <c>await using</c>.
/// </summary>
sealed class KokoroStubServer : IAsyncDisposable
{
    readonly WebApplication app;

    /// <summary>Thread-safe mode switch — tests flip this to simulate state transitions.</summary>
    public volatile KokoroStubMode Mode;

    /// <summary>The base URI the stub is listening on (e.g. <c>http://127.0.0.1:12345</c>).</summary>
    public Uri BaseUri { get; }

    KokoroStubServer(WebApplication app, Uri baseUri, KokoroStubMode initial)
    {
        this.app = app;
        BaseUri = baseUri;
        Mode = initial;
    }

    /// <summary>
    /// Builds, wires, and starts the stub server. Returns once the server is ready to accept
    /// connections so the caller can immediately construct an <see cref="HttpClient"/> against it.
    /// </summary>
    public static async Task<KokoroStubServer> StartAsync(
        KokoroStubMode initial = KokoroStubMode.ServeCannedWav)
    {
        var builder = WebApplication.CreateSlimBuilder();

        // Suppress all hosting noise in test output.
        builder.Logging.ClearProviders();

        // Bind to port 0 so the OS assigns a free port.
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();

        // Placeholder — replaced below once we have the server reference.
        KokoroStubServer? serverRef = null;

        app.MapPost("/v1/audio/speech", async (HttpContext ctx) =>
        {
            // serverRef is set before any request arrives (StartAsync awaits app.StartAsync).
            var server = serverRef;
            if (server is null)
            {
                ctx.Response.StatusCode = 500;
                return;
            }

            switch (server.Mode)
            {
                case KokoroStubMode.Fail500:
                    ctx.Response.StatusCode = 500;
                    return;

                case KokoroStubMode.DelayPastBudget:
                    // Delay longer than any test's render budget (tests use 200ms–2s budgets).
                    await Task.Delay(TimeSpan.FromSeconds(30), ctx.RequestAborted);
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "audio/wav";
                    await ctx.Response.Body.WriteAsync(CreateMinimalWav(), ctx.RequestAborted);
                    return;

                case KokoroStubMode.ServeCannedWav:
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "audio/wav";
                    await ctx.Response.Body.WriteAsync(CreateMinimalWav(), ctx.RequestAborted);
                    return;
            }
        });

        await app.StartAsync();

        // Resolve the actual bound port from the server features.
        var addresses = app.Urls;
        var uri = new Uri(addresses.First());

        var server = new KokoroStubServer(app, uri, initial);
        serverRef = server;
        return server;
    }

    public async ValueTask DisposeAsync() => await app.DisposeAsync();

    /// <summary>Minimal valid WAV: 44-byte RIFF header, zero PCM samples.</summary>
    static byte[] CreateMinimalWav()
    {
        var bytes = new byte[44];
        bytes[0] = (byte)'R'; bytes[1] = (byte)'I'; bytes[2] = (byte)'F'; bytes[3] = (byte)'F';
        WriteInt32LE(bytes, 4, 36);
        bytes[8] = (byte)'W'; bytes[9] = (byte)'A'; bytes[10] = (byte)'V'; bytes[11] = (byte)'E';
        bytes[12] = (byte)'f'; bytes[13] = (byte)'m'; bytes[14] = (byte)'t'; bytes[15] = (byte)' ';
        WriteInt32LE(bytes, 16, 16);
        WriteInt16LE(bytes, 20, 1);
        WriteInt16LE(bytes, 22, 1);
        WriteInt32LE(bytes, 24, 44100);
        WriteInt32LE(bytes, 28, 88200);
        WriteInt16LE(bytes, 32, 2);
        WriteInt16LE(bytes, 34, 16);
        bytes[36] = (byte)'d'; bytes[37] = (byte)'a'; bytes[38] = (byte)'t'; bytes[39] = (byte)'a';
        WriteInt32LE(bytes, 40, 0);
        return bytes;
    }

    static void WriteInt32LE(byte[] buf, int offset, int value)
    {
        buf[offset]     = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8)  & 0xFF);
        buf[offset + 2] = (byte)((value >> 16) & 0xFF);
        buf[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    static void WriteInt16LE(byte[] buf, int offset, short value)
    {
        buf[offset]     = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
    }
}

namespace GenWave.Tts.Tests;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

/// <summary>
/// Minimal Kestrel-backed stub standing in for <c>piper.http_server</c>'s one route (T148 review
/// finding F1) — same shape as <see cref="KokoroStubServer"/>, tailored to Piper's own wire shape
/// (<c>POST /</c>, plain text body, WAV bytes back) rather than Kokoro's JSON
/// <c>POST /v1/audio/speech</c>. Both <see cref="PiperTtsSynthesizer"/> (a fallback hop) and
/// <see cref="PiperPrimaryTtsSynthesizer"/> (F99.4's primary-engine seam) speak this exact shape
/// via the shared <see cref="PiperWireProtocol"/>, so one stub serves specs for either caller.
/// </summary>
sealed class PiperStubServer : IAsyncDisposable
{
    readonly WebApplication app;

    /// <summary>The base URI the stub is listening on (e.g. <c>http://127.0.0.1:12345</c>).</summary>
    public Uri BaseUri { get; }

    /// <summary>Number of <c>POST /</c> requests served so far.</summary>
    public int CallCount { get; private set; }

    /// <summary>The most recently received request body — the whole point of this stub: proving
    /// what did (or did not) go out on the wire (SPEC F99.4's "no per-request voice selector").</summary>
    public string? LastBody { get; private set; }

    /// <summary>The most recently received request's raw query string (empty when none) — pins
    /// that a caller's voice never rides along as a query parameter either.</summary>
    public string? LastQueryString { get; private set; }

    /// <summary>The most recently received request's Content-Type media type.</summary>
    public string? LastContentType { get; private set; }

    PiperStubServer(WebApplication app, Uri baseUri)
    {
        this.app = app;
        BaseUri = baseUri;
    }

    /// <summary>
    /// Builds, wires, and starts the stub server. Returns once ready to accept connections, same
    /// contract as <see cref="KokoroStubServer.StartAsync"/>.
    /// </summary>
    public static async Task<PiperStubServer> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();

        // Placeholder — replaced below once we have the server reference (KokoroStubServer's own
        // null-guard idiom, rather than a null-forgiving capture).
        PiperStubServer? serverRef = null;

        app.MapPost("/", async (HttpContext ctx) =>
        {
            var server = serverRef;
            if (server is null)
            {
                ctx.Response.StatusCode = 500;
                return;
            }

            server.CallCount++;
            server.LastQueryString = ctx.Request.QueryString.Value ?? "";
            server.LastContentType = ctx.Request.ContentType is { } raw
                ? raw.Split(';')[0].Trim()
                : null;
            using var reader = new StreamReader(ctx.Request.Body);
            server.LastBody = await reader.ReadToEndAsync(ctx.RequestAborted);

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "audio/wav";
            await ctx.Response.Body.WriteAsync(CreateMinimalWav(), ctx.RequestAborted);
        });

        await app.StartAsync();

        var uri = new Uri(app.Urls.First());
        var server = new PiperStubServer(app, uri);
        serverRef = server;
        return server;
    }

    public async ValueTask DisposeAsync() => await app.DisposeAsync();

    /// <summary>Minimal valid WAV: 44-byte RIFF header, zero PCM samples — byte-identical to
    /// <see cref="KokoroStubServer"/>'s own fixture.</summary>
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

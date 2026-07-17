namespace GenWave.Tts.Tests;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

/// <summary>
/// Minimal Kestrel-backed stub standing in for Kokoro's <c>POST /v1/audio/speech</c> and
/// <c>GET /v1/audio/voices</c> (STORY-124) — both routes live behind the SAME <c>Tts:Endpoint</c>
/// in production, so a single stub instance is "one endpoint" for repoint tests exactly like the
/// real Kokoro container is. Mirrors the house precedent, <c>GenWave.Host.Tests.KokoroStubServer</c>
/// / <c>KokoroVoicesStubServer</c>, folded into one type since these specs repoint BOTH calls
/// together.
///
/// <paramref name="voiceIds"/> null (the default) means "a listing-less endpoint" (SPEC F29.5):
/// the voices route is never mapped at all, so a GET against it 404s — the same shape an older
/// Kokoro build without voice-listing support would produce.
/// </summary>
sealed class KokoroStubServer : IAsyncDisposable
{
    readonly WebApplication app;

    /// <summary>The base URI the stub is listening on (e.g. <c>http://127.0.0.1:12345</c>).</summary>
    public Uri BaseUri { get; }

    /// <summary>Number of <c>POST /v1/audio/speech</c> requests served so far.</summary>
    public int SpeechCallCount { get; private set; }

    /// <summary>Number of <c>GET /v1/audio/voices</c> requests served so far.</summary>
    public int VoicesCallCount { get; private set; }

    KokoroStubServer(WebApplication app, Uri baseUri)
    {
        this.app = app;
        BaseUri = baseUri;
    }

    /// <summary>
    /// Builds, wires, and starts the stub server. Returns once the server is ready to accept
    /// connections so the caller can immediately point an options monitor's <c>Endpoint</c> at
    /// <see cref="BaseUri"/>.
    /// </summary>
    public static async Task<KokoroStubServer> StartAsync(IReadOnlyList<string>? voiceIds = null)
    {
        var builder = WebApplication.CreateSlimBuilder();

        // Suppress all hosting noise in test output.
        builder.Logging.ClearProviders();

        // Bind to port 0 so the OS assigns a free port.
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();

        // Placeholder — replaced below once we have the server reference (mirrors
        // MockCompletionsServer's null-guard idiom rather than a null-forgiving capture).
        KokoroStubServer? serverRef = null;

        app.MapPost("/v1/audio/speech", async (HttpContext ctx) =>
        {
            var server = serverRef;
            if (server is null)
            {
                ctx.Response.StatusCode = 500;
                return;
            }

            server.SpeechCallCount++;
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "audio/wav";
            await ctx.Response.Body.WriteAsync(CreateMinimalWav(), ctx.RequestAborted);
        });

        // A null voiceIds list means "listing-less endpoint" (F29.5) — the route is simply never
        // mapped, so Kestrel's own 404 is what a client sees, exactly like an unsupported upstream.
        if (voiceIds is not null)
        {
            app.MapGet("/v1/audio/voices", (HttpContext ctx) =>
            {
                var server = serverRef;
                if (server is null)
                {
                    ctx.Response.StatusCode = 500;
                    return Task.CompletedTask;
                }

                server.VoicesCallCount++;
                return ctx.Response.WriteAsJsonAsync(new { voices = voiceIds }, ctx.RequestAborted);
            });
        }

        await app.StartAsync();

        var uri = new Uri(app.Urls.First());
        var server = new KokoroStubServer(app, uri);
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

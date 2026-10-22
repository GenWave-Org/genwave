using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GenWave.VoiceOff.Tests.Support;

/// <summary>A slim Kestrel server on a free port that answers the speech contract: <c>POST /v1/audio/speech</c> and <c>GET /v1/audio/voices</c>.</summary>
internal sealed class EngineStubServer : IAsyncDisposable
{
    internal static readonly byte[] Audio = [0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4];

    private readonly WebApplication app;

    private EngineStubServer(WebApplication app, Uri baseUri, Capture capture)
    {
        this.app = app;
        BaseUri = baseUri;
        this.Requests = capture;
    }

    internal sealed class Capture
    {
        internal string? SpeechBody { get; set; }
    }

    internal Uri BaseUri { get; }

    internal Capture Requests { get; }

    /// <param name="speechDelay">How long the speech route waits before answering.</param>
    /// <param name="speechStatus">The status the speech route answers with.</param>
    /// <param name="speechBody">The body the speech route answers with; defaults to <see cref="Audio"/> on 200.</param>
    /// <param name="voicesJson">The voices route body; null leaves the route unmapped (404).</param>
    internal static async Task<EngineStubServer> StartAsync(
        TimeSpan speechDelay = default,
        int speechStatus = 200,
        byte[]? speechBody = null,
        string? voicesJson = """{"voices":["a","b"]}""",
        bool dropMidBody = false)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        WebApplication app = builder.Build();
        Capture capture = new();
        byte[] body = speechBody ?? (speechStatus == 200 ? Audio : []);

        app.MapPost("/v1/audio/speech", async (HttpContext ctx) =>
        {
            using StreamReader reader = new(ctx.Request.Body);
            capture.SpeechBody = await reader.ReadToEndAsync(ctx.RequestAborted);
            if (speechDelay > TimeSpan.Zero)
            {
                await Task.Delay(speechDelay, ctx.RequestAborted);
            }

            if (dropMidBody)
            {
                await DropMidBodyAsync(ctx);
                return;
            }

            ctx.Response.StatusCode = speechStatus;
            await ctx.Response.Body.WriteAsync(body, ctx.RequestAborted);
        });

        if (voicesJson is not null)
        {
            app.MapGet("/v1/audio/voices", async (HttpContext ctx) =>
            {
                if (dropMidBody)
                {
                    await DropMidBodyAsync(ctx);
                    return;
                }

                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(voicesJson, ctx.RequestAborted);
            });
        }

        await app.StartAsync();
        return new EngineStubServer(app, new Uri(app.Urls.First()), capture);
    }

    // Promises 100 bytes, sends 10, then kills the connection.
    private static async Task DropMidBodyAsync(HttpContext ctx)
    {
        ctx.Response.ContentLength = 100;
        await ctx.Response.Body.WriteAsync(new byte[10], ctx.RequestAborted);
        await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
        ctx.Abort();
    }

    public async ValueTask DisposeAsync() => await app.DisposeAsync();
}

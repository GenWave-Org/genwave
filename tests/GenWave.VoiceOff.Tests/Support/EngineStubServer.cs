using System.Text.Json.Nodes;
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
        private readonly Lock gate = new();
        private readonly List<string> speechBodies = [];
        private int speechRequests;
        private int totalRequests;

        /// <summary>The body of the latest speech request.</summary>
        internal string? SpeechBody { get; private set; }

        /// <summary>Every speech request body, in arrival order.</summary>
        internal IReadOnlyList<string> SpeechBodies
        {
            get
            {
                lock (gate)
                {
                    return [.. speechBodies];
                }
            }
        }

        /// <summary>The <c>voice</c> of every speech request, in arrival order.</summary>
        internal IReadOnlyList<string?> SpeechVoices => SpeechField("voice");

        /// <summary>The <c>input</c> text of every speech request, in arrival order.</summary>
        internal IReadOnlyList<string?> SpeechInputs => SpeechField("input");

        /// <summary>The <c>speed</c> of every speech request, in arrival order.</summary>
        internal IReadOnlyList<double?> SpeechSpeeds =>
            SpeechBodies.Select(b => JsonNode.Parse(b)?["speed"]?.GetValue<double>()).ToList();

        /// <summary>How many speech requests have arrived.</summary>
        internal int SpeechRequests => Volatile.Read(ref speechRequests);

        /// <summary>How many requests of any kind (any path) have arrived.</summary>
        internal int TotalRequests => Volatile.Read(ref totalRequests);

        internal void CountRequest() => Interlocked.Increment(ref totalRequests);

        internal void RecordSpeech(string body)
        {
            lock (gate)
            {
                speechBodies.Add(body);
                SpeechBody = body;
            }

            Interlocked.Increment(ref speechRequests);
        }

        private List<string?> SpeechField(string name) =>
            SpeechBodies.Select(b => JsonNode.Parse(b)?[name]?.GetValue<string>()).ToList();
    }

    internal Uri BaseUri { get; }

    internal Capture Requests { get; }

    /// <param name="speechDelay">How long the speech route waits before answering.</param>
    /// <param name="speechStatus">The status the speech route answers with.</param>
    /// <param name="speechBody">The body the speech route answers with; defaults to <see cref="Audio"/> on 200.</param>
    /// <param name="speechBodyForVoice">Answers a 200 with the body for the requested voice instead of <paramref name="speechBody"/>.</param>
    /// <param name="failSpeechRequest">The 1-based speech request that answers 500 whatever <paramref name="speechStatus"/> says.</param>
    /// <param name="voicesDelay">How long the voices route waits before answering.</param>
    /// <param name="voicesJson">The voices route body; null leaves the route unmapped (404).</param>
    internal static async Task<EngineStubServer> StartAsync(
        TimeSpan speechDelay = default,
        int speechStatus = 200,
        byte[]? speechBody = null,
        string? voicesJson = """{"voices":["a","b"]}""",
        bool dropMidBody = false,
        Func<string, byte[]>? speechBodyForVoice = null,
        int? failSpeechRequest = null,
        TimeSpan voicesDelay = default)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        WebApplication app = builder.Build();
        Capture capture = new();
        byte[] body = speechBody ?? (speechStatus == 200 ? Audio : []);

        app.Use(async (ctx, next) =>
        {
            capture.CountRequest();
            await next(ctx);
        });

        app.MapPost("/v1/audio/speech", async (HttpContext ctx) =>
        {
            using StreamReader reader = new(ctx.Request.Body);
            string requestBody = await reader.ReadToEndAsync(ctx.RequestAborted);
            capture.RecordSpeech(requestBody);
            if (speechDelay > TimeSpan.Zero)
            {
                await Task.Delay(speechDelay, ctx.RequestAborted);
            }

            if (dropMidBody)
            {
                await DropMidBodyAsync(ctx);
                return;
            }

            int status = failSpeechRequest == capture.SpeechRequests ? 500 : speechStatus;
            ctx.Response.StatusCode = status;
            string? voice = JsonNode.Parse(requestBody)?["voice"]?.GetValue<string>();
            byte[] answer = speechBodyForVoice is not null && voice is not null && status == 200 ? speechBodyForVoice(voice) : body;
            await ctx.Response.Body.WriteAsync(answer, ctx.RequestAborted);
        });

        if (voicesJson is not null)
        {
            app.MapGet("/v1/audio/voices", async (HttpContext ctx) =>
            {
                if (voicesDelay > TimeSpan.Zero)
                {
                    await Task.Delay(voicesDelay, ctx.RequestAborted);
                }

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

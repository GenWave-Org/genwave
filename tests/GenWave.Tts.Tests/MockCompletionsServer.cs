namespace GenWave.Tts.Tests;

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

/// <summary>Behaviour modes for <see cref="MockCompletionsServer"/>.</summary>
enum MockCompletionsMode
{
    /// <summary>Every request returns 200 with <see cref="MockCompletionsServer.ReplyContent"/>.</summary>
    Serve,

    /// <summary>Every request returns <see cref="MockCompletionsServer.FailStatusCode"/>.</summary>
    Fail,

    /// <summary>Every request sleeps 30s (past any test's Llm:TimeoutSeconds) before serving 200.</summary>
    Delay,
}

/// <summary>
/// Minimal Kestrel-backed stub for an OpenAI-compatible <c>POST /v1/chat/completions</c> endpoint
/// (STORY-119). Mirrors the house precedent, <c>GenWave.Host.Tests.KokoroStubServer</c>. Listens
/// on a random loopback port; <see cref="BaseUri"/> is available immediately after <see cref="StartAsync"/>.
/// Every request is logged verbatim (<see cref="Requests"/>) so specs can assert zero-HTTP for
/// templated kinds and inspect the prompt contract (system/user content, model, bearer header).
/// </summary>
sealed class MockCompletionsServer : IAsyncDisposable
{
    readonly WebApplication app;
    readonly ConcurrentQueue<CapturedCompletionRequest> requests = new();

    /// <summary>Thread-safe mode switch — tests flip this to simulate the fallback ladder.</summary>
    public volatile MockCompletionsMode Mode;

    /// <summary>Status code served in <see cref="MockCompletionsMode.Fail"/>. Default 500.</summary>
    public volatile int FailStatusCode = 500;

    /// <summary>Completion text served in <see cref="MockCompletionsMode.Serve"/>/<see cref="MockCompletionsMode.Delay"/>.</summary>
    public volatile string ReplyContent = "Great tune coming up, stay tuned.";

    /// <summary>The base URI the stub is listening on (e.g. <c>http://127.0.0.1:12345</c>).</summary>
    public Uri BaseUri { get; }

    /// <summary>Every request received so far, in arrival order.</summary>
    public IReadOnlyList<CapturedCompletionRequest> Requests => requests.ToArray();

    /// <summary>Number of requests received so far — the zero-HTTP assertion for templated kinds.</summary>
    public int RequestCount => requests.Count;

    MockCompletionsServer(WebApplication app, Uri baseUri, MockCompletionsMode initial)
    {
        this.app = app;
        BaseUri = baseUri;
        Mode = initial;
    }

    /// <summary>
    /// Builds, wires, and starts the stub server. Returns once the server is ready to accept
    /// connections so the caller can immediately construct an <see cref="HttpClient"/> against it.
    /// </summary>
    public static async Task<MockCompletionsServer> StartAsync(MockCompletionsMode initial = MockCompletionsMode.Serve)
    {
        var builder = WebApplication.CreateSlimBuilder();

        // Suppress all hosting noise in test output.
        builder.Logging.ClearProviders();

        // Bind to port 0 so the OS assigns a free port.
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();

        // Placeholder — replaced below once we have the server reference.
        MockCompletionsServer? serverRef = null;

        app.MapPost("/v1/chat/completions", async (HttpContext ctx) =>
        {
            var server = serverRef;
            if (server is null)
            {
                ctx.Response.StatusCode = 500;
                return;
            }

            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync(ctx.RequestAborted);
            var authHeader = ctx.Request.Headers.Authorization.ToString();
            server.requests.Enqueue(new CapturedCompletionRequest(body, authHeader));

            switch (server.Mode)
            {
                case MockCompletionsMode.Fail:
                    ctx.Response.StatusCode = server.FailStatusCode;
                    return;

                case MockCompletionsMode.Delay:
                    // Longer than any test's Llm:TimeoutSeconds (tests use 1-2s budgets).
                    await Task.Delay(TimeSpan.FromSeconds(30), ctx.RequestAborted);
                    await WriteReplyAsync(ctx, server.ReplyContent);
                    return;

                case MockCompletionsMode.Serve:
                default:
                    await WriteReplyAsync(ctx, server.ReplyContent);
                    return;
            }
        });

        await app.StartAsync();

        var uri = new Uri(app.Urls.First());
        var server = new MockCompletionsServer(app, uri, initial);
        serverRef = server;
        return server;
    }

    public async ValueTask DisposeAsync() => await app.DisposeAsync();

    static Task WriteReplyAsync(HttpContext ctx, string content)
    {
        ctx.Response.StatusCode = 200;
        return ctx.Response.WriteAsJsonAsync(
            new { choices = new[] { new { message = new { content } } } },
            ctx.RequestAborted);
    }
}

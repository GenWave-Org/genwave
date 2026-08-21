// Extracted from Story196_LlmCallInspector.cs and Story353_LlmCauseTaxonomy.cs (T334 review round
// 1, advisory a): both files carried their own verbatim ~90-line "a Kestrel-backed OpenAI-
// compatible completions stub, plus a WebApplicationFactory<Program> that boots the real host
// against it" — a `file`-scoped type genuinely cannot cross files, but a normal internal type in
// the test project's own Support/ folder can (mirrors CrosstalkWorkerHarness.cs's own identical
// precedent one file over: "T335 needs it a third time next task"). Both spec files now call these
// shared types instead of keeping their own copy.
//
// T335 (STORY-350/351/353, SPEC F138.2/F138.4/F138.5) extended this minimally, additively, for the
// wire-proof spec: QueueReplies scripts a reply PER CALL NUMBER (the re-ask ladder fires a SECOND
// completions call inside one WriteAsync — a scenario proving "poisoned, then clean" needs the stub
// itself to answer those two calls differently), and Requests captures each call's parsed
// system/user prompt so a fact can assert the F138.5 guard line rode the real wire body, not just
// that some text arrived. Neither addition changes a single existing caller: both default to
// "nothing queued, ReplyContent answers every call" / "captured but never read", exactly the shape
// Story196/Story353 already exercise unchanged.

using System.Text.Json;
using GenWave.Host;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GenWave.Host.Tests.Support;

/// <summary>One <c>POST /v1/chat/completions</c> request <see cref="LlmCompletionsStub"/> parsed off
/// the wire (T335) — the "system"/"user" message content only (the two roles every real caller in
/// this codebase sends, LlmCopyWriter/CrosstalkScriptWriter alike), so a fact can assert on the
/// SAME F138.5 guard line the production prompt builders append, read back through a real HTTP
/// round-trip rather than a hand-rolled capturing handler.</summary>
internal sealed record CapturedCompletionsRequest(string SystemPrompt, string UserPrompt);

/// <summary>
/// Minimal Kestrel-backed stub for an OpenAI-compatible <c>POST /v1/chat/completions</c> endpoint
/// — mirrors <c>GenWave.Tts.Tests.MockCompletionsServer</c> (STORY-119) in shape, redefined here
/// since this test project has no reference to that test project (the "redefine, don't
/// cross-reference across test PROJECTS" convention Story186_CorrectionsObservability's own header
/// note explains — this is that same posture applied within ONE test project's shared Support/
/// folder instead of per spec-file duplication). Every request serves 200 with either the next
/// <see cref="QueueReplies"/> entry (call-sequenced) or, once that queue is empty, plain
/// <see cref="ReplyContent"/> — callers needing the fuller Serve/Fail/Delay repertoire should reach
/// for <c>GenWave.Tts.Tests</c>' own original instead of extending this one.
/// </summary>
internal sealed class LlmCompletionsStub : IAsyncDisposable
{
    readonly WebApplication app;
    readonly object gate = new();
    readonly Queue<string> queuedReplies = new();
    readonly List<CapturedCompletionsRequest> requests = [];

    public string ReplyContent { get; set; } = "Great tune coming up, stay tuned.";
    public Uri BaseUri { get; }

    /// <summary>Every request this stub has served so far, in call order (T335) — see
    /// <see cref="CapturedCompletionsRequest"/>'s own remarks.</summary>
    public IReadOnlyList<CapturedCompletionsRequest> Requests
    {
        get
        {
            lock (gate)
                return requests.ToArray();
        }
    }

    LlmCompletionsStub(WebApplication app, Uri baseUri)
    {
        this.app = app;
        BaseUri = baseUri;
    }

    /// <summary>Scripts the reply for the NEXT calls, in order — the Nth queued reply answers the
    /// Nth request from this point on (T335, SPEC F138.4's re-ask ladder: a scenario proving the
    /// gate re-asks queues "poisoned, then clean" so the SAME stub instance answers both legs of one
    /// render differently). Once exhausted, every further request falls back to
    /// <see cref="ReplyContent"/> unchanged — the pre-T335 behavior every existing caller relies on.</summary>
    public void QueueReplies(params string[] contents)
    {
        lock (gate)
        {
            foreach (var content in contents)
                queuedReplies.Enqueue(content);
        }
    }

    public static async Task<LlmCompletionsStub> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();
        LlmCompletionsStub? stubRef = null;

        app.MapPost("/v1/chat/completions", async (HttpContext ctx) =>
        {
            var stub = stubRef;
            if (stub is null)
            {
                ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
                return;
            }

            var payload = await JsonSerializer.DeserializeAsync<JsonElement>(
                ctx.Request.Body, cancellationToken: ctx.RequestAborted);
            var (systemPrompt, userPrompt) = ExtractPrompts(payload);

            string reply;
            lock (stub.gate)
            {
                reply = stub.queuedReplies.Count > 0 ? stub.queuedReplies.Dequeue() : stub.ReplyContent;
                stub.requests.Add(new CapturedCompletionsRequest(systemPrompt, userPrompt));
            }

            ctx.Response.StatusCode = StatusCodes.Status200OK;
            await ctx.Response.WriteAsJsonAsync(
                new { choices = new[] { new { message = new { content = reply } } } },
                ctx.RequestAborted);
        });

        await app.StartAsync();
        var stub = new LlmCompletionsStub(app, new Uri(app.Urls.First()));
        stubRef = stub;
        return stub;
    }

    /// <summary>Pulls the "system"/"user" message content out of an OpenAI-shaped chat-completions
    /// request body (T335) — the same <c>messages: [{ role, content }, …]</c> shape every real
    /// caller here (LlmCopyWriter, CrosstalkScriptWriter) sends. Missing/malformed fields default to
    /// "" rather than throwing — a capture helper must never be the reason a scenario's real request
    /// fails.</summary>
    static (string SystemPrompt, string UserPrompt) ExtractPrompts(JsonElement payload)
    {
        var systemPrompt = "";
        var userPrompt = "";

        if (payload.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
        {
            foreach (var message in messages.EnumerateArray())
            {
                var role = message.TryGetProperty("role", out var roleProp) ? roleProp.GetString() : null;
                var content = message.TryGetProperty("content", out var contentProp) ? contentProp.GetString() ?? "" : "";

                if (role == "system")
                    systemPrompt = content;
                else if (role == "user")
                    userPrompt = content;
            }
        }

        return (systemPrompt, userPrompt);
    }

    public async ValueTask DisposeAsync() => await app.DisposeAsync();
}

/// <summary>
/// Boots the real host with a real <c>Llm:Endpoint</c> (a genuine <see cref="LlmCompletionsStub"/>)
/// so <c>LlmCopyWriter</c>/<c>LlmCallRing</c>/<c>LlmCallCauseCounters</c> are the exact production
/// singletons <c>AddGenWaveTts</c> wires — nothing about the LLM pipeline is faked. Only hosted
/// services are removed (no Liquidsoap/Postgres background work during a test); every
/// Postgres-backed controller dependency a caller's own render might touch (e.g.
/// <c>PersonaController</c>) is left as its REAL, Lazy-backed registration, since a draft-fields
/// preview never forces any of them to actually connect (see Story196_LlmCallInspector.cs's own
/// original header note for the full rationale this extraction carries forward unchanged).
/// <c>Llm:Model</c> is fixed to <see cref="Model"/> for every caller — no fact so far has needed a
/// second value, so this stays a constant rather than a second constructor parameter.
/// </summary>
internal sealed class LlmCompletionsWebFactory(string llmEndpoint) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-llm-completions";
    internal const string Model = "test-model";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Llm:Endpoint", llmEndpoint);
        builder.UseSetting("Llm:Model", Model);
        builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
    }
}

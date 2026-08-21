// Extracted from Story196_LlmCallInspector.cs and Story353_LlmCauseTaxonomy.cs (T334 review round
// 1, advisory a): both files carried their own verbatim ~90-line "a Kestrel-backed OpenAI-
// compatible completions stub, plus a WebApplicationFactory<Program> that boots the real host
// against it" — a `file`-scoped type genuinely cannot cross files, but a normal internal type in
// the test project's own Support/ folder can (mirrors CrosstalkWorkerHarness.cs's own identical
// precedent one file over: "T335 needs it a third time next task"). Both spec files now call these
// shared types instead of keeping their own copy.

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

/// <summary>
/// Minimal Kestrel-backed stub for an OpenAI-compatible <c>POST /v1/chat/completions</c> endpoint
/// — mirrors <c>GenWave.Tts.Tests.MockCompletionsServer</c> (STORY-119) in shape, redefined here
/// since this test project has no reference to that test project (the "redefine, don't
/// cross-reference across test PROJECTS" convention Story186_CorrectionsObservability's own header
/// note explains — this is that same posture applied within ONE test project's shared Support/
/// folder instead of per spec-file duplication). Every request always serves 200 with
/// <see cref="ReplyContent"/> — callers needing the fuller Serve/Fail/Delay repertoire should reach
/// for <c>GenWave.Tts.Tests</c>' own original instead of extending this one.
/// </summary>
internal sealed class LlmCompletionsStub : IAsyncDisposable
{
    readonly WebApplication app;

    public string ReplyContent { get; set; } = "Great tune coming up, stay tuned.";
    public Uri BaseUri { get; }

    LlmCompletionsStub(WebApplication app, Uri baseUri)
    {
        this.app = app;
        BaseUri = baseUri;
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

            ctx.Response.StatusCode = StatusCodes.Status200OK;
            await ctx.Response.WriteAsJsonAsync(
                new { choices = new[] { new { message = new { content = stub.ReplyContent } } } },
                ctx.RequestAborted);
        });

        await app.StartAsync();
        var stub = new LlmCompletionsStub(app, new Uri(app.Urls.First()));
        stubRef = stub;
        return stub;
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

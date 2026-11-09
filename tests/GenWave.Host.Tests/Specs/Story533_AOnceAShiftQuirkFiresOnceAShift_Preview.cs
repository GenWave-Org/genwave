// STORY-533 — A once-a-shift quirk fires once a shift (gh-#293 gh-#920 · SPEC F218 · PLAN T686)
//
// The real admin endpoint (POST /api/personas/preview) through WebApplicationFactory<Program>: real
// controller, real LlmCopyWriter, real QuirkSelector; only the LLM HTTP handler and the spend ledger
// (a recording fake instead of Postgres) are swapped. A spy decorates the selector so the fact is
// not vacuous: it proves the preview reached the selector with spend off. The preview structurally
// loads no card (WritePreviewAsync passes card: null), so no rule can fire here; the on-air
// counterpart (rule card, shift key, spend row written) lives in the Tts Writer spec.
//
// Spans four files; see tests/GenWave.Core.Tests/Specs/Story533_AOnceAShiftQuirkFiresOnceAShift_Card.cs for the map.

using System.Net;
using System.Net.Http.Json;
using System.Text;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Memory;
using GenWave.Tts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace GenWave.Host.Tests.Specs;

file sealed class RecordingSpendLedger : IQuirkSpendLedger
{
    public List<string> Spends { get; } = [];

    public Task<int> CountSpendsAsync(long personaId, string content, CancellationToken ct) =>
        Task.FromResult(Spends.Count(s => s == content));

    public Task RecordSpendAsync(long personaId, string content, CancellationToken ct)
    {
        Spends.Add(content);
        return Task.CompletedTask;
    }
}

/// <summary>Delegates to the real selector and remembers the spend flag of every call.</summary>
file sealed class SpyQuirkSelector(IQuirkSelector inner) : IQuirkSelector
{
    public List<bool> SpendFlags { get; } = [];

    public Task<QuirkSelection> SelectAsync(
        long? personaId, PersonaCard? card, ShiftKey? shiftKey, bool spend, CancellationToken ct)
    {
        SpendFlags.Add(spend);
        return inner.SelectAsync(personaId, card, shiftKey, spend, ct);
    }
}

file sealed class StubLlmHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"choices":[{"message":{"content":"Here is a short preview line for the listeners."}}]}""",
                Encoding.UTF8, "application/json"),
        });
}

file sealed class QuirkPreviewWebFactory : WebApplicationFactory<Program>
{
    internal const string AdminPassword = "test-password-x7z";

    internal RecordingSpendLedger Ledger { get; } = new();
    internal SpyQuirkSelector? Spy { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", AdminPassword);
        builder.UseSetting("Llm:Endpoint", "http://llm.test");
        builder.UseSetting("Llm:Model", "stub");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.AddHttpClient(LlmCopyWriter.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new StubLlmHandler());
            services.RemoveAll<IQuirkSpendLedger>();
            services.AddSingleton<IQuirkSpendLedger>(Ledger);
            services.RemoveAll<IQuirkSelector>();
            services.AddSingleton<IQuirkSelector>(sp =>
                Spy = new SpyQuirkSelector(new QuirkSelector(sp.GetRequiredService<IQuirkSpendLedger>())));
        });
    }
}

public static class FeatureAOnceAShiftQuirkFiresOnceAShiftPreview
{
    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC7 — preview never spends
    // entry point: WebApplicationFactory<Program> admin persona prompt-preview endpoint
    public sealed class ScenarioPreviewNeverSpends
    {
        // Given the admin preview endpoint
        // When  it builds a prompt for a persona with a maxPerShift quirk

        [Fact]
        public async Task NoSpendRowIsWritten()
        {
            await using var factory = new QuirkPreviewWebFactory();
            var client = factory.CreateClient();
            await PreviewAsync(client);

            Assert.Empty(factory.Ledger.Spends);
        }

        [Fact]
        public async Task ThePreviewReachedTheSelectorWithSpendOff()
        {
            await using var factory = new QuirkPreviewWebFactory();
            await PreviewAsync(factory.CreateClient());

            Assert.Equal([false], factory.Spy?.SpendFlags);
        }

        static async Task PreviewAsync(HttpClient client)
        {
            var login = await client.PostAsJsonAsync("/api/auth/login", new { password = QuirkPreviewWebFactory.AdminPassword });
            if (login.StatusCode != HttpStatusCode.NoContent)
                throw new InvalidOperationException($"Login failed: {login.StatusCode}");

            var response = await client.PostAsJsonAsync(
                "/api/personas/preview",
                new { kind = "LeadIn", name = "DJ Nova", backstory = "A night owl." });
            if (response.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"Preview failed: {response.StatusCode}");
        }
    }
}

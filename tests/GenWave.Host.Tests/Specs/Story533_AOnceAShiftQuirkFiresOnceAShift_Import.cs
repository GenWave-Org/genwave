// STORY-533 — A once-a-shift quirk fires once a shift (gh-#293 gh-#920 · SPEC F218 · PLAN T683)
//
// The import-boundary check for PersonaCard.QuirkRules (SPEC F218.5), through the real
// POST /api/personas/{slug}/import route: real routing/auth/content-negotiation pipeline,
// IPersonaImportStore/ITtsVoiceLister replaced by tiny fakes (no live Postgres). The card map for the
// rest of the story is in tests/GenWave.Core.Tests/Specs/Story533_AOnceAShiftQuirkFiresOnceAShift_Card.cs.

using System.Net;
using System.Net.Http.Json;
using System.Text;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace GenWave.Host.Tests.Specs;

file sealed class AcceptingImportStore : IPersonaImportStore
{
    public Task<PersonaImportOutcome> ImportAsync(PersonaImportRequest request, CancellationToken ct) =>
        Task.FromResult<PersonaImportOutcome>(new PersonaImportOutcome.Imported(1, WasCreated: true));
}

file sealed class KnownVoiceLister : ITtsVoiceLister
{
    public Task<IReadOnlyList<string>> ListVoicesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(["af_heart"]);
}

file sealed class QuirkImportWebFactory : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-x7z";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IPersonaImportStore>();
            services.AddSingleton<IPersonaImportStore, AcceptingImportStore>();
            services.RemoveAll<ITtsVoiceLister>();
            services.AddSingleton<ITtsVoiceLister, KnownVoiceLister>();
        });
    }
}

public static class FeatureAOnceAShiftQuirkFiresOnceAShiftImport
{
    const string Pun = "signs off with a pun";

    static string CardJson(string? quirksJson, string rulesJson) =>
        "{\"schemaVersion\":1,\"name\":\"DJ Nova\",\"tagline\":\"Late-night frequencies.\"," +
        "\"soul\":\"Chases pirate signals.\"," +
        (quirksJson is null ? "" : $"\"quirks\":{quirksJson},") +
        "\"voice\":{\"engine\":\"kokoro\",\"voiceId\":\"af_heart\",\"pace\":1.0,\"language\":\"en\"}," +
        "\"energyDisposition\":0.4,\"lore\":[],\"corrections\":[]," +
        $"\"quirkRules\":{rulesJson}}}";

    static async Task<HttpResponseMessage> ImportAsync(string cardJson)
    {
        await using var factory = new QuirkImportWebFactory();
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = QuirkImportWebFactory.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        var response = await client.PostAsync(
            "/api/personas/dj-nova/import", new StringContent(cardJson, Encoding.UTF8, "application/json"));
        // Buffer before the factory (and its server) is disposed.
        await response.Content.LoadIntoBufferAsync();
        return response;
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioAValidCardWithRulesImports
    {
        // Given a card whose quirkRules all appear in quirks and sit inside their ranges
        // When  it is POSTed to the import route

        [Fact]
        public async Task ItIsCreated()
        {
            var response = await ImportAsync(CardJson(
                $"[\"hums between sentences\",\"{Pun}\"]",
                $"[{{\"text\":\"{Pun}\",\"maxPerShift\":1,\"perBreakChance\":0.15}}]"));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class SadPathAnInvalidRuleIsRefused
    {
        [Fact]
        public async Task ARuleTextNotInQuirksIs400()
        {
            var response = await ImportAsync(CardJson("[\"hums between sentences\"]", $"[{{\"text\":\"{Pun}\"}}]"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task AMaxPerShiftOfZeroIs400()
        {
            var response = await ImportAsync(CardJson($"[\"{Pun}\"]", $"[{{\"text\":\"{Pun}\",\"maxPerShift\":0}}]"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task APerBreakChanceOfZeroIs400()
        {
            var response = await ImportAsync(CardJson($"[\"{Pun}\"]", $"[{{\"text\":\"{Pun}\",\"perBreakChance\":0}}]"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task APerBreakChanceAboveOneIs400()
        {
            var response = await ImportAsync(CardJson($"[\"{Pun}\"]", $"[{{\"text\":\"{Pun}\",\"perBreakChance\":1.5}}]"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task RulesWithNoQuirksListIs400NotA500()
        {
            var response = await ImportAsync(CardJson(quirksJson: null, $"[{{\"text\":\"{Pun}\"}}]"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}

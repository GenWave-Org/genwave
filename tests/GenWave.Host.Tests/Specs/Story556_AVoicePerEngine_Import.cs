// STORY-556 — Every speaker keeps a voice per engine, the import half (SPEC F219.7 · PLAN T751)
//
// The import-boundary check for PersonaCard.Voices, through the real POST /api/personas/{slug}/import
// route (same harness as Story533_..._Import: tiny fakes, no live Postgres). The card map is in
// tests/GenWave.Core.Tests/Specs/Story556_AVoicePerEngine_Card.cs.

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

file sealed class VoicesAcceptingImportStore : IPersonaImportStore
{
    public Task<PersonaImportOutcome> ImportAsync(PersonaImportRequest request, CancellationToken ct) =>
        Task.FromResult<PersonaImportOutcome>(new PersonaImportOutcome.Imported(1, WasCreated: true));
}

file sealed class VoicesKnownVoiceLister : ITtsVoiceLister
{
    public Task<IReadOnlyList<string>> ListVoicesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(["af_heart"]);
}

file sealed class VoicesImportWebFactory : WebApplicationFactory<Program>
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
            services.AddSingleton<IPersonaImportStore, VoicesAcceptingImportStore>();
            services.RemoveAll<ITtsVoiceLister>();
            services.AddSingleton<ITtsVoiceLister, VoicesKnownVoiceLister>();
        });
    }
}

public static class FeatureAVoicePerEngineImport
{
    static string CardJson(string voicesJson) =>
        "{\"schemaVersion\":1,\"name\":\"DJ Nova\",\"tagline\":\"\",\"soul\":\"\",\"quirks\":[]," +
        "\"voice\":{\"engine\":\"kokoro\",\"voiceId\":\"af_heart\",\"pace\":1.0,\"language\":\"en\"}," +
        "\"energyDisposition\":0.4,\"lore\":[],\"corrections\":[]," +
        $"\"voices\":{voicesJson}}}";

    static async Task<HttpResponseMessage> ImportAsync(string cardJson)
    {
        await using var factory = new VoicesImportWebFactory();
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = VoicesImportWebFactory.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        var response = await client.PostAsync(
            "/api/personas/dj-nova/import", new StringContent(cardJson, Encoding.UTF8, "application/json"));
        await response.Content.LoadIntoBufferAsync();
        return response;
    }

    public sealed class ScenarioAMapThatAgreesWithVoiceImports
    {
        [Fact]
        public async Task ItIsCreated()
        {
            var response = await ImportAsync(CardJson("{\"kokoro\":\"af_heart\",\"chatterbox\":\"ref\"}"));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
    }

    public sealed class SadPathADisagreeingMapIsRefused
    {
        [Fact]
        public async Task AMapThatDisagreesWithVoiceIs400()
        {
            var response = await ImportAsync(CardJson("{\"kokoro\":\"am_adam\"}"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task AMapWithoutTheVoiceEngineIs400()
        {
            var response = await ImportAsync(CardJson("{\"chatterbox\":\"ref\"}"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task AnEmptyMapIs400()
        {
            var response = await ImportAsync(CardJson("{}"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task ANullVoiceIdIs400()
        {
            var response = await ImportAsync(CardJson("{\"kokoro\":\"af_heart\",\"chatterbox\":null}"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task ABlankVoiceIdIs400()
        {
            var response = await ImportAsync(CardJson("{\"kokoro\":\"af_heart\",\"chatterbox\":\" \"}"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task ABlankEngineKeyIs400()
        {
            var response = await ImportAsync(CardJson("{\"kokoro\":\"af_heart\",\" \":\"ref\"}"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}

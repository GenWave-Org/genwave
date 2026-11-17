// STORY-552 — Asked and aired on the Memory tab, the api half (gh-#1001 · SPEC F218.40–F218.51 · PLAN slice 3)
//
// BDD specification — xUnit. GET api/personas/{id}/memory through the test host; the row copy is admin-ui/__specs__/personas-memory-asked-aired.spec.tsx.
// Every fact is pending at plan time (2026-11-16); the named task activates its facts, watches them fail,
// then makes them pass.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAskedAndAiredOnTheMemoryTab
{
    // AC1 — the API
    public sealed class ScenarioTheApi
    {
        // Given a memory with 4 offers in the last 28 days and aired count 1

        [Fact]
        public async Task TheRowCarriesAsked4AndAired1()
        {
            await using var world = new AskedWorld();
            world.Memory.Seed(AskedWorld.Sal, MemoryKinds.Lore, "authored", "grew up above a bakery", airedCount: 1, askedCount: 4);
            var client = await world.LoggedInClientAsync();

            var rows = (await client.GetFromJsonAsync<JsonElement>($"/api/personas/{AskedWorld.Sal}/memory")).EnumerateArray().ToList();

            var row = Assert.Single(rows);
            Assert.Equal(4, row.GetProperty("askedCount").GetInt32());
            Assert.Equal(1, row.GetProperty("airedCount").GetInt32());
        }
    }
}

/// <summary>The real pipeline with in-memory persona and memory stores and no hosted services.</summary>
file sealed class AskedWorld : WebApplicationFactory<Program>
{
    internal const long Sal = 7;
    const string Password = "test-password-story552-asked";

    public FakePersonaMemoryAdmin Memory { get; } = new();

    readonly FakePersonaStore personas = SeedPersonas();

    static FakePersonaStore SeedPersonas()
    {
        var store = new FakePersonaStore();
        store.Seed(new Persona(Sal, "Sal", "", "", "", DateTime.UtcNow, DateTime.UtcNow));
        return store;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IPersonaStore>();
            services.AddSingleton<IPersonaStore>(personas);
            services.RemoveAll<IPersonaMemoryAdmin>();
            services.AddSingleton<IPersonaMemoryAdmin>(Memory);
        });
    }

    public async Task<HttpClient> LoggedInClientAsync()
    {
        var client = CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }
}

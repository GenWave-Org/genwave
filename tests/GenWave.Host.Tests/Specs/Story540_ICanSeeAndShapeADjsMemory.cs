// STORY-540 — I can see and shape a DJ's memory (gh-#293 gh-#920 · SPEC F218 · PLAN T706)
//
// BDD specification — xUnit. AC1, AC2, AC5 here; AC3/AC4 are Jest: admin-ui/__specs__/persona-memory-section.spec.tsx.
// Every fact drives the real HTTP pipeline (WebApplicationFactory<Program>, real routing + auth + controller) with an
// in-memory IPersonaStore and IPersonaMemoryAdmin; the SQL behind the seam is pinned in MediaLibrary.Tests
// (Story540_MemoryAdminSql.cs).

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

public static class FeatureICanSeeAndShapeADjsMemory
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — List by kind
    public sealed class ScenarioListByKind
    {
        // entry point: WebApplicationFactory<Program> GET api/personas/{id}/memory
        // Given rows of kinds lore, episode and life
        // When  GET api/personas/{id}/memory

        [Fact]
        public async Task EachRowCarriesKindSourceTextAiredCountPendingAndForgottenFlags()
        {
            await using var world = new MemoryWorld();
            world.Memory.Seed(MemoryWorld.Sal, MemoryKinds.Lore, "authored", "grew up above a bakery", airedCount: 2);
            world.Memory.Seed(MemoryWorld.Sal, MemoryKinds.Lore, "drafted", "once met a bear");
            world.Memory.Seed(MemoryWorld.Sal, MemoryKinds.Episode, "accrued", "the van broke down");
            var forgotten = world.Memory.Seed(MemoryWorld.Sal, MemoryKinds.Life, "extracted", "has a beagle named Gus");
            await world.Memory.UpdateAsync(MemoryWorld.Sal, forgotten, new PersonaMemoryChange(null, false, true), CancellationToken.None);
            world.Memory.Seed(MemoryWorld.Other, MemoryKinds.Life, "extracted", "belongs to somebody else");
            var client = await world.LoggedInClientAsync();

            var response = await client.GetAsync($"/api/personas/{MemoryWorld.Sal}/memory");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var rows = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
            Assert.Equal(4, rows.Count);
            Assert.Equal("episode,life,lore,lore", string.Join(",", rows.Select(r => r.GetProperty("kind").GetString()).Order()));

            var authored = rows.Single(r => r.GetProperty("text").GetString() == "grew up above a bakery");
            Assert.Equal("authored", authored.GetProperty("source").GetString());
            Assert.Equal(2, authored.GetProperty("airedCount").GetInt32());
            Assert.False(authored.GetProperty("pending").GetBoolean());
            Assert.False(authored.GetProperty("forgotten").GetBoolean());

            Assert.True(rows.Single(r => r.GetProperty("text").GetString() == "once met a bear").GetProperty("pending").GetBoolean());
            Assert.True(rows.Single(r => r.GetProperty("text").GetString() == "has a beagle named Gus").GetProperty("forgotten").GetBoolean());
        }

        [Fact]
        public async Task TheKindFilterNarrowsTheList()
        {
            await using var world = new MemoryWorld();
            world.Memory.Seed(MemoryWorld.Sal, MemoryKinds.Lore, "authored", "grew up above a bakery");
            world.Memory.Seed(MemoryWorld.Sal, MemoryKinds.Life, "extracted", "has a beagle named Gus");
            var client = await world.LoggedInClientAsync();

            var rows = (await client.GetFromJsonAsync<JsonElement>($"/api/personas/{MemoryWorld.Sal}/memory?kind=life")).EnumerateArray().ToList();

            Assert.Equal("has a beagle named Gus", Assert.Single(rows).GetProperty("text").GetString());
        }
    }

    // AC2 — The actions
    public sealed class ScenarioTheActions
    {
        // Given a listed row
        // When  PATCH text / PATCH approved / PATCH forgotten / DELETE

        [Fact]
        public async Task TheRowSTextChanges()
        {
            await using var world = new MemoryWorld();
            var row = world.Memory.Seed(MemoryWorld.Sal, MemoryKinds.Lore, "authored", "old line");
            var client = await world.LoggedInClientAsync();

            var response = await client.PatchAsJsonAsync($"/api/personas/{MemoryWorld.Sal}/memory/{row}", new { text = "  new line  " });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("new line", world.Memory.Find(MemoryWorld.Sal, row)?.Content);
        }

        [Fact]
        public async Task ApprovedAtIsSet()
        {
            await using var world = new MemoryWorld();
            var row = world.Memory.Seed(MemoryWorld.Sal, MemoryKinds.Lore, "drafted", "once met a bear");
            var client = await world.LoggedInClientAsync();

            var response = await client.PatchAsJsonAsync($"/api/personas/{MemoryWorld.Sal}/memory/{row}", new { approved = true });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(world.Memory.Find(MemoryWorld.Sal, row)?.ApprovedAt);
            Assert.False((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("pending").GetBoolean());
        }

        [Fact]
        public async Task ApprovingANonDraftedRowIsRejectedAndTheTextIsNotWritten()
        {
            await using var world = new MemoryWorld();
            var row = world.Memory.Seed(MemoryWorld.Sal, MemoryKinds.Lore, "authored", "old line");
            var client = await world.LoggedInClientAsync();

            var response = await client.PatchAsJsonAsync($"/api/personas/{MemoryWorld.Sal}/memory/{row}", new { text = "new line", approved = true });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Only a drafted row can be approved.", await response.Content.ReadAsStringAsync());
            var stored = world.Memory.Find(MemoryWorld.Sal, row);
            Assert.Equal("old line", stored?.Content);
            Assert.Null(stored?.ApprovedAt);
        }

        [Fact]
        public async Task ForgottenAtIsSet()
        {
            await using var world = new MemoryWorld();
            var row = world.Memory.Seed(MemoryWorld.Sal, MemoryKinds.Life, "extracted", "has a beagle named Gus");
            var client = await world.LoggedInClientAsync();

            var response = await client.PatchAsJsonAsync($"/api/personas/{MemoryWorld.Sal}/memory/{row}", new { forgotten = true });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(world.Memory.Find(MemoryWorld.Sal, row)?.ForgottenAt);

            await client.PatchAsJsonAsync($"/api/personas/{MemoryWorld.Sal}/memory/{row}", new { forgotten = false });
            Assert.Null(world.Memory.Find(MemoryWorld.Sal, row)?.ForgottenAt);
        }

        [Fact]
        public async Task TheRowIsGone()
        {
            await using var world = new MemoryWorld();
            var row = world.Memory.Seed(MemoryWorld.Sal, MemoryKinds.Episode, "accrued", "the van broke down");
            var client = await world.LoggedInClientAsync();

            var response = await client.DeleteAsync($"/api/personas/{MemoryWorld.Sal}/memory/{row}");

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Null(world.Memory.Find(MemoryWorld.Sal, row));
        }

        [Fact]
        public async Task AuthoredLoreIsAdded()
        {
            await using var world = new MemoryWorld();
            var client = await world.LoggedInClientAsync();

            var response = await client.PostAsJsonAsync($"/api/personas/{MemoryWorld.Sal}/memory", new { text = "  grew up above a bakery " });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var added = Assert.Single(await world.Memory.ListForAdminAsync(MemoryWorld.Sal, "lore", CancellationToken.None));
            Assert.Equal("grew up above a bakery", added.Content);
            Assert.Equal("authored", added.Source);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task BlankTextIsRejected(string text)
        {
            await using var world = new MemoryWorld();
            var row = world.Memory.Seed(MemoryWorld.Sal, MemoryKinds.Lore, "authored", "keep me");
            var client = await world.LoggedInClientAsync();

            var post = await client.PostAsJsonAsync($"/api/personas/{MemoryWorld.Sal}/memory", new { text });
            var patch = await client.PatchAsJsonAsync($"/api/personas/{MemoryWorld.Sal}/memory/{row}", new { text });

            Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);
            Assert.Equal("keep me", world.Memory.Find(MemoryWorld.Sal, row)?.Content);
        }

        [Fact]
        public async Task OverLongTextIsRejected()
        {
            await using var world = new MemoryWorld();
            var client = await world.LoggedInClientAsync();

            var response = await client.PostAsJsonAsync($"/api/personas/{MemoryWorld.Sal}/memory", new { text = new string('x', 201) });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await world.Memory.ListForAdminAsync(MemoryWorld.Sal, null, CancellationToken.None));
        }

        [Fact]
        public async Task ARowOfAnotherPersonaCannotBeTouched()
        {
            await using var world = new MemoryWorld();
            var theirs = world.Memory.Seed(MemoryWorld.Other, MemoryKinds.Lore, "authored", "not yours");
            var client = await world.LoggedInClientAsync();

            var patch = await client.PatchAsJsonAsync($"/api/personas/{MemoryWorld.Sal}/memory/{theirs}", new { text = "stolen" });
            var delete = await client.DeleteAsync($"/api/personas/{MemoryWorld.Sal}/memory/{theirs}");
            var unknownPersona = await client.GetAsync("/api/personas/999/memory");

            Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, unknownPersona.StatusCode);
            Assert.Equal("not yours", world.Memory.Find(MemoryWorld.Other, theirs)?.Content);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC5 — No public surface
    public sealed class ScenarioNoPublicSurface
    {
        // Given the spectator role
        // When  GET api/personas/{id}/memory

        // FINDING: the spectator (public, session-less) caller is turned away with 401, not the 403 the story's wording
        // says — the same AdminOnly gate every admin route has (Story195's precedent). Nothing is readable either way.
        [Fact]
        public async Task TheResponseIs401NotAdmitted()
        {
            await using var world = new MemoryWorld(spectatorMode: true);
            world.Memory.Seed(MemoryWorld.Sal, MemoryKinds.Lore, "authored", "grew up above a bakery");
            var client = world.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.GetAsync($"/api/personas/{MemoryWorld.Sal}/memory");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.DoesNotContain("bakery", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task EveryMemoryRouteIsAdminPlaneAndNeverASpectatorSurface()
        {
            await using var world = new MemoryWorld(spectatorMode: true);

            var endpoints = world.Services.GetRequiredService<EndpointDataSource>().Endpoints
                .OfType<RouteEndpoint>()
                .Where(e => e.RoutePattern.RawText?.StartsWith("api/personas/{id:long}/memory", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            Assert.Equal(4, endpoints.Count);
            foreach (var endpoint in endpoints)
            {
                var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).ToList();
                Assert.Contains(policies, p => AuthorizationPolicies.AdminPlanePolicies.Contains(p));
                Assert.DoesNotContain(AuthorizationPolicies.Spectator, policies);
                Assert.Null(endpoint.Metadata.GetMetadata<SpectatorSurfaceAttribute>());
                Assert.NotNull(endpoint.Metadata.GetMetadata<AdminSurfaceAttribute>());
            }
        }
    }
}

/// <summary>The real pipeline with in-memory persona and memory stores and no hosted services.</summary>
file sealed class MemoryWorld(bool spectatorMode = false) : WebApplicationFactory<Program>
{
    internal const long Sal = 7;
    internal const long Other = 8;
    const string Password = "test-password-story540-memory";

    public FakePersonaMemoryAdmin Memory { get; } = new();

    readonly FakePersonaStore personas = SeedPersonas();

    static FakePersonaStore SeedPersonas()
    {
        var store = new FakePersonaStore();
        foreach (var (id, name) in new[] { (Sal, "Sal"), (Other, "Other") })
            store.Seed(new Persona(id, name, "", "", "", DateTime.UtcNow, DateTime.UtcNow));
        return store;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        if (spectatorMode)
            builder.UseSetting("Station:SpectatorMode", "true");

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

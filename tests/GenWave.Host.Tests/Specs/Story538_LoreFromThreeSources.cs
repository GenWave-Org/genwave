// STORY-538 — Lore from three sources (gh-#293 gh-#920 · SPEC F218 · PLAN T705)
//
// BDD specification — xUnit. AC2–AC4 (HTTP half) here; AC1 and the recall-eligibility half of AC4 are SQL facts in tests/GenWave.MediaLibrary.Tests/Specs/Story538_LoreFromThreeSourcesSql.cs; AC5 in tests/GenWave.Memory.Tests/Specs/Story538_PendingLoreIsSilent.cs.
// Every fact drives the real HTTP pipeline (WebApplicationFactory<Program>) with an in-memory IPersonaStore,
// IPersonaMemoryAdmin and a fake language model.

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
using GenWave.Memory;

namespace GenWave.Host.Tests.Specs;

public static class FeatureLoreFromThreeSources
{
    const string FiveLines = "Grew up above a bakery.\nOnce met a bear.\nCollects broken radios.\nHates Tuesdays.\nWon a pie contest.";

    // AC2 — I add one
    public sealed class ScenarioIAddOne
    {
        // entry point: WebApplicationFactory<Program> POST api/personas/{id}/memory

        [Fact]
        public async Task ARowKindLoreSourceAuthoredExists()
        {
            await using var world = new LoreWorld(FiveLines);
            var client = await world.LoggedInClientAsync();

            var response = await client.PostAsJsonAsync($"/api/personas/{LoreWorld.Sal}/memory", new { text = "Lost a coin toss." });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var row = Assert.Single(await world.Memory.ListForAdminAsync(LoreWorld.Sal, MemoryKinds.Lore, CancellationToken.None));
            Assert.Equal("Lost a coin toss.", row.Content);
            Assert.Equal(MemorySources.Authored, row.Source);
        }
    }

    // AC3 — The LLM drafts five
    public sealed class ScenarioTheLLMDraftsFive
    {
        // entry point: WebApplicationFactory<Program> POST api/personas/{id}/memory/suggest

        [Fact]
        public async Task FiveRowsKindLoreSourceDraftedApprovedAtNullExist()
        {
            await using var world = new LoreWorld(FiveLines);
            var client = await world.LoggedInClientAsync();

            var response = await client.PostAsync($"/api/personas/{LoreWorld.Sal}/memory/suggest", null);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var rows = await world.Memory.ListForAdminAsync(LoreWorld.Sal, MemoryKinds.Lore, CancellationToken.None);
            Assert.Equal(5, rows.Count);
            Assert.All(rows, r =>
            {
                Assert.Equal(MemorySources.Drafted, r.Source);
                Assert.Null(r.ApprovedAt);
            });
            var body = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
            Assert.Equal(5, body.Count);
            Assert.All(body, r => Assert.True(r.GetProperty("pending").GetBoolean()));
        }

        [Fact]
        public async Task ALineTheDjAlreadyHasIsNotDraftedAgainAndTheReplyIsCappedAtFive()
        {
            await using var world = new LoreWorld("- 1. Once met a bear!\n\"Grew up above a bakery.\"\nA\nB\nC\nD\nE\nF");
            world.Memory.Seed(LoreWorld.Sal, MemoryKinds.Lore, MemorySources.Authored, "once met a bear");
            var client = await world.LoggedInClientAsync();

            var response = await client.PostAsync($"/api/personas/{LoreWorld.Sal}/memory/suggest", null);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var drafted = (await world.Memory.ListForAdminAsync(LoreWorld.Sal, MemoryKinds.Lore, CancellationToken.None))
                .Where(r => r.Source == MemorySources.Drafted).Select(r => r.Content).Reverse().ToList();
            Assert.Equal(["Grew up above a bakery.", "A", "B", "C", "D"], drafted);
        }

        [Fact]
        public async Task NoModelSetUpIs409AndStoresNothing()
        {
            await using var world = new LoreWorld(null);
            var client = await world.LoggedInClientAsync();

            var response = await client.PostAsync($"/api/personas/{LoreWorld.Sal}/memory/suggest", null);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("No language model is set up.", await response.Content.ReadAsStringAsync());
            Assert.Empty(await world.Memory.ListForAdminAsync(LoreWorld.Sal, null, CancellationToken.None));
        }

        [Fact]
        public async Task AModelFaultIs502AndStoresNothing()
        {
            await using var world = new LoreWorld(FiveLines, fault: true);
            var client = await world.LoggedInClientAsync();

            var response = await client.PostAsync($"/api/personas/{LoreWorld.Sal}/memory/suggest", null);

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.Contains("The language model did not answer.", await response.Content.ReadAsStringAsync());
            Assert.Empty(await world.Memory.ListForAdminAsync(LoreWorld.Sal, null, CancellationToken.None));
        }

        [Fact]
        public async Task AReplyWithNothingUsableIs502()
        {
            await using var world = new LoreWorld("\n   \n```\n```");
            var client = await world.LoggedInClientAsync();

            var response = await client.PostAsync($"/api/personas/{LoreWorld.Sal}/memory/suggest", null);

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.Contains("The language model gave no usable lore.", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task AnUnknownPersonaIs404()
        {
            await using var world = new LoreWorld(FiveLines);
            var client = await world.LoggedInClientAsync();

            var response = await client.PostAsync("/api/personas/999/memory/suggest", null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task ThePromptCarriesTheBackstoryAndTheLoreAlreadyKnown()
        {
            await using var world = new LoreWorld(FiveLines);
            world.Memory.Seed(LoreWorld.Sal, MemoryKinds.Lore, MemorySources.Authored, "keeps bees");
            var client = await world.LoggedInClientAsync();

            await client.PostAsync($"/api/personas/{LoreWorld.Sal}/memory/suggest", null);

            var prompt = Assert.Single(world.Llm.UserPrompts);
            Assert.Contains("Raised on a ferry.", prompt);
            Assert.Contains("keeps bees", prompt);
        }
    }

    public sealed class ScenarioTheSoulIsThePromptSource
    {
        // entry point: WebApplicationFactory<Program> POST api/personas/{id}/memory/suggest
        // Imported and catalog personas have an empty backstory; their soul lives on the reconciled card.

        [Fact]
        public async Task AnEmptyBackstoryFallsBackToTheCardSoul()
        {
            await using var world = new LoreWorld(FiveLines, backstory: "", soul: "Dry-witted night owl from Lethbridge.");
            var client = await world.LoggedInClientAsync();

            var response = await client.PostAsync($"/api/personas/{LoreWorld.Sal}/memory/suggest", null);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var prompt = Assert.Single(world.Llm.UserPrompts);
            Assert.Contains("Dry-witted night owl from Lethbridge.", prompt);
        }

        [Fact]
        public async Task NoSoulAndNoBackstoryIs400AndTheModelIsNeverCalled()
        {
            await using var world = new LoreWorld(FiveLines, backstory: "");
            var client = await world.LoggedInClientAsync();

            var response = await client.PostAsync($"/api/personas/{LoreWorld.Sal}/memory/suggest", null);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(world.Llm.UserPrompts);
        }

        [Fact]
        public async Task OnlyTheNewestTwentyKnownLoreLinesReachThePrompt()
        {
            await using var world = new LoreWorld(FiveLines);
            for (var i = 0; i < 25; i++)
                world.Memory.Seed(LoreWorld.Sal, MemoryKinds.Lore, MemorySources.Authored, $"fact number {i}");
            var client = await world.LoggedInClientAsync();

            await client.PostAsync($"/api/personas/{LoreWorld.Sal}/memory/suggest", null);

            var prompt = Assert.Single(world.Llm.UserPrompts);
            var factLines = prompt.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("- fact number", StringComparison.Ordinal)).ToList();
            Assert.Equal(LoreDraftPrompt.MaxExistingLore, factLines.Count);
            Assert.Contains("- fact number 24", factLines);
            Assert.DoesNotContain("- fact number 4", factLines);
        }
    }

    public sealed class ScenarioTheReplyIsUntrusted
    {
        [Fact]
        public void APreambleLineAndNulCharactersAreDropped()
        {
            var lines = LoreReplyParser.Parse("Here are five facts:\nGrew up\0 above a bakery.\0\nKeeps bees.", []);

            Assert.Equal(["Grew up above a bakery.", "Keeps bees."], lines);
        }

        [Fact]
        public void ADecimalAtTheStartOfALineIsNotListNumbering()
        {
            var lines = LoreReplyParser.Parse("3.5 million listeners heard my first show.\n2) Keeps bees.\n1.Grew up above a bakery.", []);

            Assert.Equal(["3.5 million listeners heard my first show.", "Keeps bees.", "1.Grew up above a bakery."], lines);
        }
    }

    // AC4 — Approve makes it real
    public sealed class ScenarioApproveMakesItReal
    {
        // entry point: WebApplicationFactory<Program> PATCH api/personas/{id}/memory/{rowId}
        // Recall eligibility is a SQL rule: FeatureLoreFromThreeSourcesSql (MediaLibrary.Tests) pins it against Postgres.

        [Fact]
        public async Task ApprovedAtIsSet()
        {
            await using var world = new LoreWorld(FiveLines);
            var rowId = world.Memory.Seed(LoreWorld.Sal, MemoryKinds.Lore, MemorySources.Drafted, "once met a bear");
            var client = await world.LoggedInClientAsync();

            var response = await client.PatchAsJsonAsync($"/api/personas/{LoreWorld.Sal}/memory/{rowId}", new { approved = true });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var row = world.Memory.Find(LoreWorld.Sal, rowId);
            Assert.NotNull(row?.ApprovedAt);
            Assert.False(row.Pending);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(body.GetProperty("pending").GetBoolean());
        }
    }
}

file sealed class ScriptedLoreLlm(string? reply, bool fault) : ILifeClaimLlm
{
    public List<string> UserPrompts { get; } = [];

    public Task<string?> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        UserPrompts.Add(userPrompt);
        return fault ? throw new HttpRequestException("down") : Task.FromResult(reply);
    }
}

/// <summary>The real pipeline with in-memory persona and memory stores, a scripted model and no hosted services.</summary>
file sealed class LoreWorld(string? reply, bool fault = false, string backstory = "Raised on a ferry.", string? soul = null) : WebApplicationFactory<Program>
{
    internal const long Sal = 7;
    const string Password = "test-password-story538-lore";

    public FakePersonaMemoryAdmin Memory { get; } = new();
    public ScriptedLoreLlm Llm { get; } = new(reply, fault);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        var personas = new FakePersonaStore();
        personas.Seed(new Persona(Sal, "Sal", backstory, "", "", DateTime.UtcNow, DateTime.UtcNow));
        if (soul is not null)
            personas.SeedCard(Sal, new PersonaCard(1, "Sal", "", soul, [], new VoiceSpec("kokoro", "v", 1.0, "en"), 0.5, [], []));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IPersonaStore>();
            services.AddSingleton<IPersonaStore>(personas);
            services.RemoveAll<IPersonaMemoryAdmin>();
            services.AddSingleton<IPersonaMemoryAdmin>(Memory);
            services.RemoveAll<ILifeClaimLlm>();
            services.AddSingleton<ILifeClaimLlm>(Llm);
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

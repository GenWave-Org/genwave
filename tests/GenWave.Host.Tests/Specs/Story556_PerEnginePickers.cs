// STORY-556 — The DJ page has a voice picker per engine, the backend half (SPEC F219.10 · PLAN T754a)
//
// BDD specification — xUnit through the deployed entry points on the real host (WebApplicationFactory<Program>,
// real DI; only the engine transport and the persona store are stand-ins): GET /api/voices?engine=, POST
// /api/tts/preview with an engine, and PATCH /api/personas/{id} with voices. Kokoro is the active engine in
// every scenario; chatterbox is in the registry and is the engine being asked for.

namespace GenWave.Host.Tests.Specs;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// A persona store that keeps one DJ in memory and folds edits into its card with the same lockstep rule the real store uses.
file sealed class OneDjStore : IPersonaStore
{
    public const long Id = 5;

    Persona row = new(Id, "DJ Nova", "", "", "af_nova", DateTime.UtcNow, DateTime.UtcNow);
    PersonaCard card = new(
        PersonaCard.CurrentSchemaVersion, "DJ Nova", "", "", [], new VoiceSpec("", "af_nova", 1.0, "en"), 0, [], []);

    public Task<IReadOnlyList<Persona>> GetAllAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Persona>>([row]);

    public Task<Persona?> GetByIdAsync(long id, CancellationToken ct) => Task.FromResult<Persona?>(id == Id ? row : null);

    public Task<PersonaWriteResult> CreateAsync(PersonaDraft draft, CancellationToken ct) =>
        Task.FromResult<PersonaWriteResult>(new PersonaWriteResult.Created(row));

    public Task<PersonaWriteResult> UpdateAsync(long id, PersonaDraft draft, CancellationToken ct)
    {
        row = row with { Name = draft.Name, Voice = draft.Voice };
        card = PersonaVoiceLockstep.Apply(card.WithVoice(card.Voice with { VoiceId = draft.Voice }), draft);
        return Task.FromResult<PersonaWriteResult>(new PersonaWriteResult.Updated(row));
    }

    public Task<PersonaWriteResult> DeleteAsync(long id, CancellationToken ct) =>
        Task.FromResult<PersonaWriteResult>(new PersonaWriteResult.NotFound());

    public Task<PersonaCard?> GetCardByIdAsync(long id, CancellationToken ct) => Task.FromResult<PersonaCard?>(card);

    public Task<long?> GetIdBySlugAsync(string slug, CancellationToken ct) => Task.FromResult<long?>(Id);

    public Task<IReadOnlyDictionary<long, PersonaCard>> GetCardsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<long, PersonaCard>>(new Dictionary<long, PersonaCard> { [Id] = card });
}

public static class FeatureAVoicePickerPerEngine
{
    static async Task<JsonElement> JsonBodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    // AC5 — the picker for an engine lists that engine's voices
    public sealed class ScenarioListingAnEnginesVoices : EngineSettingScenario
    {
        HttpResponseMessage chatterbox = new();
        HttpResponseMessage active = new();

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            chatterbox = await Client.GetAsync("/api/voices?engine=chatterbox");
            active = await Client.GetAsync("/api/voices");
        }

        [Fact]
        public void TheListingIs200() => Assert.Equal(HttpStatusCode.OK, chatterbox.StatusCode);

        [Fact]
        public async Task ItListsTheEnginesVoices() =>
            Assert.Equal(["reader-1", "reader-2"], (await JsonBodyAsync(chatterbox)).EnumerateArray().Select(v => v.GetString() ?? "").ToArray());

        [Fact]
        public void TheRequestWentToThatEngine() => Assert.Contains(EngineRequests, uri => uri.Host == "chatterbox");

        [Fact]
        public void NoEngineNamedIsTheActiveEnginesListAsBefore() => Assert.Equal(HttpStatusCode.OK, active.StatusCode);
    }

    // Sad paths — the listing refuses and degrades
    public sealed class ScenarioListingAnUnknownEngine : EngineSettingScenario
    {
        HttpResponseMessage response = new();

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            response = await Client.GetAsync("/api/voices?engine=nope");
        }

        [Fact]
        public void ItIs400() => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        [Fact]
        public void NoEngineIsAsked() => Assert.Empty(EngineRequests);
    }

    public sealed class ScenarioListingAnEngineThatIsDown : EngineSettingScenario
    {
        [Fact]
        public async Task ItIs502()
        {
            EnginesUnreachable = true;

            var response = await Client.GetAsync("/api/voices?engine=chatterbox");

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        }
    }

    // AC6 — "Hear it" on a picker renders on that engine, whichever engine is active
    public sealed class ScenarioHearingAVoiceOnAnotherEngine : EngineSettingScenario
    {
        HttpResponseMessage response = new();

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            response = await Client.PostAsJsonAsync(
                "/api/tts/preview", new { text = "Hello there.", voice = "reader-1", engine = "chatterbox" });
        }

        [Fact]
        public void ItReturnsAudio() => Assert.Equal("audio/wav", response.Content.Headers.ContentType?.MediaType);

        [Fact]
        public void TheRenderReachedThatEngine() => Assert.Equal("chatterbox", Assert.Single(EngineRequests).Host);

        [Fact]
        public void ItAskedForThatVoice() => Assert.Contains("reader-1", Assert.Single(EngineRequestBodies), StringComparison.Ordinal);
    }

    public sealed class ScenarioHearingAVoiceWithNoEngineNamed : EngineSettingScenario
    {
        [Fact]
        public async Task ItRendersOnTheActiveEngine()
        {
            await Client.PostAsJsonAsync("/api/tts/preview", new { text = "Hello there.", voice = "af_heart" });

            Assert.NotEqual("chatterbox", Assert.Single(EngineRequests).Host);
        }
    }

    // Sad paths — the preview refuses what it cannot voice, and says so when the engine is down
    public sealed class ScenarioHearingAVoiceOnAnUnknownEngine : EngineSettingScenario
    {
        HttpResponseMessage response = new();

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            response = await Client.PostAsJsonAsync("/api/tts/preview", new { text = "Hello.", voice = "reader-1", engine = "nope" });
        }

        [Fact]
        public void ItIs400() => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        [Fact]
        public void NoRenderIsSent() => Assert.Empty(EngineRequests);
    }

    public sealed class ScenarioHearingAnEngineMapOnANamedEngine : EngineSettingScenario
    {
        HttpResponseMessage response = new();

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            response = await Client.PostAsJsonAsync(
                "/api/tts/preview", new { text = "Hello.", voice = "{\"chatterbox\":\"reader-1\"}", engine = "chatterbox" });
        }

        [Fact]
        public void ItIs400() => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        [Fact]
        public void NoRenderIsSent() => Assert.Empty(EngineRequests);
    }

    public sealed class ScenarioHearingAVoiceOnAnEngineThatIsDown : EngineSettingScenario
    {
        [Fact]
        public async Task ItIs502AndNeverAnotherEnginesVoice()
        {
            EnginesUnreachable = true;

            var response = await Client.PostAsJsonAsync(
                "/api/tts/preview", new { text = "Hello.", voice = "reader-1", engine = "chatterbox" });

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        }
    }

    // AC6 — "Hear it" sounds like air: a rule beats a correction on the same word, on the named engine too
    public sealed class ScenarioHearingARuleAndACorrectionOnTheSameWord : EngineSettingScenario
    {
        const string Text = "Now playing: MacLeod.";

        async Task<string> PreviewBodyAsync(string? engine)
        {
            var saved = await Client.PutAsJsonAsync(
                "/api/settings", new[] { new { key = "Tts:Corrections", value = "[{\"from\":\"MacLeod\",\"to\":\"Maa-cloud\"}]" } });
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

            var response = await Client.PostAsJsonAsync("/api/tts/preview", new
            {
                text = Text,
                voice = "reader-1",
                engine,
                candidateRules = new[] { new { pattern = "MacLeod", word = "MacLeod", ipa = "mˈʌklˈoʊd" } },
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return Assert.Single(EngineRequestBodies);
        }

        [Fact]
        public async Task TheNamedEngineSendsTheRulesSpellingNotTheCorrection() =>
            Assert.DoesNotContain("Maa-cloud", await PreviewBodyAsync("chatterbox"), StringComparison.OrdinalIgnoreCase);

        [Fact]
        public async Task TheActiveEngineDoesTheSame() =>
            Assert.DoesNotContain("Maa-cloud", await PreviewBodyAsync(null), StringComparison.OrdinalIgnoreCase);
    }

    // AC5 — a DJ's voice per engine is saved with the DJ and read back
    public sealed class ScenarioSavingAVoicePerEngine : EngineSettingScenario
    {
        HttpResponseMessage patched = new();
        JsonElement listed;

        protected override void ConfigureServices(IServiceCollection services)
        {
            services.RemoveAll<IPersonaStore>();
            services.AddSingleton<IPersonaStore>(new OneDjStore());
        }

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            patched = await Client.PatchAsJsonAsync($"/api/personas/{OneDjStore.Id}", new
            {
                name = "DJ Nova",
                voice = "af_nova",
                voices = new Dictionary<string, string> { ["chatterbox"] = "reader-1" },
            });
            listed = await JsonBodyAsync(await Client.GetAsync("/api/personas"));
        }

        [Fact]
        public void TheSaveIs200() => Assert.Equal(HttpStatusCode.OK, patched.StatusCode);

        [Fact]
        public async Task TheSaveReturnsTheChatterboxVoice() =>
            Assert.Equal("reader-1", (await JsonBodyAsync(patched)).GetProperty("voices").GetProperty("chatterbox").GetString());

        [Fact]
        public void TheListReadsTheChatterboxVoiceBack() =>
            Assert.Equal("reader-1", listed[0].GetProperty("voices").GetProperty("chatterbox").GetString());

        [Fact]
        public void TheKokoroVoiceStaysInLockstepWithTheVoice() =>
            Assert.Equal("af_nova", listed[0].GetProperty("voices").GetProperty("kokoro").GetString());

        [Fact]
        public async Task ASaveWithoutVoicesLeavesTheMapAlone()
        {
            await Client.PatchAsJsonAsync($"/api/personas/{OneDjStore.Id}", new { name = "DJ Nova", voice = "af_nova" });

            var again = await JsonBodyAsync(await Client.GetAsync("/api/personas"));

            Assert.Equal("reader-1", again[0].GetProperty("voices").GetProperty("chatterbox").GetString());
        }
    }

    // A voice-only edit after a map save moves the kokoro entry with it; other engines survive
    public sealed class ScenarioAVoiceOnlyEditAfterAMapSave : EngineSettingScenario
    {
        JsonElement listed;

        protected override void ConfigureServices(IServiceCollection services)
        {
            services.RemoveAll<IPersonaStore>();
            services.AddSingleton<IPersonaStore>(new OneDjStore());
        }

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            await Client.PatchAsJsonAsync($"/api/personas/{OneDjStore.Id}", new
            {
                name = "DJ Nova",
                voice = "af_nova",
                voices = new Dictionary<string, string> { ["kokoro"] = "af_nova", ["chatterbox"] = "reader-1" },
            });
            await Client.PatchAsJsonAsync($"/api/personas/{OneDjStore.Id}", new { name = "DJ Nova", voice = "af_sky" });
            listed = await JsonBodyAsync(await Client.GetAsync("/api/personas"));
        }

        [Fact]
        public void TheKokoroEntryIsTheNewVoice() =>
            Assert.Equal("af_sky", listed[0].GetProperty("voices").GetProperty("kokoro").GetString());

        [Fact]
        public void TheChatterboxEntrySurvives() =>
            Assert.Equal("reader-1", listed[0].GetProperty("voices").GetProperty("chatterbox").GetString());
    }

    // Sad paths — what the persona write refuses
    public sealed class ScenarioSavingAnInvalidVoiceMap : EngineSettingScenario
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            services.RemoveAll<IPersonaStore>();
            services.AddSingleton<IPersonaStore>(new OneDjStore());
        }

        async Task<HttpStatusCode> PatchAsync(Dictionary<string, string> voices) =>
            (await Client.PatchAsJsonAsync($"/api/personas/{OneDjStore.Id}", new { name = "DJ Nova", voice = "af_nova", voices })).StatusCode;

        [Fact]
        public async Task AnEngineThatIsNotInTheRegistryIs400() =>
            Assert.Equal(HttpStatusCode.BadRequest, await PatchAsync(new() { ["nope"] = "reader-1" }));

        [Fact]
        public async Task ABlankVoiceIdIs400() =>
            Assert.Equal(HttpStatusCode.BadRequest, await PatchAsync(new() { ["chatterbox"] = " " }));

        [Fact]
        public async Task AnEngineNamedTwiceByCaseIs400() =>
            Assert.Equal(HttpStatusCode.BadRequest, await PatchAsync(new() { ["chatterbox"] = "a", ["Chatterbox"] = "b" }));

        [Fact]
        public async Task CreatingWithAnUnknownEngineIs400() =>
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (await Client.PostAsJsonAsync("/api/personas", new { name = "New DJ", voices = new Dictionary<string, string> { ["nope"] = "x" } })).StatusCode);
    }
}

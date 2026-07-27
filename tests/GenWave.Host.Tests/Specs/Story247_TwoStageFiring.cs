// STORY-247 — Two-stage firing with a parachute (SPEC F94.2, F91.9, PLAN T121/T128)
//
// BDD specification — xUnit. The Fire modal (export offer before Delete enables, cancel = no-op)
// is T128 browser acceptance per the T92 precedent. These facts pin the server contracts: the FK
// guard and benched-delete, driven through the real HTTP pipeline (WebApplicationFactory<Program>,
// real POST /api/auth/login, real DELETE/POST/GET /api/personas routes — mirrors
// Story251_ExplicitOverrideEndpoint.cs's idiom) with IPersonaStore replaced by ONE stateful fake
// (FakeBenchStore below, mirrors Story237_ImportProvenance.cs's FakePersonaStation) so a rejected
// or accepted delete's actual effect on the row — not just the HTTP status — is provable.
//
// ScenarioBenchingByUnpainting is NOT covered in this file: its own Given/When ("a slot is removed
// via PUT /api/schedule") names a wire endpoint T122 has not built yet, and proving "no longer
// appears in any schedule row" needs a real station.segment_schedule to query against, which this
// project has no Postgres fixture for (Story251's own remarks document this Host/MediaLibrary
// split). The repository-level equivalent — ScheduleRepository.ReplaceWeekAsync unpainting a slot,
// PersonaRepository proving the record survives — is Story247_BenchTransition.cs in
// GenWave.MediaLibrary.Tests (real Postgres, Story240_ScheduleStore.cs's own fixture family). The
// API-level round trip through GET/PUT /api/schedule re-pins here once T122/T129 land.

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
using GenWave.Host.Api;

namespace GenWave.Host.Tests.Specs;

// ── In-process fake ───────────────────────────────────────────────────────────────────────────────

/// <summary>Derives the same lowercase-hyphenated slug <see cref="FakeBenchStore.CreateAsync"/>
/// stamps for a new row, so a test can address <c>GET /api/personas/{slug}/export</c> without the
/// fake exposing a slug field on its own DTO.</summary>
file static class TestSlug
{
    public static string Of(string name) => name.ToLowerInvariant().Replace(' ', '-');
}

/// <summary>
/// Stateful <see cref="IPersonaStore"/> double — mirrors <c>Story237_ImportProvenance.cs</c>'s
/// <c>FakePersonaStation</c>: a real backing dictionary, not just a scriptable single result, because
/// this file's facts need to prove an actual effect (or the absence of one) on the row, not merely
/// the HTTP status of one call in isolation.
/// </summary>
file sealed class FakeBenchStore : IPersonaStore
{
    readonly Dictionary<long, Persona> byId = [];
    readonly Dictionary<string, long> idBySlug = new(StringComparer.Ordinal);
    readonly Dictionary<long, string> slugById = [];
    readonly Dictionary<long, PersonaCard> cardById = [];
    long nextId = 1;

    /// <summary>
    /// Scripts the NEXT <see cref="DeleteAsync"/> call's outcome WITHOUT touching the backing
    /// dictionaries — <c>ScenarioScheduledPersonasAreUndeletable</c>'s own device for proving "the row
    /// survives a rejected delete" against a real row (a subsequent list still contains it), rather
    /// than a delete result alone, which proves nothing about whether anything was actually removed.
    /// <see langword="null"/> (the default) means <see cref="DeleteAsync"/> runs its normal
    /// remove-or-NotFound behavior — <c>ScenarioDeletingFromTheBench</c>'s own path.
    /// </summary>
    public PersonaWriteResult? DeleteOverride { get; set; }

    public Task<IReadOnlyList<Persona>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Persona>>(byId.Values.OrderBy(p => p.Name, StringComparer.Ordinal).ToList());

    public Task<Persona?> GetByIdAsync(long id, CancellationToken ct) =>
        Task.FromResult(byId.TryGetValue(id, out var persona) ? persona : null);

    public Task<PersonaWriteResult> CreateAsync(PersonaDraft draft, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var id = nextId++;
        var slug = TestSlug.Of(draft.Name);
        var persona = new Persona(id, draft.Name, draft.Backstory, draft.Style, draft.Voice, now, now);

        byId[id] = persona;
        idBySlug[slug] = id;
        slugById[id] = slug;
        cardById[id] = new PersonaCard(
            PersonaCard.CurrentSchemaVersion, draft.Name, "Tagline", draft.Backstory, [],
            new VoiceSpec(Engine: "", VoiceId: "", Pace: 1.0, Language: "en"), 0, [], []);

        return Task.FromResult<PersonaWriteResult>(new PersonaWriteResult.Created(persona));
    }

    public Task<PersonaWriteResult> UpdateAsync(long id, PersonaDraft draft, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story247's scenarios.");

    public Task<PersonaWriteResult> DeleteAsync(long id, CancellationToken ct)
    {
        if (DeleteOverride is { } scripted)
            return Task.FromResult(scripted);

        if (!byId.Remove(id))
            return Task.FromResult<PersonaWriteResult>(new PersonaWriteResult.NotFound());

        if (slugById.Remove(id, out var slug))
            idBySlug.Remove(slug);
        cardById.Remove(id);

        return Task.FromResult<PersonaWriteResult>(new PersonaWriteResult.Deleted());
    }

    public Task<PersonaCard?> GetCardByIdAsync(long id, CancellationToken ct) =>
        Task.FromResult(cardById.TryGetValue(id, out var card) ? card : null);

    public Task<long?> GetIdBySlugAsync(string slug, CancellationToken ct) =>
        Task.FromResult(idBySlug.TryGetValue(slug, out var id) ? id : (long?)null);
}

/// <summary>Always-empty <see cref="IPersonaMemory"/> double — <c>Export</c> (T66) reads through this
/// on every call; this file's scenarios never seed lore, so empty is the only script needed
/// (mirrors Story208_PersonaExport.cs's own <c>FakePersonaMemory</c> shape without its scripting
/// knobs, which this file's facts never exercise).</summary>
file sealed class EmptyPersonaMemory : IPersonaMemory
{
    public Task<IReadOnlyList<PersonaMemoryEntry>> ListAsync(long personaId, PersonaMemorySource source, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PersonaMemoryEntry>>([]);

    public Task<long> RecordAsync(long personaId, string kind, string content, PersonaMemorySource source, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story247's scenarios.");

    public Task MarkAiredAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story247's scenarios.");

    public Task<IReadOnlyList<PersonaMemoryEntry>> RecallAsync(long personaId, RecallSpec spec, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story247's scenarios.");
}

/// <summary>Always-empty <see cref="IPersonaTasteReader"/> double — same reason as
/// <see cref="EmptyPersonaMemory"/> above.</summary>
file sealed class EmptyPersonaTasteReader : IPersonaTasteReader
{
    public Task<IReadOnlyList<PersonaTasteEntry>> ListAsync(long personaId, PersonaTasteSource? source, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PersonaTasteEntry>>([]);
}

// ── WebApplicationFactory driving the real HTTP pipeline ─────────────────────────────────────────

/// <summary>
/// Boots the real Program.cs graph (routing, cookie auth, the production
/// <c>POST/GET/DELETE /api/personas</c> routes) with <see cref="IPersonaStore"/> replaced by
/// <paramref name="store"/> — mirrors Story237's <c>PersonaProvenanceWebFactory</c>.
/// </summary>
file sealed class PersonaDeleteWebFactory(FakeBenchStore store) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-two-stage-firing";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        builder.ConfigureTestServices(services =>
        {
            // No Liquidsoap/DB connections during this test.
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IPersonaStore>();
            services.AddSingleton<IPersonaStore>(store);

            // Export (T66) also reads IPersonaMemory/IPersonaTasteReader — both real implementations
            // are Postgres-backed against ConnectionStrings:Station, which this factory leaves at its
            // unreachable dev-mode default; ExportRemainsAvailableUntilTheDelete is the only fact here
            // that reaches Export, so both are swapped for always-empty doubles.
            services.RemoveAll<IPersonaMemory>();
            services.AddSingleton<IPersonaMemory>(new EmptyPersonaMemory());

            services.RemoveAll<IPersonaTasteReader>();
            services.AddSingleton<IPersonaTasteReader>(new EmptyPersonaTasteReader());
        });
    }

    /// <summary>Logs in via the real POST /api/auth/login round trip (mirrors Story251's own helper) and returns the cookie-bearing client.</summary>
    public static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }
}

// ── In-process tests ──────────────────────────────────────────────────────────────────────────────

public static class FeatureTwoStageFiring
{
    static async Task<PersonaDto> CreateAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(
            "/api/personas", new { name, backstory = "", style = "", voice = (string?)null });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PersonaDto>())!;
    }

    public sealed class ScenarioBenchingByUnpainting
    {
        // Given a DJ scheduled in one slot, When that slot is removed via PUT /api/schedule.
        //
        // Pending T122 (PUT /api/schedule does not exist yet) — see this file's own header for why,
        // and Story247_BenchTransition.cs (GenWave.MediaLibrary.Tests) for the repository-level proof
        // of the same "unpainting" behavior that exists today.

        [Fact(Skip = "Pending (T122): needs the real PUT /api/schedule endpoint — see Story247_BenchTransition.cs for the repository-level equivalent")]
        public void PersonaRecordIsUntouched() { }

        [Fact(Skip = "Pending (T122): needs the real PUT /api/schedule endpoint — see Story247_BenchTransition.cs for the repository-level equivalent")]
        public void PersonaNoLongerAppearsInAnyScheduleRow() { }
    }

    public sealed class ScenarioDeletingFromTheBench
    {
        // Given a benched persona (zero schedule rows), When DELETE /api/personas/{id}.

        [Fact]
        public async Task BenchedDeleteProceeds()
        {
            var store = new FakeBenchStore();
            await using var factory = new PersonaDeleteWebFactory(store);
            var client = await PersonaDeleteWebFactory.LoggedInClientAsync(factory);
            var created = await CreateAsync(client, "Bench Test DJ");

            var response = await client.DeleteAsync($"/api/personas/{created.Id}");

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [Fact]
        public async Task ExportRemainsAvailableUntilTheDelete()
        {
            var store = new FakeBenchStore();
            await using var factory = new PersonaDeleteWebFactory(store);
            var client = await PersonaDeleteWebFactory.LoggedInClientAsync(factory);
            var created = await CreateAsync(client, "Bench Export DJ");
            var slug = TestSlug.Of(created.Name);

            var beforeDelete = await client.GetAsync($"/api/personas/{slug}/export");
            Assert.Equal(HttpStatusCode.OK, beforeDelete.StatusCode);

            var deleteResponse = await client.DeleteAsync($"/api/personas/{created.Id}");
            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

            var afterDelete = await client.GetAsync($"/api/personas/{slug}/export");
            Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
        }
    }

    public sealed class ScenarioScheduledPersonasAreUndeletable
    {
        // Sad path — F91.9: the FK guard replaces delete-clears-active (F35.5).

        [Fact]
        public async Task DeleteReturns409NamingTheSlots()
        {
            var store = new FakeBenchStore();
            await using var factory = new PersonaDeleteWebFactory(store);
            var client = await PersonaDeleteWebFactory.LoggedInClientAsync(factory);
            var created = await CreateAsync(client, "Scheduled DJ");
            store.DeleteOverride = new PersonaWriteResult.ScheduledElsewhere(
                [new ScheduledSlot(DayOfWeek.Monday, 540, 720), new ScheduledSlot(DayOfWeek.Tuesday, 840, 960)]);

            var response = await client.DeleteAsync($"/api/personas/{created.Id}");

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            var detail = body.GetProperty("detail").GetString();
            Assert.Contains("Mon 09:00–12:00", detail, StringComparison.Ordinal);
            Assert.Contains("Tue 14:00–16:00", detail, StringComparison.Ordinal);
        }

        [Fact]
        public async Task NothingIsDeletedOn409()
        {
            var store = new FakeBenchStore();
            await using var factory = new PersonaDeleteWebFactory(store);
            var client = await PersonaDeleteWebFactory.LoggedInClientAsync(factory);
            var created = await CreateAsync(client, "Scheduled DJ Two");
            store.DeleteOverride = new PersonaWriteResult.ScheduledElsewhere([new ScheduledSlot(DayOfWeek.Wednesday, 0, 1440)]);

            var response = await client.DeleteAsync($"/api/personas/{created.Id}");
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            var afterList = await client.GetFromJsonAsync<PersonaDto[]>("/api/personas");
            Assert.Contains(afterList!, p => p.Id == created.Id);
        }

        [Fact(Skip = "Pending (T128): browser acceptance — export offered before Delete enables; cancel is a no-op")]
        public void FireModalFlowIsBrowserAcceptance() { }
    }
}

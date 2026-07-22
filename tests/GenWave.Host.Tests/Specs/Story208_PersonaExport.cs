// STORY-208 — Card export carries character, never memory
//
// BDD specification — xUnit (SPEC F79.1, F79.2). PLAN T66 wires the export endpoint. Every scenario
// drives the real admin route (WebApplicationFactory<Program>, mirrors Story148's
// FacetsAndExactParamsWebFactory idiom: real routing/auth, IPersonaStore/IPersonaMemory/
// IPersonaTasteStore replaced by scriptable fakes, no live Postgres) so "zero accrued rows" is proven
// at the production surface — not an internal method call — exactly per F79.1's "pinned by test on a
// persona that has both" requirement.
//
// The zero-accrued fact (and every other happy-path fact) seeds BOTH authored and accrued rows into
// the SAME fakes; FakePersonaMemory/FakePersonaTasteStore filter their own Rows by the `source`
// argument they are called with — exactly like the real station.persona_memory/persona_taste WHERE
// clauses (PersonaMemoryRepository.ListAsync/PersonaTasteRepository.ListAsync) — rather than
// returning a fixed, pre-filtered list regardless of the argument. That is what makes "the export
// excludes accrued" a proof about the controller's own source-filtered CALL (recorded in each
// fake's LastListSource below), not an accident of what the test happened to seed.

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Scriptable, slug-keyed <see cref="IPersonaStore"/> double. Only <see cref="GetIdBySlugAsync"/> and
/// <see cref="GetCardByIdAsync"/> are reachable through <see cref="Api.PersonaController.Export"/>;
/// every write throws if a scenario ever hits it by mistake (this file never CRUDs a persona).
/// </summary>
file sealed class FakePersonaStore : IPersonaStore
{
    readonly Dictionary<string, long> idsBySlug = [];
    readonly Dictionary<long, PersonaCard> cardsById = [];

    public void Seed(string slug, long id, PersonaCard card)
    {
        idsBySlug[slug] = id;
        cardsById[id] = card;
    }

    public Task<long?> GetIdBySlugAsync(string slug, CancellationToken ct) =>
        Task.FromResult(idsBySlug.TryGetValue(slug, out var id) ? id : (long?)null);

    public Task<PersonaCard?> GetCardByIdAsync(long id, CancellationToken ct) =>
        Task.FromResult(cardsById.TryGetValue(id, out var card) ? card : null);

    public Task<IReadOnlyList<Persona>> GetAllAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story208's export scenarios.");

    public Task<Persona?> GetByIdAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story208's export scenarios.");

    public Task<PersonaWriteResult> CreateAsync(PersonaDraft draft, CancellationToken ct) =>
        throw new NotSupportedException("Export never writes through IPersonaStore.");

    public Task<PersonaWriteResult> UpdateAsync(long id, PersonaDraft draft, CancellationToken ct) =>
        throw new NotSupportedException("Export never writes through IPersonaStore.");

    public Task<PersonaWriteResult> DeleteAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Export never writes through IPersonaStore.");
}

/// <summary>
/// Scriptable <see cref="IPersonaMemory"/> double that filters <see cref="Rows"/> by
/// <c>(personaId, source)</c> inside <see cref="ListAsync"/> — mirroring
/// <c>PersonaMemoryRepository.ListAsync</c>'s own <c>WHERE persona_id = @PersonaId and source =
/// @Source</c> — rather than handing back a pre-filtered list. <see cref="LastListSource"/> records
/// what the controller actually asked for, so a scenario can assert BOTH the response content AND
/// that the exclusion happened via the source-filtered call, not by chance.
/// </summary>
file sealed class FakePersonaMemory : IPersonaMemory
{
    public List<PersonaMemoryEntry> Rows { get; } = [];
    public PersonaMemorySource? LastListSource { get; private set; }

    public Task<IReadOnlyList<PersonaMemoryEntry>> ListAsync(long personaId, PersonaMemorySource source, CancellationToken ct)
    {
        LastListSource = source;
        IReadOnlyList<PersonaMemoryEntry> result =
            Rows.Where(r => r.PersonaId == personaId && r.Source == source).ToList();
        return Task.FromResult(result);
    }

    public Task<long> RecordAsync(long personaId, string kind, string content, PersonaMemorySource source, CancellationToken ct) =>
        throw new NotSupportedException("Export never writes through IPersonaMemory.");

    public Task MarkAiredAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Export never writes through IPersonaMemory.");

    public Task<IReadOnlyList<PersonaMemoryEntry>> RecallAsync(long personaId, RecallSpec spec, CancellationToken ct) =>
        throw new NotSupportedException("Export reads via ListAsync, never RecallAsync.");
}

/// <summary>
/// Scriptable <see cref="IPersonaTasteStore"/> double — same self-filtering idiom as
/// <see cref="FakePersonaMemory"/>, mirroring <c>PersonaTasteRepository.ListAsync</c>'s own
/// <c>WHERE persona_id = @PersonaId and (@Source is null or source = @Source)</c>.
/// </summary>
file sealed class FakePersonaTasteStore : IPersonaTasteStore
{
    public List<PersonaTasteEntry> Rows { get; } = [];
    public PersonaTasteSource? LastListSource { get; private set; }

    public Task<IReadOnlyList<PersonaTasteEntry>> ListAsync(long personaId, PersonaTasteSource? source, CancellationToken ct)
    {
        LastListSource = source;
        IReadOnlyList<PersonaTasteEntry> result =
            Rows.Where(r => r.PersonaId == personaId && (source is null || r.Source == source)).ToList();
        return Task.FromResult(result);
    }

    public Task<long> InsertAsync(long personaId, TasteRule rule, PersonaTasteSource source, CancellationToken ct) =>
        throw new NotSupportedException("Export never writes through IPersonaTasteStore.");

    public Task<long> ReplaceAsync(long personaId, TasteRule rule, PersonaTasteSource source, CancellationToken ct) =>
        throw new NotSupportedException("Export never writes through IPersonaTasteStore.");

    public Task<int> DeleteAsync(long personaId, PersonaTasteSource source, CancellationToken ct) =>
        throw new NotSupportedException("Export never writes through IPersonaTasteStore.");
}

// ── WebApplicationFactory ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> that brings up the real HTTP pipeline (routing,
/// auth, the production <c>GET /api/personas/{slug}/export</c> route) while replacing
/// <see cref="IPersonaStore"/>/<see cref="IPersonaMemory"/>/<see cref="IPersonaTasteStore"/> with
/// scriptable fakes — mirrors Story148's <c>FacetsAndExactParamsWebFactory</c>. No live Postgres:
/// <c>ConnectionStrings:Station</c>/<c>Library</c> are left at their unreachable defaults, and every
/// hosted service that would otherwise touch them is removed.
/// </summary>
file sealed class PersonaExportWebFactory(
    FakePersonaStore? personaStore = null,
    FakePersonaMemory? personaMemory = null,
    FakePersonaTasteStore? personaTaste = null) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-x7z";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development config provides Station:Id/Name/Voice/Scope/SafeScope and Tts:Endpoint so
        // ValidateOnStart() is satisfied without injecting them manually.
        builder.UseEnvironment("Development");

        // AddMediaLibrary reads the Library connection string at composition time in Program.cs —
        // UseSetting (colon-form) reaches that read (verified empirically). A non-reachable host is
        // fine: every persona seam this test touches is replaced with a fake below, and every
        // hosted service is removed.
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        builder.ConfigureTestServices(services =>
        {
            // Remove ALL hosted services — no Liquidsoap/Postgres connections during this test.
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IPersonaStore>();
            services.AddSingleton<IPersonaStore>(personaStore ?? new FakePersonaStore());

            services.RemoveAll<IPersonaMemory>();
            services.AddSingleton<IPersonaMemory>(personaMemory ?? new FakePersonaMemory());

            services.RemoveAll<IPersonaTasteStore>();
            services.RemoveAll<IPersonaTasteReader>();
            var taste = personaTaste ?? new FakePersonaTasteStore();
            services.AddSingleton<IPersonaTasteStore>(taste);
            services.AddSingleton<IPersonaTasteReader>(taste);
        });
    }
}

// ── Shared fixture (file-scoped: SeedLivingPersona's return type carries the file-scoped fakes
// above, and a file-scoped type cannot appear in a member signature of a non-file-scoped type) ────

file static class PersonaExportFixture
{
    public const long PersonaId = 42;
    public const string Slug = "dj-nova";

    public static readonly PersonaCard StoredCard = new(
        SchemaVersion: 1,
        Name: "DJ Nova",
        Tagline: "Late-night gravity.",
        Soul: "A slow-orbit voice for the small hours.",
        Quirks: ["Never says goodnight twice."],
        Voice: new VoiceSpec(Engine: "kokoro", VoiceId: "af_heart", Pace: 1.0, Language: "en"),
        EnergyDisposition: -0.2,
        // Deliberately non-empty and DIFFERENT from the seeded persona_memory content below — proves
        // the export replaces the stored definition's own (vestigial) lore, never appends to it.
        Lore: ["stale definition lore that must never reach the export"],
        Corrections: [new PersonaCorrection("Nova", "NOH-vah")]);

    /// <summary>
    /// Seeds a persona holding BOTH authored and accrued memory, and BOTH authored and accrued (plus
    /// operator) taste — F79.1's "pinned by test on a persona that has both" arrangement, shared by
    /// every ScenarioExportOfALivingPersona fact below.
    /// </summary>
    public static (FakePersonaStore Store, FakePersonaMemory Memory, FakePersonaTasteStore Taste) SeedLivingPersona()
    {
        var store = new FakePersonaStore();
        store.Seed(Slug, PersonaId, StoredCard);

        var memory = new FakePersonaMemory();
        var now = DateTime.UtcNow;
        memory.Rows.Add(new PersonaMemoryEntry(1, PersonaId, "bit", "Once got lost inside a 20-minute Tangerine Dream fade.", PersonaMemorySource.Authored, 0, null, now));
        memory.Rows.Add(new PersonaMemoryEntry(2, PersonaId, "callback", "Still owes the overnight crew a mixtape.", PersonaMemorySource.Authored, 0, null, now));
        memory.Rows.Add(new PersonaMemoryEntry(3, PersonaId, "bit", "Accrued bit nobody authored — must never export.", PersonaMemorySource.Accrued, 2, now, now));

        var taste = new FakePersonaTasteStore();
        taste.Rows.Add(new PersonaTasteEntry(
            1, PersonaId,
            new TasteRule(new TastePredicate(Artist: "Led Zeppelin", Genre: null, Tag: null), new TasteContext([DayOfWeek.Sunday], 6, 12), 0.75),
            PersonaTasteSource.Authored, now, now));
        taste.Rows.Add(new PersonaTasteEntry(
            2, PersonaId,
            new TasteRule(new TastePredicate(Artist: "Nickelback", Genre: null, Tag: null), new TasteContext([], null, null), -0.9),
            PersonaTasteSource.Accrued, now, now));
        taste.Rows.Add(new PersonaTasteEntry(
            3, PersonaId,
            new TasteRule(new TastePredicate(Artist: null, Genre: "Vaporwave", Tag: null), new TasteContext([], null, null), 0.3),
            PersonaTasteSource.Operator, now, now));

        return (store, memory, taste);
    }
}

// ── Specs ──────────────────────────────────────────────────────────────────────────────────────────

public static class FeaturePersonaCardExport
{
    /// <summary>Logs in with the factory's fixed test password and returns the now-authenticated client.</summary>
    static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = PersonaExportWebFactory.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    const string Slug = PersonaExportFixture.Slug;

    // ---------------------------------------------------------------------
    // HAPPY PATH — export of a persona holding both authored and accrued state
    // ---------------------------------------------------------------------

    public sealed class ScenarioExportOfALivingPersona
    {
        [Fact]
        public async Task ExportContainsSchemaVersionAndCardFields()
        {
            var (store, memory, taste) = PersonaExportFixture.SeedLivingPersona();
            await using var factory = new PersonaExportWebFactory(store, memory, taste);
            var client = await LoggedInClientAsync(factory);

            var response = await client.GetAsync($"/api/personas/{Slug}/export");
            var card = PersonaCardSerializer.Deserialize(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(card);
            Assert.Equal(1, card.SchemaVersion);
            Assert.Equal("DJ Nova", card.Name);
            Assert.Equal("Late-night gravity.", card.Tagline);
            Assert.Equal("af_heart", card.Voice.VoiceId);
            Assert.Equal(-0.2, card.EnergyDisposition);
            Assert.Equal([new PersonaCorrection("Nova", "NOH-vah")], card.Corrections);
        }

        [Fact]
        public async Task ExportContainsAuthoredLore()
        {
            var (store, memory, taste) = PersonaExportFixture.SeedLivingPersona();
            await using var factory = new PersonaExportWebFactory(store, memory, taste);
            var client = await LoggedInClientAsync(factory);

            var response = await client.GetAsync($"/api/personas/{Slug}/export");
            var card = PersonaCardSerializer.Deserialize(await response.Content.ReadAsStringAsync());

            Assert.NotNull(card);
            Assert.Equal(
                ["Once got lost inside a 20-minute Tangerine Dream fade.", "Still owes the overnight crew a mixtape."],
                card.Lore);
        }

        [Fact]
        public async Task ExportContainsAuthoredTasteRules()
        {
            var (store, memory, taste) = PersonaExportFixture.SeedLivingPersona();
            await using var factory = new PersonaExportWebFactory(store, memory, taste);
            var client = await LoggedInClientAsync(factory);

            var response = await client.GetAsync($"/api/personas/{Slug}/export");
            var card = PersonaCardSerializer.Deserialize(await response.Content.ReadAsStringAsync());

            Assert.NotNull(card);
            var rule = Assert.Single(card.Taste ?? []);
            Assert.Equal("Led Zeppelin", rule.Predicate.Artist);
            Assert.Equal(0.75, rule.Weight);
        }

        [Fact]
        public async Task ExportContainsZeroAccruedRowsOfAnyKind()
        {
            // The seeded persona HOLDS accrued memory AND accrued (plus operator) taste; the export
            // must hold none of either (F79.1) — proven both in the response body and via the
            // source-filtered call the fakes recorded, so a regression that read everything and
            // trimmed in memory would fail this fact even if it happened to produce the same body.
            var (store, memory, taste) = PersonaExportFixture.SeedLivingPersona();
            await using var factory = new PersonaExportWebFactory(store, memory, taste);
            var client = await LoggedInClientAsync(factory);

            var response = await client.GetAsync($"/api/personas/{Slug}/export");
            var body = await response.Content.ReadAsStringAsync();
            var card = PersonaCardSerializer.Deserialize(body);

            Assert.NotNull(card);
            Assert.DoesNotContain("Accrued bit nobody authored", body, StringComparison.Ordinal);
            Assert.DoesNotContain(card.Lore, l => l.Contains("Accrued", StringComparison.Ordinal));
            Assert.DoesNotContain(card.Taste ?? [], r => r.Weight is -0.9 or 0.3);
            Assert.Equal(PersonaMemorySource.Authored, memory.LastListSource);
            Assert.Equal(PersonaTasteSource.Authored, taste.LastListSource);
        }

        [Fact]
        public async Task ExportFileNameIsSlugPersonaJson()
        {
            var (store, memory, taste) = PersonaExportFixture.SeedLivingPersona();
            await using var factory = new PersonaExportWebFactory(store, memory, taste);
            var client = await LoggedInClientAsync(factory);

            var response = await client.GetAsync($"/api/personas/{Slug}/export");

            var disposition = response.Content.Headers.ContentDisposition;
            Assert.NotNull(disposition);
            Assert.Equal("dj-nova.persona.json", disposition.FileName?.Trim('"'));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — unknown slug
    // ---------------------------------------------------------------------

    public sealed class SadPathUnknownSlug
    {
        [Fact]
        public async Task ExportOfUnknownSlugReturns404()
        {
            await using var factory = new PersonaExportWebFactory(new FakePersonaStore());
            var client = await LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/personas/no-such-persona/export");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}

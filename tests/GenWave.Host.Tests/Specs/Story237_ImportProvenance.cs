// STORY-237 — Where did this DJ come from? (SPEC F90.7, PLAN T98, T105)
//
// BDD specification — xUnit, pending. Provenance stamping through the real import endpoint
// (db/25: persona.imported_from / imported_at); the Personas-page badge itself is T105
// browser acceptance — these facts pin the columns, stamps, and projection.
//
// Mirrors Story209_PersonaImport.cs's WebApplicationFactory idiom: real routing/auth/content-
// negotiation pipeline, IPersonaImportStore/IPersonaStore replaced by ONE scriptable fake
// (FakePersonaStation below) rather than two independent ones — production has exactly one
// underlying station.persona table behind both repository seams, so a fact asserting "the list
// projection reflects what the import endpoint just committed" needs the two write/read paths to
// share state the same way. FakePersonaStation replicates each production repository's own
// stamping rule: PersonaRepository.CreateAsync never names imported_from/imported_at (an authored
// persona keeps both NULL); PersonaImportRepository.ImportAsync always stamps them, insert or
// update, unconditionally.

using System.Net.Http.Json;
using System.Text;
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

/// <summary>
/// One scriptable double standing in for BOTH <see cref="IPersonaStore"/> and
/// <see cref="IPersonaImportStore"/> — see the file header for why a single shared backing
/// dictionary, not two independent fakes, is the faithful shape here. Keyed by id for
/// <see cref="Persona"/> rows and separately by slug (a <see cref="Persona"/> itself carries no
/// slug — that lives only in the DB row and <see cref="GetIdBySlugAsync"/>) so re-import can find
/// the same row a first import created.
/// </summary>
file sealed class FakePersonaStation : IPersonaStore, IPersonaImportStore
{
    readonly Dictionary<long, Persona> byId = [];
    readonly Dictionary<string, long> idBySlug = new(StringComparer.Ordinal);
    long nextId = 1;

    public IReadOnlyList<Persona> Snapshot => byId.Values.ToList();

    public Task<IReadOnlyList<Persona>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Persona>>(byId.Values.OrderBy(p => p.Name, StringComparer.Ordinal).ToList());

    public Task<Persona?> GetByIdAsync(long id, CancellationToken ct) =>
        Task.FromResult(byId.TryGetValue(id, out var persona) ? persona : null);

    /// <summary>Mirrors <c>PersonaRepository.CreateAsync</c>: never sets ImportedFrom/ImportedAt —
    /// an authored-in-place persona keeps both NULL (SPEC F90.7).</summary>
    public Task<PersonaWriteResult> CreateAsync(PersonaDraft draft, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var persona = new Persona(nextId, draft.Name, draft.Backstory, draft.Style, draft.Voice, now, now);
        byId[nextId] = persona;
        nextId++;
        return Task.FromResult<PersonaWriteResult>(new PersonaWriteResult.Created(persona));
    }

    public Task<PersonaWriteResult> UpdateAsync(long id, PersonaDraft draft, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story237's scenarios.");

    public Task<PersonaWriteResult> DeleteAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story237's scenarios.");

    public Task<PersonaCard?> GetCardByIdAsync(long id, CancellationToken ct) =>
        Task.FromResult<PersonaCard?>(null);

    public Task<long?> GetIdBySlugAsync(string slug, CancellationToken ct) =>
        Task.FromResult(idBySlug.TryGetValue(slug, out var id) ? id : (long?)null);

    /// <summary>Mirrors <c>PersonaImportRepository.ImportAsync</c>/<c>UpsertPersonaAsync</c> in
    /// full, not just its two provenance columns: upsert-by-slug, resetting backstory/style to
    /// <c>""</c> (an imported persona's narrative lives entirely in the card, SPEC F79.3), bumping
    /// <c>UpdatedAt</c>, and stamping imported_from/imported_at UNCONDITIONALLY — insert or update
    /// alike (SPEC F90.7) — so a re-import onto a living row refreshes the stamp exactly like every
    /// other field the real UPDATE rewrites.</summary>
    public Task<PersonaImportOutcome> ImportAsync(PersonaImportRequest request, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        if (idBySlug.TryGetValue(request.Slug, out var existingId))
        {
            var existing = byId[existingId];
            byId[existingId] = existing with
            {
                Name = request.Card.Name,
                Backstory = "",
                Style = "",
                Voice = request.LegacyVoice,
                UpdatedAt = now,
                ImportedFrom = request.ImportedFrom,
                ImportedAt = now,
            };
            return Task.FromResult<PersonaImportOutcome>(new PersonaImportOutcome.Imported(existingId, WasCreated: false));
        }

        var id = nextId++;
        idBySlug[request.Slug] = id;
        byId[id] = new Persona(id, request.Card.Name, "", "", request.LegacyVoice, now, now, request.ImportedFrom, now);
        return Task.FromResult<PersonaImportOutcome>(new PersonaImportOutcome.Imported(id, WasCreated: true));
    }
}

// ── WebApplicationFactory ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Brings up the real HTTP pipeline (routing, auth, the production import/list/create routes) over
/// ONE shared <see cref="FakePersonaStation"/> — mirrors Story209's <c>PersonaImportWebFactory</c>.
/// No live Postgres: <c>ConnectionStrings:Station</c>/<c>Library</c> are left at their unreachable
/// defaults; every hosted service that would otherwise touch them is removed. Every OTHER
/// <see cref="PersonaController"/> dependency (voice lister, settings store, preview writer, ...)
/// stays wired to its real production implementation, exactly like Story209 — none of it is ever
/// invoked by the import/list/create routes this file exercises.
/// </summary>
file sealed class PersonaProvenanceWebFactory(FakePersonaStation store) : WebApplicationFactory<Program>
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

            services.RemoveAll<IPersonaStore>();
            services.AddSingleton<IPersonaStore>(store);

            services.RemoveAll<IPersonaImportStore>();
            services.AddSingleton<IPersonaImportStore>(store);
        });
    }
}

// ── Shared fixture ─────────────────────────────────────────────────────────────────────────────────

file static class ProvenanceFixture
{
    public const string CatalogEntrySlug = "midnight-mabel";

    public sealed record Arrangement(
        FakePersonaStation Store, long CatalogPersonaId, long FilePersonaId, long AuthoredPersonaId);

    static PersonaCard BuildCard(string name) =>
        new(
            SchemaVersion: PersonaCard.CurrentSchemaVersion,
            Name: name,
            Tagline: "A voice for the small hours.",
            Soul: "Late-night gravity.",
            Quirks: [],
            // Empty VoiceId short-circuits PersonaController.ResolveVoiceAsync before it ever
            // reaches ITtsVoiceLister — no fake/override needed for this file's routes.
            Voice: new VoiceSpec(Engine: "", VoiceId: "", Pace: 1.0, Language: "en"),
            EnergyDisposition: 0,
            Lore: [],
            Corrections: [],
            Taste: null);

    public static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = PersonaProvenanceWebFactory.Password });
        Assert.Equal(System.Net.HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    static Task<HttpResponseMessage> PostCardAsync(HttpClient client, string slug, PersonaCard card, string? catalogSlug) =>
        client.PostAsync(
            catalogSlug is null
                ? $"/api/personas/{slug}/import"
                : $"/api/personas/{slug}/import?catalogSlug={Uri.EscapeDataString(catalogSlug)}",
            new StringContent(PersonaCardSerializer.Serialize(card), Encoding.UTF8, "application/json"));

    /// <summary>
    /// Commits the three rows <c>ScenarioStampsOnImport</c>'s facts all read from: a catalog import
    /// (<see cref="CatalogEntrySlug"/>), a file import (no <c>catalogSlug</c>), and an
    /// authored-in-place persona (<c>POST /api/personas</c>, never import) — through the REAL routes,
    /// over one shared <see cref="FakePersonaStation"/>. Called once per fact (mirrors Story209's own
    /// per-fact re-arrangement idiom — a fresh <see cref="WebApplicationFactory{TEntryPoint}"/> per
    /// call, never shared mutable HTTP state across facts) and returns the resulting ids for each
    /// fact's own, single assertion.
    /// </summary>
    public static async Task<Arrangement> CommitAsync()
    {
        var store = new FakePersonaStation();
        await using var factory = new PersonaProvenanceWebFactory(store);
        var client = await LoggedInClientAsync(factory);

        var catalogResponse = await PostCardAsync(client, "dj-catalog", BuildCard("DJ Catalog"), CatalogEntrySlug);
        Assert.True(catalogResponse.IsSuccessStatusCode, await catalogResponse.Content.ReadAsStringAsync());
        var catalogBody = await catalogResponse.Content.ReadFromJsonAsync<PersonaImportResponse>();

        var fileResponse = await PostCardAsync(client, "dj-file", BuildCard("DJ File"), catalogSlug: null);
        Assert.True(fileResponse.IsSuccessStatusCode, await fileResponse.Content.ReadAsStringAsync());
        var fileBody = await fileResponse.Content.ReadFromJsonAsync<PersonaImportResponse>();

        var createResponse = await client.PostAsJsonAsync(
            "/api/personas", new { name = "DJ Authored", backstory = "", style = "", voice = (string?)null });
        Assert.True(createResponse.IsSuccessStatusCode, await createResponse.Content.ReadAsStringAsync());
        var createdDto = await createResponse.Content.ReadFromJsonAsync<PersonaDto>();

        return new Arrangement(store, catalogBody!.Id, fileBody!.Id, createdDto!.Id);
    }

    /// <summary>Re-import fixture for <c>ScenarioReImport</c>: two imports of the SAME slug, the
    /// second under a DIFFERENT catalogSlug, over one shared store/client/factory (a genuine
    /// re-import onto a living row, not two independent first imports).</summary>
    public static async Task<FakePersonaStation> CommitThenReImportAsync()
    {
        var store = new FakePersonaStation();
        await using var factory = new PersonaProvenanceWebFactory(store);
        var client = await LoggedInClientAsync(factory);

        var first = await PostCardAsync(client, "dj-reimport", BuildCard("DJ Reimport"), "old-catalog-slug");
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());

        var second = await PostCardAsync(client, "dj-reimport", BuildCard("DJ Reimport"), "new-catalog-slug");
        Assert.True(second.IsSuccessStatusCode, await second.Content.ReadAsStringAsync());

        return store;
    }
}

// ── Specs ──────────────────────────────────────────────────────────────────────────────────────────

public static class FeatureImportProvenance
{
    public sealed class ScenarioStampsOnImport
    {
        // Given one catalog import (entry slug known), one file import, one authored-in-place
        // persona, When each commits (ProvenanceFixture.CommitAsync).

        [Fact]
        public async Task CatalogImportStampsTheEntrySlug()
        {
            var arrangement = await ProvenanceFixture.CommitAsync();

            var catalogPersona = arrangement.Store.Snapshot.Single(p => p.Id == arrangement.CatalogPersonaId);
            Assert.Equal(ProvenanceFixture.CatalogEntrySlug, catalogPersona.ImportedFrom);
        }

        [Fact]
        public async Task FileImportStampsFile()
        {
            var arrangement = await ProvenanceFixture.CommitAsync();

            var filePersona = arrangement.Store.Snapshot.Single(p => p.Id == arrangement.FilePersonaId);
            Assert.Equal(PersonaImportRequest.FileSource, filePersona.ImportedFrom);
        }

        [Fact]
        public async Task BothImportPathsStampImportedAt()
        {
            var arrangement = await ProvenanceFixture.CommitAsync();

            var imported = arrangement.Store.Snapshot
                .Where(p => p.Id == arrangement.CatalogPersonaId || p.Id == arrangement.FilePersonaId)
                .ToList();
            Assert.All(imported, p => Assert.NotNull(p.ImportedAt));
        }

        [Fact]
        public async Task AuthoredPersonaKeepsNullProvenance()
        {
            var arrangement = await ProvenanceFixture.CommitAsync();

            var authored = arrangement.Store.Snapshot.Single(p => p.Id == arrangement.AuthoredPersonaId);
            Assert.Null(authored.ImportedFrom);
        }

        [Fact]
        public async Task PersonaProjectionExposesProvenanceFields()
        {
            var arrangement = await ProvenanceFixture.CommitAsync();
            await using var factory = new PersonaProvenanceWebFactory(arrangement.Store);
            var client = await ProvenanceFixture.LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/personas");
            var body = await response.Content.ReadAsStringAsync();

            // Pins BOTH the camelCase wire name and the stamped value in one assertion — a raw
            // string check (not a typed deserialize-then-compare) is what actually proves the JSON
            // key is "importedFrom", not ".NET's case-insensitive deserializer being generous".
            Assert.Contains($"\"importedFrom\":\"{ProvenanceFixture.CatalogEntrySlug}\"", body, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioReImport
    {
        // Given an already-imported slug, When the same card is imported again (under a new
        // catalogSlug — ProvenanceFixture.CommitThenReImportAsync).

        [Fact]
        public async Task ReImportRefreshesTheStamp()
        {
            var store = await ProvenanceFixture.CommitThenReImportAsync();

            var persona = Assert.Single(store.Snapshot);
            Assert.Equal("new-catalog-slug", persona.ImportedFrom);
        }
    }
}

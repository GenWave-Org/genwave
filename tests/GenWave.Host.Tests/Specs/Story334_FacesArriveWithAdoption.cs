// STORY-334 — Faces arrive with adoption (SPEC F128.7/.8 · PLAN T297)
//
// BDD specification — xUnit. Backend halves only: the trust modal's face render (AC1) is admin-ui
// jest (adoption-shows-the-face.spec.tsx).
//
// WIRED T297 — every Fact below drives the real production route through
// WebApplicationFactory<Program> (real routing/auth/content-negotiation pipeline, real ffmpeg via the
// real ImageNormalizeService — no mock of the re-validation pipeline itself, mirrors
// Story333_TheWornFace.cs's own posture and Story332_AvatarPacksIntoTheLibrary.cs's own WIRED T293
// posture) against a fake catalog origin (mirrors Story331_TheShelfGainsTheVisualKinds.cs's own
// VisualKindShelfWebFactory idiom) plus FakePersonaAvatarStore and a persona-only in-process
// IPersonaStore/IPersonaImportStore double (this project has no Postgres fixture; the REAL
// station.persona_avatar SQL is T290's own coverage against real Postgres).
//
// One assertion per Fact where the scenario allows it; happy path first and exhaustive; the sad path
// (file-import/export untouched, plus the face-failure-degrades ruling) is its own block.

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

public static class FeatureFacesArriveWithAdoption
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioConfirmedImportInstallsTheFace
    {
        [Fact]
        public async Task TheImportedPersonaWearsTheEntrysFace()
        {
            // Given a catalog persona entry carrying an avatar asset, served by a fake origin,
            await using var factory = new PersonaAdoptionWebFactory();
            var client = await PersonaAdoptionWebFactory.LoggedInClientAsync(factory);

            // When the SAME entry is imported through the real production route (catalogSlug set —
            // the F90.7 provenance signal),
            var response = await AdoptionFixtures.PostCardAsync(client, "dj-mabel", AdoptionFixtures.FacedEntrySlug);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var imported = await response.Content.ReadFromJsonAsync<PersonaImportResponse>();

            // Then the persona wears the entry's own face: source='catalog', imported_from = the
            // entry slug, a freshly-minted 128-bit hex token — installed AFTER the import's own write
            // already committed (this task's own "mint in the import path" call site).
            var avatar = await factory.PersonaAvatarStore.GetByPersonaIdAsync(imported!.Id, CancellationToken.None);
            Assert.NotNull(avatar);
            Assert.Equal(PersonaAvatarSource.Catalog, avatar!.Source);
            Assert.Equal(AdoptionFixtures.FacedEntrySlug, avatar.ImportedFrom);
            Assert.Matches("\\A[0-9a-f]{32}\\z", avatar.Token);
        }

        [Fact]
        public async Task AFacelessEntryImportsExactlyAsBefore()
        {
            // Given a catalog persona entry declaring NO avatar asset,
            await using var factory = new PersonaAdoptionWebFactory();
            var client = await PersonaAdoptionWebFactory.LoggedInClientAsync(factory);

            // When it is imported through the real production route,
            var response = await AdoptionFixtures.PostCardAsync(client, "dj-quiet", AdoptionFixtures.FacelessEntrySlug);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var imported = await response.Content.ReadFromJsonAsync<PersonaImportResponse>();

            // Then the import behaves exactly as it did before F128 — no avatar row exists for it at
            // all (the SAME "absent ⇒ placeholder" shape a never-faced persona already has).
            Assert.Null(await factory.PersonaAvatarStore.GetByPersonaIdAsync(imported!.Id, CancellationToken.None));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the byte-stability fences, and the decorative-face ruling
    // ---------------------------------------------------------------------

    public sealed class ScenarioFileImportAndExportAreUntouched
    {
        [Fact]
        public async Task FileUploadImportAcceptsCardJsonOnly()
        {
            // Given the plain file-upload import door (no catalogSlug at all — the T104 seam),
            await using var factory = new PersonaAdoptionWebFactory();
            var client = await PersonaAdoptionWebFactory.LoggedInClientAsync(factory);

            // When a card is imported through it,
            var response = await AdoptionFixtures.PostCardAsync(client, "dj-file-only", catalogSlug: null);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

            // Then the catalog origin was NEVER touched — no image side-channel exists on this path
            // at all, never merely "a face that happened not to resolve".
            Assert.Empty(factory.CatalogHandler.Requests);
        }

        [Fact]
        public async Task ExportBytesAreIdenticalToThePreF128Shape()
        {
            // Given a persona authored via a plain file import (no face yet — the export route's
            // OWN, unmodified behavior),
            await using var factory = new PersonaAdoptionWebFactory();
            var client = await PersonaAdoptionWebFactory.LoggedInClientAsync(factory);
            var importResponse = await AdoptionFixtures.PostCardAsync(client, "dj-export-parity", catalogSlug: null);
            Assert.True(importResponse.IsSuccessStatusCode, await importResponse.Content.ReadAsStringAsync());
            var imported = await importResponse.Content.ReadFromJsonAsync<PersonaImportResponse>();

            var unfacedExport = await client.GetAsync("/api/personas/dj-export-parity/export");
            var unfacedBytes = await unfacedExport.Content.ReadAsByteArrayAsync();

            // When that SAME persona is made to wear a face (a real, normalized 512×512 PNG — F79's
            // export byte-shape is what this fact pins, not how the face got there),
            await factory.PersonaAvatarStore.UpsertAsync(
                new PersonaAvatarInput(
                    imported!.Id, TestImages.CreatePng(512, 512), "some-sha256", "some-token",
                    PersonaAvatarSource.Upload, null),
                CancellationToken.None);
            var facedExport = await client.GetAsync("/api/personas/dj-export-parity/export");
            var facedBytes = await facedExport.Content.ReadAsByteArrayAsync();

            // Then the export bytes are byte-for-byte identical either way — F79's export shape never
            // changes, faced or not (SPEC F128.8; the export route reads no avatar seam at all).
            Assert.Equal(unfacedBytes, facedBytes);
        }
    }

    public sealed class ScenarioAFaceFailureDegradesGracefully
    {
        [Fact]
        public async Task AFaceThatFailsReValidationWarnsAndImportsFaceless()
        {
            // Given a catalog persona entry whose declared avatar asset is NOT a real image (fails
            // the T291 magic-bytes gate on re-fetch — a hash-mismatched/oversize/unreachable asset
            // would degrade identically, this is simply the deterministic case to arrange),
            await using var factory = new PersonaAdoptionWebFactory();
            var client = await PersonaAdoptionWebFactory.LoggedInClientAsync(factory);

            // When that entry is imported through the real production route,
            var response = await AdoptionFixtures.PostCardAsync(client, "dj-corrupt-face", AdoptionFixtures.CorruptFaceEntrySlug);

            // Then the import STILL SUCCEEDS (the face is decorative, SPEC F128.9 — a face-side
            // failure never fails the persona import that already committed), and the persona ends up
            // faceless — never a partially-written or crashed row.
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var imported = await response.Content.ReadFromJsonAsync<PersonaImportResponse>();
            Assert.Null(await factory.PersonaAvatarStore.GetByPersonaIdAsync(imported!.Id, CancellationToken.None));
        }

        [Fact]
        public async Task AStoreExceptionOnTheUpsertItselfStillImportsFacelessNotA500()
        {
            // Given a face that WILL pass re-validation (a real, normalizable PNG — this fact's own
            // point is what happens the instant AFTER that: the write itself), wired through an
            // IPersonaAvatarStore whose UpsertAsync throws — the closest a test double gets to the
            // real Npgsql failure CatalogPersonaAvatarInstaller.InstallIfPresentAsync's own outer
            // try/catch exists to catch (a concurrent persona-delete FK race, a dropped connection —
            // any store-side fault, modeled here as a plain exception since the catch is by type
            // Exception, not by any Npgsql-specific shape),
            var throwingAvatarStore = new ThrowingUpsertPersonaAvatarStore();
            await using var factory = new PersonaAdoptionWebFactory(personaAvatarStore: throwingAvatarStore);
            var client = await PersonaAdoptionWebFactory.LoggedInClientAsync(factory);

            // When that entry is imported through the real production route,
            var response = await AdoptionFixtures.PostCardAsync(client, "dj-store-throws", AdoptionFixtures.FacedEntrySlug);

            // Then the import STILL SUCCEEDS with 201 — never a 500 off the uncaught store
            // exception — and the persona ends up faceless, since the failing write never actually
            // landed a row. This is what makes THE FACE IS DECORATIVE ruling a LAW, not merely true
            // of the failure shapes this file's fixtures happen to exercise.
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var imported = await response.Content.ReadFromJsonAsync<PersonaImportResponse>();
            Assert.Null(await throwingAvatarStore.GetByPersonaIdAsync(imported!.Id, CancellationToken.None));
        }
    }
}

// ── Test harness ───────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for this file's own Facts — boots the real
/// Program.cs graph with <c>Community:CatalogIndexUrl</c> pointed at a fake origin serving
/// <see cref="AdoptionFixtures"/>'s three persona entries (faced / faceless / corrupt-face),
/// <see cref="IPersonaStore"/>/<see cref="IPersonaImportStore"/> replaced by one shared
/// <see cref="FakePersonaStation"/> (mirrors Story237_ImportProvenance.cs's own reasoning: production
/// carries exactly one underlying <c>station.persona</c> table behind both seams), and
/// <see cref="IPersonaAvatarStore"/>/<see cref="IPersonaMemory"/>/<see cref="IPersonaTasteReader"/>
/// replaced with minimal doubles the export byte-stability fact needs (mirrors
/// Story208_PersonaExport.cs's own <c>PersonaExportWebFactory</c>).
/// <see cref="GenWave.Host.Images.ImageNormalizeService"/> is left WIRED to its real production
/// registration (real ffmpeg) — never faked, mirrors Story333_TheWornFace.cs's own posture.
/// <paramref name="personaAvatarStore"/> defaults to a plain <see cref="FakePersonaAvatarStore"/> but
/// is overridable per-Fact (mirrors Story333_TheWornFace.cs's own <c>PersonaAvatarWebFactory</c>
/// constructor-injection idiom) — this file's own store-throws Fact is the one caller that supplies
/// something else.
/// </summary>
file sealed class PersonaAdoptionWebFactory(IPersonaAvatarStore? personaAvatarStore = null) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story334-adoption";

    public FakePersonaStation PersonaStation { get; } = new();
    public IPersonaAvatarStore PersonaAvatarStore { get; } = personaAvatarStore ?? new FakePersonaAvatarStore();
    public FakeHttpMessageHandler CatalogHandler { get; } = AdoptionFixtures.BuildRoutedHandler();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Community:CatalogIndexUrl", AdoptionFixtures.IndexUrl);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IPersonaStore>();
            services.AddSingleton<IPersonaStore>(PersonaStation);

            services.RemoveAll<IPersonaImportStore>();
            services.AddSingleton<IPersonaImportStore>(PersonaStation);

            services.RemoveAll<IPersonaAvatarStore>();
            services.AddSingleton<IPersonaAvatarStore>(PersonaAvatarStore);

            services.RemoveAll<IPersonaMemory>();
            services.AddSingleton<IPersonaMemory>(new EmptyPersonaMemory());

            services.RemoveAll<IPersonaTasteReader>();
            services.AddSingleton<IPersonaTasteReader>(new EmptyPersonaTasteReader());

            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(CatalogHandler));
        });
    }

    public static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }
}

/// <summary>
/// One scriptable double standing in for BOTH <see cref="IPersonaStore"/> and
/// <see cref="IPersonaImportStore"/> — mirrors Story237_ImportProvenance.cs's own
/// <c>FakePersonaStation</c> shape, widened here to also retain each imported persona's own
/// <see cref="PersonaCard"/> (<see cref="GetCardByIdAsync"/>) — the ONE extra capability this file's
/// export byte-stability fact needs that Story237's copy never did.
/// </summary>
file sealed class FakePersonaStation : IPersonaStore, IPersonaImportStore
{
    readonly Dictionary<long, Persona> byId = [];
    readonly Dictionary<long, PersonaCard> cardsById = [];
    readonly Dictionary<string, long> idBySlug = new(StringComparer.Ordinal);
    long nextId = 1;

    public Task<IReadOnlyList<Persona>> GetAllAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story334's scenarios.");

    public Task<Persona?> GetByIdAsync(long id, CancellationToken ct) =>
        Task.FromResult(byId.TryGetValue(id, out var persona) ? persona : null);

    public Task<PersonaWriteResult> CreateAsync(PersonaDraft draft, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story334's scenarios — every persona here arrives via import.");

    public Task<PersonaWriteResult> UpdateAsync(long id, PersonaDraft draft, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story334's scenarios.");

    public Task<PersonaWriteResult> DeleteAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story334's scenarios.");

    public Task<PersonaCard?> GetCardByIdAsync(long id, CancellationToken ct) =>
        Task.FromResult(cardsById.TryGetValue(id, out var card) ? card : null);

    public Task<long?> GetIdBySlugAsync(string slug, CancellationToken ct) =>
        Task.FromResult(idBySlug.TryGetValue(slug, out var id) ? id : (long?)null);

    /// <summary>Mirrors <c>PersonaImportRepository.ImportAsync</c>'s own upsert-by-slug shape (SPEC
    /// F90.7) — insert or update alike, stamping provenance unconditionally and retaining the card
    /// itself so a later <see cref="GetCardByIdAsync"/> (the export route's own read) reflects exactly
    /// what THIS import request carried.</summary>
    public Task<PersonaImportOutcome> ImportAsync(PersonaImportRequest request, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        if (idBySlug.TryGetValue(request.Slug, out var existingId))
        {
            byId[existingId] = byId[existingId] with
            {
                Name = request.Card.Name,
                UpdatedAt = now,
                ImportedFrom = request.ImportedFrom,
                ImportedAt = now,
            };
            cardsById[existingId] = request.Card;
            return Task.FromResult<PersonaImportOutcome>(new PersonaImportOutcome.Imported(existingId, WasCreated: false));
        }

        var id = nextId++;
        idBySlug[request.Slug] = id;
        byId[id] = new Persona(id, request.Card.Name, "", "", request.LegacyVoice, now, now, request.ImportedFrom, now, request.Slug);
        cardsById[id] = request.Card;
        return Task.FromResult<PersonaImportOutcome>(new PersonaImportOutcome.Imported(id, WasCreated: true));
    }
}

/// <summary>Always-empty <see cref="IPersonaMemory"/> double — the export byte-stability fact needs
/// only that <c>PersonaController.Export</c>'s own <c>ListAsync</c> call resolves at all (no live
/// Postgres fixture in this project); the CONTENT is irrelevant to a fact comparing two exports of the
/// SAME persona against each other. Every write member throws — this file never records memory.</summary>
file sealed class EmptyPersonaMemory : IPersonaMemory
{
    public Task<long> RecordAsync(long personaId, string kind, string content, PersonaMemorySource source, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story334's scenarios.");

    public Task MarkAiredAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story334's scenarios.");

    public Task<IReadOnlyList<PersonaMemoryEntry>> RecallAsync(long personaId, RecallSpec spec, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story334's scenarios.");

    public Task<IReadOnlyList<PersonaMemoryEntry>> ListAsync(long personaId, PersonaMemorySource source, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PersonaMemoryEntry>>([]);
}

/// <summary>Always-empty <see cref="IPersonaTasteReader"/> double — see <see cref="EmptyPersonaMemory"/>'s
/// own remarks for why empty, not seeded, content is the right double for this file's one export
/// fact.</summary>
file sealed class EmptyPersonaTasteReader : IPersonaTasteReader
{
    public Task<IReadOnlyList<PersonaTasteEntry>> ListAsync(long personaId, PersonaTasteSource? source, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PersonaTasteEntry>>([]);
}

/// <summary>
/// Fixture documents + a routed fake HTTP double for this file's own Facts — three persona entries
/// sharing one index: <see cref="FacedEntrySlug"/> (one real, normalizable PNG sidecar face),
/// <see cref="FacelessEntrySlug"/> (no <c>assets</c> at all — the pre-F128 shape), and
/// <see cref="CorruptFaceEntrySlug"/> (a declared sidecar asset that hash-verifies but is NOT a real
/// image — fails the T291 magic-bytes gate on re-fetch, this file's own face-failure-degrades
/// arrangement). Every sha256 computed from the served content itself, mirrors
/// <c>VisualKindCatalogFixtures</c>'s own established idiom (Story331_TheShelfGainsTheVisualKinds.cs).
/// <c>file</c>-scoped.
/// </summary>
file static class AdoptionFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/index.json";
    const string Directory = "https://catalog.test/repo/";

    public const string FacedEntrySlug = "midnight-mabel";
    public const string FacelessEntrySlug = "quiet-dj";
    public const string CorruptFaceEntrySlug = "corrupt-face-dj";

    static readonly byte[] FaceBytes = TestImages.CreatePng(512, 512);
    static readonly byte[] CorruptFaceBytes = "not-a-real-png-at-all"u8.ToArray();

    static string CardJson(string name) => $$"""
        {
          "schemaVersion": 1,
          "name": "{{name}}",
          "tagline": "",
          "soul": "",
          "quirks": [],
          "voice": { "engine": "kokoro", "voiceId": "af_heart", "pace": 1.0, "language": "en" },
          "energyDisposition": 0,
          "lore": [],
          "corrections": []
        }
        """;

    static string MetaJson(string description) => $$"""
        {
          "author": "Test Fixture",
          "description": "{{description}}",
          "audience": "everyone",
          "added": "2026-08-16"
        }
        """;

    static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    static string IndexJson() => $$"""
        { "generatedAt": "2026-08-16", "entries": [
          { "slug": "{{FacedEntrySlug}}", "kind": "persona", "audience": "everyone",
            "manifest": { "path": "entries/{{FacedEntrySlug}}/{{FacedEntrySlug}}.persona.json", "sha256": "{{Sha256Hex(CardJson("Midnight Mabel"))}}" },
            "meta": { "path": "entries/{{FacedEntrySlug}}/{{FacedEntrySlug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson("Wears its own sidecar face."))}}" },
            "assets": [
              { "path": "entries/{{FacedEntrySlug}}/{{FacedEntrySlug}}.avatar.png", "sha256": "{{Sha256Hex(FaceBytes)}}", "bytes": {{FaceBytes.Length}} }
            ] },
          { "slug": "{{FacelessEntrySlug}}", "kind": "persona", "audience": "everyone",
            "manifest": { "path": "entries/{{FacelessEntrySlug}}/{{FacelessEntrySlug}}.persona.json", "sha256": "{{Sha256Hex(CardJson("Quiet DJ"))}}" },
            "meta": { "path": "entries/{{FacelessEntrySlug}}/{{FacelessEntrySlug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson("Carries no sidecar face."))}}" } },
          { "slug": "{{CorruptFaceEntrySlug}}", "kind": "persona", "audience": "everyone",
            "manifest": { "path": "entries/{{CorruptFaceEntrySlug}}/{{CorruptFaceEntrySlug}}.persona.json", "sha256": "{{Sha256Hex(CardJson("Corrupt Face DJ"))}}" },
            "meta": { "path": "entries/{{CorruptFaceEntrySlug}}/{{CorruptFaceEntrySlug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson("A declared face that is not really an image."))}}" },
            "assets": [
              { "path": "entries/{{CorruptFaceEntrySlug}}/{{CorruptFaceEntrySlug}}.avatar.png", "sha256": "{{Sha256Hex(CorruptFaceBytes)}}", "bytes": {{CorruptFaceBytes.Length}} }
            ] } ] }
        """;

    /// <summary>Serves every fixture document at its own resolved URL, 404 for anything else — every
    /// request is still recorded on <see cref="FakeHttpMessageHandler.Requests"/> (the
    /// <c>FileUploadImportAcceptsCardJsonOnly</c> fact's own "zero requests reached the catalog origin"
    /// proof).</summary>
    public static FakeHttpMessageHandler BuildRoutedHandler()
    {
        var routes = new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(),
            [Directory + "entries/" + FacedEntrySlug + "/" + FacedEntrySlug + ".persona.json"] = CardJson("Midnight Mabel"),
            [Directory + "entries/" + FacedEntrySlug + "/" + FacedEntrySlug + ".meta.json"] = MetaJson("Wears its own sidecar face."),
            [Directory + "entries/" + FacelessEntrySlug + "/" + FacelessEntrySlug + ".persona.json"] = CardJson("Quiet DJ"),
            [Directory + "entries/" + FacelessEntrySlug + "/" + FacelessEntrySlug + ".meta.json"] = MetaJson("Carries no sidecar face."),
            [Directory + "entries/" + CorruptFaceEntrySlug + "/" + CorruptFaceEntrySlug + ".persona.json"] = CardJson("Corrupt Face DJ"),
            [Directory + "entries/" + CorruptFaceEntrySlug + "/" + CorruptFaceEntrySlug + ".meta.json"] = MetaJson("A declared face that is not really an image."),
        };
        var faceAssetUrl = Directory + "entries/" + FacedEntrySlug + "/" + FacedEntrySlug + ".avatar.png";
        var corruptFaceAssetUrl = Directory + "entries/" + CorruptFaceEntrySlug + "/" + CorruptFaceEntrySlug + ".avatar.png";

        return new((request, _) =>
        {
            var absoluteUri = request.RequestUri!.AbsoluteUri;
            if (absoluteUri == faceAssetUrl)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(FaceBytes) });
            if (absoluteUri == corruptFaceAssetUrl)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(CorruptFaceBytes) });

            return Task.FromResult(
                routes.TryGetValue(absoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
    }

    static PersonaCard BuildCard(string name) =>
        new(
            SchemaVersion: PersonaCard.CurrentSchemaVersion,
            Name: name,
            Tagline: "A voice for the small hours.",
            Soul: "Late-night gravity.",
            Quirks: [],
            // Empty VoiceId short-circuits PersonaController.ResolveVoiceAsync before it ever reaches
            // ITtsVoiceLister — no fake/override needed for this file's routes (mirrors
            // Story237_ImportProvenance.cs's own ProvenanceFixture.BuildCard).
            Voice: new VoiceSpec(Engine: "", VoiceId: "", Pace: 1.0, Language: "en"),
            EnergyDisposition: 0,
            Lore: [],
            Corrections: [],
            Taste: null);

    /// <summary>POSTs a freshly-built card to <c>POST /api/personas/{slug}/import</c>, with or without
    /// <c>?catalogSlug=</c> — the one request shape every Fact in this file drives.</summary>
    public static Task<HttpResponseMessage> PostCardAsync(HttpClient client, string routeSlug, string? catalogSlug)
    {
        var url = catalogSlug is null
            ? $"/api/personas/{routeSlug}/import"
            : $"/api/personas/{routeSlug}/import?catalogSlug={Uri.EscapeDataString(catalogSlug)}";
        var card = BuildCard(routeSlug);
        return client.PostAsync(url, new StringContent(PersonaCardSerializer.Serialize(card), Encoding.UTF8, "application/json"));
    }
}

/// <summary>
/// Wraps a <see cref="FakePersonaAvatarStore"/> so <see cref="UpsertAsync"/> ALWAYS throws — models
/// the class of store-side fault <c>CatalogPersonaAvatarInstaller.InstallIfPresentAsync</c>'s own
/// outer try/catch exists to catch (a concurrent persona-delete FK race, a dropped connection — any
/// such failure lands the same way here, since that catch is by type <see cref="Exception"/>, never
/// by any Npgsql-specific shape). Reads/deletes pass straight through to the wrapped store, which
/// stays permanently empty since the one write this file's throwing Fact drives never actually lands
/// a row — the assertion that proves it reads the SAME wrapped instance back directly.
/// </summary>
file sealed class ThrowingUpsertPersonaAvatarStore : IPersonaAvatarStore
{
    readonly FakePersonaAvatarStore inner = new();

    public Task<PersonaAvatar?> GetByPersonaIdAsync(long personaId, CancellationToken ct) =>
        inner.GetByPersonaIdAsync(personaId, ct);

    public Task<PersonaAvatar?> GetByTokenAsync(string token, CancellationToken ct) =>
        inner.GetByTokenAsync(token, ct);

    public Task UpsertAsync(PersonaAvatarInput avatar, CancellationToken ct) =>
        throw new InvalidOperationException(
            "Simulated persona-avatar store fault — the class of failure CatalogPersonaAvatarInstaller's own outer try/catch exists to catch.");

    public Task<bool> DeleteAsync(long personaId, CancellationToken ct) =>
        inner.DeleteAsync(personaId, ct);
}

// STORY-209 — Card import lands safely on a stranger's station
//
// BDD specification — xUnit (SPEC F79.2, F79.3, F79.4, F79.6, F79.7). PLAN T67 wires the import
// endpoint; T69 is the cross-station round-trip proof (ScenarioFreshImport below stays pending —
// that scenario needs a REAL export-then-import round trip across two stations, T69's own job).
//
// Mirrors Story208's WebApplicationFactory idiom exactly: real routing/auth/content-negotiation
// pipeline, IPersonaImportStore/ITtsVoiceLister replaced by scriptable fakes, no live Postgres.
// FakePersonaImportStore doesn't merely record calls — it REPLICATES the production repository's own
// upsert-by-slug + delete-authored-then-insert semantics (never touching accrued rows), so
// "accrued state survives a re-import" is a fact about the CONTROLLER's request shape reaching the
// store correctly, not an artifact of a fake that happens to keep everything forever.

using System.Net;
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

// ── In-process fakes ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Scriptable <see cref="IPersonaImportStore"/> double that REPLICATES
/// <c>PersonaImportRepository</c>'s own transactional shape in memory: upsert <see cref="BySlug"/> by
/// slug, then replace ONLY the <c>source == Authored</c> rows of <see cref="MemoryRows"/>/
/// <see cref="TasteRows"/> for that persona — any <c>Accrued</c> (or, for taste, <c>Operator</c>) row
/// already present is never touched. <see cref="Calls"/> records every request this store ever saw,
/// so a rejected-import scenario can assert the store was never even reached.
/// </summary>
file sealed class FakePersonaImportStore : IPersonaImportStore
{
    public sealed record StoredPersona(long Id, PersonaCard Card, string LegacyVoice);

    public Dictionary<string, StoredPersona> BySlug { get; } = [];
    public List<PersonaMemoryEntry> MemoryRows { get; } = [];
    public List<PersonaTasteEntry> TasteRows { get; } = [];
    public List<PersonaImportRequest> Calls { get; } = [];

    long nextPersonaId = 100;
    long nextMemoryId = 1000;
    long nextTasteId = 2000;

    public Task<PersonaImportOutcome> ImportAsync(PersonaImportRequest request, CancellationToken ct)
    {
        Calls.Add(request);

        long personaId;
        bool wasCreated;
        if (BySlug.TryGetValue(request.Slug, out var existing))
        {
            personaId = existing.Id;
            wasCreated = false;
        }
        else
        {
            personaId = nextPersonaId++;
            wasCreated = true;
        }

        BySlug[request.Slug] = new StoredPersona(personaId, request.Card, request.LegacyVoice);

        MemoryRows.RemoveAll(r => r.PersonaId == personaId && r.Source == PersonaMemorySource.Authored);
        var now = DateTime.UtcNow;
        MemoryRows.AddRange(request.Card.Lore.Select(content =>
            new PersonaMemoryEntry(nextMemoryId++, personaId, "lore", content, PersonaMemorySource.Authored, 0, null, now)));

        TasteRows.RemoveAll(r => r.PersonaId == personaId && r.Source == PersonaTasteSource.Authored);
        TasteRows.AddRange((request.Card.Taste ?? []).Select(rule =>
            new PersonaTasteEntry(nextTasteId++, personaId, rule, PersonaTasteSource.Authored, now, now)));

        return Task.FromResult<PersonaImportOutcome>(new PersonaImportOutcome.Imported(personaId, wasCreated));
    }
}

/// <summary>Scriptable <see cref="ITtsVoiceLister"/> double — set <see cref="Voices"/> for a reachable
/// engine, or <see cref="Fault"/> to simulate an unreachable one (SPEC F79.4's engine-down case).</summary>
file sealed class FakeTtsVoiceLister : ITtsVoiceLister
{
    public IReadOnlyList<string> Voices { get; set; } = [];
    public Exception? Fault { get; set; }

    public Task<IReadOnlyList<string>> ListVoicesAsync(CancellationToken ct)
    {
        if (Fault is not null)
            throw Fault;

        return Task.FromResult(Voices);
    }
}

// ── WebApplicationFactory ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> that brings up the real HTTP pipeline (routing,
/// auth, the production <c>POST /api/personas/{slug}/import</c> route) while replacing
/// <see cref="IPersonaImportStore"/>/<see cref="ITtsVoiceLister"/> with scriptable fakes — mirrors
/// Story208's <c>PersonaExportWebFactory</c>. No live Postgres: <c>ConnectionStrings:Station</c>/
/// <c>Library</c> are left at their unreachable defaults, and every hosted service that would
/// otherwise touch them is removed.
/// </summary>
file sealed class PersonaImportWebFactory(
    FakePersonaImportStore? importStore = null,
    FakeTtsVoiceLister? voiceLister = null) : WebApplicationFactory<Program>
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
            services.AddSingleton<IPersonaImportStore>(importStore ?? new FakePersonaImportStore());

            services.RemoveAll<ITtsVoiceLister>();
            services.AddSingleton<ITtsVoiceLister>(voiceLister ?? new FakeTtsVoiceLister());
        });
    }
}

// ── Shared fixture ─────────────────────────────────────────────────────────────────────────────────

file static class PersonaImportFixture
{
    public const string Slug = "dj-nova";

    public static PersonaCard BuildCard(
        string tagline = "Late-night gravity.",
        string voiceId = "af_heart",
        int schemaVersion = 1,
        IReadOnlyList<string>? lore = null,
        IReadOnlyList<TasteRule>? taste = null) =>
        new(
            SchemaVersion: schemaVersion,
            Name: "DJ Nova",
            Tagline: tagline,
            Soul: "A slow-orbit voice for the small hours.",
            Quirks: ["Never says goodnight twice."],
            Voice: new VoiceSpec(Engine: "kokoro", VoiceId: voiceId, Pace: 1.0, Language: "en"),
            EnergyDisposition: -0.2,
            Lore: lore ?? ["Once got lost inside a 20-minute Tangerine Dream fade."],
            Corrections: [new PersonaCorrection("Nova", "NOH-vah")],
            Taste: taste ?? [DefaultRule]);

    public static readonly TasteRule DefaultRule =
        new(new TastePredicate("Led Zeppelin", null, null), new TasteContext([DayOfWeek.Sunday], 6, 12), 0.75);
}

/// <summary>
/// Shared arrangement for <c>ScenarioReImportOntoALivingPersona</c> below (file-scoped: its return
/// type carries the file-scoped <see cref="FakePersonaImportStore"/>, which cannot appear in a member
/// signature of a non-file-scoped type — mirrors Story208's own <c>PersonaExportFixture</c>).
/// </summary>
file static class PersonaReImportFixture
{
    public const string Slug = PersonaImportFixture.Slug;
    public const long PersonaId = 42;

    public static readonly TasteRule OldAuthoredRule =
        new(new TastePredicate("Old Artist", null, null), new TasteContext([], null, null), 0.1);
    public static readonly TasteRule AccruedRule =
        new(new TastePredicate("Nickelback", null, null), new TasteContext([], null, null), -0.9);

    /// <summary>
    /// Seeds a persona holding an OLD authored card plus BOTH an authored and an accrued row in each
    /// of persona_memory/persona_taste — the arrangement every fact in
    /// <c>ScenarioReImportOntoALivingPersona</c> shares.
    /// </summary>
    public static FakePersonaImportStore SeedLivingPersona()
    {
        var store = new FakePersonaImportStore();
        var oldCard = PersonaImportFixture.BuildCard(
            tagline: "OLD tagline", lore: ["OLD lore"], taste: [OldAuthoredRule]);
        store.BySlug[Slug] = new FakePersonaImportStore.StoredPersona(PersonaId, oldCard, "af_heart");

        var now = DateTime.UtcNow;
        store.MemoryRows.Add(new PersonaMemoryEntry(1, PersonaId, "lore", "OLD lore", PersonaMemorySource.Authored, 0, null, now));
        store.MemoryRows.Add(new PersonaMemoryEntry(2, PersonaId, "bit", "Accrued bit must survive.", PersonaMemorySource.Accrued, 3, now, now));

        store.TasteRows.Add(new PersonaTasteEntry(1, PersonaId, OldAuthoredRule, PersonaTasteSource.Authored, now, now));
        store.TasteRows.Add(new PersonaTasteEntry(2, PersonaId, AccruedRule, PersonaTasteSource.Accrued, now, now));

        return store;
    }
}

// ── Specs ──────────────────────────────────────────────────────────────────────────────────────────

public static class FeaturePersonaCardImport
{
    /// <summary>Logs in with the factory's fixed test password and returns the now-authenticated client.</summary>
    static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = PersonaImportWebFactory.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    static Task<HttpResponseMessage> PostCardAsync(HttpClient client, string slug, PersonaCard card) =>
        PostRawAsync(client, slug, PersonaCardSerializer.Serialize(card));

    static Task<HttpResponseMessage> PostRawAsync(HttpClient client, string slug, string body) =>
        client.PostAsync($"/api/personas/{slug}/import", new StringContent(body, Encoding.UTF8, "application/json"));

    public static class ScenarioFreshImport
    {
        // Arrange (T67/T69): an export produced from a seeded "station A" persona (character,
        // voice, pronunciations, signature taste) imported into a fresh station via the API.

        [Fact(Skip = "Pending T69 — see docs/PLAN.md")]
        public static void ImportedPersonaCarriesTheSameCharacterFields()
        {
            // soul/quirks/tagline byte-equal after round-trip (F79.7)
            Assert.Fail("pending T69");
        }

        [Fact(Skip = "Pending T69 — see docs/PLAN.md")]
        public static void ImportedPersonaCarriesTheSameVoiceSettings()
        {
            Assert.Fail("pending T69");
        }

        [Fact(Skip = "Pending T69 — see docs/PLAN.md")]
        public static void ImportedPersonaCarriesThePronunciations()
        {
            // card corrections present and merged UNDER station rules (F71.7 unchanged)
            Assert.Fail("pending T69");
        }

        [Fact(Skip = "Pending T69 — see docs/PLAN.md")]
        public static void ImportedPersonaCarriesTheSignatureTaste()
        {
            // authored taste rows exist with source='authored' (F79.3)
            Assert.Fail("pending T69");
        }

        [Fact(Skip = "Pending T69 — see docs/PLAN.md")]
        public static void ImportedPersonaHasEmptyAccruedMemory()
        {
            Assert.Fail("pending T69");
        }
    }

    // ---------------------------------------------------------------------
    // Re-import onto a persona that already has accrued state
    // ---------------------------------------------------------------------

    public sealed class ScenarioReImportOntoALivingPersona
    {
        const string Slug = PersonaImportFixture.Slug;
        const long PersonaId = PersonaReImportFixture.PersonaId;

        [Fact]
        public async Task CardFieldsAreReplacedByTheUpdate()
        {
            var store = PersonaReImportFixture.SeedLivingPersona();
            await using var factory = new PersonaImportWebFactory(store);
            var client = await LoggedInClientAsync(factory);

            var response = await PostCardAsync(client, Slug, PersonaImportFixture.BuildCard(tagline: "NEW tagline"));

            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            Assert.Equal("NEW tagline", store.BySlug[Slug].Card.Tagline);
        }

        [Fact]
        public async Task AuthoredRowsAreUpserted()
        {
            var store = PersonaReImportFixture.SeedLivingPersona();
            await using var factory = new PersonaImportWebFactory(store);
            var client = await LoggedInClientAsync(factory);

            var newRule = new TasteRule(new TastePredicate("New Artist", null, null), new TasteContext([], null, null), 0.4);
            var response = await PostCardAsync(
                client, Slug, PersonaImportFixture.BuildCard(lore: ["NEW lore"], taste: [newRule]));

            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

            var authoredMemory = store.MemoryRows.Where(r => r.PersonaId == PersonaId && r.Source == PersonaMemorySource.Authored).ToList();
            var authoredTaste = store.TasteRows.Where(r => r.PersonaId == PersonaId && r.Source == PersonaTasteSource.Authored).ToList();

            Assert.Equal(["NEW lore"], authoredMemory.Select(r => r.Content));
            // Assert.Equivalent, not Assert.Equal: TasteContext.DaysOfWeek is an IReadOnlyList<DayOfWeek>
            // — record equality on that interface-typed member is reference equality, and the round
            // trip through JSON deserialization (List<DayOfWeek>) never reference-equals a
            // hand-built array literal even when both are empty. Equivalent compares structurally.
            Assert.Equivalent(new[] { newRule }, authoredTaste.Select(r => r.Rule).ToArray());
        }

        [Fact]
        public async Task EveryAccruedRowSurvivesUntouched()
        {
            var store = PersonaReImportFixture.SeedLivingPersona();
            var accruedMemoryBefore = store.MemoryRows.Single(r => r.Source == PersonaMemorySource.Accrued);
            var accruedTasteBefore = store.TasteRows.Single(r => r.Source == PersonaTasteSource.Accrued);

            await using var factory = new PersonaImportWebFactory(store);
            var client = await LoggedInClientAsync(factory);

            var response = await PostCardAsync(client, Slug, PersonaImportFixture.BuildCard(tagline: "NEW tagline"));

            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            Assert.Contains(accruedMemoryBefore, store.MemoryRows);
            Assert.Contains(accruedTasteBefore, store.TasteRows);
        }
    }

    // ---------------------------------------------------------------------
    // Voice resolution — the card names a voice this station doesn't have
    // ---------------------------------------------------------------------

    public sealed class ScenarioUnknownVoiceResolution
    {
        [Fact]
        public async Task ImportSucceedsWithTheStationDefaultVoice()
        {
            var store = new FakePersonaImportStore();
            var voiceLister = new FakeTtsVoiceLister { Voices = ["af_heart", "af_bella"] };
            await using var factory = new PersonaImportWebFactory(store, voiceLister);
            var client = await LoggedInClientAsync(factory);

            var response = await PostCardAsync(
                client, "dj-ghost", PersonaImportFixture.BuildCard(voiceId: "af_ghost"));

            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            Assert.Equal(string.Empty, store.BySlug["dj-ghost"].LegacyVoice);
        }

        [Fact]
        public async Task AVisibleWarningNamesTheUnresolvedVoice()
        {
            var store = new FakePersonaImportStore();
            var voiceLister = new FakeTtsVoiceLister { Voices = ["af_heart"] };
            await using var factory = new PersonaImportWebFactory(store, voiceLister);
            var client = await LoggedInClientAsync(factory);

            var response = await PostCardAsync(
                client, "dj-ghost", PersonaImportFixture.BuildCard(voiceId: "af_ghost"));
            var body = await response.Content.ReadFromJsonAsync<PersonaImportResponse>();

            Assert.NotNull(body);
            Assert.Contains(body.Warnings, w => w.Contains("af_ghost", StringComparison.Ordinal));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — fail-closed validation, each gate ahead of the transactional write
    // ---------------------------------------------------------------------

    public sealed class SadPathFailClosedValidation
    {
        [Fact]
        public async Task NewerSchemaMajorIsRejectedNamingBothVersions()
        {
            var store = new FakePersonaImportStore();
            await using var factory = new PersonaImportWebFactory(store);
            var client = await LoggedInClientAsync(factory);

            var response = await PostCardAsync(client, "dj-future", PersonaImportFixture.BuildCard(schemaVersion: 2));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("2", body, StringComparison.Ordinal);
            Assert.Contains(PersonaCard.CurrentSchemaVersion.ToString(), body, StringComparison.Ordinal);
            Assert.Empty(store.Calls);
        }

        [Fact]
        public async Task OversizedPayloadIsRejected()
        {
            var store = new FakePersonaImportStore();
            await using var factory = new PersonaImportWebFactory(store);
            var client = await LoggedInClientAsync(factory);

            var oversized = new string('a', 300 * 1024); // > 256 KB
            var response = await PostRawAsync(client, "dj-oversized", oversized);

            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
            Assert.Empty(store.Calls);
        }

        [Fact]
        public async Task MalformedPayloadIsRejected()
        {
            var store = new FakePersonaImportStore();
            await using var factory = new PersonaImportWebFactory(store);
            var client = await LoggedInClientAsync(factory);

            var notJsonResponse = await PostRawAsync(client, "dj-not-json", "{ this is not valid json");
            Assert.Equal(HttpStatusCode.BadRequest, notJsonResponse.StatusCode);

            // Carried T56 review note: TasteRule.Weight's [-1, 1] bound throws
            // ArgumentOutOfRangeException from inside the record's own constructor, on EVERY
            // construction path including deserialization — the import must map this to 400, never
            // let it surface as an unhandled 500.
            var validJson = PersonaCardSerializer.Serialize(PersonaImportFixture.BuildCard());
            var outOfRangeJson = validJson.Replace("\"weight\":0.75", "\"weight\":5.0", StringComparison.Ordinal);
            Assert.NotEqual(validJson, outOfRangeJson); // guards the Replace actually matched

            var outOfRangeResponse = await PostRawAsync(client, "dj-out-of-range", outOfRangeJson);
            Assert.Equal(HttpStatusCode.BadRequest, outOfRangeResponse.StatusCode);

            Assert.Empty(store.Calls);
        }

        [Fact]
        public async Task BadSlugFormatIsRejected()
        {
            var store = new FakePersonaImportStore();
            await using var factory = new PersonaImportWebFactory(store);
            var client = await LoggedInClientAsync(factory);

            var response = await PostCardAsync(client, "DJ_Nova", PersonaImportFixture.BuildCard());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(store.Calls);
        }

        [Fact]
        public async Task ARejectedImportChangesNothing()
        {
            var store = new FakePersonaImportStore();
            await using var factory = new PersonaImportWebFactory(store);
            var client = await LoggedInClientAsync(factory);

            await PostCardAsync(client, "DJ_Nova", PersonaImportFixture.BuildCard());
            await PostCardAsync(client, "dj-newer-major", PersonaImportFixture.BuildCard(schemaVersion: 2));
            await PostRawAsync(client, "dj-not-json", "not json at all");
            await PostRawAsync(client, "dj-oversized", new string('a', 300 * 1024));

            Assert.Empty(store.Calls);
            Assert.Empty(store.BySlug);
            Assert.Empty(store.MemoryRows);
            Assert.Empty(store.TasteRows);
        }
    }
}

// STORY-231 — A shelf is born: the golden-card parity pin (SPEC F89.1, PLAN T107)
//
// BDD specification — xUnit. Both repos pin the SAME artifact: genwave-catalog's
// fixtures/golden.persona.json (a real F79 export) is copied byte-for-byte into
// Fixtures/golden.persona.json here (T107; see Fixtures/README.md for the sync contract) and must
// import through the real F79 endpoint unmodified — if either side drifts, exactly one
// deterministic fact goes red, no cross-repo network involved.
//
// ScenarioGoldenFixtureImports mirrors Story237_ImportProvenance.cs's WebApplicationFactory idiom:
// real routing/auth/content-negotiation pipeline, IPersonaStore/IPersonaImportStore replaced by ONE
// scriptable fake (FakeGoldenCardStation below) rather than two independent ones, for the same
// reason Story237's own header gives — production has exactly one underlying station.persona table
// behind both repository seams. Unlike Story237, every fact here only ever imports (never re-reads
// through IPersonaStore), so FakeGoldenCardStation additionally records every PersonaImportRequest
// it receives (mirrors Story209_PersonaImport.cs's own FakePersonaImportStore.Calls) — the taste-
// rule and provenance facts assert on that captured request, the most honest seam available: the
// PersonaCard/ImportedFrom fields the controller decided to write, not a reimplementation of the
// store's own stamping logic.
//
// ScenarioByteFidelity pins the T95 property (Story208_CardTasteContract.cs's
// CardRoundTripsByteStableWithTaste) against the fixture FILE's own bytes, not a card built in C# —
// a hand-edit of Fixtures/golden.persona.json that breaks PersonaCardSerializer byte-exactness goes
// red here, app-side, independent of the catalog repo's own schema-validation copy of this fact.

using System.Net.Http.Headers;
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

// ── Fixture file access ───────────────────────────────────────────────────────────────────────────

/// <summary>
/// Locates and reads <c>Fixtures/golden.persona.json</c> from its SOURCE location (not a build
/// output copy) — mirrors <c>KokoroFixture.LocateComposeFile</c>'s own convention for a non-code file
/// a test needs at runtime: walk up from <see cref="AppContext.BaseDirectory"/> until the repo root
/// (<c>GenWave.sln</c>) is found, then address the file by its fixed source-tree path.
/// </summary>
file static class GoldenFixtureFile
{
    /// <summary>The exact bytes committed at <c>Fixtures/golden.persona.json</c> — read fresh on every
    /// call (no shared mutable state across facts, mirrors this file's per-fact-arrangement idiom).</summary>
    public static byte[] ReadBytes() => File.ReadAllBytes(LocatePath());

    static string LocatePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("repo root (GenWave.sln) not found");

        return Path.Combine(dir.FullName, "tests", "GenWave.Host.Tests", "Fixtures", "golden.persona.json");
    }
}

// ── In-process fakes ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One scriptable double standing in for BOTH <see cref="IPersonaStore"/> and
/// <see cref="IPersonaImportStore"/> — see the file header for why. <see cref="Calls"/> records every
/// <see cref="PersonaImportRequest"/> this store ever saw (mirrors Story209's
/// <c>FakePersonaImportStore.Calls</c>), so a fact can assert on the exact card/provenance the
/// controller decided to write without re-deriving it from a persisted row.
/// </summary>
file sealed class FakeGoldenCardStation : IPersonaStore, IPersonaImportStore
{
    readonly Dictionary<long, Persona> byId = [];
    readonly Dictionary<string, long> idBySlug = new(StringComparer.Ordinal);
    long nextId = 1;

    public List<PersonaImportRequest> Calls { get; } = [];

    public Task<IReadOnlyList<Persona>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Persona>>(byId.Values.OrderBy(p => p.Name, StringComparer.Ordinal).ToList());

    public Task<Persona?> GetByIdAsync(long id, CancellationToken ct) =>
        Task.FromResult(byId.TryGetValue(id, out var persona) ? persona : null);

    public Task<PersonaWriteResult> CreateAsync(PersonaDraft draft, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story231's scenarios.");

    public Task<PersonaWriteResult> UpdateAsync(long id, PersonaDraft draft, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story231's scenarios.");

    public Task<PersonaWriteResult> DeleteAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story231's scenarios.");

    public Task<PersonaCard?> GetCardByIdAsync(long id, CancellationToken ct) =>
        Task.FromResult<PersonaCard?>(null);

    public Task<long?> GetIdBySlugAsync(string slug, CancellationToken ct) =>
        Task.FromResult(idBySlug.TryGetValue(slug, out var id) ? id : (long?)null);

    /// <summary>Mirrors <c>PersonaImportRepository.ImportAsync</c>'s insert shape (this file never
    /// re-imports the same slug, so the upsert branch Story237's own fake exercises is unneeded here)
    /// — stamps <c>ImportedFrom</c>/<c>ImportedAt</c> unconditionally, exactly like production.</summary>
    public Task<PersonaImportOutcome> ImportAsync(PersonaImportRequest request, CancellationToken ct)
    {
        Calls.Add(request);

        var now = DateTime.UtcNow;
        var id = nextId++;
        idBySlug[request.Slug] = id;
        byId[id] = new Persona(id, request.Card.Name, "", "", request.LegacyVoice, now, now, request.ImportedFrom, now);
        return Task.FromResult<PersonaImportOutcome>(new PersonaImportOutcome.Imported(id, WasCreated: true));
    }
}

/// <summary>Scriptable <see cref="ITtsVoiceLister"/> double — mirrors Story209's own. The golden
/// card's voice id (<c>af_heart</c>) is non-empty, so <c>PersonaController.ResolveVoiceAsync</c> always
/// reaches this seam (unlike Story237's empty-VoiceId cards, which short-circuit before it).</summary>
file sealed class FakeTtsVoiceLister : ITtsVoiceLister
{
    public IReadOnlyList<string> Voices { get; set; } = [];

    public Task<IReadOnlyList<string>> ListVoicesAsync(CancellationToken ct) => Task.FromResult(Voices);
}

// ── WebApplicationFactory ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Brings up the real HTTP pipeline (routing, auth, the production import route) over ONE shared
/// <see cref="FakeGoldenCardStation"/> — mirrors Story237's <c>PersonaProvenanceWebFactory</c>. No live
/// Postgres: <c>ConnectionStrings:Station</c>/<c>Library</c> are left at their unreachable defaults;
/// every hosted service that would otherwise touch them is removed.
/// </summary>
file sealed class GoldenCardWebFactory(FakeGoldenCardStation store) : WebApplicationFactory<Program>
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

            services.RemoveAll<ITtsVoiceLister>();
            services.AddSingleton<ITtsVoiceLister>(new FakeTtsVoiceLister { Voices = ["af_heart"] });
        });
    }
}

// ── Specs ──────────────────────────────────────────────────────────────────────────────────────────

public static class FeatureGoldenCardParity
{
    public sealed class ScenarioGoldenFixtureImports
    {
        // Given Fixtures/golden.persona.json's raw bytes, byte-for-byte from the catalog repo,
        // When they are POSTed, unmodified and with no catalogSlug, to the real F79 import endpoint.

        const string Slug = "golden-dj";

        static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
        {
            var client = factory.CreateClient();
            var login = await client.PostAsJsonAsync("/api/auth/login", new { password = GoldenCardWebFactory.Password });
            Assert.Equal(System.Net.HttpStatusCode.NoContent, login.StatusCode);
            return client;
        }

        static Task<HttpResponseMessage> PostGoldenCardAsync(HttpClient client)
        {
            var content = new ByteArrayContent(GoldenFixtureFile.ReadBytes());
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return client.PostAsync($"/api/personas/{Slug}/import", content);
        }

        [Fact]
        public async Task GoldenCardImportsUnmodified()
        {
            var store = new FakeGoldenCardStation();
            await using var factory = new GoldenCardWebFactory(store);
            var client = await LoggedInClientAsync(factory);

            var response = await PostGoldenCardAsync(client);

            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }

        // The golden card's own two authored taste rules (SPEC F79.3) — hand-transcribed from
        // Fixtures/golden.persona.json's own bytes, NOT re-derived from the serializer, so a content
        // drift in either the fixture or the import path is what turns one of the two facts below
        // red, never a tautology against whatever the fixture happens to say today.
        static readonly TasteRule SundayZeppelinRule = new(
            new TastePredicate(Artist: "Led Zeppelin", Genre: null, Tag: null),
            new TasteContext(DaysOfWeek: [DayOfWeek.Sunday], StartHour: 6, EndHour: 12),
            Weight: 0.75);

        static readonly TasteRule AmbientSmallHoursRule = new(
            new TastePredicate(Artist: null, Genre: "ambient", Tag: null),
            new TasteContext(DaysOfWeek: [], StartHour: 1, EndHour: 5),
            Weight: 0.9);

        [Fact]
        public async Task ImportedGoldenPersonaCarriesTheSundayZeppelinRule()
        {
            var store = new FakeGoldenCardStation();
            await using var factory = new GoldenCardWebFactory(store);
            var client = await LoggedInClientAsync(factory);

            var response = await PostGoldenCardAsync(client);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

            var taste = store.Calls.Single().Card.Taste ?? [];
            // Assert.Equivalent, not Assert.Equal: TasteContext.DaysOfWeek is an
            // IReadOnlyList<DayOfWeek> — record equality on that interface-typed member is reference
            // equality (Story209_PersonaImport.cs's own AuthoredRowsAreUpserted carries the same note);
            // Equivalent compares structurally. strict: true — non-strict is superset-blind on
            // collections (an actual DaysOfWeek widened to [Sunday, Saturday] would still "match" a
            // non-strict [Sunday] expectation), so strict is what actually pins the rule's shape.
            Assert.Equivalent(SundayZeppelinRule, Assert.Single(taste, r => r.Predicate.Artist == "Led Zeppelin"), strict: true);
        }

        [Fact]
        public async Task ImportedGoldenPersonaCarriesTheAmbientSmallHoursRule()
        {
            var store = new FakeGoldenCardStation();
            await using var factory = new GoldenCardWebFactory(store);
            var client = await LoggedInClientAsync(factory);

            var response = await PostGoldenCardAsync(client);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

            var taste = store.Calls.Single().Card.Taste ?? [];
            // strict: true — non-strict treats this rule's empty DaysOfWeek as a subset of ANY actual
            // day gate, so it would pass even if the import stopped carrying the empty-list "every
            // day" shape at all.
            Assert.Equivalent(AmbientSmallHoursRule, Assert.Single(taste, r => r.Predicate.Genre == "ambient"), strict: true);
        }

        [Fact]
        public async Task GoldenCardStampsFileProvenance()
        {
            var store = new FakeGoldenCardStation();
            await using var factory = new GoldenCardWebFactory(store);
            var client = await LoggedInClientAsync(factory);

            var response = await PostGoldenCardAsync(client);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

            // No catalogSlug on the request above ⇒ PersonaController.Import's own default applies
            // (SPEC F90.7): file-source provenance, not a catalog entry slug.
            var call = Assert.Single(store.Calls);
            Assert.Equal(PersonaImportRequest.FileSource, call.ImportedFrom);
        }
    }

    public sealed class ScenarioByteFidelity
    {
        // Given Fixtures/golden.persona.json's raw bytes,
        // When they deserialize then re-serialize through PersonaCardSerializer (the T95 property).

        [Fact]
        public void FixtureRoundTripsByteExactlyThroughPersonaCardSerializer()
        {
            var original = Encoding.UTF8.GetString(GoldenFixtureFile.ReadBytes());

            var card = PersonaCardSerializer.Deserialize(original);

            Assert.NotNull(card);
            Assert.Equal(original, PersonaCardSerializer.Serialize(card));
        }
    }
}

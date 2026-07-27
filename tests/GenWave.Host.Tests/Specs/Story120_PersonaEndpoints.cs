// STORY-120 — Persona CRUD (WIRE)
//
// BDD specification — xUnit. Drives the deployed entry points (PersonaController routes) through
// direct controller construction with a fake IPersonaStore at the boundary (mirrors Story112's
// RatingController-spec idiom) — no live stack required; the real-Postgres behavior behind
// IPersonaStore is Story118's job. NO If-Match anywhere — documented F18.6 deviation (single
// writer, no background contender).
//
// The "one active persona setting" scenario this file used to own (Station:Persona:ActiveId,
// delete-clears-active) is RETIRED (SPEC F91.5/F91.9, PLAN T120) — the format-clock schedule
// replaces it; the key's own retirement is Story242_ActiveIdKeyRetired.cs. PersonaController.Delete's
// new F91.9 FK-guard scaffolding (a scheduled persona's delete raises a 409, T121 finishes the real
// shape) is covered in this file's own ScenarioRejectingInvalidWrites — the SQLSTATE→PersonaWriteResult
// mapping itself lives in PersonaRepository (T120 review F4), so this file's fake just returns
// PersonaWriteResult.ScheduledElsewhere directly, exactly like every other IPersonaStore double here.
//
// The two posture negatives (401 without a cookie, 415 without JSON) drive the real HTTP pipeline
// via WebApplicationFactory<Program> (mirrors Story112's RatingApiWebFactory) since they are
// properties of the auth/routing middleware, not of the controller's own logic.

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Options;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Scriptable, call-recording <see cref="IPersonaStore"/> double. Mirrors Story112's
/// <c>FakeMediaRating</c>: returns the configured outcome from each method and records every
/// call's arguments so a scenario can assert what <see cref="PersonaController"/> passed through.
/// </summary>
file sealed class FakePersonaStore : IPersonaStore
{
    public IReadOnlyList<Persona> AllResult { get; set; } = [];
    public Persona? GetByIdResult { get; set; }
    public PersonaWriteResult CreateResult { get; set; } =
        new PersonaWriteResult.Created(new Persona(1, "Unused", "", "", "", DateTime.UtcNow, DateTime.UtcNow));
    public PersonaWriteResult UpdateResult { get; set; } =
        new PersonaWriteResult.Updated(new Persona(1, "Unused", "", "", "", DateTime.UtcNow, DateTime.UtcNow));
    public PersonaWriteResult DeleteResult { get; set; } = new PersonaWriteResult.Deleted();

    public List<long> GetByIdCalls { get; } = [];
    public List<PersonaDraft> CreateCalls { get; } = [];
    public List<(long Id, PersonaDraft Draft)> UpdateCalls { get; } = [];
    public List<long> DeleteCalls { get; } = [];

    public Task<IReadOnlyList<Persona>> GetAllAsync(CancellationToken ct) => Task.FromResult(AllResult);

    public Task<Persona?> GetByIdAsync(long id, CancellationToken ct)
    {
        GetByIdCalls.Add(id);
        return Task.FromResult(GetByIdResult);
    }

    public Task<PersonaWriteResult> CreateAsync(PersonaDraft draft, CancellationToken ct)
    {
        CreateCalls.Add(draft);
        return Task.FromResult(CreateResult);
    }

    public Task<PersonaWriteResult> UpdateAsync(long id, PersonaDraft draft, CancellationToken ct)
    {
        UpdateCalls.Add((id, draft));
        return Task.FromResult(UpdateResult);
    }

    public Task<PersonaWriteResult> DeleteAsync(long id, CancellationToken ct)
    {
        DeleteCalls.Add(id);
        return Task.FromResult(DeleteResult);
    }

    // Not exercised by Story120's CRUD scenarios (none of them read a card) — a plain null keeps
    // this double satisfying IPersonaStore without scripting a path nothing here calls.
    public Task<PersonaCard?> GetCardByIdAsync(long id, CancellationToken ct) =>
        Task.FromResult<PersonaCard?>(null);

    // Export (T66) is Story208's own coverage, not Story120's CRUD scenarios.
    public Task<long?> GetIdBySlugAsync(string slug, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story120's CRUD scenarios.");
}

/// <summary>Unused-by-CRUD <see cref="IPersonaMemory"/> double (T66 added this constructor
/// dependency to <see cref="PersonaController"/> for its export endpoint; none of this file's CRUD
/// scenarios call it).</summary>
file sealed class NotUsedPersonaMemory : IPersonaMemory
{
    public Task<long> RecordAsync(long personaId, string kind, string content, PersonaMemorySource source, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story120's CRUD scenarios.");

    public Task MarkAiredAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story120's CRUD scenarios.");

    public Task<IReadOnlyList<PersonaMemoryEntry>> RecallAsync(long personaId, RecallSpec spec, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story120's CRUD scenarios.");

    public Task<IReadOnlyList<PersonaMemoryEntry>> ListAsync(long personaId, PersonaMemorySource source, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story120's CRUD scenarios.");
}

/// <summary>Unused-by-CRUD <see cref="IPersonaTasteReader"/> double — same reason as
/// <see cref="NotUsedPersonaMemory"/> above.</summary>
file sealed class NotUsedPersonaTasteReader : IPersonaTasteReader
{
    public Task<IReadOnlyList<PersonaTasteEntry>> ListAsync(long personaId, PersonaTasteSource? source, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story120's CRUD scenarios.");
}

/// <summary>
/// Minimal <see cref="IOptionsMonitor{T}"/> that returns <see cref="CurrentValue"/> on every read.
/// File-scoped: a file-scoped type cannot cross files, so every spec file with this need defines
/// its own copy (mirrors Story084/Story096's precedent).
/// </summary>
file sealed class FakeOptionsMonitor<T> : IOptionsMonitor<T>
{
    T current;
    internal FakeOptionsMonitor(T initial) => current = initial;
    public T CurrentValue => current;
    public T Get(string? name) => current;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>
/// Unused-by-these-scenarios <see cref="IPersonaPreviewWriter"/> double (T7 added this constructor
/// dependency to <see cref="PersonaController"/> for its preview endpoint; none of this file's CRUD
/// scenarios call it). Throws if a scenario ever does reach it — Story123 owns the real coverage.
/// </summary>
file sealed class NotUsedPersonaPreviewWriter : IPersonaPreviewWriter
{
    public Task<PersonaPreviewResult> WritePreviewAsync(
        SegmentRequest request, Persona? personaOverride, CancellationToken ct) =>
        throw new InvalidOperationException("Not exercised by Story120's CRUD scenarios.");
}

/// <summary>Always-none <see cref="IActivePersonaAccessor"/> double — unused by this file's CRUD scenarios.</summary>
file sealed class NotUsedActivePersonaAccessor : IActivePersonaAccessor
{
    public Task<Persona?> ResolveAsync(CancellationToken ct) => Task.FromResult<Persona?>(null);
}

/// <summary>Always-empty <see cref="IAdminMediaLookup"/> double — unused by this file's CRUD scenarios.</summary>
file sealed class NotUsedAdminMediaLookup : IAdminMediaLookup
{
    public Task<(AdminMediaDto Row, long LibraryId)?> GetByIdWithLibraryAsync(long id, CancellationToken ct) =>
        Task.FromResult<(AdminMediaDto Row, long LibraryId)?>(null);
}

/// <summary>
/// Unused-by-these-scenarios <see cref="IPersonaImportStore"/> double (T67 added this constructor
/// dependency to <see cref="PersonaController"/> for its import endpoint; none of this file's CRUD
/// scenarios call it). Throws if a scenario ever does reach it — Story209 owns the real coverage.
/// </summary>
file sealed class NotUsedPersonaImportStore : IPersonaImportStore
{
    public Task<PersonaImportOutcome> ImportAsync(PersonaImportRequest request, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story120's CRUD scenarios.");
}

/// <summary>Unused-by-these-scenarios <see cref="ITtsVoiceLister"/> double — same reason as
/// <see cref="NotUsedPersonaImportStore"/> above.</summary>
file sealed class NotUsedTtsVoiceLister : ITtsVoiceLister
{
    public Task<IReadOnlyList<string>> ListVoicesAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story120's CRUD scenarios.");
}

// ── WebApplicationFactory for auth/content-type AC tests ─────────────────────────────────────────

/// <summary>
/// Minimal <see cref="WebApplicationFactory{TEntryPoint}"/> that brings up the real HTTP pipeline
/// (routing, auth, content-type negotiation) while removing hosted services that would attempt
/// real Liquidsoap/Postgres connections. Mirrors Story112's <c>RatingApiWebFactory</c>: neither
/// posture scenario ever resolves <see cref="IPersonaStore"/> (401 is rejected by auth middleware,
/// 415 by action-selection) — both happen before <see cref="PersonaController"/> is constructed —
/// so the persona store's connection string is left at its (empty, dev-mode) default.
/// </summary>
file sealed class PersonaApiWebFactory(bool withAdminPassword) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development config provides Station:Id/Name/Voice/Scope/SafeScope and Tts:Endpoint
        // so ValidateOnStart() is satisfied without injecting them manually.
        builder.UseEnvironment("Development");

        // AddMediaLibrary reads the Library connection string at composition time in Program.cs —
        // UseSetting (colon-form) reaches that read (verified empirically), so no process env var
        // is mutated and no other test class can race with this per-instance value. A
        // non-reachable host is fine: neither scenario below ever resolves IMediaCatalog or
        // IPersonaStore — the request is rejected by auth/routing middleware before any controller
        // is constructed.
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");

        if (withAdminPassword)
        {
            builder.UseSetting("Admin:Password", "test-password-x7z");
        }

        builder.ConfigureTestServices(services =>
        {
            // Remove ALL hosted services — no Liquidsoap or DB connections during this test.
            services.RemoveAll<IHostedService>();
        });
    }
}

// ── In-process tests ──────────────────────────────────────────────────────────────────────────────

public static class FeaturePersonaEndpoints
{
    static StationOptions BuildStationOptions() => new()
    {
        Id = "genwave-1",
        Name = "Test Station",
        Voice = "af_heart",
        Scope = new StationScopeOptions { LibraryIds = [1] },
        SafeScope = new StationScopeOptions { LibraryIds = [1] },
    };

    static PersonaController BuildController(
        IPersonaStore store,
        IOptionsMonitor<StationOptions> stationMonitor) =>
        new(
            store, stationMonitor,
            new NotUsedPersonaPreviewWriter(), new NotUsedActivePersonaAccessor(),
            new NotUsedAdminMediaLookup(), new FakeStationScopeProvider(LibraryScope.None),
            new NotUsedPersonaMemory(), new NotUsedPersonaTasteReader(),
            new NotUsedPersonaImportStore(), new NotUsedTtsVoiceLister(),
            NullLogger<PersonaController>.Instance);

    // ---------------------------------------------------------------------
    // HAPPY PATH — CRUD round-trips through the production routes
    // ---------------------------------------------------------------------

    public sealed class ScenarioCrudRoundTrip
    {
        [Fact]
        public async Task PostCreatesAndReturns201WithTheRow()
        {
            // POST /api/personas { name, backstory, style, voice? } → 201 (F35.4, AC1).
            var now = DateTime.UtcNow;
            var created = new Persona(7, "Neon Nightowl", "Spins vinyl til dawn.", "moody, late-night", "af_heart", now, now);
            var store = new FakePersonaStore { CreateResult = new PersonaWriteResult.Created(created) };
            var controller = BuildController(store, new FakeOptionsMonitor<StationOptions>(BuildStationOptions()));

            var result = await controller.Create(
                new PersonaRequest("Neon Nightowl", "Spins vinyl til dawn.", "moody, late-night", "af_heart"),
                CancellationToken.None);

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status201Created, objectResult.StatusCode);
            var dto = Assert.IsType<PersonaDto>(objectResult.Value);
            Assert.Equal(7, dto.Id);
            Assert.Equal("Neon Nightowl", dto.Name);
            var draft = Assert.Single(store.CreateCalls);
            Assert.Equal("Neon Nightowl", draft.Name);
        }

        [Fact]
        public async Task GetListsPersonas()
        {
            // GET /api/personas → 200 [{ id, name, backstory, style, voice }] (F35.4, AC1).
            var now = DateTime.UtcNow;
            var store = new FakePersonaStore
            {
                AllResult =
                [
                    new Persona(1, "Anchor Alice", "", "", "", now, now),
                    new Persona(2, "Night Owl", "Spins vinyl.", "moody", "af_sky", now, now),
                ],
            };
            var controller = BuildController(store, new FakeOptionsMonitor<StationOptions>(BuildStationOptions()));

            var result = await controller.List(CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var dtos = Assert.IsAssignableFrom<IEnumerable<PersonaDto>>(ok.Value).ToList();
            Assert.Equal(2, dtos.Count);
            Assert.Contains(dtos, d => d.Id == 1 && d.Name == "Anchor Alice");
            Assert.Contains(dtos, d => d.Id == 2 && d.Voice == "af_sky");
        }

        [Fact]
        public async Task PatchEditsAndReturns200()
        {
            // No If-Match required (F35.4, AC1).
            var now = DateTime.UtcNow;
            var updated = new Persona(3, "Anchor Alice", "New backstory", "crisp", "", now, now);
            var store = new FakePersonaStore { UpdateResult = new PersonaWriteResult.Updated(updated) };
            var controller = BuildController(store, new FakeOptionsMonitor<StationOptions>(BuildStationOptions()));

            var result = await controller.Update(
                3, new PersonaRequest("Anchor Alice", "New backstory", "crisp", null), CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var dto = Assert.IsType<PersonaDto>(ok.Value);
            Assert.Equal("New backstory", dto.Backstory);
            var call = Assert.Single(store.UpdateCalls);
            Assert.Equal(3, call.Id);
        }

        [Fact]
        public async Task DeleteReturns204()
        {
            // (F35.4, AC1).
            var store = new FakePersonaStore { DeleteResult = new PersonaWriteResult.Deleted() };
            var controller = BuildController(store, new FakeOptionsMonitor<StationOptions>(BuildStationOptions()));

            var result = await controller.Delete(9, CancellationToken.None);

            Assert.IsType<NoContentResult>(result);
            Assert.Equal(9, Assert.Single(store.DeleteCalls));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — validation and auth teeth
    // ---------------------------------------------------------------------

    public sealed class ScenarioRejectingInvalidWrites
    {
        [Fact]
        public async Task BlankNameReturns400()
        {
            // (F35.4, AC5).
            var controller = BuildController(new FakePersonaStore(), new FakeOptionsMonitor<StationOptions>(BuildStationOptions()));

            var result = await controller.Create(new PersonaRequest("   ", null, null, null), CancellationToken.None);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.IsType<ProblemDetails>(badRequest.Value);
        }

        [Fact]
        public async Task DuplicateNameReturns409()
        {
            // (F35.4, AC5).
            var store = new FakePersonaStore { CreateResult = new PersonaWriteResult.NameConflict() };
            var controller = BuildController(store, new FakeOptionsMonitor<StationOptions>(BuildStationOptions()));

            var result = await controller.Create(
                new PersonaRequest("Existing Name", null, null, null), CancellationToken.None);

            var conflict = Assert.IsType<ConflictObjectResult>(result);
            Assert.IsType<ProblemDetails>(conflict.Value);
        }

        [Fact]
        public async Task UnknownIdReturns404()
        {
            // PATCH/DELETE on a missing id (F35.4, AC5).
            var store = new FakePersonaStore
            {
                UpdateResult = new PersonaWriteResult.NotFound(),
                DeleteResult = new PersonaWriteResult.NotFound(),
            };
            var controller = BuildController(store, new FakeOptionsMonitor<StationOptions>(BuildStationOptions()));

            var patchResult = await controller.Update(
                999_999, new PersonaRequest("Anyone", null, null, null), CancellationToken.None);
            Assert.IsType<NotFoundObjectResult>(patchResult);

            var deleteResult = await controller.Delete(999_999, CancellationToken.None);
            Assert.IsType<NotFoundObjectResult>(deleteResult);
        }

        [Fact]
        public async Task DeletingAScheduledPersonaReturns409()
        {
            // PLAN T120 scaffolding (SPEC F91.9): PersonaRepository maps the FK RESTRICT on
            // station.segment_schedule.persona_id (SQLSTATE 23503) to
            // PersonaWriteResult.ScheduledElsewhere itself (T120 review F4 — the store, never this
            // controller, turns a raw Postgres SQLSTATE into a PersonaWriteResult case); the
            // controller answers a GENERIC 409 for that case. T121 replaces the body with one naming
            // the offending slots.
            var store = new FakePersonaStore { DeleteResult = new PersonaWriteResult.ScheduledElsewhere() };
            var controller = BuildController(store, new FakeOptionsMonitor<StationOptions>(BuildStationOptions()));

            var result = await controller.Delete(5, CancellationToken.None);

            var conflict = Assert.IsType<ConflictObjectResult>(result);
            Assert.IsType<ProblemDetails>(conflict.Value);
        }

        [Fact]
        public async Task NonJsonWriteReturns415()
        {
            // F18.7 posture applies (F35.4, AC5). No Admin:Password set — content-type negotiation
            // is tested in isolation, without needing a valid cookie (mirrors Story112).
            await using var factory = new PersonaApiWebFactory(withAdminPassword: false);
            var client = factory.CreateClient();

            var body = new StringContent(
                "name=Test", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
            var response = await client.PostAsync("/api/personas", body);

            // [Consumes("application/json")] returns 415 Unsupported Media Type.
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        }

        [Fact]
        public async Task AnonymousRequestReturns401WhenPasswordSet()
        {
            // (AC6).
            await using var factory = new PersonaApiWebFactory(withAdminPassword: true);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });

            var response = await client.GetAsync("/api/personas");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}

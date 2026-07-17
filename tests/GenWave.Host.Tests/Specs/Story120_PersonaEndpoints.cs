// STORY-120 — Persona CRUD + one live active persona (WIRE)
//
// BDD specification — xUnit. Drives the deployed entry points (PersonaController routes) through
// direct controller construction with a fake IPersonaStore/IStationSettingsStore at the boundary
// (mirrors Story112's RatingController-spec idiom) — no live stack required; the real-Postgres
// behavior behind IPersonaStore is Story118's job. NO If-Match anywhere — documented F18.6
// deviation (single writer, no background contender).
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Configuration;
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
}

/// <summary>
/// Scriptable <see cref="IStationSettingsStore"/> double (mirrors Story100's
/// <c>FakeSettingsStore</c>) that records every write so a scenario can assert the
/// delete-clears-active overlay write (F35.5) happened — or didn't.
/// </summary>
file sealed class FakeStationSettingsStore : IStationSettingsStore
{
    readonly Dictionary<string, string> overrides = new(StringComparer.OrdinalIgnoreCase);

    public List<(string Key, object Value)> WriteCalls { get; } = [];

    public Task WriteAsync(string key, object value, CancellationToken cancellationToken = default)
    {
        if (!StationSettingsAllowlist.ByKey.ContainsKey(key))
            throw new ArgumentException($"Key '{key}' is not allowlisted.", nameof(key));

        overrides[key] = value.ToString() ?? string.Empty;
        WriteCalls.Add((key, value));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, string> result =
            new Dictionary<string, string>(overrides, StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(result);
    }
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
/// Minimal <see cref="ILogger{T}"/> that collects Warning-and-above messages for assertion
/// (mirrors GenWave.Tts.Tests' <c>CapturingLogger&lt;T&gt;</c>). Test-scope only.
/// </summary>
file sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Warnings { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel >= LogLevel.Warning)
            Warnings.Add(formatter(state, exception));
    }
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
    const string LibraryConnVar = "ConnectionStrings__Library";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development config provides Station:Id/Name/Voice/Scope/SafeScope and Tts:Endpoint
        // so ValidateOnStart() is satisfied without injecting them manually.
        builder.UseEnvironment("Development");

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

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // AddMediaLibrary reads Library connection string early in Program.cs (before
        // ConfigureTestServices runs), so it is injected via the environment. A non-reachable
        // host is fine: neither scenario below ever resolves IMediaCatalog or IPersonaStore — the
        // request is rejected by auth/routing middleware before any controller is constructed.
        var prev = Environment.GetEnvironmentVariable(LibraryConnVar);
        Environment.SetEnvironmentVariable(LibraryConnVar, "Host=nowhere;Database=test");
        try
        {
            return base.CreateHost(builder);
        }
        finally
        {
            Environment.SetEnvironmentVariable(LibraryConnVar, prev);
        }
    }
}

// ── In-process tests ──────────────────────────────────────────────────────────────────────────────

public static class FeaturePersonaEndpoints
{
    static StationOptions BuildStationOptions(long activeId = 0) => new()
    {
        Id = "genwave-1",
        Name = "Test Station",
        Voice = "af_heart",
        Scope = new StationScopeOptions { LibraryIds = [1] },
        SafeScope = new StationScopeOptions { LibraryIds = [1] },
        Persona = new StationPersonaOptions { ActiveId = activeId },
    };

    static PersonaController BuildController(
        IPersonaStore store,
        IStationSettingsStore settingsStore,
        IOptionsMonitor<StationOptions> stationMonitor) =>
        new(
            store, settingsStore, stationMonitor,
            new NotUsedPersonaPreviewWriter(), new NotUsedActivePersonaAccessor(),
            new NotUsedAdminMediaLookup(), new FakeStationScopeProvider(LibraryScope.None),
            NullLogger<PersonaController>.Instance);

    static IConfiguration BuildConfig(IEnumerable<KeyValuePair<string, string?>> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

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
            var controller = BuildController(
                store, new FakeStationSettingsStore(), new FakeOptionsMonitor<StationOptions>(BuildStationOptions()));

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
            var controller = BuildController(
                store, new FakeStationSettingsStore(), new FakeOptionsMonitor<StationOptions>(BuildStationOptions()));

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
            var controller = BuildController(
                store, new FakeStationSettingsStore(), new FakeOptionsMonitor<StationOptions>(BuildStationOptions()));

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
            // (F35.4, AC1). Deleted id (9) is NOT the active persona (0 = none) — the overlay
            // write must stay untouched (F35.5's negative half; a regression that clears on every
            // delete, not just the active one, must fail this fact).
            var store = new FakePersonaStore { DeleteResult = new PersonaWriteResult.Deleted() };
            var settingsStore = new FakeStationSettingsStore();
            var controller = BuildController(
                store, settingsStore, new FakeOptionsMonitor<StationOptions>(BuildStationOptions(activeId: 0)));

            var result = await controller.Delete(9, CancellationToken.None);

            Assert.IsType<NoContentResult>(result);
            Assert.Equal(9, Assert.Single(store.DeleteCalls));
            Assert.Empty(settingsStore.WriteCalls);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the one active persona setting
    // ---------------------------------------------------------------------

    public sealed class ScenarioActivePersonaSetting
    {
        [Fact]
        public async Task ActiveIdAppearsInSettingsWithLiveApplyMode()
        {
            // GET /api/settings carries Station:Persona:ActiveId, applyMode live (F35.2, F36.2, AC2).
            var config = BuildConfig([new("Station:Persona:ActiveId", "0")]);
            var settingsStore = new FakeStationSettingsStore();
            var controller = new SettingsController(
                config, settingsStore, new SettingValidator(config), NullLogger<SettingsController>.Instance);

            var result = await controller.Get(CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value).ToList();
            var activeId = items.Single(i =>
                string.Equals(i.Key, "Station:Persona:ActiveId", StringComparison.OrdinalIgnoreCase));

            Assert.Equal("live", activeId.ApplyMode);
            Assert.Equal("number", activeId.Kind);
            Assert.Equal("0", activeId.Value);
        }

        [Fact]
        public async Task PutPersistsActiveIdToTheOverlay()
        {
            // PUT round-trip; 0 = none (F35.2, AC2).
            var config = BuildConfig([new("Station:Persona:ActiveId", "0")]);
            var settingsStore = new FakeStationSettingsStore();
            var controller = new SettingsController(
                config, settingsStore, new SettingValidator(config), NullLogger<SettingsController>.Instance);

            var putResult = await controller.Put(
                [new SettingUpdateRequest("Station:Persona:ActiveId", "5")], CancellationToken.None);

            Assert.IsType<OkObjectResult>(putResult);
            var write = Assert.Single(settingsStore.WriteCalls);
            Assert.Equal("Station:Persona:ActiveId", write.Key);
            Assert.Equal("5", write.Value);
        }

        [Fact]
        public async Task DeletingTheActivePersonaClearsTheOverlayInTheSameRequest()
        {
            // DELETE /api/personas/{active} → 204 AND ActiveId cleared (F35.5, AC3).
            var store = new FakePersonaStore { DeleteResult = new PersonaWriteResult.Deleted() };
            var settingsStore = new FakeStationSettingsStore();
            var controller = BuildController(
                store, settingsStore, new FakeOptionsMonitor<StationOptions>(BuildStationOptions(activeId: 7)));

            var result = await controller.Delete(7, CancellationToken.None);

            Assert.IsType<NoContentResult>(result);
            var write = Assert.Single(settingsStore.WriteCalls);
            Assert.Equal(PersonaController.ActiveIdKey, write.Key);
            Assert.Equal(0, write.Value);
        }

        [Fact]
        public async Task AStaleActiveIdResolvesToPersonaLessWithAWarn()
        {
            // Accessor yields null + WARN — never a throw (F35.5, AC4).
            var store = new FakePersonaStore { GetByIdResult = null };
            var logger = new CapturingLogger<ActivePersonaAccessor>();
            var accessor = new ActivePersonaAccessor(
                new FakeOptionsMonitor<StationOptions>(BuildStationOptions(activeId: 42)), store, logger);

            var persona = await accessor.ResolveAsync(CancellationToken.None);

            Assert.Null(persona);
            Assert.Single(logger.Warnings);
            Assert.Equal(42, Assert.Single(store.GetByIdCalls));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — validation and auth teeth
    // ---------------------------------------------------------------------

    // PersonaApiWebFactory.CreateHost mutates the ConnectionStrings__Library process env var for
    // the boot window — shared with Story056's/Story058's/Story084's/Story112's factories, so this
    // class opts into the serializing collection (see EnvVarMutatingWebFactoryCollection) rather
    // than racing them under xUnit's default parallelism.
    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioRejectingInvalidWrites
    {
        [Fact]
        public async Task BlankNameReturns400()
        {
            // (F35.4, AC5).
            var controller = BuildController(
                new FakePersonaStore(), new FakeStationSettingsStore(),
                new FakeOptionsMonitor<StationOptions>(BuildStationOptions()));

            var result = await controller.Create(new PersonaRequest("   ", null, null, null), CancellationToken.None);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.IsType<ProblemDetails>(badRequest.Value);
        }

        [Fact]
        public async Task DuplicateNameReturns409()
        {
            // (F35.4, AC5).
            var store = new FakePersonaStore { CreateResult = new PersonaWriteResult.NameConflict() };
            var controller = BuildController(
                store, new FakeStationSettingsStore(), new FakeOptionsMonitor<StationOptions>(BuildStationOptions()));

            var result = await controller.Create(
                new PersonaRequest("Existing Name", null, null, null), CancellationToken.None);

            var conflict = Assert.IsType<ConflictObjectResult>(result);
            Assert.IsType<ProblemDetails>(conflict.Value);
        }

        [Fact]
        public async Task UnknownIdReturns404()
        {
            // PATCH/DELETE on a missing id (F35.4, AC5). ActiveId is deliberately set to the SAME
            // id being deleted (999_999) — a NotFound delete must still clear nothing (F35.5's
            // negative half; a regression that writes the overlay outside the `is Deleted` branch
            // must fail this fact).
            var store = new FakePersonaStore
            {
                UpdateResult = new PersonaWriteResult.NotFound(),
                DeleteResult = new PersonaWriteResult.NotFound(),
            };
            var settingsStore = new FakeStationSettingsStore();
            var controller = BuildController(
                store, settingsStore, new FakeOptionsMonitor<StationOptions>(BuildStationOptions(activeId: 999_999)));

            var patchResult = await controller.Update(
                999_999, new PersonaRequest("Anyone", null, null, null), CancellationToken.None);
            Assert.IsType<NotFoundObjectResult>(patchResult);

            var deleteResult = await controller.Delete(999_999, CancellationToken.None);
            Assert.IsType<NotFoundObjectResult>(deleteResult);
            Assert.Empty(settingsStore.WriteCalls);
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

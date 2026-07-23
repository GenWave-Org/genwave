// STORY-219 — I can inspect what my persona's taste is and what it has learned (SPEC F86.6, F86.9)
//
// BDD specification — xUnit. PLAN T77 wires GET /api/personas/{id}/taste (docs/PLAN.md Phase V24).
// Read-only, AdminOnly: source-grouped rules (predicate summary, context gate, weight, updated-at)
// plus the accrued count against the 50-cap (F84.3). This release adds NO taste write surface beyond
// the existing thumbs — the sad path pins that structurally, not just behaviorally.
//
// Entry-point discipline: the grouping/predicate-summary/context-gate/weight/cap-meter/404 facts
// drive the REAL PersonaController.Taste action through DIRECT CONSTRUCTION with a fake
// IPersonaStore/IPersonaTasteReader at the repository seam (mirrors Story120/123's own
// controller-direct idiom — no live Postgres; the taste reader's self-filtering fake mirrors
// PersonaTasteRepository.ListAsync's own `where persona_id = @PersonaId and (@Source is null or
// source = @Source)`, the same idiom Story208_PersonaExport.cs's own FakePersonaTasteStore already
// established). The three structural facts (no mutation route, AdminOnly policy, spectator absence)
// instead drive the real HTTP pipeline via WebApplicationFactory<Program> and enumerate
// EndpointDataSource — properties of routing/auth metadata, not of the controller's own logic
// (mirrors Story195_BoothLog's/Story196_LlmCallInspector's own structural facts).

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Configuration;
using GenWave.Host.Options;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// In-memory, id-keyed <see cref="IPersonaStore"/> double — only <see cref="GetByIdAsync"/> is
/// reachable through <see cref="PersonaController.Taste"/>; every write throws if a scenario ever
/// hits it by mistake (this file never CRUDs a persona).
/// </summary>
file sealed class FakePersonaStore : IPersonaStore
{
    public Dictionary<long, Persona> Rows { get; } = [];

    public Task<Persona?> GetByIdAsync(long id, CancellationToken ct) =>
        Task.FromResult(Rows.TryGetValue(id, out var persona) ? persona : null);

    public Task<IReadOnlyList<Persona>> GetAllAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story219's taste-inspector scenarios.");

    public Task<PersonaWriteResult> CreateAsync(PersonaDraft draft, CancellationToken ct) =>
        throw new NotSupportedException("Taste never writes through IPersonaStore.");

    public Task<PersonaWriteResult> UpdateAsync(long id, PersonaDraft draft, CancellationToken ct) =>
        throw new NotSupportedException("Taste never writes through IPersonaStore.");

    public Task<PersonaWriteResult> DeleteAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Taste never writes through IPersonaStore.");

    public Task<PersonaCard?> GetCardByIdAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story219's taste-inspector scenarios.");

    public Task<long?> GetIdBySlugAsync(string slug, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story219's taste-inspector scenarios.");
}

/// <summary>
/// Scriptable <see cref="IPersonaTasteReader"/> double that filters <see cref="Rows"/> by
/// <c>(personaId, source)</c> inside <see cref="ListAsync"/> — mirrors
/// <c>PersonaTasteRepository.ListAsync</c>'s own <c>WHERE persona_id = @PersonaId and (@Source is
/// null or source = @Source)</c> (the same idiom Story208_PersonaExport.cs's own
/// <c>FakePersonaTasteStore</c> already established), so a scenario proves the CONTROLLER's own
/// unfiltered (<c>source: null</c>) call groups every source correctly, not an accident of a fake
/// that hands back a fixed list regardless of the argument.
/// </summary>
file sealed class FakePersonaTasteReader : IPersonaTasteReader
{
    public List<PersonaTasteEntry> Rows { get; } = [];

    public Task<IReadOnlyList<PersonaTasteEntry>> ListAsync(long personaId, PersonaTasteSource? source, CancellationToken ct)
    {
        IReadOnlyList<PersonaTasteEntry> result = Rows
            .Where(r => r.PersonaId == personaId && (source is null || r.Source == source))
            .ToList();
        return Task.FromResult(result);
    }
}

/// <summary>Unused-by-Taste <see cref="IStationSettingsStore"/> double — the constructor dependency
/// exists for the sibling CRUD actions, never called by these taste-inspector scenarios.</summary>
file sealed class NotUsedStationSettingsStore : IStationSettingsStore
{
    public Task WriteAsync(string key, object value, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by Story219's taste-inspector scenarios.");

    public Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by Story219's taste-inspector scenarios.");
}

/// <summary>Unused-by-Taste <see cref="IPersonaPreviewWriter"/> double — same reason as
/// <see cref="NotUsedStationSettingsStore"/> above.</summary>
file sealed class NotUsedPersonaPreviewWriter : IPersonaPreviewWriter
{
    public Task<PersonaPreviewResult> WritePreviewAsync(
        SegmentRequest request, Persona? personaOverride, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story219's taste-inspector scenarios.");
}

/// <summary>Unused-by-Taste <see cref="IAdminMediaLookup"/> double — same reason as
/// <see cref="NotUsedStationSettingsStore"/> above.</summary>
file sealed class NotUsedAdminMediaLookup : IAdminMediaLookup
{
    public Task<(AdminMediaDto Row, long LibraryId)?> GetByIdWithLibraryAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story219's taste-inspector scenarios.");
}

/// <summary>Unused-by-Taste <see cref="IPersonaMemory"/> double — same reason as
/// <see cref="NotUsedStationSettingsStore"/> above.</summary>
file sealed class NotUsedPersonaMemory : IPersonaMemory
{
    public Task<long> RecordAsync(long personaId, string kind, string content, PersonaMemorySource source, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story219's taste-inspector scenarios.");

    public Task MarkAiredAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story219's taste-inspector scenarios.");

    public Task<IReadOnlyList<PersonaMemoryEntry>> RecallAsync(long personaId, RecallSpec spec, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story219's taste-inspector scenarios.");

    public Task<IReadOnlyList<PersonaMemoryEntry>> ListAsync(long personaId, PersonaMemorySource source, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story219's taste-inspector scenarios.");
}

/// <summary>Unused-by-Taste <see cref="IPersonaImportStore"/> double — same reason as
/// <see cref="NotUsedStationSettingsStore"/> above.</summary>
file sealed class NotUsedPersonaImportStore : IPersonaImportStore
{
    public Task<PersonaImportOutcome> ImportAsync(PersonaImportRequest request, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story219's taste-inspector scenarios.");
}

/// <summary>Unused-by-Taste <see cref="ITtsVoiceLister"/> double — same reason as
/// <see cref="NotUsedStationSettingsStore"/> above.</summary>
file sealed class NotUsedTtsVoiceLister : ITtsVoiceLister
{
    public Task<IReadOnlyList<string>> ListVoicesAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story219's taste-inspector scenarios.");
}

/// <summary>
/// Builds a <see cref="PersonaController"/> wired to the given fakes — every dependency
/// <see cref="PersonaController.Taste"/> never touches is a throwing "not used" double (mirrors
/// Story120/123's own controller-direct idiom).
/// </summary>
file static class PersonaTasteControllerFactory
{
    public static PersonaController Build(FakePersonaStore personaStore, FakePersonaTasteReader personaTaste) =>
        new(
            personaStore,
            new NotUsedStationSettingsStore(),
            new FakeOptionsMonitor<StationOptions>(new StationOptions { Id = "test", Name = "Test Station", Voice = "af_heart" }),
            new NotUsedPersonaPreviewWriter(),
            new FakeActivePersonaAccessor(),
            new NotUsedAdminMediaLookup(),
            new FakeStationScopeProvider(new LibraryScope([1])),
            new NotUsedPersonaMemory(),
            personaTaste,
            new NotUsedPersonaImportStore(),
            new NotUsedTtsVoiceLister(),
            NullLogger<PersonaController>.Instance);
}

// ── WebApplicationFactory for the structural (routing/auth) facts ──────────────────────────────────

/// <summary>
/// Minimal <see cref="WebApplicationFactory{TEntryPoint}"/> that brings up the real HTTP pipeline
/// (routing, auth, the production <c>GET /api/personas/{id}/taste</c> route) while removing hosted
/// services that would attempt real Liquidsoap/Postgres connections — mirrors Story195's/Story196's
/// own structural-fact factories. None of this file's structural facts ever resolve
/// <see cref="IPersonaStore"/>/<see cref="IPersonaTasteReader"/> — they only enumerate
/// <see cref="EndpointDataSource"/> metadata — so the persona stores' own connection strings are left
/// at their (empty, dev-mode) defaults.
/// </summary>
file sealed class PersonaTasteRouteWebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-x7z");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
        });
    }
}

// ── Shared fixture (file-scoped: the fixture's return type carries file-scoped fake types, and a
// file-scoped type cannot appear in a member signature of a non-file-scoped type — same reason
// Story208_PersonaExport.cs's own PersonaExportFixture is file-scoped) ─────────────────────────────

file static class PersonaTasteFixture
{
    public const long PersonaId = 42;

    static readonly Persona LivingPersona = new(PersonaId, "DJ Nova", "", "", "", DateTime.UtcNow, DateTime.UtcNow);

    /// <summary>
    /// Seeds a persona holding one authored rule (day/hour-gated), one operator rule (ungated), and
    /// one accrued rule (ungated) — distinct predicates, context gates, and weights, so a scenario can
    /// tell "which rule is this" apart at a glance (F86.6's "arrange once" idiom, mirrors
    /// Story208_PersonaExport.cs's own SeedLivingPersona).
    /// </summary>
    public static (FakePersonaStore Store, FakePersonaTasteReader Taste,
        PersonaTasteEntry Authored, PersonaTasteEntry Operator, PersonaTasteEntry Accrued) SeedDistinctRules()
    {
        var store = new FakePersonaStore();
        store.Rows[PersonaId] = LivingPersona;

        var t1 = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 7, 10, 14, 30, 0, DateTimeKind.Utc);
        var t3 = new DateTime(2026, 7, 20, 3, 15, 0, DateTimeKind.Utc);

        var authored = new PersonaTasteEntry(
            1, PersonaId,
            new TasteRule(new TastePredicate("Pink Floyd", null, null), new TasteContext([DayOfWeek.Sunday], 6, 12), 0.8),
            PersonaTasteSource.Authored, t1, t1);
        var operatorRule = new PersonaTasteEntry(
            2, PersonaId,
            new TasteRule(new TastePredicate(null, "Vaporwave", null), new TasteContext([], null, null), -0.6),
            PersonaTasteSource.Operator, t2, t2);
        var accrued = new PersonaTasteEntry(
            3, PersonaId,
            new TasteRule(new TastePredicate("The Waveforms", null, null), new TasteContext([], null, null), 0.3),
            PersonaTasteSource.Accrued, t3, t3);

        var taste = new FakePersonaTasteReader();
        taste.Rows.Add(authored);
        taste.Rows.Add(operatorRule);
        taste.Rows.Add(accrued);

        return (store, taste, authored, operatorRule, accrued);
    }

    /// <summary>
    /// Seeds a persona holding one authored rule, one operator rule, and <paramref name="accruedCount"/>
    /// accrued rules — arrangement for the cap-meter scenario, where the fact under test is which rows
    /// the reported count includes, not what any individual rule looks like.
    /// </summary>
    public static (FakePersonaStore Store, FakePersonaTasteReader Taste) SeedWithAccruedCount(int accruedCount)
    {
        var store = new FakePersonaStore();
        store.Rows[PersonaId] = LivingPersona;

        var now = DateTime.UtcNow;
        var taste = new FakePersonaTasteReader();
        taste.Rows.Add(new PersonaTasteEntry(
            100, PersonaId, new TasteRule(new TastePredicate("Led Zeppelin", null, null), new TasteContext([], null, null), 0.9),
            PersonaTasteSource.Authored, now, now));
        taste.Rows.Add(new PersonaTasteEntry(
            101, PersonaId, new TasteRule(new TastePredicate(null, "Ambient", null), new TasteContext([], null, null), -0.4),
            PersonaTasteSource.Operator, now, now));

        for (var i = 0; i < accruedCount; i++)
        {
            taste.Rows.Add(new PersonaTasteEntry(
                200 + i, PersonaId,
                new TasteRule(new TastePredicate($"Accrued Artist {i}", null, null), new TasteContext([], null, null), 0.1),
                PersonaTasteSource.Accrued, now, now));
        }

        return (store, taste);
    }
}

// ── Specs ────────────────────────────────────────────────────────────────────────────────────────

public static class FeaturePersonaTasteInspector
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTasteReturnsGroupedRules
    {
        // Arrange (shared by every fact below): a persona holding authored, operator, and accrued
        // rules with distinct predicates, context gates, and weights.

        [Fact]
        public async Task RulesReturnGroupedBySource()
        {
            var (store, taste, authored, operatorRule, accrued) = PersonaTasteFixture.SeedDistinctRules();
            var controller = PersonaTasteControllerFactory.Build(store, taste);

            var result = await controller.Taste(PersonaTasteFixture.PersonaId, CancellationToken.None);

            var response = Assert.IsType<PersonaTasteResponseDto>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal("Pink Floyd", Assert.Single(response.Authored).PredicateSummary);
            Assert.Equal("Vaporwave", Assert.Single(response.Operator).PredicateSummary);
            Assert.Equal("The Waveforms", Assert.Single(response.Accrued).PredicateSummary);
        }

        [Fact]
        public async Task EachRuleCarriesItsPredicateSummary()
        {
            // Artist-over-genre precedence (F86.6): the authored rule opinions about an artist and
            // renders that; the operator rule opinions about a genre only (no artist) and renders
            // that instead — proving the precedence, not just "some string is present".
            var (store, taste, _, _, _) = PersonaTasteFixture.SeedDistinctRules();
            var controller = PersonaTasteControllerFactory.Build(store, taste);

            var result = await controller.Taste(PersonaTasteFixture.PersonaId, CancellationToken.None);

            var response = Assert.IsType<PersonaTasteResponseDto>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal("Pink Floyd", Assert.Single(response.Authored).PredicateSummary);
            Assert.Equal("Vaporwave", Assert.Single(response.Operator).PredicateSummary);
        }

        [Fact]
        public async Task EachRuleCarriesItsContextGate()
        {
            // The authored rule carries a day/hour gate; the operator rule (ungated) surfaces "none" —
            // empty days, null hours — the same unbounded-field convention TasteContext itself uses.
            var (store, taste, _, _, _) = PersonaTasteFixture.SeedDistinctRules();
            var controller = PersonaTasteControllerFactory.Build(store, taste);

            var result = await controller.Taste(PersonaTasteFixture.PersonaId, CancellationToken.None);

            var response = Assert.IsType<PersonaTasteResponseDto>(Assert.IsType<OkObjectResult>(result).Value);
            var gated = Assert.Single(response.Authored);
            Assert.Equal([DayOfWeek.Sunday], gated.DaysOfWeek);
            Assert.Equal(6, gated.StartHour);
            Assert.Equal(12, gated.EndHour);

            var ungated = Assert.Single(response.Operator);
            Assert.Empty(ungated.DaysOfWeek);
            Assert.Null(ungated.StartHour);
            Assert.Null(ungated.EndHour);
        }

        [Fact]
        public async Task EachRuleCarriesItsSignedWeightAndUpdatedAt()
        {
            // Dislikes are taste too (F82.1): the operator rule's weight stays negative, never
            // clamped/abs'd away, and each row's own updated-at reaches the wire unchanged.
            var (store, taste, authored, operatorRule, _) = PersonaTasteFixture.SeedDistinctRules();
            var controller = PersonaTasteControllerFactory.Build(store, taste);

            var result = await controller.Taste(PersonaTasteFixture.PersonaId, CancellationToken.None);

            var response = Assert.IsType<PersonaTasteResponseDto>(Assert.IsType<OkObjectResult>(result).Value);
            var authoredDto = Assert.Single(response.Authored);
            Assert.Equal(0.8, authoredDto.Weight);
            Assert.Equal(authored.UpdatedAt, authoredDto.UpdatedAt);

            var operatorDto = Assert.Single(response.Operator);
            Assert.Equal(-0.6, operatorDto.Weight);
            Assert.Equal(operatorRule.UpdatedAt, operatorDto.UpdatedAt);
        }
    }

    public sealed class ScenarioCapMeter
    {
        [Fact]
        public async Task ResponseReportsAccruedCountAgainstTheFiftyCap()
        {
            var (store, taste) = PersonaTasteFixture.SeedWithAccruedCount(accruedCount: 3);
            var controller = PersonaTasteControllerFactory.Build(store, taste);

            var result = await controller.Taste(PersonaTasteFixture.PersonaId, CancellationToken.None);

            var response = Assert.IsType<PersonaTasteResponseDto>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(3, response.AccruedCount);
            Assert.Equal(IPersonaTasteAccrualStore.Cap, response.AccruedCap);
        }

        [Fact]
        public async Task AuthoredAndOperatorRulesDoNotCountTowardTheCap()
        {
            // The fixture seeds ONE authored row and ONE operator row alongside 5 accrued rows —
            // the reported count must reflect only the 5 accrued rows, not all 7.
            var (store, taste) = PersonaTasteFixture.SeedWithAccruedCount(accruedCount: 5);
            var controller = PersonaTasteControllerFactory.Build(store, taste);

            var result = await controller.Taste(PersonaTasteFixture.PersonaId, CancellationToken.None);

            var response = Assert.IsType<PersonaTasteResponseDto>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(5, response.AccruedCount);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — read-only, unknown id, admin plane only
    // ---------------------------------------------------------------------

    public sealed class ScenarioReadOnlySurface
    {
        [Fact]
        public async Task NoMutationRouteExistsUnderTheTastePath()
        {
            // Route-table enumeration finds GET only under /api/personas/{id}/taste —
            // no POST/PUT/PATCH/DELETE (F86.6's read-only contract, structural).
            await using var factory = new PersonaTasteRouteWebFactory();

            var endpoints = TastePathEndpoints(factory.Services);

            Assert.NotEmpty(endpoints);
            var verbs = endpoints
                .SelectMany(e => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Distinct()
                .ToList();
            Assert.Equal(["GET"], verbs);
        }
    }

    public sealed class ScenarioUnknownPersona
    {
        [Fact]
        public async Task UnknownPersonaIdReturns404()
        {
            var controller = PersonaTasteControllerFactory.Build(new FakePersonaStore(), new FakePersonaTasteReader());

            var result = await controller.Taste(999_999, CancellationToken.None);

            var notFound = Assert.IsType<NotFoundObjectResult>(result);
            Assert.IsType<ProblemDetails>(notFound.Value);
        }
    }

    public sealed class ScenarioAdminPlaneOnly
    {
        [Fact]
        public async Task TasteEndpointRequiresTheAdminOnlyPolicy()
        {
            await using var factory = new PersonaTasteRouteWebFactory();

            var endpoint = Assert.Single(TastePathEndpoints(factory.Services));

            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).ToList();
            Assert.Contains(AuthorizationPolicies.AdminOnly, policies);
        }

        [Fact]
        public async Task TasteIsAbsentFromEverySpectatorSurface()
        {
            // Structural proof, not just a runtime probe (mirrors Story195_BoothLog's/
            // Story196_LlmCallInspector's own admin-vs-spectator classification facts): this endpoint
            // carries neither the Spectator policy nor the SpectatorSurfaceAttribute marker, so it
            // cannot become reachable on the public/spectator surface by accident (F86.9).
            await using var factory = new PersonaTasteRouteWebFactory();

            var endpoint = Assert.Single(TastePathEndpoints(factory.Services));

            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).ToList();
            Assert.DoesNotContain(AuthorizationPolicies.Spectator, policies);
            Assert.Null(endpoint.Metadata.GetMetadata<SpectatorSurfaceAttribute>());
        }
    }

    /// <summary>Every endpoint whose route pattern ends the <c>/api/personas/...</c> path with a
    /// <c>taste</c> segment — constraint-agnostic (matches <c>{id:long}/taste</c> regardless of the
    /// exact constraint token), mirrors Story166_AdminKillSwitch's own route-enumeration idiom.</summary>
    static IReadOnlyList<RouteEndpoint> TastePathEndpoints(IServiceProvider services) =>
        services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } raw &&
                raw.TrimStart('/').StartsWith("api/personas/", StringComparison.OrdinalIgnoreCase) &&
                raw.TrimEnd('/').EndsWith("/taste", StringComparison.OrdinalIgnoreCase))
            .ToList();
}

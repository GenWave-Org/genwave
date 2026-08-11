// STORY-317 — Dated specials shadow the grid (F120.3) — endpoint half (PLAN T259)
//
// BDD specification — xUnit. WIRED T259 — every Fact below drives the real production
// /api/schedule/specials routes (AdminSurface + Settings posture) through WebApplicationFactory<Program>
// with real cookie auth (mirrors Story305_ShowsApi.cs's own idiom), against FakeScheduleSpecialStore/
// FakePersonaStore/FakeShowStore/FakeStationClockProvider doubles — no live Postgres, this project has
// none for Host.Tests. The repository half (real Postgres, db/36's own CHECK/EXCLUDE/FK constraints,
// and SpecialsRepository.CreateAsync's own SQLSTATE-to-ScheduleSpecialCreateResult translation) lives
// in GenWave.MediaLibrary.Tests/Specs/Story317_SpecialsStore.cs — this file never re-derives that
// validation, only the WIRE mapping (SpecialsController's own app-side gates, plus its
// ScheduleSpecialCreateResult-to-409 mapping, scripted via FakeScheduleSpecialStore.NextCreateResult —
// the store's own typed result, never a raw PostgresException: GenWave.Architecture.Tests' L2 law
// confines Npgsql to GenWave.MediaLibrary's repository layer, so this controller — and this file's own
// fakes — never reference it at all, unlike Story240_GridHoldsTheWeek.cs's older, pre-existing
// PostgresException-scripting idiom for ScheduleController.Put's own grandfathered exemption).
//
// PLAN T259's own honesty note (SpecialsController's class remarks): these Facts prove the store is
// LIVE (authorable/listable/deletable through the Admin UI's own API surface) — none of them touch
// GenWave.Orchestration.ScheduleResolver/CachingScheduleResolver directly; the resolver's own
// consumption of this store (PLAN T260, now landed) is proven in
// GenWave.Orchestration.Tests/Story241_StationFollowsTheClock.cs's own ScenarioSpecialsRideTheCache
// instead — this file stays scoped to the WIRE mapping described above.

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
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

public static class FeatureSpecialsApi
{
    // The FakeStationClockProvider "now" every Fact in this file gets by default — station-local
    // 2026-08-15, chosen so both a past date (2026-08-14) and a future date (2026-08-20) are
    // unambiguous relative to it.
    static readonly DateTimeOffset Today = new(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);

    public sealed class ScenarioCrudThroughTheProductionSurface
    {
        [Fact]
        public async Task CreateListsAndDeletesRoundTripThroughTheEndpoints()
        {
            // Given an authenticated admin session and a known persona/show to reference
            var persona = new Persona(1, "Nova", "", "", "", DateTime.UtcNow, DateTime.UtcNow);
            var show = new Show(1, "Night Moves", "night-moves", null, null, null, null, DateTime.UtcNow, DateTime.UtcNow);
            var specialStore = new FakeScheduleSpecialStore();
            await using var factory = new SpecialsApiWebFactory(
                specialStore, personaStore: new FakePersonaStore([persona]), showStore: new FakeShowStore([show]));
            var client = await SpecialsApiWebFactory.LoggedInClientAsync(factory);

            // When a special is created, then listed, then deleted via /api/schedule/specials
            var createResponse = await client.PostAsJsonAsync("/api/schedule/specials", new
            {
                onDate = "2026-08-20",
                startMinute = 540,
                endMinute = 720,
                personaId = persona.Id,
                genres = new[] { "synthwave" },
                energyMin = 0.4,
                energyMax = 0.8,
                showId = show.Id,
            });
            var created = await createResponse.Content.ReadFromJsonAsync<SpecialDto>();
            Assert.NotNull(created);

            var list = await (await client.GetAsync("/api/schedule/specials")).Content.ReadFromJsonAsync<SpecialDto[]>();

            var deleteResponse = await client.DeleteAsync($"/api/schedule/specials/{created.Id}");
            var afterDeleteList =
                await (await client.GetAsync("/api/schedule/specials")).Content.ReadFromJsonAsync<SpecialDto[]>();

            // Then creation succeeds naming every submitted field, the list carries the new row, and
            // the delete removes it cleanly
            Assert.Equal(
                (Create: HttpStatusCode.Created, OnDate: new DateOnly(2026, 8, 20), PersonaId: (long?)persona.Id,
                 ShowId: (long?)show.Id, Listed: true, Delete: HttpStatusCode.NoContent, GoneAfterDelete: true),
                (Create: createResponse.StatusCode, created.OnDate, created.PersonaId, created.ShowId,
                 Listed: list!.Any(s => s.Id == created.Id),
                 Delete: deleteResponse.StatusCode, GoneAfterDelete: !afterDeleteList!.Any(s => s.Id == created.Id)));
        }

        [Fact]
        public async Task DeletingAnUnknownIdReturns404()
        {
            var specialStore = new FakeScheduleSpecialStore();
            await using var factory = new SpecialsApiWebFactory(specialStore);
            var client = await SpecialsApiWebFactory.LoggedInClientAsync(factory);

            var response = await client.DeleteAsync("/api/schedule/specials/999");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    public sealed class ScenarioAppSideValidationGates
    {
        [Fact]
        public async Task AMalformedOnDateIsRejected400()
        {
            // Not this controller's own code — [ApiController]'s built-in invalid-model-state filter
            // 400s a body System.Text.Json's DateOnly converter can't parse before Create's own action
            // body ever runs (SpecialRequestDto.OnDate never binds to a placeholder default). Pinned
            // here (PLAN T259 review) so this framework-level behavior stays proven, not just assumed.
            var response = await PostAsync(new { onDate = "not-a-date", startMinute = 540, endMinute = 720 });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task AnOffGridStartMinuteIsRejected400()
        {
            var response = await PostAsync(new { onDate = "2026-08-20", startMinute = 545, endMinute = 720 });
            var detail = await DetailAsync(response);
            Assert.Equal(
                (Status: HttpStatusCode.BadRequest, NamesStart: true),
                (Status: response.StatusCode, NamesStart: detail.Contains("startMinute", StringComparison.Ordinal)));
        }

        [Fact]
        public async Task AnOffGridEndMinuteIsRejected400()
        {
            var response = await PostAsync(new { onDate = "2026-08-20", startMinute = 540, endMinute = 725 });
            var detail = await DetailAsync(response);
            Assert.Equal(
                (Status: HttpStatusCode.BadRequest, NamesEnd: true),
                (Status: response.StatusCode, NamesEnd: detail.Contains("endMinute", StringComparison.Ordinal)));
        }

        [Fact]
        public async Task AnEndNotAfterStartIsRejected400()
        {
            var response = await PostAsync(new { onDate = "2026-08-20", startMinute = 720, endMinute = 540 });
            var detail = await DetailAsync(response);
            Assert.Equal(
                (Status: HttpStatusCode.BadRequest, NamesGreaterThan: true),
                (Status: response.StatusCode, NamesGreaterThan: detail.Contains("greater than", StringComparison.OrdinalIgnoreCase)));
        }

        [Fact]
        public async Task ADateInThePastIsRejected400()
        {
            // "Today" (per FakeStationClockProvider, station-local 2026-08-15) is allowed; only a
            // date that has already fully elapsed is refused (PLAN T259 product call).
            var response = await PostAsync(new { onDate = "2026-08-14", startMinute = 540, endMinute = 720 });
            var detail = await DetailAsync(response);
            Assert.Equal(
                (Status: HttpStatusCode.BadRequest, NamesPast: true),
                (Status: response.StatusCode, NamesPast: detail.Contains("past", StringComparison.OrdinalIgnoreCase)));
        }

        [Fact]
        public async Task TodayItselfIsAccepted()
        {
            var specialStore = new FakeScheduleSpecialStore();
            await using var factory = new SpecialsApiWebFactory(specialStore, now: Today);
            var client = await SpecialsApiWebFactory.LoggedInClientAsync(factory);

            var response = await client.PostAsJsonAsync(
                "/api/schedule/specials", new { onDate = "2026-08-15", startMinute = 540, endMinute = 720 });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        [Fact]
        public async Task AnUnknownPersonaIdIsRejected400NamingPersona()
        {
            var response = await PostAsync(new { onDate = "2026-08-20", startMinute = 540, endMinute = 720, personaId = 999 });
            var detail = await DetailAsync(response);
            Assert.Equal(
                (Status: HttpStatusCode.BadRequest, NamesPersona: true),
                (Status: response.StatusCode, NamesPersona: detail.Contains("persona", StringComparison.OrdinalIgnoreCase)));
        }

        [Fact]
        public async Task AnUnknownShowIdIsRejected400NamingShow()
        {
            var response = await PostAsync(new { onDate = "2026-08-20", startMinute = 540, endMinute = 720, showId = 999 });
            var detail = await DetailAsync(response);
            Assert.Equal(
                (Status: HttpStatusCode.BadRequest, NamesShow: true),
                (Status: response.StatusCode, NamesShow: detail.Contains("show", StringComparison.OrdinalIgnoreCase)));
        }

        static async Task<HttpResponseMessage> PostAsync(object body)
        {
            var specialStore = new FakeScheduleSpecialStore();
            await using var factory = new SpecialsApiWebFactory(specialStore, now: Today);
            var client = await SpecialsApiWebFactory.LoggedInClientAsync(factory);
            return await client.PostAsJsonAsync("/api/schedule/specials", body);
        }
    }

    public sealed class ScenarioDatabaseRejectionsSurfaceHonestly
    {
        [Fact]
        public async Task AnOverlapRaisesA409()
        {
            // The store's own db/36 EXCLUDE constraint, and SpecialsRepository.CreateAsync's own
            // exclusion_violation-to-ScheduleSpecialCreateResult.Overlap translation, are both proven
            // for real against Postgres in GenWave.MediaLibrary.Tests — this project has no such
            // fixture, so the already-typed result is scripted directly (mirrors
            // Story305_ShowsApi.cs's own FakeShowStore.NextCreateResult idiom) to prove
            // SpecialsController.Create's own HTTP mapping, not the constraint or the translation.
            var specialStore = new FakeScheduleSpecialStore
            {
                NextCreateResult = new ScheduleSpecialCreateResult.Overlap(),
            };
            await using var factory = new SpecialsApiWebFactory(specialStore, now: Today);
            var client = await SpecialsApiWebFactory.LoggedInClientAsync(factory);

            var response = await client.PostAsJsonAsync(
                "/api/schedule/specials", new { onDate = "2026-08-20", startMinute = 540, endMinute = 720 });

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task AConcurrentPersonaDeleteRaceRaisesA409()
        {
            var persona = new Persona(1, "Nova", "", "", "", DateTime.UtcNow, DateTime.UtcNow);
            var specialStore = new FakeScheduleSpecialStore
            {
                NextCreateResult = new ScheduleSpecialCreateResult.UnknownReference(),
            };
            await using var factory = new SpecialsApiWebFactory(
                specialStore, personaStore: new FakePersonaStore([persona]), now: Today);
            var client = await SpecialsApiWebFactory.LoggedInClientAsync(factory);

            var response = await client.PostAsJsonAsync(
                "/api/schedule/specials",
                new { onDate = "2026-08-20", startMinute = 540, endMinute = 720, personaId = persona.Id });

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
    }

    public sealed class ScenarioAuthPosture
    {
        [Fact]
        public async Task AnUnauthenticatedListReturns401()
        {
            // Admin:Password set, no cookie -> 401 — the same deny-by-default AdminOnly-plane posture
            // Story305_ShowsApi.cs's own AnUnauthenticatedListReturns401 already proves for /api/shows.
            var specialStore = new FakeScheduleSpecialStore();
            await using var factory = new SpecialsApiWebFactory(specialStore, withAdminPassword: true);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.GetAsync("/api/schedule/specials");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    static async Task<string> DetailAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("detail").GetString() ?? "";
    }
}

// ── In-process fakes local to this file ───────────────────────────────────────────────────────────

/// <summary>Scriptable <see cref="IPersonaStore"/> double, narrowed to what
/// <see cref="SpecialsController"/> actually calls (<see cref="IPersonaStore.GetByIdAsync"/>) — mirrors
/// Story120_PersonaEndpoints.cs's own per-file <c>FakePersonaStore</c> idiom (a file-scoped copy per
/// spec file that needs one, rather than growing a shared <c>Fakes/FakePersonaStore.cs</c> no other
/// file in this project needs yet). Every other member throws — nothing in this file's Facts ever
/// creates/updates/deletes a persona, lists them, or reads a card/slug.</summary>
file sealed class FakePersonaStore(IEnumerable<Persona>? seed = null) : IPersonaStore
{
    readonly Dictionary<long, Persona> byId = (seed ?? []).ToDictionary(p => p.Id);

    public Task<Persona?> GetByIdAsync(long id, CancellationToken ct) =>
        Task.FromResult(byId.GetValueOrDefault(id));

    public Task<IReadOnlyList<Persona>> GetAllAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story317's specials Facts.");

    public Task<PersonaWriteResult> CreateAsync(PersonaDraft draft, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story317's specials Facts.");

    public Task<PersonaWriteResult> UpdateAsync(long id, PersonaDraft draft, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story317's specials Facts.");

    public Task<PersonaWriteResult> DeleteAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story317's specials Facts.");

    public Task<PersonaCard?> GetCardByIdAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story317's specials Facts.");

    public Task<long?> GetIdBySlugAsync(string slug, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story317's specials Facts.");
}

// ── Test harness ───────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for this file's own Facts — mirrors
/// Story305_ShowsApi.cs's own <c>ShowsApiWebFactory</c> idiom: <see cref="IScheduleSpecialStore"/>,
/// <see cref="IPersonaStore"/>, <see cref="IShowStore"/>, and <see cref="IStationClockProvider"/> all
/// replaced by stateful/deterministic fakes (an empty persona/show store when a Fact doesn't need one
/// — every Fact that omits a persona/show id entirely never calls either).
/// </summary>
file sealed class SpecialsApiWebFactory(
    FakeScheduleSpecialStore specialStore,
    FakePersonaStore? personaStore = null,
    FakeShowStore? showStore = null,
    DateTimeOffset? now = null,
    bool withAdminPassword = true)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story317-specialsapi";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");

        if (withAdminPassword)
        {
            builder.UseSetting("Admin:Password", Password);
        }

        builder.ConfigureTestServices(services =>
        {
            // No Liquidsoap/DB connections during this test.
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IScheduleSpecialStore>();
            services.AddSingleton<IScheduleSpecialStore>(specialStore);

            services.RemoveAll<IPersonaStore>();
            services.AddSingleton<IPersonaStore>(personaStore ?? new FakePersonaStore());

            services.RemoveAll<IShowStore>();
            services.AddSingleton<IShowStore>(showStore ?? new FakeShowStore());

            services.RemoveAll<IStationClockProvider>();
            services.AddSingleton<IStationClockProvider>(new FakeStationClockProvider(now ?? DateTimeOffset.UtcNow));
        });
    }

    /// <summary>Logs in via the real POST /api/auth/login round trip (mirrors Story305's own helper) and returns the cookie-bearing client.</summary>
    public static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }
}

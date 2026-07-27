// STORY-240 — The grid holds the week (SPEC F91.1, F91.8, PLAN T118/T122)
//
// BDD specification — xUnit. Entry-point discipline: every scenario drives
// GET/PUT /api/schedule through WebApplicationFactory<Program> with real cookie auth (real
// POST /api/auth/login — mirrors Story251_ExplicitOverrideEndpoint.cs's idiom) — never the week
// repository directly.
//
// Layering (PLAN T122 review): ScheduleRepository's own per-cell validation (30-minute step,
// overlap, unknown persona, atomic delete-then-insert) is REAL code proven against a REAL Postgres
// fixture in GenWave.MediaLibrary.Tests/Specs/Story240_ScheduleStore.cs — reimplementing that
// validation inside a Host.Tests double would be a lookalike double (this cycle's own banned
// pattern), and this project has no Postgres fixture to prove it against honestly anyway. This file
// therefore scopes itself to the WIRE concerns only: DTO mapping fidelity (every
// ScheduleSegment/ScheduleCellError field survives the trip through ScheduleSegmentDto/
// ScheduleCellErrorDto unchanged), the 400 shape carrying the store's per-cell errors (under the
// "cellErrors" key — deliberately not "errors", which collides with ASP.NET Core's own automatic
// model-binding 400 on this same endpoint+status), auth parity, the 415 content-type CSRF guard
// (mirrors Story112_RatingEndpoints.cs's own idiom), the 409/500 split on a thrown PostgresException
// (FakeScheduleStore.NextThrow), and the 200 response shape — driven against FakeScheduleStore
// (Fakes/FakeScheduleStore.cs), a stateful echo-and-assign-ids double that never judges a
// submission's validity itself. Sad-path facts SCRIPT the store's rejection
// (FakeScheduleStore.NextReplaceResult) with realistic errors rather than deriving them, so this
// file proves the controller maps a real ScheduleReplaceResult.ValidationFailed correctly, without
// re-deciding what counts as invalid.

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
using Npgsql;

namespace GenWave.Host.Tests.Specs;

// ── WebApplicationFactory driving the real HTTP pipeline ─────────────────────────────────────────

/// <summary>
/// Boots the real Program.cs graph (routing, cookie auth, the production
/// <c>GET/PUT /api/schedule</c> route) with <see cref="IScheduleStore"/> replaced by
/// <paramref name="store"/> — mirrors Story251's <c>ExplicitOverrideApiWebFactory</c>.
/// </summary>
file sealed class ScheduleApiWebFactory(FakeScheduleStore store, bool withAdminPassword = true)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-grid-holds-the-week";

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

            services.RemoveAll<IScheduleStore>();
            services.AddSingleton<IScheduleStore>(store);
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

public static class FeatureGridHoldsTheWeek
{
    static ScheduleSegmentDto MusicOnly(int day, int start, int end) =>
        new(Id: null, day, start, end, PersonaId: null, Genres: null, EnergyMin: null, EnergyMax: null);

    static ScheduleSegmentDto Staffed(int day, int start, int end, long personaId, string[]? genres = null, double? energyMin = null, double? energyMax = null) =>
        new(Id: null, day, start, end, personaId, genres, energyMin, energyMax);

    public sealed class ScenarioStoringAValidWeek
    {
        // Given segment rows on 30-minute boundaries — some NULL persona (music-only),
        // some NULL envelope fields (station default), one midnight-spanning show as
        // two per-day rows — When the week is PUT and then GET.

        [Fact]
        public async Task RoundTripReturnsTheIdenticalWeekDocument()
        {
            // A scaled-down 12-DJ-style week: several staffed slots across different days, mixed
            // with music-only rows and mixed envelope overrides.
            var request = new ScheduleWeekDto(
            [
                Staffed(1, 0, 600, 11, ["jazz", "funk"], 0.3, 0.8),
                MusicOnly(1, 600, 1440),
                Staffed(3, 480, 1020, 22),
                MusicOnly(6, 0, 1440),
            ]);

            var store = new FakeScheduleStore();
            await using var factory = new ScheduleApiWebFactory(store);
            var client = await ScheduleApiWebFactory.LoggedInClientAsync(factory);

            var putResponse = await client.PutAsJsonAsync("/api/schedule", request);
            Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
            var putBody = await putResponse.Content.ReadFromJsonAsync<ScheduleWeekDto>();

            var getResponse = await client.GetAsync("/api/schedule");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
            var getBody = await getResponse.Content.ReadFromJsonAsync<ScheduleWeekDto>();

            Assert.NotNull(putBody);
            Assert.NotNull(getBody);
            Assert.Equal(request.Segments.Count, putBody.Segments.Count);
            Assert.Equal(putBody.Segments.Count, getBody.Segments.Count);

            // Every submitted field survives the round trip untouched, matched by (day, startMinute)
            // since a fresh store-assigned id makes direct list equality with the request too strict.
            // Genres is asserted with Assert.Equal's own sequence comparison (element-wise), not
            // record equality — two independently-deserialized List<string> instances are never
            // reference-equal, and List<T> has no value-equality override of its own.
            foreach (var submitted in request.Segments)
            {
                var fromPut = putBody.Segments.Single(s => s.Day == submitted.Day && s.StartMinute == submitted.StartMinute);
                var fromGet = getBody.Segments.Single(s => s.Day == submitted.Day && s.StartMinute == submitted.StartMinute);

                foreach (var stored in new[] { fromPut, fromGet })
                {
                    Assert.NotNull(stored.Id);
                    Assert.Equal(submitted.EndMinute, stored.EndMinute);
                    Assert.Equal(submitted.PersonaId, stored.PersonaId);
                    Assert.Equal(submitted.Genres, stored.Genres);
                    Assert.Equal(submitted.EnergyMin, stored.EnergyMin);
                    Assert.Equal(submitted.EnergyMax, stored.EnergyMax);
                }

                // The PUT response and the immediately-following GET must agree on the store-assigned
                // id too — the caller (T129) trusts the PUT response as the fresh document without
                // issuing a follow-up GET.
                Assert.Equal(fromPut.Id, fromGet.Id);
            }
        }

        [Fact]
        public async Task MusicOnlySegmentsCarryNullPersona()
        {
            var request = new ScheduleWeekDto([MusicOnly(2, 0, 1440)]);
            var store = new FakeScheduleStore();
            await using var factory = new ScheduleApiWebFactory(store);
            var client = await ScheduleApiWebFactory.LoggedInClientAsync(factory);

            var response = await client.PutAsJsonAsync("/api/schedule", request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            var segment = Assert.Single(body.GetProperty("segments").EnumerateArray());
            // The "personaId" property must be PRESENT with an explicit JSON null — not simply
            // absent — so the T129 editor can distinguish "music-only" from "field omitted".
            Assert.Equal(JsonValueKind.Null, segment.GetProperty("personaId").ValueKind);
        }

        [Fact]
        public async Task MidnightSpanningShowIsTwoPerDayRows()
        {
            // A show that runs Monday 23:00 through Tuesday 01:00 has no single-row representation
            // (SPEC F91.1's day_of_week grid) — it is submitted (and stored) as two per-day rows.
            var request = new ScheduleWeekDto(
            [
                Staffed(1, 1380, 1440, 7), // Monday 23:00–24:00
                Staffed(2, 0, 60, 7),      // Tuesday 00:00–01:00
            ]);
            var store = new FakeScheduleStore();
            await using var factory = new ScheduleApiWebFactory(store);
            var client = await ScheduleApiWebFactory.LoggedInClientAsync(factory);

            var response = await client.PutAsJsonAsync("/api/schedule", request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<ScheduleWeekDto>();
            Assert.NotNull(body);
            Assert.Equal(2, body.Segments.Count);
            Assert.Contains(body.Segments, s => s.Day == 1 && s.StartMinute == 1380 && s.EndMinute == 1440 && s.PersonaId == 7);
            Assert.Contains(body.Segments, s => s.Day == 2 && s.StartMinute == 0 && s.EndMinute == 60 && s.PersonaId == 7);
        }
    }

    public sealed class ScenarioAtomicReplace
    {
        // Given an existing stored week and a new valid week document, When PUT succeeds.

        [Fact]
        public async Task StoreHoldsExactlyTheNewWeek()
        {
            var store = new FakeScheduleStore();
            await using var factory = new ScheduleApiWebFactory(store);
            var client = await ScheduleApiWebFactory.LoggedInClientAsync(factory);

            await client.PutAsJsonAsync("/api/schedule", new ScheduleWeekDto([MusicOnly(0, 0, 1440), MusicOnly(1, 0, 1440)]));

            var response = await client.PutAsJsonAsync("/api/schedule", new ScheduleWeekDto([Staffed(5, 600, 660, 3)]));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<ScheduleWeekDto>();
            Assert.NotNull(body);
            var only = Assert.Single(body.Segments);
            Assert.Equal(5, only.Day);
            Assert.Equal(3, only.PersonaId);
        }

        [Fact]
        public async Task OldRowsAreGoneInTheSameTransaction()
        {
            // The DB-level atomicity guarantee (one transaction, old rows physically gone) is
            // proven against real Postgres in
            // GenWave.MediaLibrary.Tests/Specs/Story240_ScheduleStore.cs
            // (ScenarioAtomicReplace.ReplacingTheWeekLeavesExactlyTheNewRowsTheOldOnesAreGone). This
            // wire-level fact proves the OTHER half: a GET issued straight after a successful PUT
            // reflects ONLY that PUT's result — no trace of a previous week survives on the read
            // path either.
            var store = new FakeScheduleStore();
            await using var factory = new ScheduleApiWebFactory(store);
            var client = await ScheduleApiWebFactory.LoggedInClientAsync(factory);

            await client.PutAsJsonAsync("/api/schedule", new ScheduleWeekDto([MusicOnly(0, 0, 1440), MusicOnly(1, 0, 1440)]));
            await client.PutAsJsonAsync("/api/schedule", new ScheduleWeekDto([Staffed(5, 600, 660, 3)]));

            var response = await client.GetAsync("/api/schedule");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<ScheduleWeekDto>();
            Assert.NotNull(body);
            var only = Assert.Single(body.Segments);
            Assert.Equal(5, only.Day);
            Assert.DoesNotContain(body.Segments, s => s.Day == 0 || s.Day == 1);
        }
    }

    public sealed class ScenarioRejectingInvalidWeeks
    {
        // Sad path — F91.1 constraints surface as per-cell 400s; the stored week never changes.
        // FakeScheduleStore.NextReplaceResult scripts a realistic ScheduleReplaceResult.ValidationFailed
        // so these facts prove the WIRE mapping (ScheduleCellError -> ScheduleCellErrorDto, field for
        // field) rather than re-deriving what ScheduleRepository itself would reject.

        [Fact]
        public async Task OverlappingSegmentsReturnPerCellErrorNamingDayAndRange()
        {
            var store = new FakeScheduleStore();
            store.NextReplaceResult = new ScheduleReplaceResult.ValidationFailed(
                [new ScheduleCellError(1, DayOfWeek.Monday, 300, 900, ScheduleCellErrorKind.Overlap, "overlaps another segment on Monday.")]);
            await using var factory = new ScheduleApiWebFactory(store);
            var client = await ScheduleApiWebFactory.LoggedInClientAsync(factory);

            var response = await client.PutAsJsonAsync(
                "/api/schedule", new ScheduleWeekDto([MusicOnly(1, 0, 600), MusicOnly(1, 300, 900)]));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            var error = Assert.Single(body.GetProperty("cellErrors").EnumerateArray());
            Assert.Equal(1, error.GetProperty("rowIndex").GetInt32());
            Assert.Equal(1, error.GetProperty("day").GetInt32());
            Assert.Equal(300, error.GetProperty("startMinute").GetInt32());
            Assert.Equal(900, error.GetProperty("endMinute").GetInt32());
            Assert.Equal("overlap", error.GetProperty("kind").GetString());
            Assert.Contains("Monday", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task OffGridStartMinuteIsRejected()
        {
            var store = new FakeScheduleStore();
            store.NextReplaceResult = new ScheduleReplaceResult.ValidationFailed(
                [new ScheduleCellError(0, DayOfWeek.Monday, 15, 1440, ScheduleCellErrorKind.InvalidMinuteRange,
                    "start_minute 15 must be a multiple of 30 within [0, 1410].")]);
            await using var factory = new ScheduleApiWebFactory(store);
            var client = await ScheduleApiWebFactory.LoggedInClientAsync(factory);

            var response = await client.PutAsJsonAsync("/api/schedule", new ScheduleWeekDto([MusicOnly(1, 15, 1440)]));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            var error = Assert.Single(body.GetProperty("cellErrors").EnumerateArray());
            Assert.Equal("invalidMinuteRange", error.GetProperty("kind").GetString());
            Assert.Equal(15, error.GetProperty("startMinute").GetInt32());
        }

        [Fact]
        public async Task InvalidDayIsRejected()
        {
            // day 9 is off ScheduleSegmentDto's own 0-6 range (see that DTO's remarks: an
            // out-of-range Day is never rejected by the controller itself, only by the store's
            // app-side validation) — this fact proves ScheduleCellErrorKind.InvalidDay actually
            // reaches the wire, which no other fact in this file exercised before.
            var store = new FakeScheduleStore();
            store.NextReplaceResult = new ScheduleReplaceResult.ValidationFailed(
                [new ScheduleCellError(0, (DayOfWeek)9, 0, 1440, ScheduleCellErrorKind.InvalidDay,
                    "day 9 is not a defined day of week.")]);
            await using var factory = new ScheduleApiWebFactory(store);
            var client = await ScheduleApiWebFactory.LoggedInClientAsync(factory);

            var response = await client.PutAsJsonAsync("/api/schedule", new ScheduleWeekDto([MusicOnly(9, 0, 1440)]));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            var error = Assert.Single(body.GetProperty("cellErrors").EnumerateArray());
            Assert.Equal("invalidDay", error.GetProperty("kind").GetString());
            Assert.Equal(9, error.GetProperty("day").GetInt32());
        }

        [Fact]
        public async Task UnknownPersonaIdIsRejected()
        {
            var store = new FakeScheduleStore();
            store.NextReplaceResult = new ScheduleReplaceResult.ValidationFailed(
                [new ScheduleCellError(0, DayOfWeek.Monday, 0, 1440, ScheduleCellErrorKind.UnknownPersona,
                    "persona id 999999 does not exist.")]);
            await using var factory = new ScheduleApiWebFactory(store);
            var client = await ScheduleApiWebFactory.LoggedInClientAsync(factory);

            var response = await client.PutAsJsonAsync("/api/schedule", new ScheduleWeekDto([Staffed(1, 0, 1440, 999_999)]));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            var error = Assert.Single(body.GetProperty("cellErrors").EnumerateArray());
            Assert.Equal("unknownPersona", error.GetProperty("kind").GetString());
            Assert.Contains("999999", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task RejectionLeavesTheStoredWeekUnchanged()
        {
            var store = new FakeScheduleStore();
            await using var factory = new ScheduleApiWebFactory(store);
            var client = await ScheduleApiWebFactory.LoggedInClientAsync(factory);
            await client.PutAsJsonAsync("/api/schedule", new ScheduleWeekDto([MusicOnly(1, 0, 1440)]));

            store.NextReplaceResult = new ScheduleReplaceResult.ValidationFailed(
                [new ScheduleCellError(0, DayOfWeek.Tuesday, 15, 1440, ScheduleCellErrorKind.InvalidMinuteRange, "off-grid start minute.")]);
            var rejected = await client.PutAsJsonAsync("/api/schedule", new ScheduleWeekDto([MusicOnly(2, 15, 1440)]));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

            var getResponse = await client.GetAsync("/api/schedule");
            var body = await getResponse.Content.ReadFromJsonAsync<ScheduleWeekDto>();
            Assert.NotNull(body);
            var only = Assert.Single(body.Segments);
            Assert.Equal(1, only.Day);
        }

        [Fact]
        public async Task UnauthenticatedCallsMatchSettingsEndpointPosture()
        {
            // Admin:Password set, no cookie → 401 for BOTH /api/schedule and /api/settings — the
            // same deny-by-default AdminOnly-plane posture, proven side by side rather than assumed.
            var store = new FakeScheduleStore();
            await using var factory = new ScheduleApiWebFactory(store, withAdminPassword: true);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var scheduleResponse = await client.GetAsync("/api/schedule");
            var settingsResponse = await client.GetAsync("/api/settings");

            Assert.Equal(HttpStatusCode.Unauthorized, scheduleResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, settingsResponse.StatusCode);
        }
    }

    public sealed class ScenarioCellErrorKindWireMapping
    {
        // PLAN T122 review (F4): ScheduleController.KindWireValue hand-maps each
        // ScheduleCellErrorKind to a camelCase wire string with a trailing default arm as
        // exhaustiveness insurance for a future fifth member — but nothing pinned that every EXISTING
        // member actually reaches its own string rather than silently falling through that default
        // arm. This Theory drives every defined kind through the real HTTP pipeline and asserts none
        // of them lands on the "unknown" sentinel.

        public static IEnumerable<object[]> AllKinds() =>
            Enum.GetValues<ScheduleCellErrorKind>().Select(kind => new object[] { kind });

        [Theory]
        [MemberData(nameof(AllKinds))]
        public async Task NoDefinedKindMapsToTheUnknownSentinel(ScheduleCellErrorKind kind)
        {
            var store = new FakeScheduleStore();
            store.NextReplaceResult = new ScheduleReplaceResult.ValidationFailed(
                [new ScheduleCellError(0, DayOfWeek.Monday, 0, 1440, kind, "scripted for wire-mapping coverage.")]);
            await using var factory = new ScheduleApiWebFactory(store);
            var client = await ScheduleApiWebFactory.LoggedInClientAsync(factory);

            var response = await client.PutAsJsonAsync("/api/schedule", new ScheduleWeekDto([MusicOnly(1, 0, 1440)]));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            var error = Assert.Single(body.GetProperty("cellErrors").EnumerateArray());
            Assert.NotEqual("unknown", error.GetProperty("kind").GetString());
        }
    }

    public sealed class ScenarioStoreThrows
    {
        // ScheduleController's own remarks: IScheduleStore.ReplaceWeekAsync can throw a raw
        // PostgresException when a persona a validated row names is deleted out from under a
        // concurrent PUT between validation and insert — a 23503 foreign-key violation. These facts
        // script that exception via FakeScheduleStore.NextThrow (this project has no Postgres
        // fixture to raise a real one against) to prove the controller's narrowed catch (PLAN T122
        // review, F2): only that specific SQLSTATE maps to the generic 409, with no raw Postgres
        // detail on the wire; every OTHER PostgresException (permission errors, disk full, a real
        // CHECK/EXCLUDE bug) propagates to the generic 500 instead of being folded into "reload and
        // try again".

        [Fact]
        public async Task ForeignKeyViolationDuringReplaceIsA409WithNoRawPostgresDetailOnTheWire()
        {
            var store = new FakeScheduleStore
            {
                NextThrow = new PostgresException(
                    messageText: "insert or update on table \"segment_schedule\" violates foreign key constraint \"segment_schedule_persona_id_fkey\"",
                    severity: "ERROR",
                    invariantSeverity: "ERROR",
                    sqlState: "23503",
                    detail: null,
                    hint: null,
                    position: 0,
                    internalPosition: 0,
                    internalQuery: null,
                    where: null,
                    schemaName: "station",
                    tableName: "segment_schedule",
                    columnName: null,
                    dataTypeName: null,
                    constraintName: "segment_schedule_persona_id_fkey",
                    file: null,
                    line: null,
                    routine: null),
            };
            await using var factory = new ScheduleApiWebFactory(store);
            var client = await ScheduleApiWebFactory.LoggedInClientAsync(factory);

            var response = await client.PutAsJsonAsync("/api/schedule", new ScheduleWeekDto([Staffed(1, 0, 1440, 11)]));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var bodyText = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("23503", bodyText, StringComparison.Ordinal);
            Assert.DoesNotContain("segment_schedule", bodyText, StringComparison.Ordinal);
        }

        [Fact]
        public async Task NonForeignKeyPostgresExceptionDuringReplacePropagatesAsA500()
        {
            var store = new FakeScheduleStore
            {
                // 53100 = disk_full — a real operational failure, never the persona race; folding it
                // into "reload and try again" would hide a fault reloading cannot fix.
                NextThrow = new PostgresException(
                    "could not extend file \"base/16400/16912\": No space left on device",
                    "ERROR", "ERROR", "53100"),
            };
            await using var factory = new ScheduleApiWebFactory(store);
            var client = await ScheduleApiWebFactory.LoggedInClientAsync(factory);

            var response = await client.PutAsJsonAsync("/api/schedule", new ScheduleWeekDto([Staffed(1, 0, 1440, 11)]));

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
    }

    public sealed class ScenarioContentTypeGuard
    {
        [Fact]
        public async Task APutWithoutJsonContentTypeReturns415()
        {
            // Valid cookie not needed — content-type negotiation is tested in isolation, mirrors
            // Story112_RatingEndpoints.cs's own AWriteWithoutJsonContentTypeReturns415 (itself
            // mirroring Story058's idiom): [Consumes("application/json")] rejects the request before
            // auth or the store are ever touched.
            var store = new FakeScheduleStore();
            await using var factory = new ScheduleApiWebFactory(store, withAdminPassword: false);
            var client = factory.CreateClient();

            var body = new StringContent(
                "day=1&startMinute=0", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
            var response = await client.PutAsync("/api/schedule", body);

            // [Consumes("application/json")] returns 415 Unsupported Media Type.
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        }
    }
}

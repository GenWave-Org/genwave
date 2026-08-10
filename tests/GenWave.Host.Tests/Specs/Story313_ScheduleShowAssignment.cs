// STORY-313 — Span-assign & imaging scope (F119.2), the wire half (SPEC F119.2, PLAN T243)
//
// BDD specification — xUnit. Entry-point discipline: every scenario drives
// POST /api/schedule/assign-show through WebApplicationFactory<Program> with real cookie auth (real
// POST /api/auth/login — mirrors Story240_GridHoldsTheWeek.cs's own idiom) — never the schedule store
// directly.
//
// Layering (mirrors Story240_GridHoldsTheWeek.cs's own review note): ScheduleRepository.AssignShowAsync's
// own F119.2 run-span algorithm — contiguous same-persona run, stops at interruptions, narrow-to-one,
// clear, transactionality — is REAL code proven against a REAL Postgres fixture in
// GenWave.MediaLibrary.Tests/Specs/Story313_ScheduleShowAssignment.cs. Reimplementing that algorithm
// inside a Host.Tests double would be a lookalike double; this file scopes itself to the WIRE concerns
// only: request-field passthrough to the store (blockId/showId/applyToRun reach AssignShowAsync
// unchanged — the wire-level proof of narrow-to-one and clear), the 200 response body naming every
// updated block id, the 400 shape for an unknown block/show (ShowAssignResult.BlockNotFound/
// ShowNotFound scripted via FakeScheduleStore.NextAssignShowResult), auth parity, and the 415
// content-type CSRF guard.

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

// ── WebApplicationFactory driving the real HTTP pipeline ─────────────────────────────────────────

/// <summary>
/// Boots the real Program.cs graph (routing, cookie auth, the production
/// <c>POST /api/schedule/assign-show</c> route) with <see cref="IScheduleStore"/> replaced by
/// <paramref name="store"/> — mirrors Story240_GridHoldsTheWeek.cs's own <c>ScheduleApiWebFactory</c>.
/// </summary>
file sealed class ScheduleAssignShowApiWebFactory(FakeScheduleStore store, bool withAdminPassword = true)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story313-assign-show";

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

    /// <summary>Logs in via the real POST /api/auth/login round trip and returns the cookie-bearing client.</summary>
    public static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }
}

// ── In-process tests ──────────────────────────────────────────────────────────────────────────────

public static class FeatureScheduleShowAssignmentWire
{
    public sealed class ScenarioRequestFieldsPassThroughToTheStore
    {
        [Fact]
        public async Task ARunDefaultAssignmentReachesTheStoreWithApplyToRunTrue()
        {
            // Given a store that reports two blocks updated at a known fingerprint
            var store = new FakeScheduleStore
            {
                NextAssignShowResult = new ShowAssignResult.Assigned([11, 12], "week-version-abc"),
            };
            await using var factory = new ScheduleAssignShowApiWebFactory(store);
            var client = await ScheduleAssignShowApiWebFactory.LoggedInClientAsync(factory);

            // When the picker's own run-default request is posted
            var response = await client.PostAsJsonAsync(
                "/api/schedule/assign-show", new AssignShowRequestDto(BlockId: 11, ShowId: 7, ApplyToRun: true));

            // Then 200 names every updated block id and carries the store's own fingerprint (SPEC F2)
            // through unchanged, and the store received the exact fields submitted
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<AssignShowResponseDto>();
            Assert.Equal([11L, 12L], body!.UpdatedBlockIds);
            Assert.Equal("week-version-abc", body.Version);
            var call = Assert.Single(store.AssignShowAsyncCalls);
            Assert.Equal((11L, (long?)7, true), call);
        }

        [Fact]
        public async Task NarrowToOneReachesTheStoreWithApplyToRunFalse()
        {
            // Given a store that reports a single block updated
            var store = new FakeScheduleStore
            {
                NextAssignShowResult = new ShowAssignResult.Assigned([11], "week-version-def"),
            };
            await using var factory = new ScheduleAssignShowApiWebFactory(store);
            var client = await ScheduleAssignShowApiWebFactory.LoggedInClientAsync(factory);

            // When the panel's own narrow-to-one checkbox is checked
            var response = await client.PostAsJsonAsync(
                "/api/schedule/assign-show", new AssignShowRequestDto(BlockId: 11, ShowId: 7, ApplyToRun: false));

            // Then 200 names only the one block, and applyToRun reached the store as false
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<AssignShowResponseDto>();
            Assert.Equal([11L], body!.UpdatedBlockIds);
            var call = Assert.Single(store.AssignShowAsyncCalls);
            Assert.False(call.ApplyToRun);
        }

        [Fact]
        public async Task ANullShowIdReachesTheStoreAsNullTheClearCase()
        {
            // Given a store that reports the clear landed
            var store = new FakeScheduleStore
            {
                NextAssignShowResult = new ShowAssignResult.Assigned([11], "week-version-ghi"),
            };
            await using var factory = new ScheduleAssignShowApiWebFactory(store);
            var client = await ScheduleAssignShowApiWebFactory.LoggedInClientAsync(factory);

            // When a null showId (clear) is submitted
            var response = await client.PostAsJsonAsync(
                "/api/schedule/assign-show", new AssignShowRequestDto(BlockId: 11, ShowId: null, ApplyToRun: true));

            // Then 200, and the store received showId as null — a JSON-present explicit null, not an
            // absent field silently defaulting to something else.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var call = Assert.Single(store.AssignShowAsyncCalls);
            Assert.Null(call.ShowId);
        }

        [Fact]
        public async Task AnAbsentApplyToRunFieldReachesTheStoreAsTrueTheGridDefault()
        {
            // Given a store that reports a run-span update (SPEC F6: the grid's documented default)
            var store = new FakeScheduleStore
            {
                NextAssignShowResult = new ShowAssignResult.Assigned([11, 12], "week-version-jkl"),
            };
            await using var factory = new ScheduleAssignShowApiWebFactory(store);
            var client = await ScheduleAssignShowApiWebFactory.LoggedInClientAsync(factory);

            // When the request body omits applyToRun entirely — never a client sending an explicit
            // false, and never System.Text.Json's own "missing non-nullable bool" default either
            var response = await client.PostAsJsonAsync(
                "/api/schedule/assign-show", new { blockId = 11L, showId = 7L });

            // Then the store received applyToRun as true — the run-span default, not the narrow-to-one
            // false a non-nullable wire field would have silently produced
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var call = Assert.Single(store.AssignShowAsyncCalls);
            Assert.True(call.ApplyToRun);
        }
    }

    public sealed class ScenarioRejections
    {
        [Fact]
        public async Task AnUnknownBlockIdReturns400NamingTheBlockNeverA404()
        {
            var store = new FakeScheduleStore { NextAssignShowResult = new ShowAssignResult.BlockNotFound() };
            await using var factory = new ScheduleAssignShowApiWebFactory(store);
            var client = await ScheduleAssignShowApiWebFactory.LoggedInClientAsync(factory);

            var response = await client.PostAsJsonAsync(
                "/api/schedule/assign-show", new AssignShowRequestDto(BlockId: 999_999, ShowId: 7, ApplyToRun: true));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("999999", await DetailAsync(response), StringComparison.Ordinal);
        }

        [Fact]
        public async Task AnUnknownShowIdReturns400NamingTheShow()
        {
            var store = new FakeScheduleStore { NextAssignShowResult = new ShowAssignResult.ShowNotFound() };
            await using var factory = new ScheduleAssignShowApiWebFactory(store);
            var client = await ScheduleAssignShowApiWebFactory.LoggedInClientAsync(factory);

            var response = await client.PostAsJsonAsync(
                "/api/schedule/assign-show", new AssignShowRequestDto(BlockId: 11, ShowId: 999_999, ApplyToRun: true));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("999999", await DetailAsync(response), StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioAuthAndContentTypePosture
    {
        [Fact]
        public async Task UnauthenticatedCallsMatchScheduleEndpointPosture()
        {
            // Admin:Password set, no cookie -> 401, the same deny-by-default AdminOnly-plane posture
            // GET/PUT /api/schedule already carry (Story240_GridHoldsTheWeek.cs's own parity fact).
            var store = new FakeScheduleStore();
            await using var factory = new ScheduleAssignShowApiWebFactory(store, withAdminPassword: true);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.PostAsJsonAsync(
                "/api/schedule/assign-show", new AssignShowRequestDto(BlockId: 1, ShowId: 1, ApplyToRun: true));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task APostWithoutJsonContentTypeReturns415()
        {
            // Mirrors Story240_GridHoldsTheWeek.cs's own APutWithoutJsonContentTypeReturns415:
            // [Consumes("application/json")] rejects the request before auth or the store are ever
            // touched.
            var store = new FakeScheduleStore();
            await using var factory = new ScheduleAssignShowApiWebFactory(store, withAdminPassword: false);
            var client = factory.CreateClient();

            var body = new StringContent(
                "blockId=1&showId=1", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
            var response = await client.PostAsync("/api/schedule/assign-show", body);

            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        }
    }

    static async Task<string> DetailAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("detail").GetString() ?? "";
    }
}

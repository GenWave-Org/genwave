// STORY-195 — Booth log (WIRE, admin-feed half)
//
// BDD specification — xUnit (SPEC F72.2, F72.4). Host.Tests has no station Postgres by convention
// (MediaLibrary.Tests owns DB Integration) — this file covers the endpoint's paging/admin-only/
// not-public behavior behind a fake IBoothLogReader (the endpoint's read seam). The narrative-row
// content (F72.1) and retention (F72.3) — both real-DB behavior a fake store would never exercise
// honestly — live in GenWave.MediaLibrary.Tests/Specs/Story195_BoothLogStore.cs instead, driving
// real event objects through the real BoothLogWriter/BoothLogDrainService into a real (test) DB.
// The admin-UI feed page is T40 (browser-verified).

using System.Net;
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

using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// In-memory <see cref="IBoothLogReader"/> double: performs the SAME keyset-paging predicate
/// <c>BoothLogRepository.ReadAsync</c> does (row-wise <c>(occurred_at, id) &lt; before</c>,
/// newest-first) over a fixed, caller-supplied (already newest-first) row set — so a scenario proves
/// the CONTROLLER's cursor round-trip honestly without a real database. Records every call.
/// </summary>
file sealed class FakeBoothLogReader(IReadOnlyList<BoothLogEntry> allNewestFirst) : IBoothLogReader
{
    public List<(BoothLogCursor? Before, int Take)> Calls { get; } = [];

    public Task<BoothLogPage> ReadAsync(BoothLogCursor? before, int take, CancellationToken ct)
    {
        Calls.Add((before, take));

        var candidates = before is null
            ? allNewestFirst
            : allNewestFirst.Where(e => IsBefore(e, before)).ToList();

        var page = candidates.Take(take).ToList();
        var nextBefore = candidates.Count > take ? new BoothLogCursor(page[^1].OccurredAt, page[^1].Id) : null;

        return Task.FromResult(new BoothLogPage(page, nextBefore));
    }

    static bool IsBefore(BoothLogEntry e, BoothLogCursor cursor) =>
        e.OccurredAt < cursor.OccurredAt || (e.OccurredAt == cursor.OccurredAt && e.Id < cursor.Id);

    public Task<long?> GetMediaIdAsync(long id, CancellationToken ct) =>
        Task.FromResult(allNewestFirst.FirstOrDefault(e => e.Id == id)?.MediaId);
}

/// <summary>
/// <see cref="IPersonaTasteAccrualStore"/> stub for this file's facts, none of which exercise the
/// taste-thumb route (STORY-215, PLAN T70) — only the paging/admin-surface behavior behind
/// <see cref="IBoothLogReader"/> is this file's concern.
/// </summary>
file sealed class NotSupportedPersonaTasteAccrualStore : IPersonaTasteAccrualStore
{
    public Task<TasteThumbOutcome> ThumbAsync(long boothLogId, TasteThumbDirection direction, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story195_BoothLog.cs's paging/admin-surface facts.");
}

/// <summary>Builds a <see cref="BoothLogController"/> wired to the given fake reader.</summary>
file static class BoothLogControllerFactory
{
    public static BoothLogController Build(IBoothLogReader reader) =>
        new(reader, new NotSupportedPersonaTasteAccrualStore(), new FakeMediaLibraryMembership(),
            new FakeSafeScopeProvider(), NullLogger<BoothLogController>.Instance);
}

// ── WebApplicationFactory for the auth/surface posture ACs ──────────────────────────────────────────

/// <summary>
/// Minimal <see cref="WebApplicationFactory{TEntryPoint}"/> that brings up the real HTTP pipeline
/// (routing, auth, the surface gate) while removing hosted services that would attempt real
/// Liquidsoap/Postgres connections. Mirrors Story163's <c>PoliciesWebFactory</c>/Story171's
/// <c>SpectatorHardeningWebFactory</c> shape — neither scenario below ever resolves
/// <see cref="IBoothLogReader"/> (401 is rejected by auth middleware before the controller is ever
/// constructed), so the booth log's own connection string is left at its (empty, dev-mode) default.
/// </summary>
file sealed class BoothLogApiWebFactory(bool spectatorMode = false) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        if (spectatorMode)
        {
            builder.UseSetting("Station:SpectatorMode", "true");
            builder.UseSetting("Station:PublicStreamUrl", "https://demo.example/stream");
        }

        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-x7z");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
        });
    }
}

// ── Specs ────────────────────────────────────────────────────────────────────────────────────────

public static class FeatureBoothLog
{
    static BoothLogEntry Entry(long id, DateTime occurredAt, string kind, string summary) =>
        new(id, occurredAt, kind, summary);

    static readonly DateTime Now = new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);

    // Five rows, newest-first — the fixed row set every ScenarioAdminFeed fact pages over.
    static readonly BoothLogEntry[] FiveRowsNewestFirst =
    [
        Entry(5, Now, "track-started", "Started 'Fifth Song' by Artist E"),
        Entry(4, Now.AddMinutes(-1), "patter-aired", "Patter aired (LeadIn, voice: af_heart)"),
        Entry(3, Now.AddMinutes(-2), "track-started", "Started 'Third Song' by Artist C"),
        Entry(2, Now.AddMinutes(-3), "mode-changed", "LLM degradation: Normal → Soft (…)"),
        Entry(1, Now.AddMinutes(-4), "track-started", "Started 'First Song' by Artist A"),
    ];

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioAdminFeed
    {
        [Fact]
        public async Task Endpoint_pages_newest_first_with_stable_paging()
        {
            // Given booth log rows spanning several pages...
            var controller = BoothLogControllerFactory.Build(new FakeBoothLogReader(FiveRowsNewestFirst));

            // When the AdminOnly endpoint is paged, two rows at a time...
            var page1 = Assert.IsType<BoothLogPageDto>(
                Assert.IsType<OkObjectResult>(await controller.List(before: null, take: 2, CancellationToken.None)).Value);
            var page2 = Assert.IsType<BoothLogPageDto>(
                Assert.IsType<OkObjectResult>(await controller.List(before: page1.NextBefore, take: 2, CancellationToken.None)).Value);
            var page3 = Assert.IsType<BoothLogPageDto>(
                Assert.IsType<OkObjectResult>(await controller.List(before: page2.NextBefore, take: 2, CancellationToken.None)).Value);

            // Then rows return newest-first with stable paging — every row exactly once, in order,
            // and the final page's cursor is exhausted.
            Assert.Equal(["Started 'Fifth Song' by Artist E", "Patter aired (LeadIn, voice: af_heart)"],
                page1.Entries.Select(e => e.Summary));
            Assert.NotNull(page1.NextBefore);

            Assert.Equal(["Started 'Third Song' by Artist C", "LLM degradation: Normal → Soft (…)"],
                page2.Entries.Select(e => e.Summary));
            Assert.NotNull(page2.NextBefore);

            Assert.Equal(["Started 'First Song' by Artist A"], page3.Entries.Select(e => e.Summary));
            Assert.Null(page3.NextBefore);
        }

        [Fact]
        public async Task Malformed_cursor_returns_400()
        {
            var controller = BoothLogControllerFactory.Build(new FakeBoothLogReader([]));

            var result = await controller.List(before: "not-a-cursor", take: 10, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Anonymous_request_is_rejected()
        {
            // Given no session cookie...
            await using var factory = new BoothLogApiWebFactory();
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            // When the AdminOnly endpoint is requested...
            var response = await client.GetAsync("/api/booth-log");

            // Then it is rejected, same as every other admin route.
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    public sealed class SadPathPublicSurface
    {
        [Fact]
        public async Task No_booth_log_content_or_endpoint_is_publicly_reachable()
        {
            // Given SpectatorMode on...
            await using var factory = new BoothLogApiWebFactory(spectatorMode: true);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            // When the booth log route is requested without a session...
            var response = await client.GetAsync("/api/booth-log");

            // Then it is never publicly reachable — the same AdminOnly gate as with SpectatorMode off.
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

            // And when every route is enumerated...
            var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
                .Single(e => (e as RouteEndpoint)?.RoutePattern.RawText
                    ?.Equals("api/booth-log", StringComparison.OrdinalIgnoreCase) == true);

            // Then the booth log endpoint carries the AdminOnly policy, not Spectator, and no
            // SpectatorSurface marker — it is not classified (or reachable) as a spectator/public
            // route by construction (F72.4).
            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).ToList();
            Assert.Contains(AuthorizationPolicies.AdminOnly, policies);
            Assert.DoesNotContain(AuthorizationPolicies.Spectator, policies);
            Assert.Null(endpoint.Metadata.GetMetadata<SpectatorSurfaceAttribute>());
            Assert.NotNull(endpoint.Metadata.GetMetadata<AdminSurfaceAttribute>());
        }
    }
}

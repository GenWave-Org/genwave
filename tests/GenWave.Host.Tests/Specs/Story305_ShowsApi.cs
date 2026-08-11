// STORY-305 — The show entity & API (F115.1, F115.4, F115.5) — endpoint half
//
// BDD specification — xUnit. WIRED T240 — every Fact below drives the real production
// /api/shows routes (F79/F90 AdminSurface + Settings posture) through WebApplicationFactory<Program>
// with real cookie auth (real POST /api/auth/login — mirrors Story240_GridHoldsTheWeek.cs's own
// idiom), against FakeShowStore/FakeScheduleStore/FakeShowImagingScope doubles — no live Postgres,
// this project has none for Host.Tests. The repository half (real Postgres, ShowRepository's own
// InvalidName/budget/slug-conflict validation) lives in
// GenWave.MediaLibrary.Tests/Specs/Story305_ShowRepository.cs — this file never re-derives that
// validation, only the WIRE mapping a scripted GenWave.Core.Domain.ShowWriteResult produces
// (mirrors FakeScheduleStore's own posture, see that Fake's remarks).
//
// ScenarioGateParityAcrossCreateAndUpdate extends PLAN T207's own 7-row BadBodyTable precedent
// (Story287_SaveAsOwn.cs): narrowed to this store's five app-seam gates (blank/fallback-slug name,
// and the three SPEC F115.1 budgets) since Show writes have no multi-phase manifest pipeline to
// drift the way the two theme-write routes once did — see ShowsController's own class remarks for
// why that gate lives as one shared WriteProblem mapping instead of a second ThemeWriteGate-shaped
// type.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

// ── In-process tests ──────────────────────────────────────────────────────────────────────────────

public static class FeatureShowsApi
{
    public sealed class ScenarioCrudThroughTheProductionSurface
    {
        [Fact]
        public async Task CrudRoundTripsThroughTheEndpoints()
        {
            // Given an authenticated admin session
            var store = new FakeShowStore();
            await using var factory = new ShowsApiWebFactory(store);
            var client = await ShowsApiWebFactory.LoggedInClientAsync(factory);

            // When a show is created, listed, edited, and fetched via /api/shows
            var createResponse = await client.PostAsJsonAsync(
                "/api/shows", new { name = "Night Moves", tagline = "Late-night deep cuts", flavor = "moody, sparse" });
            var created = await createResponse.Content.ReadFromJsonAsync<ShowDto>();
            Assert.NotNull(created);

            var list = await (await client.GetAsync("/api/shows")).Content.ReadFromJsonAsync<ShowDto[]>();

            var editResponse = await client.PatchAsJsonAsync(
                $"/api/shows/{created.Slug}",
                new { name = "Night Moves", tagline = "Revisited", flavor = "moodier, sparser" });
            var edited = await editResponse.Content.ReadFromJsonAsync<ShowDto>();

            var fetched = await (await client.GetAsync($"/api/shows/{created.Slug}")).Content.ReadFromJsonAsync<ShowDto>();

            // Then every field round-trips end to end: creation succeeds, the list carries the new
            // row, the edit lands, and a fresh read agrees with the edit response — reachable at all
            // only because LoggedInClientAsync's real cookie round trip passed the AdminSurface/
            // Settings gate every action above sits behind
            Assert.Equal(
                (Create: HttpStatusCode.Created, Listed: true, Edit: HttpStatusCode.OK,
                 EditedFields: (Name: "Night Moves", Tagline: "Revisited", Flavor: "moodier, sparser"),
                 FetchMatchesEdit: true),
                (Create: createResponse.StatusCode, Listed: list!.Any(s => s.Slug == created.Slug),
                 Edit: editResponse.StatusCode, EditedFields: (edited!.Name, edited.Tagline, edited.Flavor),
                 FetchMatchesEdit: fetched == edited));
        }

        [Fact]
        public async Task UnreferencedShowDeletesClean()
        {
            // Given a show no block, special, or imaging row references
            var store = new FakeShowStore();
            var imagingScope = new FakeShowImagingScope();
            await using var factory = new ShowsApiWebFactory(store, imagingScope: imagingScope);
            var client = await ShowsApiWebFactory.LoggedInClientAsync(factory);
            var created = await (await client.PostAsJsonAsync("/api/shows", new { name = "To Be Deleted" }))
                .Content.ReadFromJsonAsync<ShowDto>();

            // When DELETE /api/shows/{slug} runs
            var response = await client.DeleteAsync($"/api/shows/{created!.Slug}");

            // Then 204, the row is gone, and the unscope seam was called exactly once — proving a
            // clean delete still runs the best-effort cleanup step (never skipped just because there
            // was nothing scoped to find)
            var afterDelete = await client.GetAsync($"/api/shows/{created.Slug}");
            Assert.Equal(
                (Delete: HttpStatusCode.NoContent, Gone: HttpStatusCode.NotFound, UnscopeCalls: 1),
                (Delete: response.StatusCode, Gone: afterDelete.StatusCode, UnscopeCalls: imagingScope.UnscopeCalls.Count));
        }
    }

    public sealed class ScenarioGuardedDelete
    {
        [Fact]
        public async Task DeleteWithReferencesFails409NamingBlocks()
        {
            // Given a show referenced by schedule blocks — the store's own FK case (Referenced) is
            // scripted (this project has no Postgres fixture for Host.Tests to trigger the real
            // segment_schedule FK), and the referencing block detail is seeded on FakeScheduleStore
            // the same way ShowsController.Delete itself re-queries it (SPEC F115.4)
            var show = new Show(1, "Scheduled Show", "scheduled-show", null, null, null, null, DateTime.UtcNow, DateTime.UtcNow);
            var store = new FakeShowStore([show]) { NextDeleteResult = new ShowWriteResult.Referenced() };
            var scheduleStore = new FakeScheduleStore();
            scheduleStore.SlotsByShowId[show.Id] = [new ScheduledSlot(DayOfWeek.Monday, 540, 720)];
            var imagingScope = new FakeShowImagingScope();
            await using var factory = new ShowsApiWebFactory(store, scheduleStore, imagingScope);
            var client = await ShowsApiWebFactory.LoggedInClientAsync(factory);

            // When DELETE runs
            var response = await client.DeleteAsync($"/api/shows/{show.Slug}");

            // Then 409 whose body names the referencing blocks (the F104 guard precedent) — and
            // nothing was touched on the imaging side: a block-refused delete unscopes nothing (this
            // action's own "ordering is deliberate" rule)
            var detail = await DetailAsync(response);
            Assert.Equal(
                (Status: HttpStatusCode.Conflict, NamesTheDay: true, NamesTheTime: true, UnscopeCalls: 0),
                (Status: response.StatusCode,
                 NamesTheDay: detail.Contains("Mon", StringComparison.Ordinal),
                 NamesTheTime: detail.Contains("09:00", StringComparison.Ordinal),
                 UnscopeCalls: imagingScope.UnscopeCalls.Count));
        }

        [Fact]
        public async Task ScopedImagingRowsAreNamedAndUnscopedBestEffort()
        {
            // Given a show referenced only by a scoped imaging row (no FK — F117.1)
            var show = new Show(1, "Imaging-Scoped Show", "imaging-scoped-show", null, null, null, null, DateTime.UtcNow, DateTime.UtcNow);
            var store = new FakeShowStore([show]);
            var imagingScope = new FakeShowImagingScope(
                new Dictionary<long, IReadOnlyList<ScopedImagingRow>> { [show.Id] = [new ScopedImagingRow(42, "Sunset Ident")] });
            await using var factory = new ShowsApiWebFactory(store, imagingScope: imagingScope);
            var client = await ShowsApiWebFactory.LoggedInClientAsync(factory);

            // When DELETE runs
            var response = await client.DeleteAsync($"/api/shows/{show.Slug}");

            // Then the response names the row and the library-connection unscope write is issued
            // (idempotent second write — F115.4): the delete itself succeeded (nothing blocked it —
            // library.media.show_id carries no FK). Array/list members don't compare structurally
            // inside a bundled tuple (ValueTuple.Equals uses reference equality per component), so
            // the "called exactly once, with this show's id" claim gets its own Assert.Equal call.
            var body = await response.Content.ReadFromJsonAsync<ShowDeleteResponse>();
            Assert.Equal(
                (Status: HttpStatusCode.OK, NamedRow: "Sunset Ident"),
                (Status: response.StatusCode, NamedRow: body!.UnscopedImaging.Single().Title));
            Assert.Equal([show.Id], imagingScope.UnscopeCalls);
        }

        [Fact]
        public async Task UnscopeFailureStillReportsDeleteSuccessAndLogsTheError()
        {
            // Given a show whose post-delete imaging unscope will fail (e.g. the library connection
            // drops mid round-trip) — the failure is scripted on the fake rather than reproduced for
            // real, mirroring FakeScheduleStore's own NextThrow idiom
            var show = new Show(1, "Fragile Unscope Show", "fragile-unscope-show", null, null, null, null, DateTime.UtcNow, DateTime.UtcNow);
            var store = new FakeShowStore([show]);
            var imagingScope = new FakeShowImagingScope { NextThrow = new InvalidOperationException("library connection dropped") };
            var logs = new CapturingWarningLoggerProvider();
            await using var factory = new ShowsApiWebFactory(store, imagingScope: imagingScope, logs: logs);
            var client = await ShowsApiWebFactory.LoggedInClientAsync(factory);

            // When DELETE runs and the best-effort unscope throws
            var response = await client.DeleteAsync($"/api/shows/{show.Slug}");

            // Then the delete itself still reports success (204 — nothing was actually reported as
            // unscoped, since the failed call never got to name what it cleared), the failure is
            // logged naming the show so an operator can hand-recover, and the row is genuinely gone
            // despite the cleanup failure — a post-commit cleanup fault never surfaces as a 500
            // (this action's own "best-effort" contract, see ShowsController's own class remarks)
            var afterDelete = await client.GetAsync($"/api/shows/{show.Slug}");
            Assert.Equal(
                (Delete: HttpStatusCode.NoContent, Gone: HttpStatusCode.NotFound, UnscopeAttempted: 1, ErrorLogged: true),
                (Delete: response.StatusCode, Gone: afterDelete.StatusCode, UnscopeAttempted: imagingScope.UnscopeCalls.Count,
                 ErrorLogged: logs.Messages.Any(m =>
                     m.Contains("unscope failed", StringComparison.OrdinalIgnoreCase) &&
                     m.Contains(show.Slug, StringComparison.Ordinal))));
        }

        [Fact]
        public async Task DeleteNamesAMidnightEndingBlockAsTwentyFourHundredNotZero()
        {
            // Given a show referenced by a block running to the grid's own maximum end minute (1440 —
            // midnight) — the exact value FormatMinutes's own load-bearing comment (now shared via
            // ScheduledSlotText, PLAN T240 review) warns TimeSpan's "hh" format specifier would
            // silently misrender as "00:00"
            var show = new Show(1, "Overnight Show", "overnight-show", null, null, null, null, DateTime.UtcNow, DateTime.UtcNow);
            var store = new FakeShowStore([show]) { NextDeleteResult = new ShowWriteResult.Referenced() };
            var scheduleStore = new FakeScheduleStore();
            scheduleStore.SlotsByShowId[show.Id] = [new ScheduledSlot(DayOfWeek.Sunday, 1380, 1440)];
            await using var factory = new ShowsApiWebFactory(store, scheduleStore);
            var client = await ShowsApiWebFactory.LoggedInClientAsync(factory);

            // When DELETE runs
            var response = await client.DeleteAsync($"/api/shows/{show.Slug}");

            // Then the 409 body names the block's end as 24:00, never 00:00
            var detail = await DetailAsync(response);
            Assert.Equal(
                (Status: HttpStatusCode.Conflict, EndsAtTwentyFourHundred: true, NeverZeroZero: true),
                (Status: response.StatusCode,
                 EndsAtTwentyFourHundred: detail.Contains("23:00–24:00", StringComparison.Ordinal),
                 NeverZeroZero: !detail.Contains("00:00", StringComparison.Ordinal)));
        }

        [Fact]
        public async Task DeleteWithEmptyReferencedBlocksStillRefusesWithAGenericDetail()
        {
            // Given the store reports Referenced but the endpoint's own re-query names nothing — the
            // documented rare race in ReferencedProblem's own remarks (the FK fired, but the block
            // that caused it is gone by the time this action re-queries station.segment_schedule)
            var show = new Show(1, "Racy Show", "racy-show", null, null, null, null, DateTime.UtcNow, DateTime.UtcNow);
            var store = new FakeShowStore([show]) { NextDeleteResult = new ShowWriteResult.Referenced() };
            var scheduleStore = new FakeScheduleStore(); // SlotsByShowId left empty for show.Id
            var imagingScope = new FakeShowImagingScope();
            await using var factory = new ShowsApiWebFactory(store, scheduleStore, imagingScope);
            var client = await ShowsApiWebFactory.LoggedInClientAsync(factory);

            // When DELETE runs
            var response = await client.DeleteAsync($"/api/shows/{show.Slug}");

            // Then it still refuses 409 with the generic fallback wording (never a crash, never a
            // silently-empty-looking detail claiming nothing blocks it) — and, as with any Referenced
            // outcome, nothing was unscoped
            var detail = await DetailAsync(response);
            Assert.Equal(
                (Status: HttpStatusCode.Conflict, GenericWording: true, UnscopeCalls: 0),
                (Status: response.StatusCode,
                 GenericWording: detail.Contains("still appears in the format-clock schedule", StringComparison.Ordinal),
                 UnscopeCalls: imagingScope.UnscopeCalls.Count));
        }
    }

    public sealed class ScenarioProvenanceProtection
    {
        [Fact]
        public async Task AuthoredSaveNeverErasesImportedProvenance()
        {
            // Given an imported show
            var imported = new Show(
                1, "Retro Nights", "retro-nights", "Old tagline", null,
                ImportedFrom: "midnight-drive-catalog-entry", ImportedAt: DateTime.UtcNow,
                CreatedAt: DateTime.UtcNow, UpdatedAt: DateTime.UtcNow);
            var store = new FakeShowStore([imported]);
            await using var factory = new ShowsApiWebFactory(store);
            var client = await ShowsApiWebFactory.LoggedInClientAsync(factory);

            // When an authored save targets its slug
            var response = await client.PatchAsJsonAsync(
                "/api/shows/retro-nights", new { name = "Retro Nights", tagline = "Hijacked tagline" });

            // Then 409 — the ThemeWriteGate two-phase posture (F115.5); imported_from survives, and so
            // does every other field — the write never even reaches IShowStore.UpdateAsync
            var detail = await DetailAsync(response);
            var stillStored = await store.GetBySlugAsync("retro-nights", CancellationToken.None);
            Assert.Equal(
                (Status: HttpStatusCode.Conflict, NamesTheSlug: true,
                 ImportedFromSurvives: "midnight-drive-catalog-entry", TaglineUntouched: "Old tagline"),
                (Status: response.StatusCode, NamesTheSlug: detail.Contains("retro-nights", StringComparison.Ordinal),
                 ImportedFromSurvives: stillStored?.ImportedFrom, TaglineUntouched: stillStored?.Tagline));
        }
    }

    public sealed class ScenarioGateParityAcrossCreateAndUpdate
    {
        public static TheoryData<GateParityRow> Rows
        {
            get
            {
                var data = new TheoryData<GateParityRow>();
                foreach (var row in GateParityTable.Rows)
                    data.Add(row);

                return data;
            }
        }

        [Theory]
        [MemberData(nameof(Rows))]
        public async Task BothWriteRoutesRefuseWithTheIdenticalStatusAndDetail(GateParityRow row)
        {
            // Given an existing authored show to PATCH onto (POST always targets a fresh row) and the
            // SAME request body submitted to both routes (SlugConflictProblem's own detail embeds the
            // submitted name, so byte-identity across routes needs the same name either side)
            var existing = new Show(1, "Existing Show", "existing-show", null, null, null, null, DateTime.UtcNow, DateTime.UtcNow);
            var store = new FakeShowStore([existing]);
            await using var factory = new ShowsApiWebFactory(store);
            var client = await ShowsApiWebFactory.LoggedInClientAsync(factory);
            var body = new { name = "Night Moves", tagline = (string?)null, flavor = (string?)null };

            // When the SAME app-seam gate outcome is scripted for both the create and the update write
            store.NextCreateResult = row.Result;
            var createResponse = await client.PostAsJsonAsync("/api/shows", body);
            store.NextUpdateResult = row.Result;
            var updateResponse = await client.PatchAsJsonAsync("/api/shows/existing-show", body);

            // Then both refuse with the row's own expected status and byte-identical, content-bearing
            // detail text — proving ShowsController.WriteProblem produces the SAME body regardless of
            // which write route hit it (mirrors PLAN T207's own byte-identical-copy proof format)
            var createDetail = await DetailAsync(createResponse);
            var updateDetail = await DetailAsync(updateResponse);
            Assert.Equal(
                (CreateStatus: row.ExpectedStatus, UpdateStatus: row.ExpectedStatus,
                 DetailsMatch: true, NamesTheExpectedContent: true),
                (CreateStatus: createResponse.StatusCode, UpdateStatus: updateResponse.StatusCode,
                 DetailsMatch: createDetail == updateDetail,
                 NamesTheExpectedContent: updateDetail.Contains(row.ExpectedFragment, StringComparison.Ordinal)));
        }
    }

    public sealed class ScenarioAuthPosture
    {
        [Fact]
        public async Task AnUnauthenticatedListReturns401()
        {
            // Admin:Password set, no cookie -> 401 — the same deny-by-default AdminOnly-plane
            // posture GET/PUT /api/schedule and POST /api/media/{id}/vote already carry
            // (Story240_GridHoldsTheWeek.cs's UnauthenticatedCallsMatchSettingsEndpointPosture /
            // Story112_RatingEndpoints.cs's AWriteWithoutACookieReturns401 — the precedent PLAN
            // T244 names). ShowsApiWebFactory's own withAdminPassword knob (declared at T240) had
            // no Fact ever passing it explicitly, so no Fact in this file proved the deny-by-default
            // policy is actually wired for /api/shows rather than merely assumed from
            // ShowsController's [Authorize] attribute — this is that proof.
            var store = new FakeShowStore();
            await using var factory = new ShowsApiWebFactory(store, withAdminPassword: true);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.GetAsync("/api/shows");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    static async Task<string> DetailAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("detail").GetString() ?? "";
    }
}

// ── Gate-parity table (mirrors Story287_SaveAsOwn.cs's own BadBodyRow/BadBodyTable) ────────────────

/// <summary>One row of <see cref="FeatureShowsApi.ScenarioGateParityAcrossCreateAndUpdate"/>'s own
/// table (PLAN T240, extending the PLAN T207 precedent). <see cref="ToString"/> is what xUnit's test
/// explorer shows per row, so it names the gate under test, not the row's own field values.</summary>
public sealed record GateParityRow(string Label, ShowWriteResult Result, HttpStatusCode ExpectedStatus, string ExpectedFragment)
{
    public override string ToString() => Label;
}

/// <summary>
/// The five app-seam gates <c>ShowRepository.CreateAsync</c>/<c>UpdateAsync</c> share (proven for
/// real, against a real Postgres fixture, in <c>Story305_ShowRepository.cs</c>) — this table proves
/// the SEPARATE claim that <c>ShowsController</c>'s own HTTP mapping of each outcome never drifts
/// between the two write routes. Each row's <see cref="GateParityRow.ExpectedFragment"/> is content
/// the refusal MUST name, never a substring the base refusal's own boilerplate alone would already
/// satisfy (mirrors Story287_SaveAsOwn.cs's own N2 "NamesTheMissingFace pattern", generalized here).
/// </summary>
static class GateParityTable
{
    public static readonly IReadOnlyList<GateParityRow> Rows =
    [
        new("blank or fallback-slug name",
            new ShowWriteResult.InvalidName(), HttpStatusCode.BadRequest, "blank"),

        new("name over budget",
            new ShowWriteResult.BudgetExceeded(ShowBudgetField.Name), HttpStatusCode.BadRequest, "name must be at most 60"),

        new("tagline over budget",
            new ShowWriteResult.BudgetExceeded(ShowBudgetField.Tagline), HttpStatusCode.BadRequest, "tagline must be at most 120"),

        new("flavor over budget",
            new ShowWriteResult.BudgetExceeded(ShowBudgetField.Flavor), HttpStatusCode.BadRequest, "flavor must be at most 400"),

        new("slug conflict",
            new ShowWriteResult.SlugConflict(), HttpStatusCode.Conflict, "Night Moves"),
    ];
}

// ── Test harness ───────────────────────────────────────────────────────────────────────────────────

/// <summary>Captures every log entry of Warning or above so a spec can assert on
/// <c>ShowsController</c>'s own output — mirrors Story164_FailClosedWithoutPassword's own
/// <c>CapturingWarningLoggerProvider</c> idiom (a file-scoped copy per spec file that needs one,
/// rather than a fifth shared <c>Fakes/CapturingLogger.cs</c> alongside the four already in
/// MediaLibrary.Tests/Tts.Tests/Orchestration.Tests/Context.Tests — this project's own precedent for
/// wire-level log capture is already this per-file shape, not that per-project one).</summary>
file sealed class CapturingWarningLoggerProvider : ILoggerProvider
{
    readonly List<string> messages = [];
    public IReadOnlyList<string> Messages { get { lock (messages) return messages.ToList(); } }

    public ILogger CreateLogger(string categoryName) => new Logger(this);
    public void Dispose() { }

    void Add(string message) { lock (messages) messages.Add(message); }

    sealed class Logger(CapturingWarningLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) owner.Add(formatter(state, exception));
        }
    }
}

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for this file's own Facts — mirrors
/// Story240_GridHoldsTheWeek.cs's own <c>ScheduleApiWebFactory</c> idiom: <see cref="IShowStore"/>,
/// <see cref="IScheduleStore"/>, and <see cref="IShowImagingScope"/> all replaced by stateful fakes
/// (defaulted to empty ones when a Fact doesn't need to script them). <paramref name="logs"/> is
/// wired only when a Fact actually needs to assert on logged output (PLAN T240 review) — every other
/// Fact leaves it null and gets the host's ordinary logging pipeline, untouched.
/// </summary>
file sealed class ShowsApiWebFactory(
    FakeShowStore store,
    FakeScheduleStore? scheduleStore = null,
    FakeShowImagingScope? imagingScope = null,
    bool withAdminPassword = true,
    CapturingWarningLoggerProvider? logs = null)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story305-showsapi";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");

        if (withAdminPassword)
        {
            builder.UseSetting("Admin:Password", Password);
        }

        if (logs is not null)
            builder.ConfigureLogging(logging => logging.AddProvider(logs));

        builder.ConfigureTestServices(services =>
        {
            // No Liquidsoap/DB connections during this test.
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IShowStore>();
            services.AddSingleton<IShowStore>(store);

            services.RemoveAll<IScheduleStore>();
            services.AddSingleton<IScheduleStore>(scheduleStore ?? new FakeScheduleStore());

            services.RemoveAll<IShowImagingScope>();
            services.AddSingleton<IShowImagingScope>(imagingScope ?? new FakeShowImagingScope());
        });
    }

    /// <summary>Logs in via the real POST /api/auth/login round trip (mirrors Story240's own helper) and returns the cookie-bearing client.</summary>
    public static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }
}

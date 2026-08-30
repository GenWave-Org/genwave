// STORY-373 — I can install and tune Deep Cuts (SPEC F152.5–F152.7 · PLAN T362/T363)
//
// BDD specification — xUnit. AC1/AC2/AC6/AC7 WIRED T362; AC4/AC5 remain PENDING T363. Entry-point
// discipline: every fact drives the REAL production binary (WebApplicationFactory<Program>).
//
//   * AC1 (the editor saves the rule)/AC2 (the live pool size)/AC7 (validation) drive the real
//     ShowsController PUT/GET routes over WebApplicationFactory<Program> against FakeShowStore
//     (mirrors Story305_ShowsApi.cs's own "wire mapping, not re-derived validation" posture — the
//     real SQL GetEnvelopeCandidateCountAsync/GetRotationSinceAsync issue lives in MediaRepository/
//     MediaRotationRepository, not re-proven here) — a scripted IMediaCatalog/IMediaRotationSink
//     stand in for AC2's own "6 never-aired playable rows" (this file's own concern is that the
//     controller composes the show's rotation rule onto the station-default envelope and relays
//     whatever the catalog answers, not that the SQL itself counts correctly).
//
//   * AC6 (the framing pin) and the T362 review propagation fact both need the REAL
//     IShowStore/IScheduleStore/CachingScheduleResolver chain (Support/EphemeralStationDatabase over
//     a real ephemeral Postgres, the Story366/T351 factory idiom) — AC6 additionally drives the real
//     ISegmentCopyWriter against a scripted LlmCompletionsStub (the T335/T352 idiom).
namespace GenWave.Host.Tests.Specs;

using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host;
using GenWave.Host.Api;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;
using GenWave.Orchestration;

public static class FeatureInstallAndTuneDeepCuts
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — install the rule, read the pool, keep the framing plain
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheEditorSavesTheRule
    {
        // Given a show, When PUT /api/shows/{id} carries envelope.rotation {maxPlays: 1}.
        [Fact]
        public async Task StationShowEnvelopeHoldsIt()
        {
            var store = new FakeShowStore([SeedShow()]);
            await using var factory = new RotationApiWebFactory(store);
            var client = await RotationApiWebFactory.LoggedInClientAsync(factory);

            var response = await client.PutAsJsonAsync("/api/shows/1", new { rotation = new { maxPlays = 1 } });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<ShowDto>();
            Assert.NotNull(body);
            Assert.Equal(new RotationPredicate(1, null), body.Rotation);
        }

        [Fact]
        public async Task TheGetEchoesIt()
        {
            var store = new FakeShowStore([SeedShow()]);
            await using var factory = new RotationApiWebFactory(store);
            var client = await RotationApiWebFactory.LoggedInClientAsync(factory);

            await client.PutAsJsonAsync("/api/shows/1", new { rotation = new { maxPlays = 1 } });
            var fetched = await client.GetFromJsonAsync<ShowDto>("/api/shows/deep-cuts");

            Assert.NotNull(fetched);
            Assert.Equal(new RotationPredicate(1, null), fetched.Rotation);
        }

        static Show SeedShow() =>
            new(1, "Deep Cuts", "deep-cuts", null, null, null, null, DateTime.UtcNow, DateTime.UtcNow);
    }

    public sealed class ScenarioTheLivePoolSize
    {
        // Given a show with MaxPlays 0 and 6 never-aired playable rows, When GET /api/shows/{id}/rotation-pool.
        [Fact]
        public async Task TheEligibleCountIsSix()
        {
            var store = new FakeShowStore([SeedShowWithRotation()]);
            var catalog = new ScriptedRotationMediaCatalog(eligible: 6);
            await using var factory = new RotationApiWebFactory(
                store, catalog: catalog, rotationSink: new ScriptedRotationSink(since: null));
            var client = await RotationApiWebFactory.LoggedInClientAsync(factory);

            var pool = await client.GetFromJsonAsync<ShowRotationPoolDto>("/api/shows/1/rotation-pool");

            Assert.NotNull(pool);
            Assert.Equal(6, pool.Eligible);
            // And the count the controller asked for is scoped to THIS show's own rule, layered onto
            // the station-default envelope (SPEC F152.5) — not the whole-station pool.
            Assert.Equal(new RotationPredicate(0, null), catalog.LastEnvelope?.Rotation);
        }

        [Fact]
        public async Task TheSinceFieldIsAnEpoch()
        {
            var epoch = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
            var store = new FakeShowStore([SeedShowWithRotation()]);
            var catalog = new ScriptedRotationMediaCatalog(eligible: 6);
            var rotationSink = new ScriptedRotationSink(epoch);
            await using var factory = new RotationApiWebFactory(store, catalog: catalog, rotationSink: rotationSink);
            var client = await RotationApiWebFactory.LoggedInClientAsync(factory);

            var pool = await client.GetFromJsonAsync<ShowRotationPoolDto>("/api/shows/1/rotation-pool");

            Assert.NotNull(pool);
            Assert.Equal(epoch, pool.Since);
        }

        static Show SeedShowWithRotation() => new(
            1, "Deep Cuts", "deep-cuts", null, null, null, null, DateTime.UtcNow, DateTime.UtcNow,
            new RotationPredicate(0, null));
    }

    public sealed class ScenarioManifestOneOneImportsTheRule
    {
        // Given a catalog show manifest with envelope.rotation {maxPlays: 0}, When POST /api/shows/{slug}/import runs.
        [Fact(Skip = "pending T363 (STORY-373 AC4)")]
        public void TheInstalledShowsEnvelopeCarriesTheRule() => Assert.Fail("pending T363");
    }

    public sealed class ScenarioOlderManifestsStillImport
    {
        // Given a 1.0 manifest with no envelope, When it is imported.
        [Fact(Skip = "pending T363 (STORY-373 AC5)")]
        public void TheShowInstallsWithANullRotationRule() => Assert.Fail("pending T363");
    }

    public sealed class ScenarioTheFramingIsTheFlavorLineOnly
    {
        // Cadence positive so the show-flavor line is due on a FRESH gate (SPEC F116.3 — 0 disables
        // it entirely); each render below gets its own fresh ShowFlavorLineGate (a new container), so
        // no cadence WINDOW ever needs re-opening.
        const int CadenceMinutes = 30;

        // The IDENTICAL fixed instant for BOTH renders (never advanced) — two separate real ephemeral
        // Postgres instances necessarily boot moments apart in wall-clock time, but
        // LlmPromptBuilder's own station-clock line (SPEC F71.8) reads the swapped TimeProvider, not
        // the wall clock, so pinning both renders to the SAME instant is what makes a genuine
        // byte-identical comparison possible at all — any real time gap between two Docker Compose
        // boots would otherwise show up as an unrelated clock-line diff, not a rotation leak.
        static readonly DateTimeOffset FixedNow = new(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);

        // Given a Deep Cuts block on air, When a break's prompt is built.
        [Fact]
        public async Task ThePromptIsByteIdenticalToAPlainShowsPrompt()
        {
            var (deepCutsSystem, deepCutsUser) = await RenderLeadInPromptAsync(withRotation: true);
            var (plainSystem, plainUser) = await RenderLeadInPromptAsync(withRotation: false);

            // Then the two prompts are byte-identical — the rotation rule leaves no trace (SPEC
            // F152.7: the existing F116 ceremony + flavor line only, never a new prompt line).
            Assert.Equal(deepCutsSystem, plainSystem);
            Assert.Equal(deepCutsUser, plainUser);
            // And the show-flavor line (SPEC F116.3, LlmPromptBuilder.BuildUserContent's own
            // extra-line slot) DID fire — proves this is a real regression pin, not two prompts that
            // happen to agree because neither carried a show line at all.
            Assert.Contains("Deep Cuts", deepCutsUser);
            Assert.Contains("Dusting off the shelves", deepCutsUser);
        }

        /// <summary>Creates a fresh "Deep Cuts" show (rotation set or not, per <paramref name="withRotation"/>)
        /// on its own ephemeral Postgres/container, on air all week, and renders exactly one real
        /// LeadIn through the real <see cref="ISegmentCopyWriter"/> chain — returns the (system, user)
        /// prompt pair the scripted completions stub actually received.</summary>
        static async Task<(string System, string User)> RenderLeadInPromptAsync(bool withRotation)
        {
            await using var stub = await LlmCompletionsStub.StartAsync();
            await using var db = await DeepCutsStationDatabase.StartAsync();
            var clock = new FakeTimeProvider(FixedNow);
            await using var factory = new DeepCutsWebFactory(db, stub.BaseUri.ToString(), CadenceMinutes, clock);

            var showStore = factory.Services.GetRequiredService<IShowStore>();
            var scheduleStore = factory.Services.GetRequiredService<IScheduleStore>();
            var writer = factory.Services.GetRequiredService<ISegmentCopyWriter>();

            var created = await showStore.CreateAsync(
                new ShowDraft("Deep Cuts", null, "Dusting off the shelves"), CancellationToken.None);
            var show = Assert.IsType<ShowWriteResult.Created>(created).Show;
            await ScheduleFullWeekAsync(scheduleStore, show.Id);

            if (withRotation)
                await showStore.SetRotationAsync(show.Id, new RotationPredicate(MaxPlays: 0), CancellationToken.None);

            var request = new SegmentRequest(
                SegmentKind.LeadIn, "af_heart", "GenWave",
                new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
                clock.GetUtcNow(), "test-station");

            var result = await writer.WriteAsync(request, CancellationToken.None);
            Assert.True(result.FreshPerAiring || result.Text.Length > 0);
            Assert.Single(stub.Requests);

            return (stub.Requests[0].SystemPrompt, stub.Requests[0].UserPrompt);
        }
    }

    // ---------------------------------------------------------------------
    // The T362 review propagation fact — a lost/optional IShowStore registration must fail OPEN,
    // never silently drop a live rotation edit.
    // ---------------------------------------------------------------------

    public sealed class ScenarioARuleEditReachesTheResolverThroughTheContainer
    {
        [Fact]
        public async Task NoRestartNeeded()
        {
            await using var db = await DeepCutsStationDatabase.StartAsync();
            await using var factory = new DeepCutsWebFactory(db, llmEndpoint: null, patterCadenceMinutes: 0);

            var showStore = factory.Services.GetRequiredService<IShowStore>();
            var scheduleStore = factory.Services.GetRequiredService<IScheduleStore>();
            var resolver = factory.Services.GetRequiredService<CachingScheduleResolver>();

            var created = await showStore.CreateAsync(new ShowDraft("Deep Cuts"), CancellationToken.None);
            var show = Assert.IsType<ShowWriteResult.Created>(created).Show;
            await ScheduleFullWeekAsync(scheduleStore, show.Id);

            // Given the resolver has already cached this week's snapshot, with no rotation rule yet.
            var before = await resolver.ResolveAsync(CancellationToken.None);
            Assert.Null(before.Show?.Rotation);

            // When an operator PUTs a rotation rule through the real, authenticated production endpoint...
            var client = await DeepCutsWebFactory.LoggedInClientAsync(factory);
            var put = await client.PutAsJsonAsync($"/api/shows/{show.Id}", new { rotation = new { maxPlays = 0 } });
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);

            // Then the very next resolution — through the SAME CachingScheduleResolver singleton, no
            // restart, no other schedule write — sees the new rule (SPEC F152.3's T360 amendment: a
            // lost IShowStore.ShowChanged wire would leave this stale until an unrelated write).
            var after = await resolver.ResolveAsync(CancellationToken.None);
            Assert.Equal(new RotationPredicate(0, null), after.Show?.Rotation);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — bad rules refuse
    // ---------------------------------------------------------------------

    public sealed class ScenarioValidationRejectsUnboundedOrInvalidRules
    {
        // Given PUT with {maxPlays: -1}, {notAiredWithinDays: 0}, or {} (no bound), When saved.
        [Fact]
        public async Task TheNegativeMaxPlaysIsFourHundredNamingTheField()
        {
            var response = await PutRotationAsync(new { maxPlays = -1 });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.NotNull(problem);
            Assert.Contains("maxPlays", problem.Detail ?? "", StringComparison.Ordinal);
        }

        [Fact]
        public async Task TheZeroNotAiredWithinDaysIsFourHundredNamingTheField()
        {
            var response = await PutRotationAsync(new { notAiredWithinDays = 0 });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.NotNull(problem);
            Assert.Contains("notAiredWithinDays", problem.Detail ?? "", StringComparison.Ordinal);
        }

        [Fact]
        public async Task TheUnboundedEmptyRuleIsFourHundredNamingTheField()
        {
            var response = await PutRotationAsync(new { });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.NotNull(problem);
            Assert.Contains("rotation", problem.Detail ?? "", StringComparison.Ordinal);
        }

        static async Task<HttpResponseMessage> PutRotationAsync(object rotation)
        {
            var store = new FakeShowStore([
                new Show(1, "Deep Cuts", "deep-cuts", null, null, null, null, DateTime.UtcNow, DateTime.UtcNow),
            ]);
            await using var factory = new RotationApiWebFactory(store);
            var client = await RotationApiWebFactory.LoggedInClientAsync(factory);
            return await client.PutAsJsonAsync("/api/shows/1", new { rotation });
        }
    }

    // ── Shared helpers ───────────────────────────────────────────────────────────────────────────

    /// <summary>Assigns <paramref name="showId"/> to every day of the week, midnight to midnight —
    /// so "is this show on air right now" holds regardless of the wall clock/fake clock a fact runs
    /// against, with no persona (music-only, F115.2's own "a music-only block can legally carry a
    /// show" precedent) so no real <c>IPersonaStore</c> row is ever needed.</summary>
    static async Task ScheduleFullWeekAsync(IScheduleStore scheduleStore, long showId)
    {
        var week = Enum.GetValues<DayOfWeek>()
            .Select(day => new ScheduleSegment(
                Id: null, Day: day, StartMinute: 0, EndMinute: 24 * 60,
                PersonaId: null, Genres: null, EnergyMin: null, EnergyMax: null, ShowId: showId))
            .ToList();

        var result = await scheduleStore.ReplaceWeekAsync(week, expectedVersion: null, CancellationToken.None);
        _ = result as ScheduleReplaceResult.Replaced
            ?? throw new UnreachableException($"full-week schedule seed unexpectedly returned {result}");
    }
}

// ── Fakes for the wire-only facts (AC1/AC2/AC7) ─────────────────────────────────────────────────

/// <summary>Scripts <see cref="IMediaCatalog.GetEnvelopeCandidateCountAsync"/> for AC2's own
/// wire-layer fact — every other member is unreached by this file's facts (mirrors
/// GenWave.Host.Tests.FakeMediaCatalog's own "not exercised" idiom for members outside its own
/// file's concern); the real SQL this DIM's production override runs is
/// GenWave.MediaLibrary.Catalog.MediaRepository's own concern, not this file's.</summary>
file sealed class ScriptedRotationMediaCatalog(int? eligible) : IMediaCatalog
{
    public SegmentEnvelope? LastEnvelope { get; private set; }

    public Task<int?> GetEnvelopeCandidateCountAsync(LibraryScope scope, SegmentEnvelope envelope, CancellationToken ct)
    {
        LastEnvelope = envelope;
        return Task.FromResult(eligible);
    }

    public Task<MediaReference?> GetByIdAsync(LibraryScope scope, string mediaId, CancellationToken ct) =>
        Task.FromResult<MediaReference?>(null);

    public Task<MediaReference?> GetByIdUnscopedAsync(string mediaId, CancellationToken ct) =>
        Task.FromResult<MediaReference?>(null);

    public Task<MediaReference?> GetRandomReadyAsync(LibraryScope scope, IReadOnlyList<string> excludeIds, CancellationToken ct) =>
        Task.FromResult<MediaReference?>(null);

    public Task<RotationCandidate?> GetRotationCandidateAsync(
        LibraryScope scope, IReadOnlyList<string> orderedRecentIds, int artistSeparation, CancellationToken ct) =>
        Task.FromResult<RotationCandidate?>(null);

    public Task<PagedResult<MediaReference>> ListAsync(LibraryScope scope, MediaQuery query, CancellationToken ct) =>
        Task.FromResult(new PagedResult<MediaReference>([], 0, 0));

    public Task<CatalogStatusCounts> GetStatusCountsAsync(LibraryScope safeScope, CancellationToken ct) =>
        Task.FromResult(new CatalogStatusCounts(0, 0, 0, 0, 0));

    public Task<IReadOnlyList<FacetValue>> GetFacetsAsync(FacetField field, LibraryScope scope, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FacetValue>>([]);
}

/// <summary>Scripts <see cref="IMediaRotationSink.GetRotationSinceAsync"/> for AC2's own "since"
/// assertion — the ledger-write/never-aired-count members are unreached by this file's facts.</summary>
file sealed class ScriptedRotationSink(DateTimeOffset? since) : IMediaRotationSink
{
    public Task RecordAiringAsync(long mediaId, DateTimeOffset airedAt, CancellationToken ct) => Task.CompletedTask;

    public Task<DateTimeOffset?> GetRotationSinceAsync(CancellationToken ct) => Task.FromResult(since);

    public Task<long> GetNeverAiredCountAsync(CancellationToken ct) => Task.FromResult(0L);
}

/// <summary>
/// AC1/AC2/AC7's own web factory — mirrors Story305_ShowsApi.cs's <c>ShowsApiWebFactory</c> exactly
/// (this file cannot reference that type: it is <see langword="file"/>-scoped to its own file), with
/// two additional optional overrides (<paramref name="catalog"/>/<paramref name="rotationSink"/>) for
/// AC2's own scripted pool read.
/// </summary>
file sealed class RotationApiWebFactory(
    FakeShowStore store, IMediaCatalog? catalog = null, IMediaRotationSink? rotationSink = null)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story373-rotation";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IShowStore>();
            services.AddSingleton<IShowStore>(store);

            services.RemoveAll<IScheduleStore>();
            services.AddSingleton<IScheduleStore>(new FakeScheduleStore());

            services.RemoveAll<IScheduleSpecialStore>();
            services.AddSingleton<IScheduleSpecialStore>(new FakeScheduleSpecialStore());

            services.RemoveAll<IShowImagingScope>();
            services.AddSingleton<IShowImagingScope>(new FakeShowImagingScope());

            if (catalog is not null)
            {
                services.RemoveAll<IMediaCatalog>();
                services.AddSingleton(catalog);
            }

            if (rotationSink is not null)
            {
                services.RemoveAll<IMediaRotationSink>();
                services.AddSingleton(rotationSink);
            }
        });
    }

    public static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }
}

// ── Real-Postgres infrastructure for AC6 + the propagation fact ────────────────────────────────

/// <summary>This file's own thin subclass of the shared Support/EphemeralStationDatabase.cs harness
/// (the T351 review hoist — see that base type's own remarks) — supplies only the
/// <c>"genwave-deepcuts"</c> compose project-name prefix.</summary>
file sealed class DeepCutsStationDatabase : EphemeralStationDatabase
{
    DeepCutsStationDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<DeepCutsStationDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-deepcuts");
        var db = new DeepCutsStationDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>
/// Boots the real production composition root against a real ephemeral Postgres
/// (<see cref="DeepCutsStationDatabase"/>) — mirrors <c>SensorGateWebFactory</c>'s (Story366) own
/// "real Station connection string, only hosted services removed" posture, PLUS an optional real
/// <c>Llm:Endpoint</c> (a genuine <see cref="LlmCompletionsStub"/>, AC6's own concern — the
/// propagation fact needs none) and an OPTIONAL swapped <see cref="TimeProvider"/> (AC6's own
/// cadence-gate clock control).
///
/// <para>
/// <paramref name="clock"/> is deliberately <see langword="null"/>-by-default rather than always
/// swapped: ASP.NET Core's cookie authentication handler resolves <see cref="TimeProvider"/> from DI
/// for its own ticket issue/expiry timestamps (net8+), so swapping it under a session-cookie-authed
/// fact (the propagation fact's own <c>PUT /api/shows/{id}</c> through a logged-in
/// <see cref="HttpClient"/>) desyncs ticket validation from there on — every authenticated request
/// after login 401s (found the hard way: an earlier draft of this file swapped it unconditionally and
/// every <c>ScenarioARuleEditReachesTheResolverThroughTheContainer</c> request came back
/// Unauthorized). AC6's own fact never authenticates at all — it resolves <see cref="ISegmentCopyWriter"/>
/// straight off the container and calls <c>WriteAsync</c> directly (the Story350 idiom) — so it is the
/// only caller that ever supplies a non-null <paramref name="clock"/>.
/// </para>
/// </summary>
file sealed class DeepCutsWebFactory(
    DeepCutsStationDatabase db, string? llmEndpoint, int patterCadenceMinutes, TimeProvider? clock = null)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story373-deepcuts";
    internal const string Model = "test-model";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("ConnectionStrings:Station", db.StationConnectionString);
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Station:Shows:PatterCadenceMinutes", patterCadenceMinutes.ToString());

        // The exact four Station:* keys compose.yaml itself overrides in production — mirrors
        // SensorGateWebFactory/PaWireProofWebFactory's own precedent.
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");

        if (llmEndpoint is not null)
        {
            builder.UseSetting("Llm:Endpoint", llmEndpoint);
            builder.UseSetting("Llm:Model", Model);
        }

        builder.ConfigureTestServices(services =>
        {
            // No Liquidsoap/real-background-loop reach during this test — mirrors every other
            // WebApplicationFactory-based spec in this suite.
            services.RemoveAll<IHostedService>();

            // The ephemeral test Postgres is seeded from ONLY db/01 + db/06 (this project's own
            // db-compose.yaml — see DatabaseFixture/EphemeralStationDatabase's own remarks), which
            // never mounts db/36's station.schedule_special table — CachingScheduleResolver.ResolveAsync
            // reads IScheduleSpecialStore on every call alongside IScheduleStore (PLAN T260), so a REAL
            // IScheduleSpecialStore here would 42P01 the instant either fact resolves. Swapped for an
            // empty fake — mirrors Story311_SpectatorShowFields.cs's own identical swap ("an empty fake
            // is enough since none of these facts author a special") — while IShowStore/IScheduleStore
            // stay the REAL Postgres-backed implementations these facts actually exercise.
            services.RemoveAll<IScheduleSpecialStore>();
            services.AddSingleton<IScheduleSpecialStore>(new FakeScheduleSpecialStore());

            if (clock is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(clock);
            }
        });
    }

    public static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }
}

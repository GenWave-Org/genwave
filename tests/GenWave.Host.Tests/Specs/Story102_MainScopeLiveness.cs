// STORY-102 — Main scope applies live everywhere (WIRE) (Epic R / SPEC F30, gitea-#180)
//
// BDD specification — xUnit. R7 un-pins this file. StationContext sheds its LibraryScope (keeps
// Id/Name/Voice/Cadence); Orchestrator, MediaController, and ReenrichController read the live
// scope through IStationScopeProvider — the thin Core-visible seam Orchestration depends on
// instead of IOptionsMonitor<StationOptions>/StationOptions directly (SPEC F30.1). The Host
// implementation (OptionsMonitorStationScopeProvider) wraps IOptionsMonitor<StationOptions> and
// re-reads CurrentValue on every call; these in-process specs stand in a FakeStationScopeProvider
// instead so a "live reload" is simulated by mutating .Scope between two calls on the SAME
// consumer instance — no re-construction, mirroring Story084's FakeOptionsMonitor<T> pattern one
// level up the seam. The transitional hard-coded LibraryScope([1L]) sites (RandomSelectionProvider,
// /media/* minimal API) are migrated. The true no-restart round trip on live Postgres is R13's gate
// job (the exact P9 repro).

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Orchestration;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes (file-scoped: names are local to this spec) ───────────────────────────────

/// <summary>Records the scope each ListAdminAsync call receives; returns an empty page.</summary>
file sealed class RecordingBrowseQuery : IAdminMediaQuery
{
    public LibraryScope? LastScope { get; private set; }

    public Task<PagedResult<AdminMediaDto>> ListAdminAsync(LibraryScope scope, MediaQuery query, CancellationToken ct)
    {
        LastScope = scope;
        return Task.FromResult(new PagedResult<AdminMediaDto>([], 0, 0));
    }
}

file sealed class UnusedLookup : IAdminMediaLookup
{
    public Task<(AdminMediaDto Row, long LibraryId)?> GetByIdWithLibraryAsync(long id, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-102's browse scenario.");
}

file sealed class UnusedWrite : IAdminMediaWrite
{
    public Task<MediaWriteResult> UpdateAsync(string id, MediaPatch patch, string expectedVersion, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-102's browse scenario.");

    public Task<MediaUpdateOutcome> UpdateReturningVersionAsync(string id, MediaPatch patch, string expectedVersion, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-102's browse scenario.");

    public Task<int> SetEligibilityAsync(MediaQuery filter, bool eligible, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-102's browse scenario.");

    public Task<int?> BulkReassignAsync(MediaQuery filter, long toLibraryId, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-102's browse scenario.");
}

/// <summary>Records the scope each ScheduleAsync call receives; always schedules.</summary>
file sealed class RecordingReenrichment : IAdminMediaReenrichment
{
    public LibraryScope? LastScope { get; private set; }

    public Task<ReenrichResult> ScheduleAsync(string id, ReenrichFields fields, LibraryScope scope, CancellationToken ct)
    {
        LastScope = scope;
        return Task.FromResult(ReenrichResult.Scheduled);
    }

    public Task<int> ScheduleBulkAsync(MediaQuery filter, ReenrichFields fields, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-102's reenrich scenario.");
}

/// <summary>
/// Records every scope passed to <see cref="GetRandomReadyAsync"/> and mirrors MediaRepository's
/// default-deny short-circuit (F12.1): an empty scope never resolves to a row, even one that
/// exists, so <see cref="ScenarioDefaultDenyStands"/> exercises the real Orchestrator selection
/// path rather than asserting on <see cref="LibraryScope.IsEmpty"/> in isolation.
/// </summary>
file sealed class RecordingScopeCatalog(MediaReference? ready) : IMediaCatalog
{
    public List<LibraryScope> Scopes { get; } = [];

    public Task<MediaReference?> GetByIdAsync(LibraryScope scope, string mediaId, CancellationToken ct)
        => Task.FromResult<MediaReference?>(null);

    public Task<MediaReference?> GetRandomReadyAsync(LibraryScope scope, IReadOnlyList<string> excludeIds, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-102's scenarios — Orchestrator selection now calls GetRotationCandidateAsync (SPEC F41.1).");

    public Task<RotationCandidate?> GetRotationCandidateAsync(
        LibraryScope scope, IReadOnlyList<string> orderedRecentIds, int artistSeparation, CancellationToken ct)
    {
        Scopes.Add(scope);
        return Task.FromResult(scope.IsEmpty || ready is null
            ? null
            : new RotationCandidate(ready, RepeatedRecent: false, RepeatedArtist: false));
    }

    public Task<PagedResult<MediaReference>> ListAsync(LibraryScope scope, MediaQuery query, CancellationToken ct)
        => Task.FromResult(new PagedResult<MediaReference>([], 0, 0));

    public Task<CatalogStatusCounts> GetStatusCountsAsync(LibraryScope safeScope, CancellationToken ct)
        => Task.FromResult(new CatalogStatusCounts(0, 0, 0, 0, 0));

    // Not exercised by STORY-102's scenarios — facets are a curation-console concern (SPEC F52.1).
    public Task<IReadOnlyList<FacetValue>> GetFacetsAsync(FacetField field, LibraryScope scope, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<FacetValue>>([]);
}

/// <summary>No-op TTS source — every ScenarioConsumersReadTheMonitor/ScenarioDefaultDenyStands
/// cadence below disables lead-in/back-announce/station-id, so RenderAsync is never invoked.</summary>
file sealed class NoOpTtsSegmentSource : ITtsSegmentSource
{
    public Task<MediaItem?> RenderAsync(SegmentRequest request, CancellationToken ct)
        => Task.FromResult<MediaItem?>(null);
}

/// <summary>Persona-less accessor double (STORY-121 plumbing) — SilentCadence below means
/// Orchestrator never even calls ResolveAsync in this file's scenarios.</summary>
file sealed class NoOpActivePersonaAccessor : IActivePersonaAccessor
{
    public Task<Persona?> ResolveAsync(CancellationToken ct) => Task.FromResult<Persona?>(null);
}

public static class FeatureMainScopeLiveness
{
    static MediaReference MakeRef(string id) => new(
        id, $"/media/{id}.mp3", $"Track {id}", new GenWave.Core.Domain.Loudness(-23.0, -1.0, true),
        null, null, null, null, null, null, null, null);

    static CadenceConfig SilentCadence => new()
    {
        LeadInBeforeEachTrack = false,
        BackAnnounceAfterEachTrack = false,
        StationIdEveryNUnits = 0,
    };

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioStationIdentityCarriesNoScope
    {
        [Fact]
        public void StationIdentityCarriesNoLibraryScopeMember() =>
            Assert.Null(typeof(StationIdentity).GetProperty("Scope"));
    }

    public sealed class ScenarioConsumersReadTheMonitor
    {
        // Arrange: a mutable FakeStationScopeProvider (this file's Story084-style fake, one seam
        // level down — see the file banner), scope [1] → mutate .Scope to [1, 7] between two calls
        // on the SAME consumer instance, no re-construction.

        [Fact]
        public async Task BrowseHonorsAWidenedScopeWithoutRebuild()
        {
            var scopeProvider = new FakeStationScopeProvider(new LibraryScope([1L]));
            var query = new RecordingBrowseQuery();
            var controller = new MediaController(
                query, new UnusedLookup(), new UnusedWrite(), scopeProvider,
                NullLogger<MediaController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };

            await controller.List(state: null, artist: null, genre: null, libraryId: null, q: null, eligible: null);
            Assert.Equal(new long[] { 1L }, query.LastScope?.LibraryIds);

            scopeProvider.Scope = new LibraryScope([1L, 7L]);
            await controller.List(state: null, artist: null, genre: null, libraryId: null, q: null, eligible: null);

            Assert.Equal(new long[] { 1L, 7L }, query.LastScope?.LibraryIds);
        }

        [Fact]
        public async Task ReenrichScopeCheckHonorsAWidenedScopeWithoutRebuild()
        {
            // The P9 repro's in-process half: reenrich against library 7 stops seeing a stale
            // scope after a live PUT once the endpoint reads through the same live seam.
            var scopeProvider = new FakeStationScopeProvider(new LibraryScope([1L]));
            var reenrichment = new RecordingReenrichment();
            var controller = new ReenrichController(
                reenrichment, scopeProvider, NullLogger<ReenrichController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };

            await controller.Reenrich(99L, fields: null, CancellationToken.None);
            Assert.Equal(new long[] { 1L }, reenrichment.LastScope?.LibraryIds);

            scopeProvider.Scope = new LibraryScope([1L, 7L]);
            await controller.Reenrich(99L, fields: null, CancellationToken.None);

            Assert.Equal(new long[] { 1L, 7L }, reenrichment.LastScope?.LibraryIds);
        }

        [Fact]
        public async Task SelectionHonorsAWidenedScopeWithoutRebuild()
        {
            // Orchestrator's next catalog call passes the updated scope — read fresh every call,
            // never stored in a field (SPEC F30.1).
            var scopeProvider = new FakeStationScopeProvider(new LibraryScope([1L]));
            var catalog = new RecordingScopeCatalog(MakeRef("m1"));
            var identityProvider = new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default"));
            var cadenceProvider = new FakeCadenceProvider(SilentCadence);
            var rotationProvider = new FakeRotationSettingsProvider(new RotationSettings());
            var orchestrator = new Orchestrator(
                identityProvider, scopeProvider, cadenceProvider, rotationProvider, catalog, new NoOpTtsSegmentSource(),
                new NoOpActivePersonaAccessor(), NullLogger<Orchestrator>.Instance,
                new FakeRenderBudgetProvider(TimeSpan.FromSeconds(5)));

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            Assert.Equal(new long[] { 1L }, catalog.Scopes[0].LibraryIds);

            scopeProvider.Scope = new LibraryScope([1L, 7L]);
            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.Equal(new long[] { 1L, 7L }, catalog.Scopes[1].LibraryIds);
        }
    }

    public sealed class ScenarioNoCompiledInScope
    {
        static string RepoRoot =>
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        static IEnumerable<string> SourceFiles =>
            Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.cs", SearchOption.AllDirectories)
                .Where(f =>
                    !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                    !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        [Fact]
        public void NoHardCodedLibraryScopeRemains()
        {
            // Grep-style source assertion (the shipped grep-assert idiom, e.g. Story074's
            // RepoRoot pattern): zero `LibraryScope([1L])` / `LibraryScope(new[] { 1L })`
            // construction sites under src/ (F30.2). tests/ is deliberately excluded — this file
            // itself legitimately constructs `new LibraryScope([1L])` fixtures.
            var pattern = new System.Text.RegularExpressions.Regex(
                @"LibraryScope\(\s*(\[\s*1L\s*\]|new\[\]\s*\{\s*1L\s*\})\s*\)");

            var offenders = SourceFiles
                .Where(f => pattern.IsMatch(File.ReadAllText(f)))
                .ToList();

            Assert.True(
                offenders.Count == 0,
                $"Hard-coded LibraryScope([1L]) construction found outside tests/: {string.Join(", ", offenders)}");
        }
    }

    public sealed class ScenarioLiveRoundTrip
    {
        [Fact(Skip = "Pending R13 — live stack: PUT widens scope, reenrich + browse succeed with no api restart (the P9 repro); see docs/PLAN.md")]
        public void ThePNineReproPasses()
        {
            Assert.Fail("pending R13");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioDefaultDenyStands
    {
        [Fact]
        public async Task EmptyEffectiveScopeStillReturnsNothing()
        {
            // LibraryScope.None semantics unchanged (F12.1) even when the scope is sourced from
            // the live monitor: an empty Station:Scope:LibraryIds still short-circuits to nothing,
            // exercised through the real Orchestrator selection path (RecordingScopeCatalog mirrors
            // MediaRepository's scope.IsEmpty short-circuit).
            var scopeProvider = new FakeStationScopeProvider(LibraryScope.None);
            var catalog = new RecordingScopeCatalog(MakeRef("m1"));
            var identityProvider = new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default"));
            var cadenceProvider = new FakeCadenceProvider(SilentCadence);
            var rotationProvider = new FakeRotationSettingsProvider(new RotationSettings());
            var orchestrator = new Orchestrator(
                identityProvider, scopeProvider, cadenceProvider, rotationProvider, catalog, new NoOpTtsSegmentSource(),
                new NoOpActivePersonaAccessor(), NullLogger<Orchestrator>.Instance,
                new FakeRenderBudgetProvider(TimeSpan.FromSeconds(5)));

            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.Null(item);
            Assert.True(Assert.Single(catalog.Scopes).IsEmpty);
        }
    }
}

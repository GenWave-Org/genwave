// STORY-065 — Bulk endpoints honor a named library as effective scope
//
// BDD specification — xUnit. POST /api/media/bulk/reassign and /api/media/bulk/reenrich:
// a filter that explicitly names a libraryId uses that library as the effective scope, in or
// out of station scope (F23.3) — converging on the behavior bulk-eligibility already shipped
// and routing all of them through M4's shared helper. Unnamed filters stay bounded by
// Station:Scope:LibraryIds (the negative case). Recovery of a parked row — the operation that
// required SQL on 2026-07-02 — becomes { filter: { libraryId }, toLibraryId }.
//
// In-process: controllers constructed directly with recording fakes; assertions pin the
// EFFECTIVE SCOPE handed to the repo seam. Red until M5 lands. The live recovery round-trip
// is the M5 wire acceptance — operator-gated Integration.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes ─────────────────────────────────────────────────────────

/// <summary>Records the scope BulkReassignAsync receives; reports one row updated.</summary>
sealed class RecordingBulkWrite : IAdminMediaWrite
{
    public LibraryScope? LastBulkReassignScope { get; private set; }

    public Task<int?> BulkReassignAsync(MediaQuery filter, long toLibraryId, LibraryScope scope, CancellationToken ct)
    {
        LastBulkReassignScope = scope;
        return Task.FromResult<int?>(1);
    }

    public Task<MediaWriteResult> UpdateAsync(string id, MediaPatch patch, string expectedVersion, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-065.");

    public Task<MediaUpdateOutcome> UpdateReturningVersionAsync(string id, MediaPatch patch, string expectedVersion, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-065.");

    public Task<int> SetEligibilityAsync(MediaQuery filter, bool eligible, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-065.");
}

/// <summary>Records the scope ScheduleBulkAsync receives; reports one row scheduled.</summary>
sealed class RecordingBulkReenrichment : IAdminMediaReenrichment
{
    public LibraryScope? LastBulkScope { get; private set; }

    public Task<int> ScheduleBulkAsync(MediaQuery filter, ReenrichFields fields, LibraryScope scope, CancellationToken ct)
    {
        LastBulkScope = scope;
        return Task.FromResult(1);
    }

    public Task<ReenrichResult> ScheduleAsync(string id, ReenrichFields fields, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-065.");
}

sealed class UnusedQuery : IAdminMediaQuery
{
    public Task<PagedResult<AdminMediaDto>> ListAdminAsync(LibraryScope scope, MediaQuery query, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-065.");
}

sealed class UnusedLookup : IAdminMediaLookup
{
    public Task<(AdminMediaDto Row, long LibraryId)?> GetByIdWithLibraryAsync(long id, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-065.");
}

static class BulkHarness
{
    public static IStationScopeProvider ScopeProvider() =>
        new FakeStationScopeProvider(new LibraryScope([1L]));

    public static (MediaController Controller, RecordingBulkWrite Write) Media()
    {
        var write = new RecordingBulkWrite();
        var controller = new MediaController(
            new UnusedQuery(), new UnusedLookup(), write, ScopeProvider(),
            NullLogger<MediaController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return (controller, write);
    }

    public static (ReenrichController Controller, RecordingBulkReenrichment Reenrich) Reenrich()
    {
        var reenrich = new RecordingBulkReenrichment();
        var controller = new ReenrichController(
            reenrich, ScopeProvider(), NullLogger<ReenrichController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return (controller, reenrich);
    }
}

public static class FeatureBulkNamedLibraryScope
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — a named library is the effective scope, in or out of rotation
    // ---------------------------------------------------------------------

    public sealed class ScenarioBulkReassignNamesAParkedLibrary
    {
        readonly RecordingBulkWrite write;

        public ScenarioBulkReassignNamesAParkedLibrary()
        {
            var (controller, w) = BulkHarness.Media();
            write = w;
            controller.BulkReassign(
                new BulkReassignRequest(
                    ToLibraryId: 1,
                    Filter: new BulkReassignFilter(State: null, Artist: null, Genre: null, LibraryId: 2, Q: null)),
                CancellationToken.None).GetAwaiter().GetResult();
        }

        [Fact]
        public void TheNamedLibraryIsTheEffectiveScope()
        {
            Assert.Equal([2L], write.LastBulkReassignScope?.LibraryIds);
        }
    }

    public sealed class ScenarioBulkReenrichNamesAParkedLibrary
    {
        readonly RecordingBulkReenrichment reenrich;

        public ScenarioBulkReenrichNamesAParkedLibrary()
        {
            var (controller, r) = BulkHarness.Reenrich();
            reenrich = r;
            controller.BulkReenrich(
                new BulkReenrichRequest(
                    Filter: new BulkReenrichFilter(State: null, Artist: null, Genre: null, LibraryId: 2, Q: null),
                    Fields: ["cue"]),
                CancellationToken.None).GetAwaiter().GetResult();
        }

        [Fact]
        public void TheNamedLibraryIsTheEffectiveScope()
        {
            Assert.Equal([2L], reenrich.LastBulkScope?.LibraryIds);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — unnamed filters stay bounded by station scope
    // ---------------------------------------------------------------------

    public sealed class ScenarioUnnamedBulkReassignStaysScopeBounded
    {
        readonly RecordingBulkWrite write;

        public ScenarioUnnamedBulkReassignStaysScopeBounded()
        {
            var (controller, w) = BulkHarness.Media();
            write = w;
            controller.BulkReassign(
                new BulkReassignRequest(
                    ToLibraryId: 1,
                    Filter: new BulkReassignFilter(State: null, Artist: "X", Genre: null, LibraryId: null, Q: null)),
                CancellationToken.None).GetAwaiter().GetResult();
        }

        [Fact]
        public void TheStationScopeIsTheEffectiveScope()
        {
            Assert.Equal([1L], write.LastBulkReassignScope?.LibraryIds);
        }
    }

    public sealed class ScenarioUnnamedBulkReenrichStaysScopeBounded
    {
        readonly RecordingBulkReenrichment reenrich;

        public ScenarioUnnamedBulkReenrichStaysScopeBounded()
        {
            var (controller, r) = BulkHarness.Reenrich();
            reenrich = r;
            controller.BulkReenrich(
                new BulkReenrichRequest(
                    Filter: new BulkReenrichFilter(State: null, Artist: "X", Genre: null, LibraryId: null, Q: null),
                    Fields: ["cue"]),
                CancellationToken.None).GetAwaiter().GetResult();
        }

        [Fact]
        public void TheStationScopeIsTheEffectiveScope()
        {
            Assert.Equal([1L], reenrich.LastBulkScope?.LibraryIds);
        }
    }

    // ---------------------------------------------------------------------
    // WIRE — the 2026-07-02 recovery case, live (operator-gated)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioParkedRowRecoveryOnTheLiveStack
    {
        const string Skip = "Live stack + operator: M5 wire acceptance — the exact case that required SQL on 2026-07-02.";

        [Fact(Skip = Skip)]
        public void ARowParkedOutOfScopeRecoversViaBulkReassignWithANamedLibraryFilter() { }
    }
}

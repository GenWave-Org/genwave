// STORY-064 — Browse a parked library by naming it
//
// BDD specification — xUnit. GET /api/media?library-id=X: an explicitly-named library becomes
// the effective filter scope even when X is outside Station:Scope:LibraryIds (F23.2), with
// X-Out-Of-Scope: true flagging the out-of-rotation browse. Unnamed browse stays scope-bounded;
// unknown library id is a filter miss (empty page), never an error. Scope is a curation
// boundary, not a trust boundary (F23.6) — single-operator deployment.
//
// In-process: MediaController constructed directly with a recording IAdminMediaQuery fake —
// the assertions pin the EFFECTIVE SCOPE the controller hands the repo, which is where the
// one-way door lived. Red until M4's shared effective-scope helper lands.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes ─────────────────────────────────────────────────────────

/// <summary>Records the scope each ListAdminAsync call receives; returns an empty page.</summary>
sealed class RecordingAdminQuery : IAdminMediaQuery
{
    public LibraryScope? LastScope { get; private set; }

    public Task<PagedResult<AdminMediaDto>> ListAdminAsync(LibraryScope scope, MediaQuery query, CancellationToken ct)
    {
        LastScope = scope;
        return Task.FromResult(new PagedResult<AdminMediaDto>([], 0, 0));
    }
}

sealed class UnusedAdminLookup : IAdminMediaLookup
{
    public Task<(AdminMediaDto Row, long LibraryId)?> GetByIdWithLibraryAsync(long id, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-064.");
}

sealed class UnusedAdminWrite : IAdminMediaWrite
{
    public Task<MediaWriteResult> UpdateAsync(string id, MediaPatch patch, string expectedVersion, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-064.");

    public Task<MediaUpdateOutcome> UpdateReturningVersionAsync(string id, MediaPatch patch, string expectedVersion, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-064.");

    public Task<int> SetEligibilityAsync(MediaQuery filter, bool eligible, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-064.");

    public Task<int?> BulkReassignAsync(MediaQuery filter, long toLibraryId, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-064.");
}

static class BrowseHarness
{
    /// <summary>Station scope [1]; library 2 exists but is out of rotation scope.</summary>
    public static (MediaController Controller, RecordingAdminQuery Query, DefaultHttpContext Http) Build()
    {
        var query = new RecordingAdminQuery();
        var http = new DefaultHttpContext();
        var controller = new MediaController(
            query,
            new UnusedAdminLookup(),
            new UnusedAdminWrite(),
            new FakeStationScopeProvider(new LibraryScope([1L])),
            NullLogger<MediaController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
        return (controller, query, http);
    }
}

public static class FeatureBrowseNamedLibraryScope
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — named out-of-scope library becomes the effective scope
    // ---------------------------------------------------------------------

    public sealed class ScenarioNamedOutOfScopeLibrary
    {
        readonly RecordingAdminQuery query;
        readonly DefaultHttpContext http;
        readonly IActionResult result;

        public ScenarioNamedOutOfScopeLibrary()
        {
            var (controller, q, h) = BrowseHarness.Build();
            query = q;
            http = h;
            result = controller
                .List(state: null, artist: null, genre: null, libraryId: 2, q: null, eligible: null)
                .GetAwaiter().GetResult();
        }

        [Fact]
        public void TheRequestSucceeds()
        {
            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public void TheNamedLibraryIsTheEffectiveScope()
        {
            Assert.Equal([2L], query.LastScope?.LibraryIds);
        }

        [Fact]
        public void TheResponseCarriesTheOutOfScopeHeader()
        {
            Assert.Equal("true", http.Response.Headers["X-Out-Of-Scope"].ToString());
        }
    }

    public sealed class ScenarioNamedInScopeLibrary
    {
        readonly RecordingAdminQuery query;
        readonly DefaultHttpContext http;

        public ScenarioNamedInScopeLibrary()
        {
            var (controller, q, h) = BrowseHarness.Build();
            query = q;
            http = h;
            controller
                .List(state: null, artist: null, genre: null, libraryId: 1, q: null, eligible: null)
                .GetAwaiter().GetResult();
        }

        [Fact]
        public void TheNamedLibraryIsTheEffectiveScope()
        {
            Assert.Equal([1L], query.LastScope?.LibraryIds);
        }

        [Fact]
        public void ThereIsNoOutOfScopeHeader()
        {
            Assert.False(http.Response.Headers.ContainsKey("X-Out-Of-Scope"));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — unnamed browse stays bounded; unknown id is a miss, not an error
    // ---------------------------------------------------------------------

    public sealed class ScenarioUnnamedBrowseStaysScopeBounded
    {
        readonly RecordingAdminQuery query;

        public ScenarioUnnamedBrowseStaysScopeBounded()
        {
            var (controller, q, _) = BrowseHarness.Build();
            query = q;
            controller
                .List(state: null, artist: null, genre: null, libraryId: null, q: null, eligible: null)
                .GetAwaiter().GetResult();
        }

        [Fact]
        public void TheStationScopeIsTheEffectiveScope()
        {
            Assert.Equal([1L], query.LastScope?.LibraryIds);
        }
    }

    public sealed class ScenarioUnknownNamedLibrary
    {
        readonly IActionResult result;

        public ScenarioUnknownNamedLibrary()
        {
            var (controller, _, _) = BrowseHarness.Build();
            result = controller
                .List(state: null, artist: null, genre: null, libraryId: 999, q: null, eligible: null)
                .GetAwaiter().GetResult();
        }

        [Fact]
        public void TheResponseIsAnEmptyPageNotAnError()
        {
            Assert.IsType<OkObjectResult>(result);
        }
    }
}

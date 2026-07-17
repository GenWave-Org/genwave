// STORY-049 — Bulk reassignment endpoint (WIRE)
//
// BDD specification — xUnit. POST /api/media/bulk/reassign — mirrors the F3 bulk-eligibility shape.
//
// In-process tests (FeatureBulkReassignEndpointInProcess): construct MediaController directly with
// fakes — no live stack required. Mirror the Story048 FeatureSingleRowReassignViaPatchInProcess pattern.
//
// Operator-gated integration scenarios (FeatureBulkReassignEndpoint): remain Skip-pinned.

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Scriptable <see cref="IAdminMediaWrite"/> for bulk-reassign tests. Configures:
/// <list type="bullet">
///   <item><see cref="BulkReassignResult"/> — the value returned by <see cref="BulkReassignAsync"/>
///   (null = unknown library, int = rows updated).</item>
/// </list>
/// Methods not relevant to these tests are no-ops.
/// </summary>
file sealed class FakeBulkReassignWrite : IAdminMediaWrite
{
    public int? BulkReassignResult { get; set; } = 0;

    public Task<MediaWriteResult> UpdateAsync(
        string id,
        MediaPatch patch,
        string expectedVersion,
        LibraryScope scope,
        CancellationToken ct)
        => Task.FromResult(MediaWriteResult.Updated);

    public Task<MediaUpdateOutcome> UpdateReturningVersionAsync(
        string id,
        MediaPatch patch,
        string expectedVersion,
        LibraryScope scope,
        CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-049.");

    public Task<int> SetEligibilityAsync(
        MediaQuery filter,
        bool eligible,
        LibraryScope scope,
        CancellationToken ct)
        => Task.FromResult(0);

    public Task<int?> BulkReassignAsync(
        MediaQuery filter,
        long toLibraryId,
        LibraryScope scope,
        CancellationToken ct)
        => Task.FromResult(BulkReassignResult);
}

/// <summary>No-op query — BulkReassign does not call the query surface.</summary>
file sealed class NoOpAdminMediaQueryForReassign : IAdminMediaQuery
{
    public Task<PagedResult<AdminMediaDto>> ListAdminAsync(
        LibraryScope scope, MediaQuery query, CancellationToken ct)
        => Task.FromResult(new PagedResult<AdminMediaDto>([], 0, 0));
}

/// <summary>No-op lookup — BulkReassign does not call the lookup surface.</summary>
file sealed class NoOpAdminMediaLookupForReassign : IAdminMediaLookup
{
    public Task<(AdminMediaDto Row, long LibraryId)?> GetByIdWithLibraryAsync(
        long id, CancellationToken ct)
        => Task.FromResult<(AdminMediaDto Row, long LibraryId)?>(null);
}

/// <summary>
/// Builds a <see cref="MediaController"/> wired to the given fake write and returns both the
/// controller and the underlying <see cref="DefaultHttpContext"/> so tests can inspect response
/// headers after the action executes.
/// </summary>
file static class ReassignControllerFactory
{
    public static (MediaController Controller, DefaultHttpContext HttpContext) Build(
        IAdminMediaWrite adminWrite,
        IStationScopeProvider scopeProvider)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.ContentType = "application/json";

        var controller = new MediaController(
            new NoOpAdminMediaQueryForReassign(),
            new NoOpAdminMediaLookupForReassign(),
            adminWrite,
            scopeProvider,
            NullLogger<MediaController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = ctx },
        };

        return (controller, ctx);
    }

    /// <summary>Creates an <see cref="IStationScopeProvider"/> fixed to the given library scope.</summary>
    public static IStationScopeProvider ScopeWith(params long[] libraryIds) =>
        new FakeStationScopeProvider(new LibraryScope(libraryIds));
}

// ── In-process tests ──────────────────────────────────────────────────────────────────────────────

public static class FeatureBulkReassignEndpointInProcess
{
    // ── AC1 — POST {filter, toLibraryId} → 200 { updated: N } ───────────────────────────────────

    public sealed class ScenarioBulkReassignReturnsUpdatedCount
    {
        [Fact]
        public async Task ReturnsOkWithUpdatedCount()
        {
            var write = new FakeBulkReassignWrite { BulkReassignResult = 7 };
            var (controller, _) = ReassignControllerFactory.Build(write, ReassignControllerFactory.ScopeWith(1));
            var request = new BulkReassignRequest(ToLibraryId: 1L, Filter: null);

            var result = await controller.BulkReassign(request, CancellationToken.None);

            var ok   = Assert.IsType<OkObjectResult>(result);
            var json = JsonSerializer.Serialize(ok.Value);
            var doc  = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.TryGetProperty("updated", out var prop));
            Assert.Equal(7, prop.GetInt32());
        }

        [Fact]
        public async Task ZeroUpdatedCountIsAlsoOk()
        {
            // filter matches nothing but toLibraryId is valid → 200 { updated: 0 }
            var write = new FakeBulkReassignWrite { BulkReassignResult = 0 };
            var (controller, _) = ReassignControllerFactory.Build(write, ReassignControllerFactory.ScopeWith(1));
            var request = new BulkReassignRequest(ToLibraryId: 1L, Filter: null);

            var result = await controller.BulkReassign(request, CancellationToken.None);

            var ok   = Assert.IsType<OkObjectResult>(result);
            var json = JsonSerializer.Serialize(ok.Value);
            var doc  = JsonDocument.Parse(json);
            Assert.Equal(0, doc.RootElement.GetProperty("updated").GetInt32());
        }
    }

    // ── AC2/AC3 — Cross-scope destination → X-Out-Of-Scope header + outOfScope body ─────────────

    public sealed class ScenarioCrossScopeDestinationSignalsOnResponse
    {
        [Fact]
        public async Task CrossScopeDestinationSetsXOutOfScopeHeader()
        {
            // Station scope = [1]; toLibraryId = 2 (out of scope).
            var write = new FakeBulkReassignWrite { BulkReassignResult = 3 };
            var (controller, ctx) = ReassignControllerFactory.Build(write, ReassignControllerFactory.ScopeWith(1));
            var request = new BulkReassignRequest(ToLibraryId: 2L, Filter: null);

            await controller.BulkReassign(request, CancellationToken.None);

            Assert.Equal("true", ctx.Response.Headers["X-Out-Of-Scope"].ToString());
        }

        [Fact]
        public async Task CrossScopeDestinationBodyContainsOutOfScopeTrueField()
        {
            var write = new FakeBulkReassignWrite { BulkReassignResult = 3 };
            var (controller, _) = ReassignControllerFactory.Build(write, ReassignControllerFactory.ScopeWith(1));
            var request = new BulkReassignRequest(ToLibraryId: 2L, Filter: null);

            var result = await controller.BulkReassign(request, CancellationToken.None);

            var ok   = Assert.IsType<OkObjectResult>(result);
            var json = JsonSerializer.Serialize(ok.Value);
            var doc  = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.TryGetProperty("outOfScope", out var prop));
            Assert.True(prop.GetBoolean());
        }

        [Fact]
        public async Task CrossScopeDestinationBodyAlsoCarriesUpdatedCount()
        {
            var write = new FakeBulkReassignWrite { BulkReassignResult = 5 };
            var (controller, _) = ReassignControllerFactory.Build(write, ReassignControllerFactory.ScopeWith(1));
            var request = new BulkReassignRequest(ToLibraryId: 2L, Filter: null);

            var result = await controller.BulkReassign(request, CancellationToken.None);

            var ok   = Assert.IsType<OkObjectResult>(result);
            var json = JsonSerializer.Serialize(ok.Value);
            var doc  = JsonDocument.Parse(json);
            Assert.Equal(5, doc.RootElement.GetProperty("updated").GetInt32());
        }
    }

    // ── AC2 — In-scope destination → no X-Out-Of-Scope header, no outOfScope body field ─────────

    public sealed class ScenarioInScopeDestinationHasNoWarning
    {
        [Fact]
        public async Task InScopeDestinationDoesNotSetXOutOfScopeHeader()
        {
            // Station scope = [1, 2]; toLibraryId = 2 (in scope).
            var write = new FakeBulkReassignWrite { BulkReassignResult = 4 };
            var (controller, ctx) = ReassignControllerFactory.Build(write, ReassignControllerFactory.ScopeWith(1, 2));
            var request = new BulkReassignRequest(ToLibraryId: 2L, Filter: null);

            await controller.BulkReassign(request, CancellationToken.None);

            Assert.False(ctx.Response.Headers.ContainsKey("X-Out-Of-Scope"));
        }

        [Fact]
        public async Task InScopeDestinationBodyDoesNotContainOutOfScopeField()
        {
            var write = new FakeBulkReassignWrite { BulkReassignResult = 4 };
            var (controller, _) = ReassignControllerFactory.Build(write, ReassignControllerFactory.ScopeWith(1, 2));
            var request = new BulkReassignRequest(ToLibraryId: 2L, Filter: null);

            var result = await controller.BulkReassign(request, CancellationToken.None);

            var ok   = Assert.IsType<OkObjectResult>(result);
            var json = JsonSerializer.Serialize(ok.Value);
            var doc  = JsonDocument.Parse(json);
            Assert.False(doc.RootElement.TryGetProperty("outOfScope", out _));
        }
    }

    // ── AC5 — Unknown toLibraryId (repo returns null) → 400 ProblemDetails ──────────────────────

    public sealed class ScenarioUnknownToLibraryIdReturns400
    {
        [Fact]
        public async Task UnknownLibraryIdReturns400WithProblemDetails()
        {
            // Repo returns null to signal the library doesn't exist.
            var write = new FakeBulkReassignWrite { BulkReassignResult = null };
            var (controller, _) = ReassignControllerFactory.Build(write, ReassignControllerFactory.ScopeWith(1));
            var request = new BulkReassignRequest(ToLibraryId: 9999L, Filter: null);

            var result = await controller.BulkReassign(request, CancellationToken.None);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.IsType<ProblemDetails>(badRequest.Value);
        }

        [Fact]
        public async Task UnknownLibraryIdDoesNotSetXOutOfScopeHeader()
        {
            var write = new FakeBulkReassignWrite { BulkReassignResult = null };
            var (controller, ctx) = ReassignControllerFactory.Build(write, ReassignControllerFactory.ScopeWith(1));
            var request = new BulkReassignRequest(ToLibraryId: 9999L, Filter: null);

            await controller.BulkReassign(request, CancellationToken.None);

            Assert.False(ctx.Response.Headers.ContainsKey("X-Out-Of-Scope"));
        }
    }

    // ── AC6 — Missing toLibraryId → 400 ─────────────────────────────────────────────────────────

    public sealed class ScenarioMissingToLibraryIdReturns400
    {
        [Fact]
        public async Task NullToLibraryIdReturns400WithProblemDetails()
        {
            var write = new FakeBulkReassignWrite();
            var (controller, _) = ReassignControllerFactory.Build(write, ReassignControllerFactory.ScopeWith(1));
            // ToLibraryId = null simulates an absent or explicitly-null JSON field.
            var request = new BulkReassignRequest(ToLibraryId: null, Filter: null);

            var result = await controller.BulkReassign(request, CancellationToken.None);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.IsType<ProblemDetails>(badRequest.Value);
        }
    }
}

public static class FeatureBulkReassignEndpoint
{
    const string Pending = "Pending L4 — bulk reassign endpoint; operator-gated live, see docs/PLAN.md Epic J";

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioBulkReassignmentMovesEveryMatchingInScopeRow
    {
        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void PostWithFilterAndToLibraryIdReturns200WithUpdatedCount()
        {
            // POST /api/media/bulk/reassign { "filter": { "artist": "Brian Eno" }, "toLibraryId": 2 }
            // → 200 with body { updated: N } where N matches the count of in-scope rows with artist="Brian Eno".
            Assert.Fail("pending L4");
        }

        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void EveryMatchingRowNowReferencesTheDestinationLibrary()
        {
            // After the bulk reassign, every previously-matching in-scope row's library_id equals toLibraryId.
            Assert.Fail("pending L4");
        }

        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void FilterIsBoundedByStationScopeLibraryIds()
        {
            // Station:Scope:LibraryIds = [1]; matching rows exist in libraries 1 and 3.
            // The bulk reassign updates only rows whose source library_id is in [1]; rows in library 3 are untouched.
            Assert.Fail("pending L4");
        }

        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void FilterAcceptsTheSameFieldsAsGetApiMedia()
        {
            // Filter accepts { state?, artist?, genre?, q?, library-id? } — same shape as GET /api/media
            // and the F3 bulk-eligibility endpoint. q is the existing case-insensitive substring across
            // title+artist+album. WHERE values flow as parameters; no identifier interpolation.
            Assert.Fail("pending L4");
        }
    }

    public sealed class ScenarioCrossScopeDestinationSignalsOnTheResponse
    {
        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void CrossScopeDestinationCarriesXOutOfScopeTrueHeader()
        {
            // Station:Scope:LibraryIds = [1]; toLibraryId = 2 → response header X-Out-Of-Scope: true.
            Assert.Fail("pending L4");
        }

        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void CrossScopeDestinationCarriesOutOfScopeTrueInBody()
        {
            // The response JSON body carries outOfScope: true alongside { updated: <count> }.
            Assert.Fail("pending L4");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioValidation
    {
        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void UnknownToLibraryIdReturns400AndWritesNothing()
        {
            // toLibraryId references a non-existent library.library row → 400 ProblemDetails; no row is updated.
            Assert.Fail("pending L4");
        }

        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void MissingToLibraryIdReturns400()
        {
            // Body without toLibraryId → 400 ProblemDetails.
            Assert.Fail("pending L4");
        }

        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void EmptyFilterMatchesEveryInScopeRowAndIsAccepted()
        {
            // filter = {} → request proceeds; the response carries the full in-scope updated count
            // (the UI is responsible for confirmation before submission).
            Assert.Fail("pending L4");
        }
    }

    public sealed class ScenarioWriteSecurity
    {
        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void WriteWithoutCookieOrWithNonJsonContentTypeIsRejected()
        {
            // With Admin:Password set: no cookie → 401; non-JSON Content-Type → 415. Nothing is written.
            Assert.Fail("pending L4");
        }

        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void WhereClauseUsesParameterizedValuesOnly()
        {
            // For any filter combination, filter values flow as Npgsql parameters; no filter value is
            // concatenated into the SQL string (same hygiene as the F3 bulk-eligibility endpoint).
            Assert.Fail("pending L4");
        }
    }
}

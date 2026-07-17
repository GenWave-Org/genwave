// STORY-048 — Single-row reassignment via F18 PATCH (WIRE)
//
// BDD specification — xUnit. Extends PATCH /api/media/{id} with library_id + cross-scope warning.
//
// In-process tests (FeatureSingleRowReassignViaPatchInProcess): construct MediaController directly
// with fakes — no live stack required.
//
// Operator-gated integration scenarios (FeatureSingleRowReassignViaPatch): remain Skip-pinned
// until the live stack is in place (AC4).

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
/// Scriptable <see cref="IAdminMediaWrite"/>. Defaults to throwing if the result has not been set,
/// so tests that do not configure it will fail loudly rather than silently succeeding.
/// </summary>
file sealed class FakeAdminMediaWrite : IAdminMediaWrite
{
    public MediaWriteResult? UpdateResult { get; set; }

    /// <summary>Version echoed by <see cref="UpdateReturningVersionAsync"/> when the outcome is Updated.</summary>
    public string NewVersion { get; set; } = "999";

    public Task<MediaWriteResult> UpdateAsync(
        string id,
        MediaPatch patch,
        string expectedVersion,
        LibraryScope scope,
        CancellationToken ct)
        => Task.FromResult(UpdateResult ?? throw new InvalidOperationException("UpdateResult not set"));

    public Task<MediaUpdateOutcome> UpdateReturningVersionAsync(
        string id,
        MediaPatch patch,
        string expectedVersion,
        LibraryScope scope,
        CancellationToken ct)
    {
        var result = UpdateResult ?? throw new InvalidOperationException("UpdateResult not set");
        var version = result == MediaWriteResult.Updated ? NewVersion : null;
        return Task.FromResult(new MediaUpdateOutcome(result, version));
    }

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
        => Task.FromResult<int?>(0);
}

/// <summary>No-op query — the PATCH action does not call into the query surface.</summary>
file sealed class NoOpAdminMediaQuery : IAdminMediaQuery
{
    public Task<PagedResult<AdminMediaDto>> ListAdminAsync(
        LibraryScope scope, MediaQuery query, CancellationToken ct)
        => Task.FromResult(new PagedResult<AdminMediaDto>([], 0, 0));
}

/// <summary>No-op lookup — the PATCH action does not call into the lookup surface.</summary>
file sealed class NoOpAdminMediaLookup : IAdminMediaLookup
{
    public Task<(AdminMediaDto Row, long LibraryId)?> GetByIdWithLibraryAsync(
        long id, CancellationToken ct)
        => Task.FromResult<(AdminMediaDto Row, long LibraryId)?>(null);
}

/// <summary>
/// Builds a <see cref="MediaController"/> wired to the given fakes and returns both the
/// controller and the underlying <see cref="DefaultHttpContext"/> so tests can inspect
/// response headers after the action executes.
/// </summary>
file static class PatchControllerFactory
{
    public static (MediaController Controller, DefaultHttpContext HttpContext) Build(
        IAdminMediaWrite adminWrite,
        IStationScopeProvider scopeProvider,
        string ifMatch = "W/\"12345\"")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["If-Match"] = ifMatch;
        ctx.Request.ContentType = "application/json";

        var controller = new MediaController(
            new NoOpAdminMediaQuery(),
            new NoOpAdminMediaLookup(),
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

public static class FeatureSingleRowReassignViaPatchInProcess
{
    // ── AC1 — MediaPatch carries LibraryId ───────────────────────────────────────────────────────

    public sealed class ScenarioMediaPatchCarriesLibraryId
    {
        [Fact]
        public void MediaPatchWithLibraryIdPreservesTheValue()
        {
            var patch = new MediaPatch(
                Title: null, Artist: null, Album: null, Genre: null,
                Year: null, Eligible: null, LibraryId: 42L);

            Assert.Equal(42L, patch.LibraryId);
        }

        [Fact]
        public void MediaPatchWithoutLibraryIdHasNullLibraryId()
        {
            var patch = new MediaPatch(
                Title: "t", Artist: null, Album: null, Genre: null,
                Year: null, Eligible: null, LibraryId: null);

            Assert.Null(patch.LibraryId);
        }
    }

    // ── AC2 — In-scope reassign → 200, no warning header, no outOfScope body field ───────────────

    public sealed class ScenarioInScopeReassignSucceedsWithoutWarning
    {
        [Fact]
        public async Task InScopeReassignReturns200()
        {
            var write = new FakeAdminMediaWrite { UpdateResult = MediaWriteResult.Updated };
            // Scope contains library 2 — the destination is in scope.
            var (controller, _) = PatchControllerFactory.Build(write, PatchControllerFactory.ScopeWith(1, 2));
            var patch = new MediaPatch(null, null, null, null, null, null, LibraryId: 2L);

            var result = await controller.Patch(99L, patch, CancellationToken.None);

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task InScopeReassignDoesNotSetXOutOfScopeHeader()
        {
            var write = new FakeAdminMediaWrite { UpdateResult = MediaWriteResult.Updated };
            var (controller, ctx) = PatchControllerFactory.Build(write, PatchControllerFactory.ScopeWith(1, 2));
            var patch = new MediaPatch(null, null, null, null, null, null, LibraryId: 2L);

            await controller.Patch(99L, patch, CancellationToken.None);

            Assert.False(ctx.Response.Headers.ContainsKey("X-Out-Of-Scope"));
        }

        [Fact]
        public async Task InScopeReassignBodyDoesNotContainOutOfScopeField()
        {
            var write = new FakeAdminMediaWrite { UpdateResult = MediaWriteResult.Updated };
            var (controller, _) = PatchControllerFactory.Build(write, PatchControllerFactory.ScopeWith(1, 2));
            var patch = new MediaPatch(null, null, null, null, null, null, LibraryId: 2L);

            var result = await controller.Patch(99L, patch, CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var json = JsonSerializer.Serialize(ok.Value);
            var doc  = JsonDocument.Parse(json);
            Assert.False(doc.RootElement.TryGetProperty("outOfScope", out _));
        }
    }

    // ── AC3 — Cross-scope reassign → 200 + warning header + outOfScope: true body ─────────────────

    public sealed class ScenarioCrossScopeReassignSucceedsWithWarning
    {
        [Fact]
        public async Task CrossScopeReassignReturns200()
        {
            var write = new FakeAdminMediaWrite { UpdateResult = MediaWriteResult.Updated };
            // Scope only has library 1; destination is library 2 (out of scope).
            var (controller, _) = PatchControllerFactory.Build(write, PatchControllerFactory.ScopeWith(1));
            var patch = new MediaPatch(null, null, null, null, null, null, LibraryId: 2L);

            var result = await controller.Patch(99L, patch, CancellationToken.None);

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task CrossScopeReassignSetsXOutOfScopeHeader()
        {
            var write = new FakeAdminMediaWrite { UpdateResult = MediaWriteResult.Updated };
            var (controller, ctx) = PatchControllerFactory.Build(write, PatchControllerFactory.ScopeWith(1));
            var patch = new MediaPatch(null, null, null, null, null, null, LibraryId: 2L);

            await controller.Patch(99L, patch, CancellationToken.None);

            Assert.Equal("true", ctx.Response.Headers["X-Out-Of-Scope"].ToString());
        }

        [Fact]
        public async Task CrossScopeReassignBodyContainsOutOfScopeTrueField()
        {
            var write = new FakeAdminMediaWrite { UpdateResult = MediaWriteResult.Updated };
            var (controller, _) = PatchControllerFactory.Build(write, PatchControllerFactory.ScopeWith(1));
            var patch = new MediaPatch(null, null, null, null, null, null, LibraryId: 2L);

            var result = await controller.Patch(99L, patch, CancellationToken.None);

            var ok   = Assert.IsType<OkObjectResult>(result);
            var json = JsonSerializer.Serialize(ok.Value);
            var doc  = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.TryGetProperty("outOfScope", out var prop));
            Assert.True(prop.GetBoolean());
        }
    }

    // ── AC5 — Unknown library_id → 400 ProblemDetails, nothing written ────────────────────────────

    public sealed class ScenarioUnknownLibraryIdReturns400
    {
        [Fact]
        public async Task UnknownLibraryIdReturns400WithProblemDetails()
        {
            var write = new FakeAdminMediaWrite { UpdateResult = MediaWriteResult.UnknownLibraryId };
            var (controller, _) = PatchControllerFactory.Build(write, PatchControllerFactory.ScopeWith(1));
            var patch = new MediaPatch(null, null, null, null, null, null, LibraryId: 9999L);

            var result = await controller.Patch(99L, patch, CancellationToken.None);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.IsType<ProblemDetails>(badRequest.Value);
        }

        [Fact]
        public async Task UnknownLibraryIdDoesNotSetXOutOfScopeHeader()
        {
            var write = new FakeAdminMediaWrite { UpdateResult = MediaWriteResult.UnknownLibraryId };
            var (controller, ctx) = PatchControllerFactory.Build(write, PatchControllerFactory.ScopeWith(1));
            var patch = new MediaPatch(null, null, null, null, null, null, LibraryId: 9999L);

            await controller.Patch(99L, patch, CancellationToken.None);

            Assert.False(ctx.Response.Headers.ContainsKey("X-Out-Of-Scope"));
        }
    }

    // ── AC6 — Stale If-Match → 409 (inherited F18 concurrency path) ──────────────────────────────

    public sealed class ScenarioStaleIfMatchReturns409
    {
        [Fact]
        public async Task ConflictResultMaps409RegardlessOfLibraryId()
        {
            var write = new FakeAdminMediaWrite { UpdateResult = MediaWriteResult.Conflict };
            var (controller, _) = PatchControllerFactory.Build(write, PatchControllerFactory.ScopeWith(1));
            var patch = new MediaPatch(null, null, null, null, null, null, LibraryId: 2L);

            var result = await controller.Patch(99L, patch, CancellationToken.None);

            Assert.IsType<ConflictObjectResult>(result);
        }
    }

    // ── AC7 — SUPERSEDED by SPEC F43.2 (Epic V, closes gitea-#203) ───────────────────────────────────────
    // The source-row scope check (MediaWriteResult.OutOfScope → 403) is repealed: scope is a
    // curation filter, not an access gate. MediaWriteResult.OutOfScope no longer exists, so the
    // controller has no 403 arm left to map it to — the real repo-level proof (a row outside the
    // caller's scope still writes) lives in MediaLibrary.Tests'
    // ScenarioRowOutsideScopeStillUpdates (Story040) against real Postgres; the wire-level proof
    // (through the mapped HTTP route) lives in Story137_ScopeNeverBlocksRowAccess. This scenario
    // keeps AC7's slot to prove the controller-level consequence: even the strictest possible
    // caller — one whose station scope contains no library at all — gets a successful PATCH.

    public sealed class ScenarioSourceScopeNoLongerBlocksPatch
    {
        [Fact]
        public async Task PatchSucceedsEvenWhenTheCallersStationScopeExcludesEveryLibrary()
        {
            var write = new FakeAdminMediaWrite { UpdateResult = MediaWriteResult.Updated };
            var (controller, _) = PatchControllerFactory.Build(write, PatchControllerFactory.ScopeWith());
            var patch = new MediaPatch("New Title", null, null, null, null, null, LibraryId: null);

            var result = await controller.Patch(99L, patch, CancellationToken.None);

            Assert.IsType<NoContentResult>(result);
        }
    }

    // ── Non-library-id patch still returns 204 (unchanged F18 behaviour) ─────────────────────────

    public sealed class ScenarioNonLibraryPatchStillReturns204
    {
        [Fact]
        public async Task PatchWithoutLibraryIdReturns204()
        {
            var write = new FakeAdminMediaWrite { UpdateResult = MediaWriteResult.Updated };
            var (controller, _) = PatchControllerFactory.Build(write, PatchControllerFactory.ScopeWith(1));
            // No LibraryId — existing tag edit only.
            var patch = new MediaPatch("New Title", null, null, null, null, null, LibraryId: null);

            var result = await controller.Patch(99L, patch, CancellationToken.None);

            Assert.IsType<NoContentResult>(result);
        }
    }
}

// ── Operator-gated (live stack) ───────────────────────────────────────────────────────────────────

public static class FeatureSingleRowReassignViaPatch
{
    const string Pending = "Pending L3 — single-row reassign via F18 PATCH + X-Out-Of-Scope; operator-gated live, see docs/PLAN.md Epic J";

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioInScopeReassignmentSucceedsWithoutWarning
    {
        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void PatchWithLibraryIdInScopeReturns200AndMovesTheRow()
        {
            // Station:Scope:LibraryIds = [1, 2]; source row is in library 1.
            // PATCH /api/media/{id} { "library_id": 2 } + current If-Match → 200; row's library_id = 2.
            Assert.Fail("pending L3");
        }

        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void InScopeReassignmentResponseDoesNotCarryXOutOfScopeHeader()
        {
            // The 200 response from an in-scope reassign carries no X-Out-Of-Scope header
            // and no outOfScope field in the body.
            Assert.Fail("pending L3");
        }
    }

    public sealed class ScenarioCrossScopeReassignmentSucceedsWithWarning
    {
        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void PatchToALibraryOutsideScopeReturns200AndPersistsTheMove()
        {
            // Station:Scope:LibraryIds = [1]; PATCH /api/media/{id} { "library_id": 2 } where library 2 exists
            // but is out of scope → 200 with the row now in library 2.
            Assert.Fail("pending L3");
        }

        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void CrossScopeResponseCarriesXOutOfScopeTrueHeader()
        {
            // The 200 response from a cross-scope reassign carries response header X-Out-Of-Scope: true.
            Assert.Fail("pending L3");
        }

        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void CrossScopeResponseBodyCarriesOutOfScopeTrueField()
        {
            // The response JSON body carries the field outOfScope: true.
            Assert.Fail("pending L3");
        }

        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void CrossScopeMovedRowLeavesRotationOnTheNextFeederTick()
        {
            // After the cross-scope move, GetRandomReadyAsync / GET /media/random no longer returns the row
            // (the running feeder's next selection tick excludes it).
            Assert.Fail("pending L3");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioValidationAndConcurrency
    {
        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void UnknownLibraryIdReturns400AndWritesNothing()
        {
            // PATCH with { "library_id": 9999 } where no library 9999 exists → 400 ProblemDetails;
            // the row's library_id is unchanged.
            Assert.Fail("pending L3");
        }

        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void StaleIfMatchOnReassignReturns409AndWritesNothing()
        {
            // PATCH with library_id and an If-Match that no longer matches xmin → 409 Conflict;
            // the row is unchanged. Same as F18.6 — reassignment goes through the same concurrency path.
            Assert.Fail("pending L3");
        }

        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void OutOfScopeSourceRowSucceedsWithTheWarningHeader()
        {
            // SUPERSEDED by SPEC F43.2 (Epic V, closes gitea-#203): PATCH on a row whose CURRENT
            // library_id is not in Station:Scope:LibraryIds now succeeds (the old 403 is
            // repealed) and carries X-Out-Of-Scope: true.
            Assert.Fail("pending L3");
        }
    }
}

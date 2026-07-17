// STORY-103 — PATCH returns the fresh version (WIRE) (Epic R / SPEC F31.1, gitea-#181)
//
// BDD specification — xUnit. Every successful PATCH /api/media/{id} response — 204 and both 200
// reassign variants — carries ETag: W/"<new xmin>" produced by the UPDATE's RETURNING (no second
// read), symmetric with GET /api/media/{id}. Curl-through-the-production-binary proof is R13's
// gate job.
//
// In-process: MediaController constructed directly with fakes — no live stack required. Mirrors
// the Story048 FeatureSingleRowReassignViaPatchInProcess controller-spec idiom (DefaultHttpContext
// so Response.Headers is assertable).

using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes ──────────────────────────────────────────────────────────────────────────────

/// <summary>No-op query — the PATCH action does not call into the query surface.</summary>
file sealed class NoOpAdminMediaQuery : IAdminMediaQuery
{
    public Task<PagedResult<AdminMediaDto>> ListAdminAsync(
        LibraryScope scope, MediaQuery query, CancellationToken ct)
        => Task.FromResult(new PagedResult<AdminMediaDto>([], 0, 0));
}

/// <summary>
/// No-op lookup that also records whether it was ever called — the PATCH action must never fall
/// back to a lookup/read to obtain the new version (STORY-103: RETURNING, not a second read).
/// </summary>
file sealed class RecordingAdminMediaLookup : IAdminMediaLookup
{
    public int CallCount { get; private set; }

    public Task<(AdminMediaDto Row, long LibraryId)?> GetByIdWithLibraryAsync(long id, CancellationToken ct)
    {
        CallCount++;
        return Task.FromResult<(AdminMediaDto Row, long LibraryId)?>(null);
    }
}

/// <summary>
/// Scriptable, call-recording <see cref="IAdminMediaWrite"/> double. Returns the configured
/// <see cref="Outcome"/> from <see cref="UpdateReturningVersionAsync"/> and records every method
/// invoked, so a scenario can assert the new version came from that single call — the legacy
/// <see cref="UpdateAsync"/> (enum-only, no version) must never be used by the controller.
/// </summary>
file sealed class RecordingVersionWrite : IAdminMediaWrite
{
    public required MediaUpdateOutcome Outcome { get; init; }

    public List<string> CallLog { get; } = [];

    public Task<MediaUpdateOutcome> UpdateReturningVersionAsync(
        string id, MediaPatch patch, string expectedVersion, LibraryScope scope, CancellationToken ct)
    {
        CallLog.Add(nameof(UpdateReturningVersionAsync));
        return Task.FromResult(Outcome);
    }

    public Task<MediaWriteResult> UpdateAsync(
        string id, MediaPatch patch, string expectedVersion, LibraryScope scope, CancellationToken ct)
    {
        CallLog.Add(nameof(UpdateAsync));
        throw new NotSupportedException(
            "STORY-103: the controller must call UpdateReturningVersionAsync, never the enum-only UpdateAsync.");
    }

    public Task<int> SetEligibilityAsync(MediaQuery filter, bool eligible, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-103.");

    public Task<int?> BulkReassignAsync(MediaQuery filter, long toLibraryId, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-103.");
}

/// <summary>
/// <see cref="IAdminMediaWrite"/> double that models real optimistic-concurrency version bumping:
/// a write only succeeds when <c>expectedVersion</c> equals the currently-held version, and the
/// held version advances on every successful write — enough to drive the chained-PATCH scenario
/// without a live database.
/// </summary>
file sealed class ChainableVersionWrite(string startVersion) : IAdminMediaWrite
{
    string currentVersion = startVersion;

    public Task<MediaUpdateOutcome> UpdateReturningVersionAsync(
        string id, MediaPatch patch, string expectedVersion, LibraryScope scope, CancellationToken ct)
    {
        if (expectedVersion != currentVersion)
            return Task.FromResult(new MediaUpdateOutcome(MediaWriteResult.Conflict, null));

        currentVersion = (int.Parse(currentVersion, CultureInfo.InvariantCulture) + 1)
            .ToString(CultureInfo.InvariantCulture);
        return Task.FromResult(new MediaUpdateOutcome(MediaWriteResult.Updated, currentVersion));
    }

    public Task<MediaWriteResult> UpdateAsync(
        string id, MediaPatch patch, string expectedVersion, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-103's chained-PATCH scenario.");

    public Task<int> SetEligibilityAsync(MediaQuery filter, bool eligible, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-103.");

    public Task<int?> BulkReassignAsync(MediaQuery filter, long toLibraryId, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by STORY-103.");
}

/// <summary>
/// Builds a <see cref="MediaController"/> wired to the given <see cref="IAdminMediaWrite"/> fake
/// and returns both the controller and the underlying <see cref="DefaultHttpContext"/> so tests
/// can inspect response headers after the action executes.
/// </summary>
file static class PatchHarness
{
    public static (MediaController Controller, DefaultHttpContext HttpContext) Build(
        IAdminMediaWrite adminWrite,
        string ifMatch = "W/\"1\"",
        IAdminMediaLookup? adminLookup = null,
        params long[] scopeLibraryIds)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["If-Match"] = ifMatch;
        ctx.Request.ContentType = "application/json";

        var scope = new LibraryScope(scopeLibraryIds.Length == 0 ? [1L] : scopeLibraryIds);

        var controller = new MediaController(
            new NoOpAdminMediaQuery(),
            adminLookup ?? new RecordingAdminMediaLookup(),
            adminWrite,
            new FakeStationScopeProvider(scope),
            NullLogger<MediaController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = ctx },
        };

        return (controller, ctx);
    }
}

// ── In-process tests ──────────────────────────────────────────────────────────────────────────────

public static class FeaturePatchReturnsFreshEtag
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioNoContentCarriesEtag
    {
        [Fact]
        public async Task TagEditTwoOhFourCarriesTheNewWeakEtag()
        {
            var write = new RecordingVersionWrite { Outcome = new MediaUpdateOutcome(MediaWriteResult.Updated, "42") };
            var (controller, ctx) = PatchHarness.Build(write);
            var patch = new MediaPatch("New Title", null, null, null, null, null, LibraryId: null);

            var result = await controller.Patch(99L, patch, CancellationToken.None);

            Assert.IsType<NoContentResult>(result);
            Assert.Equal("W/\"42\"", ctx.Response.Headers.ETag.ToString());
        }

        [Fact]
        public async Task EligibilityToggleTwoOhFourCarriesTheNewWeakEtag()
        {
            var write = new RecordingVersionWrite { Outcome = new MediaUpdateOutcome(MediaWriteResult.Updated, "43") };
            var (controller, ctx) = PatchHarness.Build(write);
            var patch = new MediaPatch(null, null, null, null, null, Eligible: false, LibraryId: null);

            var result = await controller.Patch(99L, patch, CancellationToken.None);

            Assert.IsType<NoContentResult>(result);
            Assert.Equal("W/\"43\"", ctx.Response.Headers.ETag.ToString());
        }
    }

    public sealed class ScenarioReassignVariantsCarryEtag
    {
        [Fact]
        public async Task InScopeReassignTwoHundredCarriesTheNewWeakEtag()
        {
            var write = new RecordingVersionWrite { Outcome = new MediaUpdateOutcome(MediaWriteResult.Updated, "44") };
            // Scope contains library 2 — the destination is in scope.
            var (controller, ctx) = PatchHarness.Build(write, scopeLibraryIds: [1, 2]);
            var patch = new MediaPatch(null, null, null, null, null, null, LibraryId: 2L);

            var result = await controller.Patch(99L, patch, CancellationToken.None);

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal("W/\"44\"", ctx.Response.Headers.ETag.ToString());
        }

        [Fact]
        public async Task OutOfScopeReassignTwoHundredCarriesTheNewWeakEtag()
        {
            var write = new RecordingVersionWrite { Outcome = new MediaUpdateOutcome(MediaWriteResult.Updated, "45") };
            // Scope only has library 1; destination is library 2 (out of scope).
            var (controller, ctx) = PatchHarness.Build(write, scopeLibraryIds: [1]);
            var patch = new MediaPatch(null, null, null, null, null, null, LibraryId: 2L);

            var result = await controller.Patch(99L, patch, CancellationToken.None);

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal("W/\"45\"", ctx.Response.Headers.ETag.ToString());
            // Alongside the existing X-Out-Of-Scope: true header (F20.6 unchanged).
            Assert.Equal("true", ctx.Response.Headers["X-Out-Of-Scope"].ToString());
        }
    }

    public sealed class ScenarioVersionFromTheSameUpdate
    {
        [Fact]
        public async Task NewVersionComesFromReturningNotASecondRead()
        {
            var write  = new RecordingVersionWrite { Outcome = new MediaUpdateOutcome(MediaWriteResult.Updated, "46") };
            var lookup = new RecordingAdminMediaLookup();
            var (controller, ctx) = PatchHarness.Build(write, adminLookup: lookup);
            var patch = new MediaPatch("Title", null, null, null, null, null, LibraryId: null);

            var result = await controller.Patch(99L, patch, CancellationToken.None);

            Assert.IsType<NoContentResult>(result);
            Assert.Equal("W/\"46\"", ctx.Response.Headers.ETag.ToString());
            // The write fake's RETURNING-backed call is the only call made — no follow-up
            // lookup/read (neither the legacy UpdateAsync nor IAdminMediaLookup) ever fires.
            Assert.Equal([nameof(IAdminMediaWrite.UpdateReturningVersionAsync)], write.CallLog);
            Assert.Equal(0, lookup.CallCount);
        }

        [Fact]
        public async Task ChainedPatchWithTheReturnedEtagSucceeds()
        {
            var write = new ChainableVersionWrite(startVersion: "1");
            var (controller, ctx) = PatchHarness.Build(write, ifMatch: "W/\"1\"");
            var firstPatch = new MediaPatch("First", null, null, null, null, null, LibraryId: null);

            var firstResult = await controller.Patch(99L, firstPatch, CancellationToken.None);
            Assert.IsType<NoContentResult>(firstResult);
            var returnedEtag = ctx.Response.Headers.ETag.ToString();
            Assert.Equal("W/\"2\"", returnedEtag);

            // Take the response ETag as the next If-Match.
            var (secondController, secondCtx) = PatchHarness.Build(write, ifMatch: returnedEtag);
            var secondPatch = new MediaPatch("Second", null, null, null, null, null, LibraryId: null);

            var secondResult = await secondController.Patch(99L, secondPatch, CancellationToken.None);

            Assert.IsType<NoContentResult>(secondResult);
            Assert.Equal("W/\"3\"", secondCtx.Response.Headers.ETag.ToString());
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioFailuresCarryNoEtag
    {
        [Fact]
        public async Task StaleIfMatchConflictCarriesNoEtagHeader()
        {
            var write = new RecordingVersionWrite { Outcome = new MediaUpdateOutcome(MediaWriteResult.Conflict, null) };
            var (controller, ctx) = PatchHarness.Build(write);
            var patch = new MediaPatch("Should Not Persist", null, null, null, null, null, LibraryId: null);

            var result = await controller.Patch(99L, patch, CancellationToken.None);

            Assert.IsType<ConflictObjectResult>(result);
            Assert.False(ctx.Response.Headers.ContainsKey("ETag"));
        }
    }
}

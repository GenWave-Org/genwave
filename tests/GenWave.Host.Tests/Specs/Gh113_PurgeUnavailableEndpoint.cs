// gh-#113 — Explicit operator purge for long-unavailable tracks (commit 2: the endpoint half).
//
// BDD specification — xUnit. Drives MediaPurgeController directly with an in-process fake
// IMediaPurge, mirroring Story158_BulkRatingEndpoints' controller-direct idiom: the real SQL
// (age filter, cascade, atomic tripwire) is proven against Postgres in
// MediaLibrary.Tests/Specs/Gh113_PurgeUnavailableRepository.cs; these specs pin the WIRING — the
// AdminOnly policy, the 7-day default, the minimum-1 400, the dryRun passthrough and
// { wouldDelete } shape, the tripwire 409 ProblemDetails naming both counts, and the { deleted }
// success shape.

using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;

namespace GenWave.Host.Tests.Specs;

// ── In-process fake (file-scoped: this spec owns its own double) ────────────────────────────────

file sealed class FakeMediaPurge : IMediaPurge
{
    public MediaPurgeOutcome Outcome { get; set; } = new(Candidates: 0, LibraryTotal: 0, Deleted: 0);
    public (int OlderThanDays, bool DryRun)? LastCall { get; private set; }

    public Task<MediaPurgeOutcome> PurgeUnavailableAsync(int olderThanDays, bool dryRun, CancellationToken ct)
    {
        LastCall = (olderThanDays, dryRun);
        return Task.FromResult(Outcome);
    }
}

file static class PurgeEndpointHarness
{
    public static (MediaPurgeController Controller, FakeMediaPurge Purge) Build(MediaPurgeOutcome outcome)
    {
        var purge = new FakeMediaPurge { Outcome = outcome };
        var controller = new MediaPurgeController(purge, NullLogger<MediaPurgeController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return (controller, purge);
    }

    public static T GetProperty<T>(object? value, string name)
    {
        var prop = value?.GetType().GetProperty(name);
        Assert.NotNull(prop);
        var raw = prop.GetValue(value);
        return Assert.IsType<T>(raw);
    }
}

public static class FeaturePurgeUnavailableEndpoint
{
    public sealed class ScenarioAuthorization
    {
        [Fact]
        public void The_controller_requires_the_AdminOnly_policy()
        {
            // A hard-delete is library administration, not curation — the policy must be
            // AdminOnly, never the Curation plane the other bulk media writes use (see the
            // controller's own remarks). Pinned by attribute so a future refactor that quietly
            // re-points it fails here.
            var attribute = typeof(MediaPurgeController).GetCustomAttribute<AuthorizeAttribute>();

            Assert.NotNull(attribute);
            Assert.Equal(AuthorizationPolicies.AdminOnly, attribute.Policy);
        }
    }

    public sealed class ScenarioWindowValidation
    {
        [Fact]
        public async Task An_absent_window_defaults_to_seven_days()
        {
            var (controller, purge) = PurgeEndpointHarness.Build(new MediaPurgeOutcome(0, 100, 0));

            await controller.PurgeUnavailable(new PurgeUnavailableRequest(), CancellationToken.None);

            Assert.Equal((7, false), purge.LastCall);
        }

        [Fact]
        public async Task A_window_below_one_day_is_rejected_before_anything_is_counted()
        {
            var (controller, purge) = PurgeEndpointHarness.Build(new MediaPurgeOutcome(0, 100, 0));

            var result = await controller.PurgeUnavailable(
                new PurgeUnavailableRequest(OlderThanDays: 0), CancellationToken.None);

            var bad = Assert.IsType<BadRequestObjectResult>(result);
            var problem = Assert.IsType<ProblemDetails>(bad.Value);
            Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
            Assert.Null(purge.LastCall);
        }
    }

    public sealed class ScenarioDryRun
    {
        [Fact]
        public async Task A_dry_run_reaches_the_repository_as_a_dry_run_and_reports_wouldDelete()
        {
            var (controller, purge) = PurgeEndpointHarness.Build(
                new MediaPurgeOutcome(Candidates: 12, LibraryTotal: 100, Deleted: 0));

            var result = await controller.PurgeUnavailable(
                new PurgeUnavailableRequest(OlderThanDays: 14, DryRun: true), CancellationToken.None);

            Assert.Equal((14, true), purge.LastCall);
            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(12, PurgeEndpointHarness.GetProperty<int>(ok.Value, "wouldDelete"));
        }
    }

    public sealed class ScenarioTripwire
    {
        [Fact]
        public async Task Candidates_over_half_the_library_yield_409_naming_both_counts()
        {
            // 60 of 100 — the mount-outage pattern. The ProblemDetails must NAME the counts so
            // the operator (and the UI relaying `detail`) sees the scale of what was refused.
            var (controller, _) = PurgeEndpointHarness.Build(
                new MediaPurgeOutcome(Candidates: 60, LibraryTotal: 100, Deleted: 0));

            var result = await controller.PurgeUnavailable(
                new PurgeUnavailableRequest(), CancellationToken.None);

            var conflict = Assert.IsType<ConflictObjectResult>(result);
            var problem = Assert.IsType<ProblemDetails>(conflict.Value);
            Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
            Assert.NotNull(problem.Detail);
            Assert.Contains("60", problem.Detail);
            Assert.Contains("100", problem.Detail);
        }

        [Fact]
        public async Task The_tripwire_fires_on_dry_runs_too()
        {
            // The UI's count fetch is a dry run — it must already surface the refusal an actual
            // purge would hit, instead of naming a count the endpoint would never deliver.
            var (controller, _) = PurgeEndpointHarness.Build(
                new MediaPurgeOutcome(Candidates: 60, LibraryTotal: 100, Deleted: 0));

            var result = await controller.PurgeUnavailable(
                new PurgeUnavailableRequest(DryRun: true), CancellationToken.None);

            Assert.IsType<ConflictObjectResult>(result);
        }
    }

    public sealed class ScenarioPurgeSucceeds
    {
        [Fact]
        public async Task The_response_reports_how_many_rows_were_deleted()
        {
            var (controller, purge) = PurgeEndpointHarness.Build(
                new MediaPurgeOutcome(Candidates: 12, LibraryTotal: 100, Deleted: 12));

            var result = await controller.PurgeUnavailable(
                new PurgeUnavailableRequest(), CancellationToken.None);

            Assert.Equal((7, false), purge.LastCall);
            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(12, PurgeEndpointHarness.GetProperty<int>(ok.Value, "deleted"));
        }

        [Fact]
        public async Task Exactly_half_the_library_is_allowed_through()
        {
            // "Exceed 50%" refuses; exactly 50% is a legitimate big purge (the demo shrink is
            // usually far above this line, but the boundary is contract, not accident).
            var (controller, _) = PurgeEndpointHarness.Build(
                new MediaPurgeOutcome(Candidates: 50, LibraryTotal: 100, Deleted: 50));

            var result = await controller.PurgeUnavailable(
                new PurgeUnavailableRequest(), CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(50, PurgeEndpointHarness.GetProperty<int>(ok.Value, "deleted"));
        }
    }
}

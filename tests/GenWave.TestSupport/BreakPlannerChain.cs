using GenWave.Core.Abstractions;
using GenWave.Orchestration;
using GenWave.TestSupport.Fakes;
using Microsoft.Extensions.Logging;

namespace GenWave.TestSupport;

/// <summary>
/// What <see cref="BreakPlannerBuilder.Build"/> returns (SPEC F188, STORY-455, PLAN T521) — the
/// planner plus every collaborator a spec has ever needed to reach into directly, so no spec has to
/// fall back to a constructor call to get at them.
///
/// <para>
/// A spec that needs a fake's own members on a seam this chain doesn't expose (e.g. a counting
/// wrapper's own call tally) passes its own fake through the matching <c>With*</c> and keeps a
/// reference to it — see <see cref="OrchestratorChain"/>'s own remarks for the same rule.
/// </para>
/// </summary>
public sealed record BreakPlannerChain(
    BreakPlanner Planner,
    SpeechDeferralQueue Queue,
    TimeProvider Time,
    ILogger<BreakPlanner> Logger,
    IMediaCatalog Catalog,
    CachingScheduleResolver ScheduleResolver,
    IPersonaStore PersonaStore);

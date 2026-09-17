using GenWave.Core.Abstractions;
using GenWave.Orchestration;
using Microsoft.Extensions.Logging;

namespace GenWave.TestSupport;

/// <summary>
/// What <see cref="OrchestratorBuilder.Build"/> returns (SPEC F184.2, STORY-451, PLAN T511) — the
/// Orchestrator plus every collaborator a spec has ever needed to reach into directly, so no spec
/// has to fall back to a constructor call to get at them.
///
/// <para>
/// Every slot is typed to the seam type the matching <c>With*</c> method accepts, so <c>Build()</c>
/// never throws for an override that compiles. A spec that needs a fake's own members on a default
/// seam (e.g. <c>FakeTimeProvider.Advance</c>) passes its own fake through the matching <c>With*</c>
/// and keeps a reference to it — the default-typed value here stays widened to the interface.
/// </para>
/// </summary>
public sealed record OrchestratorChain(
    Orchestrator Orchestrator,
    SpeechDeferralQueue Queue,
    TimeProvider Time,
    ITtsSegmentSource Tts,
    IStationEventSink Events,
    ILogger<Orchestrator> Logger,
    IMediaCatalog Catalog,
    IPatterDurationEstimator? PatterEstimator);

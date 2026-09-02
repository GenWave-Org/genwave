using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SPEC F158.2/F158.3 (STORY-388, PLAN T397) — the seam <c>Orchestrator</c>'s ad drain vends
/// through, one method, in-tree only (never published to the <c>GenWave.Abstractions</c> NuGet — the
/// pipeline it fronts is a Home-side feature package, not a plugin contract).
///
/// <para>
/// <b>Why this is NOT <see cref="IAdSpotSource"/>.</b> <see cref="IAdSpotSource"/> is ONE candidate
/// in the <c>GenWave.Ads.AdSpotPipeline</c> fan-out (many sources, first non-null wins); this
/// interface is the RESULT of that whole fan-out — the one thing a drain actually calls. Folding
/// them into a single interface would force <c>AdSpotPipeline</c> itself to also look like "just
/// another source" to any future second consumer, when it is structurally the aggregate, not a
/// candidate.
/// </para>
///
/// <para>
/// <b>Why this lives in <c>GenWave.Core</c>, not <c>GenWave.Ads</c> or <c>GenWave.Orchestration</c>
/// (the L10 acyclicity call this task made).</b> <c>GenWave.Orchestration</c> must never reference
/// <c>GenWave.Ads</c> — the ads FEATURE package consumes nothing of the orchestration domain, so the
/// dependency can only run one way, and <c>GenWave.Ads</c> already depends on nothing but
/// <c>GenWave.Core</c> (see that project's own <c>.csproj</c> remarks). Defining the vend seam in
/// <c>GenWave.Core</c> — the one project both already reference — lets <c>GenWave.Ads</c>' own
/// <c>AdSpotPipeline</c> implement it and <c>GenWave.Orchestration</c>'s own <c>Orchestrator</c>
/// consume it, with neither feature project ever seeing the other. The Host composition root
/// (<c>Program.cs</c>) is the only place that sees both: <c>AddGenWaveAds</c> overrides
/// <c>AddGenWaveOrchestration</c>'s own <see cref="NoOpAdSpotVend"/> default (the override-after-the-
/// default idiom <see cref="IStationImagingSettingsProvider"/>'s own registration already
/// establishes) with the real pipeline.
/// </para>
/// </summary>
public interface IAdSpotVend
{
    /// <summary>
    /// The next ad spot to air, or <see langword="null"/> when nothing is available this break —
    /// always a legal answer, never an error (mirrors <see cref="IAdSpotSource.GetNextSpotAsync"/>'s
    /// own contract one level down the fan-out). The returned <see cref="MediaItem"/> is
    /// pre-rendered — the caller vends it straight onto the queue with no render at air time.
    /// </summary>
    /// <param name="ct">Propagated to any I/O the underlying pipeline performs while vending.</param>
    Task<MediaItem?> GetNextSpotAsync(CancellationToken ct);
}

using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Options;

namespace GenWave.Host.Announcements;

/// <summary>
/// Defense-in-depth vend refusal (SPEC F145.2, PLAN T341): while <c>Station:SpectatorMode</c> is on,
/// every claim reads back empty — a public stream never carries the house's events, restated one
/// layer down the pipeline from <c>AnnouncementsController</c>'s own F145.1 door 403 (and from the
/// private→public transition's own decline-everything sweep, F145.2's primary mechanism). The SAME
/// wrap-in-DI shape as <c>MediaExistencePushGuard</c> (gh-#612): <paramref name="inner"/> —
/// <c>AnnouncementRepository</c>, resolved through the narrow <see cref="IAnnouncementSource"/> seam
/// — stays entirely privacy-blind, and so does <c>GenWave.Orchestration.Orchestrator</c> calling
/// <see cref="ClaimDeliverableAsync"/>: an empty result already means "nothing deliverable" for every
/// OTHER reason (drained, all expired), so no caller needs to distinguish "refused" from "genuinely
/// empty" — there is nothing this refusal could usefully log on its own hot path that the door 403
/// and the transition sweep have not already said once, loudly, elsewhere.
/// </summary>
sealed class SpectatorModeAnnouncementVendGuard(IAnnouncementSource inner, IOptionsMonitor<StationOptions> stationMonitor)
    : IAnnouncementSource
{
    public Task<IReadOnlyList<AnnouncementItem>> ClaimDeliverableAsync(int max, CancellationToken ct) =>
        stationMonitor.CurrentValue.SpectatorMode
            ? Task.FromResult<IReadOnlyList<AnnouncementItem>>([])
            : inner.ClaimDeliverableAsync(max, ct);
}

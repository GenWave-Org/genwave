using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Options;

namespace GenWave.MediaLibrary.Garden;

/// <summary>
/// The <see cref="RotKind.ShelfDust"/> pass (SPEC F153.7; STORY-377; PLAN T375, gh-#529) — a thin,
/// Dapper-free orchestrator (L2 as narrowed at T357), the SAME shape <see cref="DeadFileGardenerPass"/>
/// already establishes: every statement lives in
/// <see cref="RotFindingRepository.ReconcileShelfDustAsync"/>, this type only reads
/// <see cref="GardenerOptions.ShelfDustDays"/> live via <see cref="IOptionsMonitor{TOptions}.CurrentValue"/>
/// (the same F44.2 live-editable shape every other Gardener knob honors) and calls straight through,
/// floored at one day — <see cref="GardenerOptions.ShelfDustDays"/>'s own boot-validated
/// <c>[Range(1, 3650)]</c> already guarantees this in practice, but the floor here matches every
/// sibling pass's own defensive posture rather than trusting that boot validation alone.
/// </summary>
sealed class ShelfDustGardenerPass(
    IRotFindingStore store,
    IOptionsMonitor<GardenerOptions> gardenerOptions) : IGardenerPass
{
    public RotKind Kind => RotKind.ShelfDust;

    public Task RunAsync(CancellationToken ct) => store.ReconcileShelfDustAsync(CurrentShelfAge, ct);

    /// <summary>Private (T372 review LOW-1 precedent): only <see cref="RunAsync"/> calls this.</summary>
    TimeSpan CurrentShelfAge => TimeSpan.FromDays(Math.Max(1, gardenerOptions.CurrentValue.ShelfDustDays));
}

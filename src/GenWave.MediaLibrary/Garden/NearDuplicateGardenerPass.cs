using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Options;

namespace GenWave.MediaLibrary.Garden;

/// <summary>
/// The <see cref="RotKind.NearDuplicate"/> pass (SPEC F153.5; STORY-376; PLAN T374, gh-#529) — a
/// thin, Dapper-free orchestrator (L2 as narrowed at T357), the SAME shape
/// <see cref="DeadFileGardenerPass"/> already establishes: every statement lives in
/// <see cref="RotFindingRepository.ReconcileNearDuplicatesAsync"/>, this type only reads
/// <see cref="GardenerOptions.DuplicateToleranceMs"/> live via <see cref="IOptionsMonitor{TOptions}.CurrentValue"/>
/// (the same F44.2 live-editable shape <see cref="DeadFileGardenerPass"/>'s own grace window
/// applies) and calls straight through, floored at 0 the same defensive way that pass floors its own
/// options.
///
/// <para>
/// <b>No filesystem dependency of any kind (STORY-376 AC7)</b> — the constructor's own parameter
/// list names nothing from <c>System.IO</c>/<c>Microsoft.Extensions.FileProviders</c>, so an
/// unreachable media mount can never stop this pass from opening or resolving findings: every input
/// it touches is catalog data already in Postgres.
/// </para>
/// </summary>
sealed class NearDuplicateGardenerPass(
    IRotFindingStore store,
    IOptionsMonitor<GardenerOptions> gardenerOptions) : IGardenerPass
{
    public RotKind Kind => RotKind.NearDuplicate;

    public Task RunAsync(CancellationToken ct) =>
        store.ReconcileNearDuplicatesAsync(Math.Max(0, gardenerOptions.CurrentValue.DuplicateToleranceMs), ct);
}

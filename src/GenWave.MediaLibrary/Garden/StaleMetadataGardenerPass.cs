using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Garden;

/// <summary>
/// The <see cref="RotKind.StaleMetadata"/> pass (SPEC F153.6; STORY-377; PLAN T375, gh-#529) — a
/// thin, Dapper-free orchestrator (L2 as narrowed at T357), the SAME shape
/// <see cref="DeadFileGardenerPass"/>/<see cref="NearDuplicateGardenerPass"/> already establish:
/// every statement lives in <see cref="RotFindingRepository.ReconcileStaleMetadataAsync"/>, this
/// type only calls straight through — F153.6 names no live-editable knob at all, unlike its two
/// siblings, so there is no options dependency here to read.
/// </summary>
sealed class StaleMetadataGardenerPass(IRotFindingStore store) : IGardenerPass
{
    public RotKind Kind => RotKind.StaleMetadata;

    public Task RunAsync(CancellationToken ct) => store.ReconcileStaleMetadataAsync(ct);
}

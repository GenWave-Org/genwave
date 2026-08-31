using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Options;

namespace GenWave.MediaLibrary.Garden;

/// <summary>
/// The <see cref="RotKind.DeadFile"/> pass (SPEC F153.3; STORY-374, STORY-375; PLAN T372,
/// gh-#529) — a thin, Dapper-free orchestrator (L2 as narrowed at T357): every statement lives in
/// <see cref="RotFindingRepository.ReconcileDeadFilesAsync"/>, this type only computes the live
/// grace window and calls through.
///
/// <para>
/// <b>Grace = <c>Library:Scan:MissThreshold × Library:ScanIntervalSeconds</c></b> (ORCHESTRATOR
/// ruling), read fresh on every call via <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> — the
/// same F44.2 live-editable shape <c>Scan.ScanService</c> already applies to both knobs
/// individually. Both are floored at 1 the same defensive way
/// <c>ScanService.CurrentScanInterval</c>/<c>ScanService.CurrentMissThreshold</c>
/// already floor them, even though <see cref="ScanOptions"/>/<see cref="LibraryOptions"/> are bound
/// via plain <c>Configure&lt;T&gt;</c> (never <c>ValidateDataAnnotations</c>) and so carry no boot-time
/// floor of their own.
/// </para>
/// </summary>
sealed class DeadFileGardenerPass(
    IRotFindingStore store,
    IOptionsMonitor<ScanOptions> scanOptions,
    IOptionsMonitor<LibraryOptions> libraryOptions) : IGardenerPass
{
    public RotKind Kind => RotKind.DeadFile;

    public Task RunAsync(CancellationToken ct) => store.ReconcileDeadFilesAsync(CurrentUnavailableGrace, ct);

    /// <summary>Private (T372 review LOW-1): only <see cref="RunAsync"/> calls this, and Story375's
    /// own facts exercise the grace window exclusively through the real pass's observable behaviour
    /// (an unavailable row older/younger than the grace), never a direct read of this value.</summary>
    TimeSpan CurrentUnavailableGrace => TimeSpan.FromSeconds(
        (double)Math.Max(1, scanOptions.CurrentValue.MissThreshold) * Math.Max(1, libraryOptions.CurrentValue.ScanIntervalSeconds));
}

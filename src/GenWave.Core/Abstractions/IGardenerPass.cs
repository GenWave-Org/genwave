using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// One rot-detection pass the Library Gardener runs every tick (SPEC F153.2; STORY-374; PLAN T372,
/// gh-#529) — framework-free by design (L1), so a pass is unit-testable without a Postgres, an
/// ASP.NET Core host, or any hosting type in scope. <c>GardenerService</c> (the one
/// <see cref="Microsoft.Extensions.Hosting.BackgroundService"/> that drives every registered pass)
/// resolves <c>IEnumerable&lt;IGardenerPass&gt;</c> and runs each in DI registration order, each
/// isolated in its own try/catch: a pass throwing costs the tick one WARN naming
/// <see cref="Kind"/>, never the other passes and never the next tick.
///
/// <para>
/// A pass is a thin orchestrator, never a SQL author — the L2 fence as narrowed at T357 confines
/// Npgsql/Dapper to <c>*Repository</c>-named types inside <c>GenWave.MediaLibrary.Garden</c>, so an
/// implementation (e.g. <c>Garden.DeadFileGardenerPass</c>) reads its own live options and calls
/// straight through to the matching <see cref="IRotFindingStore"/> reconcile method — the set-based
/// open/resolve logic lives in the repository, never re-implemented per row here.
/// </para>
/// </summary>
public interface IGardenerPass
{
    /// <summary>Which <see cref="RotKind"/> this pass reconciles — also the name a failure's WARN
    /// carries (SPEC F153.2, STORY-374 AC5).</summary>
    RotKind Kind { get; }

    /// <summary>Runs one full reconcile for <see cref="Kind"/> — set-based, bounded by whatever the
    /// implementation's own store method bounds (SPEC F153.2: <c>Gardener:BatchSize</c> governs only
    /// the iterative kinds; a predicate-based kind like <see cref="RotKind.DeadFile"/> is a single
    /// two-statement transaction with no batch concept at all).</summary>
    Task RunAsync(CancellationToken ct);
}

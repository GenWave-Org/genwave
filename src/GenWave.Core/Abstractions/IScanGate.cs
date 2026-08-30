using System.Diagnostics.CodeAnalysis;

namespace GenWave.Core.Abstractions;

/// <summary>
/// The scan's own single-flight gate, extracted to a shared seam (SPEC F154.6; STORY-379; PLAN T380,
/// gh-#529) so a file-action executor can hold the SAME gate <c>Scan.ScanService</c>'s own tick uses
/// — a destructive filesystem write and an in-flight scan pass may never overlap. One process-wide
/// implementation (<c>Scan.ScanGate</c>) wraps exactly one mutual-exclusion primitive; every caller
/// through this port shares it.
///
/// <para>
/// <b>Two entry shapes, one gate:</b> <see cref="TryEnter"/> is the scan's own try-enter — it never
/// waits, preserving <c>ScanService.ScanOnceAsync</c>'s existing skip-if-busy semantics byte for byte
/// (its own "Scan already in progress; skipping this tick" log line is unchanged by this
/// extraction). <see cref="EnterAsync"/> is the executor's own bounded wait — a file action is a
/// one-off admin-triggered write, not a periodic tick, so it is willing to queue briefly rather than
/// refuse outright the instant a scan happens to be running.
/// </para>
/// </summary>
public interface IScanGate
{
    /// <summary>
    /// Enters the gate immediately if free, or fails immediately if not — never waits. Returns
    /// <see langword="true"/> with <paramref name="lease"/> set to the held lease (disposing it
    /// releases the gate) when entered; otherwise <see langword="false"/> with
    /// <paramref name="lease"/> <see langword="null"/>.
    /// </summary>
    bool TryEnter([NotNullWhen(true)] out IDisposable? lease);

    /// <summary>
    /// Enters the gate, waiting up to <paramref name="timeout"/>. Returns the held lease (disposing
    /// it releases the gate) once entered, or <see langword="null"/> if <paramref name="timeout"/>
    /// elapses first.
    /// </summary>
    Task<IDisposable?> EnterAsync(TimeSpan timeout, CancellationToken ct);
}

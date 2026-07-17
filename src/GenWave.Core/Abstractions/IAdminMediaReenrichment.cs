using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// Admin re-enrichment scheduling contract (STORY-050, Epic J — SPEC F20).
/// Kept separate from <see cref="IAdminMediaWrite"/> so the write path is not coupled to
/// re-enrichment concerns and test doubles for write-only scenarios do not need to implement
/// schedule methods.
///
/// <para>
/// <b>AC4 note (caller contract):</b> Callers MUST normalize <see cref="ReenrichFields.None"/>
/// to <see cref="ReenrichFields.All"/> before invoking either method. The endpoint layer (L6)
/// owns this normalization; passing <see cref="ReenrichFields.None"/> through to the
/// implementation is a caller bug — the contract does not silently no-op it.
/// </para>
/// </summary>
public interface IAdminMediaReenrichment
{
    /// <summary>
    /// Sentinel-resets the columns selected by <paramref name="fields"/> on the track identified
    /// by <paramref name="id"/> and queues it for re-enrichment. Reaches any existing track
    /// regardless of <paramref name="scope"/> (SPEC F43.3 — scope is a curation filter, not an
    /// access gate).
    /// </summary>
    /// <param name="id">String representation of the media row id.</param>
    /// <param name="fields">
    /// Bit-field selecting which enrichment columns to reset. Must not be
    /// <see cref="ReenrichFields.None"/> — normalize to <see cref="ReenrichFields.All"/> at the
    /// endpoint layer before calling.
    /// </param>
    /// <param name="scope">
    /// Retained for interface-shape stability; no longer used to gate the write (SPEC F43.3).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="ReenrichResult.Scheduled"/> on success;
    /// <see cref="ReenrichResult.NotFound"/> when the id does not exist.
    /// </returns>
    Task<ReenrichResult> ScheduleAsync(
        string id,
        ReenrichFields fields,
        LibraryScope scope,
        CancellationToken ct);

    /// <summary>
    /// Sentinel-resets the columns selected by <paramref name="fields"/> on every track matching
    /// <paramref name="filter"/> within <paramref name="scope"/> and queues them for
    /// re-enrichment. Returns the number of tracks scheduled.
    ///
    /// <para>
    /// Scope-bounding is mandatory: rows outside <paramref name="scope"/> are never touched.
    /// An empty scope short-circuits to 0 without issuing any SQL (default-deny). No filter
    /// value is ever concatenated into SQL — all values are parameterized (same hygiene as F3).
    /// </para>
    /// </summary>
    /// <param name="filter">The same filter criteria used by the admin list view.</param>
    /// <param name="fields">
    /// Bit-field selecting which enrichment columns to reset. Must not be
    /// <see cref="ReenrichFields.None"/> — normalize to <see cref="ReenrichFields.All"/> at the
    /// endpoint layer before calling.
    /// </param>
    /// <param name="scope">Library access scope; empty scope → 0 rows scheduled, no SQL issued.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of tracks whose enrichment columns were reset and queued.</returns>
    Task<int> ScheduleBulkAsync(
        MediaQuery filter,
        ReenrichFields fields,
        LibraryScope scope,
        CancellationToken ct);
}

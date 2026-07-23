using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// Read seam for <c>station.booth_log</c> (SPEC F72.2, STORY-195): the <c>AdminOnly</c> paged feed.
/// Never on any spectator/public surface (F72.4).
/// </summary>
public interface IBoothLogReader
{
    /// <summary>
    /// Newest-first keyset page: rows strictly older than <paramref name="before"/>
    /// (<see langword="null"/> = the newest page), up to <paramref name="take"/> rows.
    /// </summary>
    Task<BoothLogPage> ReadAsync(BoothLogCursor? before, int take, CancellationToken ct);

    /// <summary>
    /// gh-#99 — the stamped catalog media id of booth-log row <paramref name="id"/>:
    /// <see langword="null"/> for a missing row, a non-track row, or a row that predates the
    /// <c>media_id</c> column. The taste-thumb endpoint resolves this first, checks safe-scope
    /// membership on the library connection, and only then lets the accrual write proceed.
    /// </summary>
    Task<long?> GetMediaIdAsync(long id, CancellationToken ct);
}

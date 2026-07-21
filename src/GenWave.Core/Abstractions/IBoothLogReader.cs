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
}

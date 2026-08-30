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

    /// <summary>
    /// SPEC F152.5, STORY-373, PLAN T362 — the Shows page's own "last airing" line: the show
    /// identified by <paramref name="showId"/>'s most recent contiguous run of <c>"track-started"</c>
    /// <c>station.booth_log</c> rows carrying that <c>show_id</c>. "Contiguous run," simply defined
    /// (this task's own call, deliberately no fancier): walking every <c>"track-started"</c> row
    /// (every show, oldest first) in <c>occurred_at</c> order, a run ends the instant either the
    /// <c>show_id</c> changes OR the gap to the next row exceeds three hours — so the LATEST run
    /// naming <paramref name="showId"/> is exactly the block of consecutive rows an operator would
    /// call "that show's last time on air," never a stitched-together sum across separate airings
    /// (e.g. two different days) that happen to share a show id. <see langword="null"/> when
    /// <paramref name="showId"/> has never aired a single <c>"track-started"</c> row — the page reads
    /// that as "no last airing yet," never a fabricated zero.
    ///
    /// ABSTRACT, not a default interface method (T362 review HIGH-2, binding): this seam has exactly
    /// ONE production implementer (<c>GenWave.MediaLibrary.Station.BoothLogRepository</c>) and a
    /// handful of Host.Tests fakes that stand in for it — a DIM's "compiles unchanged, reports null
    /// until a real implementer opts in" convenience is the wrong trade for a Core-internal seam
    /// (never a published NuGet contract, unlike <c>GenWave.Core.Abstractions.IMediaCatalog</c>,
    /// which keeps its own <c>GetEnvelopeCandidateCountAsync</c> DIM specifically because THAT
    /// interface ships to third parties who cannot be forced to recompile against a new abstract
    /// member): a silently-null "last airing" behind an unnoticed missing override is exactly the
    /// kind of bug a compiler error at every implementation site should catch instead. Every fake
    /// implementer across this repo now supplies its own (trivial, null-returning) override.
    /// </summary>
    Task<ShowLastAiring?> GetLastAiringAsync(long showId, CancellationToken ct);
}

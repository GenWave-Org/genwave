using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F33, STORY-109) — the operator taste signal on a catalog row: a clamped up/down vote
/// and a never-play flag, both standalone from curation (F33.7) — bulk eligibility, PATCH,
/// reassignment, and re-enrichment never read or write this state. Every method operates on any
/// catalog row by id with no <see cref="LibraryScope"/> gating (F33.5): rating is a per-row concern,
/// not a rotation-scope one, and safe plays are often out of main scope by default. In-process in
/// v1; a future extraction of the library to its own service only rebinds this interface (in-proc
/// impl → HTTP client) — nothing upstream moves.
/// </summary>
public interface IMediaRating
{
    /// <summary>
    /// Applies one ±1 vote, clamped to <c>[0,100]</c>, atomically in a single statement (SPEC F33.3).
    /// Repeat votes are expected and safe — clamping absorbs any number of clicks at either rail, and
    /// there is nothing to conflict on (no <c>If-Match</c>).
    /// </summary>
    /// <param name="mediaId">String representation of the media row id.</param>
    /// <param name="direction">Whether the vote raises or lowers the score.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<VoteOutcome> VoteAsync(string mediaId, VoteDirection direction, CancellationToken ct);

    /// <summary>
    /// Idempotently sets the never-play flag (SPEC F33.4): setting the same value repeatedly is a
    /// no-op, and last write wins — an explicit set has nothing to clobber, so there is no
    /// <c>If-Match</c> concurrency machinery here.
    /// </summary>
    /// <param name="mediaId">String representation of the media row id.</param>
    /// <param name="neverPlay">The flag's new value.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<NeverPlayOutcome> SetNeverPlayAsync(string mediaId, bool neverPlay, CancellationToken ct);

    /// <summary>
    /// Resolves rating state for a batch of media ids (SPEC F33.2, F33.9). One entry per id that
    /// parses as a plain catalog id; ids that do not (e.g. <c>tts:*</c>) are silently skipped, never
    /// throw. Every parseable id yields an entry — a real <c>library.media_rating</c> row if one
    /// exists, otherwise the ledger default (score 50, never-play false).
    /// </summary>
    /// <param name="mediaIds">String representations of the media row ids to resolve.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<MediaRating>> GetRatingsAsync(IReadOnlyList<string> mediaIds, CancellationToken ct);

    /// <summary>
    /// Bulk equivalent of <see cref="VoteAsync"/> (SPEC F61.1, F61.2, STORY-158): applies one ±1 vote,
    /// clamped to <c>[0,100]</c>, to every <c>library.media</c> row matching <paramref name="filter"/>
    /// within <paramref name="scope"/> — an unrated row is created at the default 50 first, exactly the
    /// per-row lazy-upsert semantics, just fanned out to a matched set in one statement. The matched
    /// set is resolved by the same admin WHERE builder <see cref="IAdminMediaQuery.ListAdminAsync"/> and
    /// <see cref="IAdminMediaWrite.SetEligibilityAsync"/> use, so a bulk vote affects exactly the rows the
    /// operator previewed on browse (F61.1's "one shared WHERE builder"). Writes touch
    /// <c>library.media_rating</c> ONLY — <c>library.media.xmin</c> never bumps (F33.1 stands).
    /// </summary>
    /// <param name="filter">The same filter criteria used by the admin list view.</param>
    /// <param name="direction">Whether the vote raises or lowers every matched row's score.</param>
    /// <param name="scope">
    /// Library access scope — bounded by <c>Station:Scope:LibraryIds</c> by default, or narrowed to a
    /// single library when the caller has already resolved an explicit <c>libraryId</c> in the filter
    /// as the effective scope (F23.3). An empty scope short-circuits to 0, no SQL issued.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of rows whose rating was created or updated.</returns>
    Task<int> BulkVoteAsync(MediaQuery filter, VoteDirection direction, LibraryScope scope, CancellationToken ct);

    /// <summary>
    /// Bulk equivalent of <see cref="SetNeverPlayAsync"/> (SPEC F61.1, F61.2, STORY-158): idempotently
    /// sets the never-play flag to <paramref name="neverPlay"/> on every <c>library.media</c> row
    /// matching <paramref name="filter"/> within <paramref name="scope"/> — repeat sweeps at the same
    /// value are safe no-ops and a later sweep with the opposite value restores every matched row
    /// (never a one-way door). Uses the same shared admin WHERE builder as
    /// <see cref="BulkVoteAsync"/>. Writes touch <c>library.media_rating</c> ONLY.
    /// </summary>
    /// <param name="filter">The same filter criteria used by the admin list view.</param>
    /// <param name="neverPlay">The flag's new value for every matched row.</param>
    /// <param name="scope">Same effective-scope contract as <see cref="BulkVoteAsync"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of rows whose rating was created or updated.</returns>
    Task<int> BulkSetNeverPlayAsync(MediaQuery filter, bool neverPlay, LibraryScope scope, CancellationToken ct);
}

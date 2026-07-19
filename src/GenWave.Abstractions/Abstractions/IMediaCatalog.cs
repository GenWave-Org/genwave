using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// Parameters for a paged, filtered catalog list query (T041).
/// All string filters are case-insensitive substring matches (ILIKE). Limit is caller-clamped
/// before being passed here; the repository also enforces a hard ceiling of 200.
/// <para>
/// <c>Eligible</c> is a tristate: null means "all rows", true means "only eligible",
/// false means "only ineligible". This is the same parameter used by the F3 bulk eligibility
/// endpoint so operators can preview the rows that will be affected.
/// </para>
/// <para>
/// <c>NeverPlay</c> (SPEC F33.10) is deliberately NOT a tristate like <c>Eligible</c>: only
/// <c>true</c> narrows the browse to flagged rows; both <c>null</c> (absent) and explicit
/// <c>false</c> apply no filter. SPEC F33.10 only requires the <c>?never-play=true</c> case, and
/// a track must never become unreachable once flagged (X is not a one-way door) — collapsing
/// <c>false</c> into "no filter" avoids inventing an "only unflagged" query nobody asked for.
/// Browse-only: <see cref="IAdminMediaQuery.ListAdminAsync"/> is the sole consumer — bulk write
/// paths (eligibility, reassignment, re-enrichment) that share this record's WHERE-builder never
/// read this field.
/// </para>
/// <para>
/// <c>Year</c>/<c>Decade</c>/<c>YearMissing</c> (SPEC F49.1) are the three ways <c>GET
/// /api/media</c> narrows by release year; the controller rejects (400) naming more than one of
/// them before this record is even built, so a caller of <see cref="IAdminMediaQuery.ListAdminAsync"/>
/// (or the bulk write paths, which share the same WHERE-builder) can freely combine at most one
/// with the rest of the filter set. <c>Year</c> is an exact match; <c>Decade</c> is the decade's
/// start year and expands to <c>year BETWEEN Decade AND Decade+9</c> — alignment (divisible by 10)
/// is a controller-side 400, not enforced here. <c>YearMissing</c> mirrors <c>NeverPlay</c>: only
/// <c>true</c> narrows to <c>year IS NULL</c>; <c>null</c>/<c>false</c> apply no filter. Unlike
/// <c>NeverPlay</c>, all three ARE folded into <see cref="MediaQuery"/>'s shared WHERE-builder —
/// <c>year</c> is a plain <c>library.media</c> column with no join required, so the bulk write
/// paths pick up the same predicates for free.
/// </para>
/// <para>
/// <c>ArtistExact</c>/<c>AlbumExact</c>/<c>GenresExact</c> (SPEC F52.3, closes gitea-#189) are additive
/// case-insensitive EQUALITY filters (<c>lower(col) = lower(@value)</c>) alongside the shipped ILIKE
/// substring fields (<c>Artist</c>/<c>Genre</c>/<c>Q</c>) — they do not change those fields' semantics.
/// "Queen" as an exact filter MUST NOT match "Queensrÿche"; the controller rejects (400) naming both a
/// field's substring and exact param in one request before this record is built (the F49.1
/// mutual-exclusion precedent), so a caller only ever sets one of a field's two filter shapes.
/// <c>GenresExact</c> is a list because genre curation is naturally multi-value — its entries
/// OR-match (any listed genre, case-insensitively); a <c>null</c> or empty list applies no filter.
/// All three are folded into the <c>MediaLibrary</c> repository's shared WHERE-builder (unlike
/// <c>NeverPlay</c>) so <see cref="IAdminMediaQuery.ListAdminAsync"/> and every bulk write path
/// (<c>SetEligibilityAsync</c>, <c>BulkReassignAsync</c>, <c>ScheduleBulkAsync</c>) inherit them.
/// </para>
/// </summary>
public sealed record MediaQuery(
    string? State = null,
    string? Artist = null,
    string? Genre = null,
    long? LibraryId = null,
    string? Q = null,
    int Page = 1,
    int Limit = 50,
    bool? Eligible = null,
    bool? NeverPlay = null,
    int? Year = null,
    int? Decade = null,
    bool? YearMissing = null,
    string? ArtistExact = null,
    string? AlbumExact = null,
    IReadOnlyList<string>? GenresExact = null);

/// <summary>A paged result set with total count and page count (T041).</summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Total,
    int Pages);

/// <summary>
/// SEAM 2 (PRD §4.2) — the library's query contract. Richer than <see cref="INextItemProvider"/>
/// because future consumers (criteria queries, UIs) need the full catalog. In-process in v1; its
/// eventual remote form is the HTTP query API (PRD §7). When the library extracts to its own process,
/// only the binding of this interface changes (in-proc impl → HTTP client); nothing upstream moves.
/// All methods are scoped: a <see cref="LibraryScope.IsEmpty"/> scope short-circuits to null without
/// touching the database (default-deny). T003 adds per-library WHERE filtering; T009 wires in the
/// real config-bound scope in place of the current transitional hard-coded sentinel.
/// </summary>
public interface IMediaCatalog
{
    /// <summary>
    /// The catalog entry for an id, or null if absent or the scope is empty. A RAW lookup: it returns
    /// the row in whatever state it is in (a not-yet-enriched <c>discovered</c> row carries
    /// default/unmeasurable loudness; an <c>unavailable</c> row is still returned). Callers that need a
    /// playable track use <see cref="GetRandomReadyAsync"/>, which filters to ready + measurable.
    /// </summary>
    Task<MediaReference?> GetByIdAsync(LibraryScope scope, string mediaId, CancellationToken ct);

    /// <summary>
    /// The catalog entry for an id, or null if absent — deliberately NOT scope-filtered (SPEC F66.2).
    /// An aired-fact lookup: the caller (the Host's duration rehydrator) uses this to recover a fact
    /// about a track that has already aired, not to select one — scope is a selection-time concern
    /// that does not apply here. Unlike <see cref="GetByIdAsync"/>, a row is returned regardless of
    /// which library it belongs to.
    /// </summary>
    Task<MediaReference?> GetByIdUnscopedAsync(string mediaId, CancellationToken ct);

    /// <summary>
    /// One random track that is ready to play (enriched and measurable), excluding the given ids so
    /// "random" can avoid recent repeats. Null when nothing is currently ready (cold/empty library) or
    /// the scope is empty.
    /// </summary>
    Task<MediaReference?> GetRandomReadyAsync(LibraryScope scope, IReadOnlyList<string> excludeIds, CancellationToken ct);

    /// <summary>
    /// One track for main rotation (SPEC F41, closes gitea-#210, gitea-#213) — a tiered preference query, not a
    /// hard exclusion. Prefers, most-binding first: (1) an id not in <paramref name="orderedRecentIds"/>;
    /// (2) an artist that does not case-insensitively match any artist among the last
    /// <paramref name="artistSeparation"/> entries of that list (<c>null</c>/blank artists exempt on
    /// both sides); (3) any id over the single most-recent entry; then <c>random()</c>. Both
    /// preferences relax rather than exclude — the returned <see cref="RotationCandidate"/> carries
    /// <see cref="RotationCandidate.RepeatedRecent"/>/<see cref="RotationCandidate.RepeatedArtist"/> so
    /// the caller can log the relaxation (F41.5). Null is returned ONLY when the playable pool
    /// (<see cref="GetRandomReadyAsync"/>'s <c>ready + measurable + eligible + not never_play</c>
    /// predicate, scoped) is empty — never because every playable row happens to be recent (F41.2) — or
    /// when <paramref name="scope"/> is empty (default-deny, no SQL issued).
    /// <para>
    /// <paramref name="orderedRecentIds"/> is the feeder's ring, oldest-first with the most-recent id
    /// LAST; the Orchestrator strips <c>tts:*</c> ids before calling (F12.6 discipline) — any id that
    /// still fails to parse is silently dropped, mirroring <see cref="GetRandomReadyAsync"/>'s
    /// exclude-list parsing. <paramref name="artistSeparation"/> &lt;= 0 disables tier 2.
    /// </para>
    /// <para>
    /// Only the Orchestrator's music selection consumes this method — <see cref="GetRandomReadyAsync"/>
    /// remains the strict seam for <c>/media/random</c> (F8.2) and <c>/internal/safe-track</c> (F21.11);
    /// it is untouched by this method (F41.7).
    /// </para>
    /// </summary>
    Task<RotationCandidate?> GetRotationCandidateAsync(
        LibraryScope scope, IReadOnlyList<string> orderedRecentIds, int artistSeparation, CancellationToken ct);

    /// <summary>
    /// Paged, filtered list of catalog entries scoped to the given libraries (T041). An empty scope
    /// short-circuits to an empty result without touching the database (default-deny).
    /// </summary>
    Task<PagedResult<MediaReference>> ListAsync(LibraryScope scope, MediaQuery query, CancellationToken ct);

    /// <summary>
    /// The <c>GET /api/status</c> aggregate's catalog counts (SPEC F28.6), in one grouped query.
    /// The four state counts are unscoped — catalog health is a library-management concern, not a
    /// rotation-scope one (mirrors <c>GET /api/libraries</c>, F20.1). <see cref="CatalogStatusCounts.Playable"/>
    /// is scoped to <paramref name="safeScope"/> and uses the exact <c>ready + measurable + eligible</c>
    /// predicate <see cref="GetRandomReadyAsync"/> selects on, so it agrees with what
    /// <c>/internal/safe-track</c> would actually be able to serve. An empty <paramref name="safeScope"/>
    /// yields <c>Playable == 0</c> without a special case — the scope predicate matches no rows.
    /// </summary>
    Task<CatalogStatusCounts> GetStatusCountsAsync(LibraryScope safeScope, CancellationToken ct);

    /// <summary>
    /// Distinct, non-NULL, non-blank values of <paramref name="field"/>'s backing column within
    /// <paramref name="scope"/>, one <see cref="FacetValue"/> per case-insensitive group — "Rock" and
    /// "rock" contribute to the same entry rather than two divided-count rows (SPEC F52.1). Ordered by
    /// <see cref="FacetValue.Value"/> case-insensitively. No pagination: the response is bounded by
    /// catalog cardinality at single-operator scale (hundreds of distinct values at 10k tracks).
    /// <para>
    /// Scoping is identical to <see cref="IAdminMediaQuery.ListAdminAsync"/>'s browse scope
    /// (<c>library_id = any(@libraryIds)</c>); an empty <paramref name="scope"/> short-circuits to an
    /// empty list without touching the database (default-deny, SPEC F52.2). Counts include every row
    /// in scope regardless of state/eligibility — they answer "how many rows would this exact filter
    /// touch," which is what a bulk-curation preview needs.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<FacetValue>> GetFacetsAsync(FacetField field, LibraryScope scope, CancellationToken ct);
}

using GenWave.Abstractions.Playout;
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
/// Browse-only: <c>IAdminMediaQuery.ListAdminAsync</c> is the sole consumer — bulk write
/// paths (eligibility, reassignment, re-enrichment) that share this record's WHERE-builder never
/// read this field.
/// </para>
/// <para>
/// <c>Year</c>/<c>Decade</c>/<c>YearMissing</c> (SPEC F49.1) are the three ways <c>GET
/// /api/media</c> narrows by release year; the controller rejects (400) naming more than one of
/// them before this record is even built, so a caller of <c>IAdminMediaQuery.ListAdminAsync</c>
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
/// <c>NeverPlay</c>) so <c>IAdminMediaQuery.ListAdminAsync</c> and every bulk write path
/// (<c>SetEligibilityAsync</c>, <c>BulkReassignAsync</c>, <c>ScheduleBulkAsync</c>) inherit them.
/// </para>
/// <para>
/// <c>MoodsExact</c> (SPEC F86.8, STORY-220) is the mood counterpart to <c>GenresExact</c>, but
/// matches against an ARRAY column (<c>library.media.moods</c>, text[]) rather than a scalar one:
/// a row matches if ANY of its stored moods case-insensitively equals ANY listed term — two ORs
/// composed (across the row's own moods, and across the query's repeated <c>mood-exact</c>
/// values), not one. A <c>null</c> or empty list applies no filter; a <c>null</c>-moods (untagged)
/// row never matches once the filter is active — there is nothing to compare against. Folded into
/// the same shared WHERE-builder as the other exact filters, so it ANDs with them for free.
/// </para>
/// <para>
/// <c>IncludeUnavailable</c> (gh-#113) opts a BROWSE back into <c>unavailable</c> rows: the default
/// catalog view hides them (a shrunk media mount must not bury the live library under hundreds of
/// dead rows), and only <c>true</c> reveals them again. An explicit <c>State</c> filter also
/// disables the hiding — <c>state=unavailable</c> would otherwise always match nothing — which
/// <see cref="HidesUnavailable"/> encodes as the single shared rule. Browse-only, exactly like
/// <c>NeverPlay</c>: <c>IAdminMediaQuery.ListAdminAsync</c> is the sole consumer — the bulk
/// write paths that share this record's WHERE-builder never read this field, so a filtered sweep
/// still reaches unavailable rows the way it always has (the browse/bulk asymmetry is documented
/// on <c>IAdminMediaQuery</c>).
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
    IReadOnlyList<string>? GenresExact = null,
    IReadOnlyList<string>? MoodsExact = null,
    bool? IncludeUnavailable = null)
{
    /// <summary>
    /// True when a browse for this query hides <c>unavailable</c> rows (gh-#113): no explicit
    /// <see cref="State"/> filter is named and <see cref="IncludeUnavailable"/> is not
    /// <c>true</c>. The one shared implementation of the rule — the repository uses it to apply
    /// the <c>state &lt;&gt; 'unavailable'</c> browse predicate and the endpoint uses it to decide
    /// whether a hidden-row count accompanies the page.
    /// </summary>
    public bool HidesUnavailable => State is null && IncludeUnavailable is not true;

    /// <summary>
    /// SPEC F149.5, STORY-368, PLAN T371 — <c>GET /api/media?never-aired=true</c> (mirrors
    /// <see cref="NeverPlay"/>'s own tristate posture: only <c>true</c> narrows; <c>null</c>/<c>false</c>
    /// apply no filter). Body <c>init</c> property, deliberately NOT a positional constructor
    /// parameter — CONTRIBUTING.md L4: this record is positional and published in
    /// <c>GenWave.Abstractions</c>, so a new positional slot would silently reorder every existing
    /// call site's trailing default arguments (the T361 lesson). <c>MediaRepository.BuildAdminWhere</c>'s
    /// shared WHERE-builder restricts the match to PLAYABLE rows only (the same posture
    /// <c>GetRotationHealthAsync</c> counts by) — an unavailable, ineligible, or never-play-flagged
    /// never-aired row is not returned (STORY-368 AC6).
    /// </summary>
    public bool? NeverAired { get; init; }

    /// <summary>
    /// SPEC F149.5, STORY-368, PLAN T371 — <c>GET /api/media?aired-before=&lt;date&gt;</c>: rows whose
    /// <c>media_rotation.last_aired_at</c> falls before midnight UTC of this date. <c>DateOnly</c>
    /// mirrors <c>SpecialsController</c>'s own date-only query convention; the controller parses the
    /// raw <c>yyyy-MM-dd</c> query string itself (400 naming the field on a bad value) before this
    /// record is ever built. Body <c>init</c> property for the same L4 reason <see cref="NeverAired"/>
    /// is — never a positional slot. Restricted to PLAYABLE rows, same as <see cref="NeverAired"/>; a
    /// row with no ledger row at all (never aired) never matches, since a <see langword="null"/>
    /// <c>last_aired_at</c> never satisfies "&lt; @airedBefore" in SQL.
    /// </summary>
    public DateOnly? AiredBefore { get; init; }
}

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
    /// SPEC F158.4 (PLAN T395) — <see cref="GetRandomReadyAsync"/>'s fenced sibling: the SAME "one
    /// random ready row, excluding recent repeats" shape, but structurally invisible to any row
    /// carrying a non-null <c>imaging_kind</c> (a liner, a station id, an ad spot) — the fence
    /// <c>MediaRepository.PlayablePredicate</c> gains this cycle. Backs <c>GET /media/random</c>
    /// specifically: <see cref="GetRandomReadyAsync"/> stays the ONE method behind
    /// <c>GET /internal/safe-track</c>, the F4.4 never-silence floor that must skip this fence by
    /// design (STORY-387 AC4) — the two endpoints cannot share one predicate any more once only one
    /// of them may see it. Null on an empty (post-fence) pool or an empty <paramref name="scope"/>
    /// (default-deny), same contract as <see cref="GetRandomReadyAsync"/>.
    /// <para>
    /// <b>Posture term (PLAN T395 review finding-1, RULED)</b>: carries NO audience-posture
    /// (SPEC F95.4) predicate — DELIBERATELY IDENTICAL to <c>/media/random</c>'s own PRE-T395
    /// behavior, which called <see cref="GetRandomReadyAsync"/> directly and never applied one
    /// either. This method's own fence is the imaging fence (F158.4) alone; the split that created
    /// it must not silently widen or narrow <c>/media/random</c>'s posture behavior as a side
    /// effect. Widening it for real is separate SPEC F95.4 work this member does not do.
    /// </para>
    /// <para>
    /// Default-implemented (not abstract) so this addition to a published MIT contract
    /// (<c>GenWave.Abstractions</c>) stays strictly additive — mirrors
    /// <see cref="GetRandomReadyByImagingKindAsync(LibraryScope,ImagingKind,CancellationToken)"/>'s own
    /// precedent: a pre-F158.4 implementer keeps compiling unchanged, falling back to
    /// <see cref="GetRandomReadyAsync"/>'s own (unfenced) answer until it opts in with a real,
    /// fenced override — the concrete catalog implementation in <c>GenWave.MediaLibrary</c> is the
    /// only production override.
    /// </para>
    /// </summary>
    Task<MediaReference?> GetRandomPlayableAsync(LibraryScope scope, IReadOnlyList<string> excludeIds, CancellationToken ct) =>
        GetRandomReadyAsync(scope, excludeIds, ct);

    /// <summary>
    /// SPEC F158.5 (PLAN T395) — the ads-pool read <c>Ads.LibraryAdSpotSource</c> (PLAN T396) draws
    /// from: one random <c>imaging_kind = 'ad'</c> row that is ready, measurable, eligible, and not
    /// never-play within <paramref name="scope"/> (the operator-named ads library), excluding
    /// <paramref name="excludeIds"/> — the in-memory anti-repeat ring
    /// <c>Station:Ads:AntiRepeatWindow</c> tracks (F158.5's own "the feeder precedent"). Mirrors
    /// <see cref="GetRandomReadyByImagingKindAsync(LibraryScope,ImagingKind,CancellationToken)"/>'s
    /// predicate shape fixed to <see cref="ImagingKind.Ad"/>, plus the same recency exclusion
    /// <see cref="GetRandomReadyAsync"/> already carries. ALSO carries the SAME audience-posture
    /// predicate that method applies (SPEC F95.4/F95.6, PLAN T395 review finding-1, RULED): an ad
    /// read has no dead-air excuse — null ("no spot this break") is this member's own always-legal
    /// answer regardless — so an explicit-flagged ad spot (the LLM sweep, SPEC F95.3, can flag one
    /// like any other authored row) is excluded exactly like every other pool-predicate query, never
    /// carved out. Null on an empty pool or an empty <paramref name="scope"/> (default-deny) —
    /// either way "no spot this break" is <c>IAdSpotSource.GetNextSpotAsync</c>'s own always-legal
    /// answer (F158.1).
    /// <para>
    /// Default-implemented (not abstract), the same additive-contract discipline as every other DIM
    /// on this interface: a pre-F158.5 implementer keeps compiling unchanged, reporting "no pool"
    /// (null) until it opts in with a real override (the concrete catalog implementation in
    /// <c>GenWave.MediaLibrary</c> is the only production override).
    /// </para>
    /// </summary>
    Task<MediaReference?> GetRandomReadyAdSpotAsync(LibraryScope scope, IReadOnlyList<string> excludeIds, CancellationToken ct) =>
        Task.FromResult<MediaReference?>(null);

    /// <summary>
    /// SPEC F110.2 (STORY-301, PLAN T231) — one random ready authored Station Imaging row of
    /// <paramref name="kind"/> (the gh-#149 <c>imaging_kind</c> column), the pool the top-of-hour
    /// <c>StationId</c> drain prefers over its templated TTS fallback whenever it is non-empty
    /// (T232 decides). Mirrors <see cref="GetRandomReadyAsync"/>'s exact playable predicate
    /// (<c>ready + measurable + eligible + not never_play</c>) with one more term ANDed in:
    /// <c>imaging_kind = kind</c>. Null when the pool is empty (no ready row of that kind in scope)
    /// or <paramref name="scope"/> is empty (default-deny) — either way the drain reads it as "no
    /// pool," its own fallback-to-template signal, never an error.
    /// <para>
    /// <c>imaging_kind = @kind</c> is a strict equality test, so it excludes <c>NULL</c> rows exactly
    /// like every other equality predicate in Postgres: an authored row that predates gh-#149 reads
    /// NULL and displays as <see cref="ImagingKind.Liner"/> in the admin UI, but a
    /// <see cref="ImagingKind.Liner"/> query here will NOT match it — only a row explicitly stamped
    /// <c>liner</c> comes back. This has no effect on <see cref="ImagingKind.StationId"/> (F110.2's
    /// only caller so far) since a pre-gh-#149 row was never a station id to begin with.
    /// </para>
    /// <para>
    /// Deliberately carries NO recent-exclusion list, unlike <see cref="GetRandomReadyAsync"/>'s own
    /// <paramref name="scope"/>-sibling <c>excludeIds</c> parameter: idents/jingles are functional
    /// station furniture, not music — repetition across hours is fine (the same F21.11 posture the
    /// safe loop already applies to its own repeats).
    /// </para>
    /// <para>
    /// <paramref name="scope"/> is caller-CHOSEN, not pinned to a fixed library: an authored imaging
    /// row can land in any library an operator names when authoring it
    /// (<c>POST /api/safe-segments</c>'s <c>libraryId</c> is a free per-call choice, not defaulted to
    /// the safe scope) — this method stays scope-parameterized like every other read on this
    /// interface, and lets its caller decide which scope answers "in scope" for this drain.
    /// </para>
    ///
    /// Default-implemented (not abstract) so this addition to a published MIT contract
    /// (<c>GenWave.Abstractions</c>) stays strictly additive — mirrors
    /// <see cref="IActivePersonaAccessor.ResolveCardAsync"/>'s precedent: every pre-F110 implementer
    /// (a test double, or a host built against an older SDK version) keeps compiling unchanged,
    /// reporting "no pool" (null) — which IS the drain's own template-fallback path, not a
    /// degraded/wrong answer — until it opts in with a real override (the concrete catalog
    /// implementation in <c>GenWave.MediaLibrary</c> is the only production override).
    /// </summary>
    Task<MediaReference?> GetRandomReadyByImagingKindAsync(LibraryScope scope, ImagingKind kind, CancellationToken ct) =>
        Task.FromResult<MediaReference?>(null);

    /// <summary>
    /// SPEC F117.1/F117.2 (STORY-309, PLAN T250) — <see cref="GetRandomReadyByImagingKindAsync(LibraryScope,ImagingKind,CancellationToken)"/>'s
    /// show-scoped sibling: a genuinely NEW interface member, never that 3-arg member widened in
    /// place. Widening the published 3-arg signature (even with a trailing optional parameter) would
    /// have been a binary break for any already-compiled caller reaching for the OLD 3-arg overload —
    /// a <see cref="System.MissingMethodException"/> at that call site, since the metadata signature
    /// itself changes — and would have silently orphaned any pre-T250 implementer's own override: an
    /// implementer that only ever overrode the 3-arg shape would, the moment a NEW caller reached for
    /// a show-scoped call, have fallen through to a hardcoded-null default instead of the real (if
    /// show-unaware) answer its own code already knows how to give.
    /// <para>
    /// Default-implemented (not abstract), same additive-contract discipline as every other DIM on
    /// this interface — but this one's default body DELEGATES to the 3-arg member above (dropping
    /// <paramref name="showId"/>) rather than fabricating <see langword="null"/>: a pre-T250
    /// implementer that overrides ONLY the 3-arg shape still answers honestly — its own unscoped pool
    /// — when reached through this NEW shape, rather than reporting a manufactured empty pool. The
    /// ONE production implementer (<c>GenWave.MediaLibrary.Catalog.MediaRepository</c>) overrides BOTH
    /// members explicitly; this default only ever matters for an implementer that has not opted in.
    /// </para>
    /// <para>
    /// <paramref name="showId"/> preference ladder (see <c>MediaRepository</c>'s own concrete override
    /// for the SQL): a row scoped to <paramref name="showId"/> is preferred, a station-wide
    /// (<c>show_id</c> null) row is the fallback, and a row scoped to a DIFFERENT show is never a
    /// candidate at all (F117.1, "scoped means scoped"). <see langword="null"/> means "no show" — the
    /// F110.2-original behavior the 3-arg member above has always given.
    /// </para>
    /// </summary>
    Task<MediaReference?> GetRandomReadyByImagingKindAsync(
        LibraryScope scope, ImagingKind kind, long? showId, CancellationToken ct) =>
        GetRandomReadyByImagingKindAsync(scope, kind, ct);

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
    /// SPEC F81.4/F81.1 — <see cref="GetRotationCandidateAsync"/>'s exact tiered rotation-window /
    /// artist-separation preference logic (F81.2's "envelope filters; the bias ranks" — this method
    /// never replaces rotation, it composes with it), additionally constrained BY CONSTRUCTION to
    /// <paramref name="envelope"/>'s genre allow-list and energy band: a track outside either never
    /// enters the candidate pool, full stop — never a post-filter over a wider fetch.
    /// <paramref name="envelope"/>'s <c>Genres</c> empty admits every genre; a <c>NULL</c> genre
    /// never satisfies a non-empty list. A <c>NULL</c> <c>energy</c> (population-wide percentile
    /// recompute lagging a recent enrichment write, SPEC F80.2) always passes the energy band —
    /// enrichment lag must never silence an otherwise-ready track. Null only when the scope is empty
    /// or the envelope-and-rotation-constrained playable pool is (same never-drains contract as
    /// <see cref="GetRotationCandidateAsync"/>); the degradation ladder that relaxes rotation, then
    /// energy, then genres when the pool is genuinely empty (SPEC F81.6) is the provider's job
    /// (a later task), not this query's.
    ///
    /// Default-implemented (not abstract) so this Q4 addition to a published MIT contract
    /// (<c>GenWave.Abstractions</c>) stays strictly additive — mirrors
    /// <see cref="IActivePersonaAccessor.ResolveCardAsync"/>'s precedent: every pre-F81 implementer
    /// (a test double, or a host built against an older SDK version) keeps compiling unchanged,
    /// falling back to the envelope-blind <see cref="GetRotationCandidateAsync"/> until it opts in
    /// with a real, envelope-aware override (the concrete catalog implementation in
    /// <c>GenWave.MediaLibrary</c> is the only production override).
    /// </summary>
    Task<RotationCandidate?> GetEnvelopeCandidateAsync(
        LibraryScope scope,
        IReadOnlyList<string> orderedRecentIds,
        int artistSeparation,
        SegmentEnvelope envelope,
        CancellationToken ct) =>
        GetRotationCandidateAsync(scope, orderedRecentIds, artistSeparation, ct);

    /// <summary>
    /// SPEC F82.2 (STORY-213, PLAN T64) — <see cref="GetEnvelopeCandidateAsync"/>'s exact
    /// by-construction envelope+rotation-tier filtering, widened to a POOL of up to
    /// <paramref name="limit"/> rows (rather than one) so <c>GenWave.Orchestration.PersonaRanker</c>
    /// has a real candidate set to score — never a wider, unconstrained fetch narrowed afterward in
    /// C# (F81.2 applies to this seam too). Each row additionally carries
    /// <see cref="EnvelopeCandidateRow.Energy"/>/<see cref="EnvelopeCandidateRow.Moods"/>, the two
    /// fields <see cref="MediaReference"/> itself does not surface that the ranker's score/taste-match
    /// formulas need. Null/empty <paramref name="scope"/> short-circuits to an empty pool (default-deny).
    ///
    /// Default-implemented (not abstract) so this Q4 addition to the published MIT contract
    /// (<c>GenWave.Abstractions</c>) stays strictly additive — mirrors <see cref="GetEnvelopeCandidateAsync"/>'s
    /// own precedent: every pre-F82 implementer (a test double, or a host built against an older SDK
    /// version) keeps compiling unchanged, falling back to AT MOST ONE row (via
    /// <see cref="GetEnvelopeCandidateAsync"/>, with a <see langword="null"/>
    /// <see cref="EnvelopeCandidateRow.Energy"/> and empty <see cref="EnvelopeCandidateRow.Moods"/>)
    /// until it opts in with a real, pool-shaped override — the concrete catalog implementation in
    /// <c>GenWave.MediaLibrary</c> is the only production override. <paramref name="limit"/> is
    /// ignored by this fallback; it can never return more than the one row
    /// <see cref="GetEnvelopeCandidateAsync"/> itself would.
    /// </summary>
    async Task<IReadOnlyList<EnvelopeCandidateRow>> GetEnvelopeCandidatePoolAsync(
        LibraryScope scope,
        IReadOnlyList<string> orderedRecentIds,
        int artistSeparation,
        SegmentEnvelope envelope,
        int limit,
        CancellationToken ct)
    {
        var candidate = await GetEnvelopeCandidateAsync(scope, orderedRecentIds, artistSeparation, envelope, ct);
        return candidate is null
            ? []
            : [new EnvelopeCandidateRow(candidate.Media, null, [], candidate.RepeatedRecent, candidate.RepeatedArtist)];
    }

    /// <summary>
    /// SPEC F152.4 (STORY-372, PLAN T361) — the rotation relax ladder's R2 diagnostic read: the
    /// discrete <paramref name="quantile"/>-th percentile of <c>coalesce(play_count, 0)</c> across
    /// <paramref name="envelope"/>'s own genre/energy/explicit-constrained playable pool WITHIN
    /// <paramref name="scope"/> — the rotation predicate itself is deliberately excluded from this
    /// read (a fixed, unconditional look at the whole envelope-matching pool's play-count
    /// distribution, never narrowed by whatever bound R0/R1 already tried) — <c>GenWave.Orchestration.MusicSelectionPolicy</c>'s
    /// own R2 rung then narrows <c>MaxPlays</c> to the result. <see langword="null"/> means "nothing to
    /// compute a percentile over" (an empty scope, or an empty envelope-matching pool) — the ladder
    /// reads that as "skip R2, try R3 instead," never a fabricated <c>MaxPlays: 0</c>.
    ///
    /// Default-implemented (not abstract) so this addition to a published MIT contract
    /// (<c>GenWave.Abstractions</c>) stays strictly additive — mirrors <see cref="GetEnvelopeCandidateAsync"/>'s
    /// own precedent: every pre-F152 implementer (a test double, or a host built against an older SDK
    /// version) keeps compiling unchanged, reporting "nothing to compute" (null) — R2 simply never
    /// fires — until it opts in with a real override (the concrete catalog implementation in
    /// <c>GenWave.MediaLibrary</c> is the only production override).
    /// </summary>
    Task<int?> GetPlayCountQuantileAsync(
        LibraryScope scope, SegmentEnvelope envelope, double quantile, CancellationToken ct) =>
        Task.FromResult<int?>(null);

    /// <summary>
    /// SPEC F152.5 (STORY-373, PLAN T362) — the Shows page's own "live pool size" read: the count of
    /// PLAYABLE rows <paramref name="envelope"/>'s genre/energy/explicit/rotation predicate admits
    /// WITHIN <paramref name="scope"/> — the exact same by-construction filter set
    /// <see cref="GetEnvelopeCandidateAsync"/>/<see cref="GetEnvelopeCandidatePoolAsync"/> already
    /// apply (rotation INCLUDED here, unlike <see cref="GetPlayCountQuantileAsync"/>'s own
    /// deliberately-unconstrained R2 read), just aggregated to a count instead of a candidate row.
    /// <paramref name="envelope"/>'s <see cref="SegmentEnvelope.Rotation"/> is caller-supplied — the
    /// Shows page passes a show's own rotation rule layered onto the station-default envelope, so this
    /// answers "how many tracks would THIS show's rule admit right now," never the station-wide pool.
    /// <see langword="null"/> means "unknown" (an empty <paramref name="scope"/>) — the page renders
    /// that as "unknown," never a fabricated zero.
    ///
    /// Default-implemented (not abstract) so this addition to a published MIT contract
    /// (<c>GenWave.Abstractions</c>) stays strictly additive — mirrors
    /// <see cref="GetPlayCountQuantileAsync"/>'s own precedent one member up: every pre-F152.5
    /// implementer (a test double, or a host built against an older SDK version) keeps compiling
    /// unchanged, reporting "unknown" (null) until it opts in with a real override (the concrete
    /// catalog implementation in <c>GenWave.MediaLibrary</c> is the only production override).
    /// </summary>
    Task<int?> GetEnvelopeCandidateCountAsync(LibraryScope scope, SegmentEnvelope envelope, CancellationToken ct) =>
        Task.FromResult<int?>(null);

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
    /// Scoping is identical to <c>IAdminMediaQuery.ListAdminAsync</c>'s browse scope
    /// (<c>library_id = any(@libraryIds)</c>); an empty <paramref name="scope"/> short-circuits to an
    /// empty list without touching the database (default-deny, SPEC F52.2). Counts include every row
    /// in scope regardless of state/eligibility — they answer "how many rows would this exact filter
    /// touch," which is what a bulk-curation preview needs.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<FacetValue>> GetFacetsAsync(FacetField field, LibraryScope scope, CancellationToken ct);
}

namespace GenWave.Orchestration;

using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Logging;

/// <summary>
/// SPEC F127.2, F127.7, F127.8 (STORY-328, PLAN T285) — casting (the drop-in-neighbor rule),
/// per-show stock (state, not I/O), vend-time staleness, and retire-at-air asset deletion for the
/// crosstalk feature (ARCHITECTURE.md "Crosstalk (F127…)"). This type is deliberately state +
/// decisions, never I/O-heavy: it never resolves a schedule snapshot itself, never times a stock
/// worker's loop (PLAN T286 owns that), never drains an exchange onto the air (PLAN T287 owns
/// that) — every schedule fact it needs arrives as an explicit parameter from a caller who already
/// resolved it, mirroring <c>ScheduleResolver</c>'s own "pure function of (snapshot, wall clock)"
/// posture one seam over.
///
/// <para>
/// <b>Two faces (SPEC F127.8):</b> <see cref="IsShowEnabled"/> answers the SCOPE question (is this
/// show named in <c>Crosstalk:Shows</c> at all — fail-closed empty); <see cref="TryCastPersonas"/>/
/// <see cref="TryCastAsync"/> answer the CASTING question (who is in the booth) — CASTING never
/// consults scope itself: a LATER task's stock-timer loop (T286) already checks
/// <see cref="IsShowEnabled"/> before ever calling either casting method, so generating a cast for a
/// disabled show is merely wasted work, never an air-facing leak. <see cref="TryVend"/> is different
/// (PLAN T285 review F2): it is the LAST gate before a stocked exchange reaches the air, so it
/// enforces scope itself rather than trusting a caller to have re-checked — a show removed from
/// <c>Crosstalk:Shows</c> after its stock already filled stops airing banter on the very next vend,
/// not merely stops refilling.
/// </para>
///
/// <para>
/// <b>Grid adjacency (SPEC F127.2) is a CYCLIC ordering</b> over a caller-supplied
/// <see cref="ScheduleWeekSnapshot"/>: every segment sorted by (day, start-minute), the host
/// block's own immediate successor is "next", its immediate predecessor is "previous" — wrapping
/// modularly, so the LAST block of the week's own next IS the FIRST, and the first's own previous
/// IS the last (PLAN T285 review F1). This is the SAME "the grid repeats every 7 days" semantics
/// <see cref="ScheduleResolver.CyclicDistance"/> already establishes one file over (SPEC F91.1) —
/// this type does not get to invent a second, contradictory answer to "what comes after Sunday" one
/// seam away from the one that answers "what plays next." The sad path SPEC F127.2 actually needs —
/// "no distinct adjacent PERSONA" — stays fully reachable under wraparound: a music-only neighbor, a
/// same-persona neighbor, a single-segment grid (whose own next/previous is itself), or an empty grid
/// (no host to even find) all still resolve to no cast; see <see cref="TryCastPersonas"/>'s own
/// remarks.
/// </para>
///
/// <para>
/// <b>Persona cards (SPEC F127.2) ride the existing <see cref="IPersonaStore"/> seam</b> —
/// reachable directly from this L1 project (<c>GenWave.Orchestration</c> already references
/// <c>GenWave.Core</c>, where <see cref="IPersonaStore"/> lives), the same seam
/// <c>OnAirPersonaAccessor.ResolveCardAsync</c> already reads one member over. No new abstraction
/// was needed for this half of the seam — only <see cref="ICrosstalkScopeProvider"/>
/// (<c>Crosstalk:Shows</c>/<c>EveryNthAiring</c>) is new, mirroring
/// <see cref="IShowPatterCadenceProvider"/>'s own "Orchestration cannot see Host options directly"
/// shape.
/// </para>
///
/// <para>
/// <b>Stock (SPEC F127.7) is an in-memory, per-show list — no schema, restart forgets by design</b>
/// (F125.4's durability posture, the same ruling this epic's own decision log records). Guarded by
/// a plain lock, mirroring <c>ShowFlavorLineGate</c>'s own per-instance lock one seam over: a
/// LATER task's stock-timer loop (T286, generating) and drain (T287, vending) both reach the same
/// instance concurrently. <see cref="Stock"/> refuses (returns <see langword="false"/>) once a show
/// already holds <see cref="StockTargetPerShow"/> exchanges (PLAN T285 review, design note) — the
/// type defends its own named invariant rather than trusting every caller to have checked
/// <see cref="StockCount"/> first; T286 still decides WHEN to generate, this method only ever decides
/// whether a slot exists to receive what it generated.
/// </para>
///
/// <para>
/// <b>Vend-time staleness (SPEC F127.7):</b> <see cref="TryVend"/> re-derives the CURRENT cast from
/// the caller-supplied CURRENT snapshot/host block and compares it against each stocked exchange's
/// own captured <see cref="StockedCrosstalkExchange.Cast"/>. A mismatch (a schedule edit moved the
/// neighbor) discards that exchange — its asset deleted, one Information line logged — and the loop
/// tries the next stocked exchange for the same show; the freed stock slot is what makes T286's own
/// "below target, generate" check trigger again on its next tick, so no separate "restock" signal
/// is needed here. An UNKNOWN host segment — <paramref name="currentHostBlock"/> not part of
/// <paramref name="currentSnapshot"/> at all, e.g. the on-air block is currently a projected special
/// (PLAN T285 review F6) — is a DIFFERENT case from staleness: <see cref="TryVend"/> returns
/// <see langword="null"/> without touching the stock at all, since a host we cannot even locate is
/// uncertainty, not evidence the schedule moved.
/// </para>
///
/// <para>
/// <b>Retire-at-air (SPEC F127.7) is this type's to own</b> (PLAN T284's recorded inheritance:
/// "retire-at-air deletion is the CALLER's — the assembler cleans only its own failure paths").
/// <see cref="TryVend"/> already removes a vended exchange from the stock the instant it hands it
/// out (single-use by construction — it can never be handed out a second time), so
/// <see cref="Retire"/>'s own job is narrower: delete the now-aired asset from disk. Called by a
/// LATER task (T287) once the vended exchange has actually aired.
/// </para>
/// </summary>
public sealed class CrosstalkPlanner(
    IPersonaStore personaStore, ICrosstalkScopeProvider scope, ILogger<CrosstalkPlanner> logger)
{
    /// <summary>SPEC F127.7's own "≤2 ready exchanges per enabled show" — the stock target
    /// <see cref="Stock"/> itself enforces (PLAN T285 review, design note), and the value a LATER
    /// task's stock-timer loop (T286) compares <see cref="StockCount"/> against to decide whether a
    /// show needs generating for.</summary>
    public const int StockTargetPerShow = 2;

    readonly object gate = new();
    readonly Dictionary<string, List<StockedCrosstalkExchange>> stock = new(StringComparer.OrdinalIgnoreCase);

    // ── Eligibility face (SPEC F127.8) ─────────────────────────────────────────────────────────

    /// <summary>
    /// Is <paramref name="showSlug"/> named in the live <c>Crosstalk:Shows</c> list? Fail-closed:
    /// a null/blank slug, or an EMPTY <see cref="ICrosstalkScopeProvider.EnabledShows"/> list,
    /// always answers <see langword="false"/> — SPEC F127.8's "empty means the feature is off"
    /// ruling, so an upgrade never changes a station's sound until an operator explicitly opts a
    /// show in. Matched case-insensitively against <c>ShowSummary.Slug</c> (PLAN T285 review F4 —
    /// SLUG, not the mutable, non-unique display name a rename could silently orphan).
    /// </summary>
    public bool IsShowEnabled(string? showSlug)
    {
        if (string.IsNullOrWhiteSpace(showSlug))
            return false;

        var enabledShows = scope.EnabledShows;
        for (var i = 0; i < enabledShows.Count; i++)
        {
            if (string.Equals(enabledShows[i], showSlug, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    // ── Casting face (SPEC F127.2) ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Casts the drop-in neighbor for <paramref name="hostBlock"/> from grid adjacency: the NEXT
    /// block's persona when it exists and differs from the host, else the PREVIOUS block's persona
    /// under the same condition, else <see langword="null"/> — no exchange is castable for this
    /// airing (SPEC F127.2's sad path: adjacent blocks sharing the host persona, or no adjacent
    /// persona at all). A host block carrying no persona of its own is never castable either — there
    /// is no host voice for a neighbor to react to.
    /// </summary>
    public static CrosstalkCast? TryCastPersonas(ScheduleSegment hostBlock, ScheduleWeekSnapshot snapshot)
    {
        if (hostBlock.PersonaId is not { } hostPersonaId)
            return null;

        var (next, previous) = FindNeighbors(hostBlock, snapshot);

        if (next?.PersonaId is { } nextPersonaId && nextPersonaId != hostPersonaId)
            return new CrosstalkCast(hostPersonaId, nextPersonaId);

        if (previous?.PersonaId is { } previousPersonaId && previousPersonaId != hostPersonaId)
            return new CrosstalkCast(hostPersonaId, previousPersonaId);

        return null;
    }

    /// <summary>
    /// <see cref="TryCastPersonas"/> plus the resolved <see cref="PersonaCard"/>s a LATER task's
    /// stock-timer loop needs to actually generate (<c>GenWave.Tts.CrosstalkExchangeRequest</c>
    /// takes cards, not ids). Degrades to <see langword="null"/> — one Information line, never a
    /// throw — when either persona's card is missing (a deleted-out-of-band persona, or one that
    /// never carried a card): the same "a discard here is discipline, not an outage" posture every
    /// other crosstalk stage in this epic already follows.
    /// </summary>
    public async Task<CrosstalkCastResult?> TryCastAsync(
        ScheduleSegment hostBlock, ScheduleWeekSnapshot snapshot, CancellationToken ct)
    {
        if (TryCastPersonas(hostBlock, snapshot) is not { } cast)
            return null;

        var hostCard = await personaStore.GetCardByIdAsync(cast.HostPersonaId, ct);
        var neighborCard = await personaStore.GetCardByIdAsync(cast.NeighborPersonaId, ct);

        if (hostCard is null || neighborCard is null)
        {
            logger.LogInformation(
                "Crosstalk cast (host={HostPersonaId}, neighbor={NeighborPersonaId}) skipped — a persona card is missing",
                cast.HostPersonaId, cast.NeighborPersonaId);
            return null;
        }

        return new CrosstalkCastResult(cast, hostCard, neighborCard);
    }

    /// <summary>The segment immediately after and immediately before <paramref name="host"/> in a
    /// CYCLIC ordering of <paramref name="snapshot"/>'s whole grid by (day, start-minute) — see this
    /// type's own remarks for why cyclic, matching <c>ScheduleResolver.CyclicDistance</c>'s SPEC
    /// F91.1 "the grid repeats every 7 days" semantics. <paramref name="host"/> is located by
    /// <see cref="ScheduleSegment.Id"/> (PLAN T285 review F6), NOT (day, start-minute): a projected
    /// special (<c>ScheduleResolver.ProjectSpecial</c>) can carry a (day, start-minute) that
    /// coincidentally collides with an UNRELATED weekly block elsewhere in the grid — its own id is
    /// always negated specifically to stay disjoint from every real <c>segment_schedule.id</c>, so
    /// id is the match that can never mis-resolve to the wrong REAL block. A null-Id host (an
    /// unpersisted projected special <c>ScheduleResolver.ProjectSpecial</c> yields with no negated id
    /// assigned yet) is its OWN exception: <c>Id == Id</c> null-matches the first null-Id segment in
    /// the grid, mis-resolving an unrelated block as this host's neighbor — so a null host id is
    /// guarded explicitly and returns "not found" rather than ever reaching the id comparison at all.
    /// <c>(null, null)</c> when <paramref name="host"/>'s id is null OR is not found in
    /// <paramref name="snapshot"/> — which, under this cyclic scheme, can ONLY mean "not found" (a
    /// found host always has a next/previous, even a single-segment grid's own self-reference) — the
    /// caller-visible signal <see cref="TryVend"/>'s own remarks use to tell "unknown host" apart from
    /// genuine staleness.</summary>
    static (ScheduleSegment? Next, ScheduleSegment? Previous) FindNeighbors(
        ScheduleSegment host, ScheduleWeekSnapshot snapshot)
    {
        if (host.Id is not { } hostId)
            return (null, null);

        var ordered = snapshot.Segments.OrderBy(WeeklyMinute).ToList();
        var index = ordered.FindIndex(s => s.Id == hostId);
        if (index < 0)
            return (null, null);

        var count = ordered.Count;
        var next = ordered[(index + 1) % count];
        var previous = ordered[(index - 1 + count) % count];
        return (next, previous);
    }

    static int WeeklyMinute(ScheduleSegment segment) => (int)segment.Day * 1440 + segment.StartMinute;

    // ── Stock face (SPEC F127.7) ───────────────────────────────────────────────────────────────

    /// <summary>Adds <paramref name="exchange"/> to its own show's stock (SPEC F127.7), UNLESS that
    /// show already holds <see cref="StockTargetPerShow"/> exchanges — refuses (returns
    /// <see langword="false"/>, <paramref name="exchange"/> is NOT added) rather than growing an
    /// unbounded queue (PLAN T285 review, design note: this type defends its own named invariant).
    /// The caller (a LATER task's stock-timer loop) still decides WHEN to generate — checking
    /// <see cref="StockCount"/> against <see cref="StockTargetPerShow"/> before spending the work to
    /// build <paramref name="exchange"/> in the first place remains its job; this method is only the
    /// last-line guard against actually exceeding the target.</summary>
    public bool Stock(StockedCrosstalkExchange exchange)
    {
        lock (gate)
        {
            if (!stock.TryGetValue(exchange.ShowSlug, out var list))
            {
                list = [];
                stock[exchange.ShowSlug] = list;
            }

            if (list.Count >= StockTargetPerShow)
                return false;

            list.Add(exchange);
            return true;
        }
    }

    /// <summary>How many ready exchanges <paramref name="showSlug"/> currently holds in stock — the
    /// value a LATER task's stock-timer loop compares against <see cref="StockTargetPerShow"/>.</summary>
    public int StockCount(string showSlug)
    {
        lock (gate)
        {
            return stock.TryGetValue(showSlug, out var list) ? list.Count : 0;
        }
    }

    /// <summary>
    /// Vends the next fresh exchange stocked for <paramref name="showSlug"/>, or
    /// <see langword="null"/> when none is available. Fail-closed on scope FIRST (PLAN T285 review
    /// F2): a show no longer named in <c>Crosstalk:Shows</c> never vends, even with exchanges still
    /// sitting in stock (they age out — a LATER task, T286, stops refilling a disabled show, but
    /// this is the gate that stops one already-stocked from airing). Next, an UNKNOWN host segment
    /// (PLAN T285 review F6 — <paramref name="currentHostBlock"/>'s id is null, or is not part of
    /// <paramref name="currentSnapshot"/> at all, e.g. the on-air block is currently a projected
    /// special that has not been persisted) returns <see langword="null"/> WITHOUT touching the stock
    /// at all — uncertainty is not evidence of staleness, and a null host id must never fall through
    /// to null-matching the first null-Id segment in the snapshot. Otherwise walks the show's stock in
    /// FIFO order, discarding — deleting the asset,
    /// logging one Information line — every exchange whose captured
    /// <see cref="StockedCrosstalkExchange.Cast"/> no longer matches the CURRENT grid adjacency
    /// (re-derived from <paramref name="currentHostBlock"/>/<paramref name="currentSnapshot"/> via
    /// <see cref="TryCastPersonas"/>, SPEC F127.7's own staleness rule) until it finds a fresh one or
    /// the stock empties. A vended exchange is removed from the stock the instant it is returned —
    /// single-use by construction, it can never be handed out a second time (SPEC F127.7's "airs once"
    /// ruling); <see cref="Retire"/> is the separate, LATER call that deletes its asset once it has
    /// actually aired. Every discard's asset-delete + log happens OUTSIDE the lock (PLAN T285 review
    /// F7) — only the list mutation itself needs to be synchronized; disk I/O and logging never do.
    /// </summary>
    public StockedCrosstalkExchange? TryVend(
        string showSlug, ScheduleSegment currentHostBlock, ScheduleWeekSnapshot currentSnapshot)
    {
        if (!IsShowEnabled(showSlug))
            return null;

        if (currentHostBlock.Id is not { } currentHostId)
            return null;

        if (!currentSnapshot.Segments.Any(s => s.Id == currentHostId))
            return null;

        var currentCast = TryCastPersonas(currentHostBlock, currentSnapshot);

        StockedCrosstalkExchange? fresh = null;
        var discarded = new List<StockedCrosstalkExchange>();

        lock (gate)
        {
            if (stock.TryGetValue(showSlug, out var list))
            {
                while (list.Count > 0)
                {
                    var candidate = list[0];
                    list.RemoveAt(0);

                    if (currentCast is not null && candidate.Cast == currentCast)
                    {
                        fresh = candidate;
                        break;
                    }

                    discarded.Add(candidate);
                }
            }
        }

        foreach (var candidate in discarded)
        {
            logger.LogInformation(
                "Crosstalk exchange for '{Show}' discarded at vend — cast (host={HostPersonaId}, " +
                "neighbor={NeighborPersonaId}) no longer matches the current grid adjacency",
                LogSanitize.Strip(showSlug), candidate.Cast.HostPersonaId, candidate.Cast.NeighborPersonaId);
            DeleteAssetBestEffort(candidate.AssetPath);
        }

        return fresh;
    }

    /// <summary>Retires <paramref name="exchange"/> after it has aired (SPEC F127.7): deletes its
    /// asset from disk. This is the deletion PLAN T284's own inheritance notes name as the caller's
    /// to own — <c>CrosstalkAssembler</c> cleans only its own failure paths, never a successfully
    /// assembled asset that goes on to air. Best-effort, mirroring every other asset cleanup in this
    /// epic (<c>CrosstalkAssembler.DeleteIfExists</c>'s own remarks) — a locked/already-gone file is
    /// a secondary concern, never worth masking the fact that this exchange has aired. The logged
    /// outcome states what actually happened (PLAN T285 review F8) — deleted, already absent, or a
    /// failed delete — rather than unconditionally claiming "asset deleted" regardless of what
    /// <see cref="DeleteAssetBestEffort"/> actually observed.</summary>
    public void Retire(StockedCrosstalkExchange exchange)
    {
        var outcome = DeleteAssetBestEffort(exchange.AssetPath);
        var outcomeText = outcome switch
        {
            AssetDeleteOutcome.Deleted => "asset deleted",
            AssetDeleteOutcome.AlreadyAbsent => "asset already absent",
            AssetDeleteOutcome.Failed => "asset delete failed",
            _ => "asset delete outcome unknown",
        };

        logger.LogInformation(
            "Crosstalk exchange for '{Show}' retired after airing — {Outcome} ({Path})",
            LogSanitize.Strip(exchange.ShowSlug), outcomeText, LogSanitize.Strip(exchange.AssetPath));
    }

    /// <summary>The three outcomes <see cref="DeleteAssetBestEffort"/> can observe — what
    /// <see cref="Retire"/>'s own log line reports truthfully (PLAN T285 review F8) instead of
    /// unconditionally claiming a deletion happened.</summary>
    enum AssetDeleteOutcome { Deleted, AlreadyAbsent, Failed }

    static AssetDeleteOutcome DeleteAssetBestEffort(string path)
    {
        try
        {
            if (!File.Exists(path))
                return AssetDeleteOutcome.AlreadyAbsent;

            File.Delete(path);
            return AssetDeleteOutcome.Deleted;
        }
        catch (IOException)
        {
            // Best-effort cleanup — mirrors CrosstalkAssembler.DeleteIfExists's own identical
            // precedent: a locked/undeletable file is a secondary concern.
            return AssetDeleteOutcome.Failed;
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup — see the IOException arm's own remarks.
            return AssetDeleteOutcome.Failed;
        }
    }
}

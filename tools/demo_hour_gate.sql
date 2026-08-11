-- demo_hour_gate.sql — the F113.2 demo-hour instrument (SPEC F113, STORY-304, PLAN T220).
--
-- MANUAL gate, not CI (run at epic close-out, the T150 pattern): proves one hour of broadcast
-- produced the full narrative mix — at least one StationId ident, at least one ContextSegment
-- (weather/history, once the F110/F111 context epic ships its own SegmentKind token), and at least
-- one further non-music kind — counted from station.booth_log's air-time-only `segment_kind` column
-- ALONE, never a substring scan over `summary` prose. That distinction is the entire point of F113.1: a
-- budget-dropped render logs `patter-aired` at RENDER time and may never actually air, while a
-- `track-started` row (and its `segment_kind` stamp) is written only at the genuine AIR-time instant
-- (PlayoutFeeder's observed advance) — so counting from `segment_kind` can never mistake a dropped
-- piece for one that aired.
--
-- Every music row (segment_kind IS NULL) is excluded from the kind tally on purpose — this gate
-- measures presentation variety around the music, not the music itself.
--
-- "Zero dead-air incidents in the same window" (F113.2) is NOT part of this query: booth_log has no
-- silence/outage table to count from (that's an engine/Icecast uptime concern, not a broadcast
-- narrative one) — verify it separately, e.g. via tools/onair_gate.sh or the Loki fleet-error
-- baseline, for the same window this query reports.
--
-- Run against the running stack's db container (production topology, compose.yaml's `db` service).
-- `-f tools/demo_hour_gate.sql` resolves INSIDE the container, where tools/ is not mounted — pipe the
-- file over stdin instead:
--   docker compose exec -T db psql -U genwave -d genwave < tools/demo_hour_gate.sql
--
-- Narrow to a specific broadcast window by adding a `where occurred_at >= ... and occurred_at < ...`
-- predicate inside the hourly_kind_counts CTE below — that shape rides station.booth_log's existing
-- booth_log_paging index (occurred_at desc, id desc) for the scan; the full-table form here is fine
-- at hobby-station row counts (SPEC F72.3's 14-day retention keeps the table small).

set role station_svc;
set search_path = station;

with hourly_kind_counts as (
    select
        date_trunc('hour', occurred_at) as broadcast_hour,
        segment_kind,
        count(*)                        as row_count
    from station.booth_log
    where kind = 'track-started'
    group by date_trunc('hour', occurred_at), segment_kind
)
select
    broadcast_hour,
    bool_or(segment_kind = 'StationId')                                              as has_station_id,
    bool_or(segment_kind = 'ContextSegment')                                         as has_context_segment,
    bool_or(segment_kind is not null and segment_kind not in ('StationId', 'ContextSegment'))
                                                                                       as has_other_non_music_kind,
    sum(row_count) filter (where segment_kind is null)                               as music_row_count,
    sum(row_count) filter (where segment_kind is not null)                           as kinded_row_count
from hourly_kind_counts
group by broadcast_hour
having
    bool_or(segment_kind = 'StationId')
    and bool_or(segment_kind = 'ContextSegment')
    and bool_or(segment_kind is not null and segment_kind not in ('StationId', 'ContextSegment'))
order by broadcast_hour;

-- ============================================================================
-- F121.2 extension (SPEC F121, STORY-310, PLAN T242) — show observability.
--
-- The query above is BYTE-IDENTICAL to the F113.2 shape (F121.2c: zero regression to the existing
-- counts) — both halves below are ADDITIONAL statements in the same batch, reading
-- `station.booth_log.show_id` (F121.1's air-time stamp) ALONE, never `station.segment_schedule`: a
-- grid repaint after the window closes can never change what already aired.
--
-- MEASURED (EXPLAIN ANALYZE, 60k track-started rows, ~120 shows) — the two halves do NOT share one
-- access pattern:
--   * Half (a): the planner DOES walk `booth_log_paging` — an Index Scan Backward feeds the
--     `track_rows`/`show_transitions` window functions (they need `occurred_at`-ordered input, and
--     the index already provides it), and the correlated `exists` subquery's own
--     `occurred_at <= transition_at` bound is itself an index condition (Bitmap Index/Heap Scan on the
--     same index) run once per transition. Measured ~590ms at 60k rows / ~120 transitions — dominated
--     by that once-per-transition subquery cost (bounded by transition COUNT, not row count) and JIT
--     compilation, not by an unindexed scan.
--   * Half (b): `hourly_show_occupancy` groups by `date_trunc('hour', occurred_at)`, an expression the
--     index cannot serve — the planner does a Seq Scan on `station.booth_log` feeding an explicit
--     Sort. Measured ~34ms at 60k rows.
-- Both bounded by SPEC F72.3's 14-day retention (this table never grows past a hobby-station's few
-- weeks of rows) — neither number, nor a new index, is a concern for a manual close-out query at that
-- scale.
--
-- Narrowing to a specific window: half (a)'s `where` predicate MUST go on the FINAL select (filtering
-- `transition_at`), never inside `track_rows`/`show_transitions` — those two CTEs must always see the
-- table's FULL history so `prev_show_id`/`prev_transition_at` stay correct at the window's own edges;
-- narrowing the source rows would make the first transition inside the window look like it has no
-- predecessor (exactly the unbounded-below shape this rewrite exists to close). Half (b) has no such
-- constraint — its CTE groups each hour independently with no cross-row window function — so its
-- predicate goes inside `hourly_show_occupancy`'s `where`, the same place `hourly_kind_counts` above
-- takes one.
--
-- Run the same way as above (piped over stdin); psql prints one result table per statement in the
-- batch, in order — this file now prints three: the F113.2 hourly mix, half (a), then half (b).
-- ============================================================================

-- Half (a): every SHOW TRANSITION in the window has a show-stamped `SignOn` row.
--
-- A transition is the first `track-started` row of a run of consecutive same-show rows — the moment
-- a new show's own airings actually start (`show_id` distinct from the immediately preceding
-- track-started row's `show_id`, and this row itself names a show). That show is COVERED when a
-- `SignOn` row (kind='track-started', segment_kind='SignOn') stamped with the SAME `show_id` aired
-- STRICTLY AFTER the immediately PRECEDING transition (any show) and at or before THIS transition's
-- own instant — ceremony fires before the incoming show's first track, never after (SPEC F116.2), so
-- in the ordinary case the SignOn row IS this transition's own row (occurred_at equal).
--
-- The lower bound is load-bearing, not cosmetic: an `exists` with only the upper bound
-- (`sign_on.occurred_at <= transition_at`) is satisfied by ANY historical SignOn ever stamped for that
-- show_id, no matter how long ago — a RECURRING show's second (and every later) airing then
-- false-passes against a SignOn from an earlier, unrelated day, since that show never re-signs-on and
-- its own genuinely-missing-today SignOn is masked by the stale match. Proven on a seeded probe (show
-- airs day 1 with a SignOn, then airs again day 2 with none): the old query rendered day 2 `true`; this
-- one renders `f`. `show_transitions_windowed.prev_transition_at` — a `lag()` over the TRANSITIONS
-- themselves, not over raw booth_log rows — carries that lower edge, bounding the match to the
-- transition's own run. It is NULL only for the very first transition this database has ever recorded,
-- where there is by definition no earlier data to false-match against, so the bound is correctly open
-- there alone. `has_show_stamped_sign_on = false` on any row here is the gate failing this half.
with track_rows as (
    select
        occurred_at,
        segment_kind,
        show_id,
        lag(show_id) over (order by occurred_at, id) as prev_show_id
    from station.booth_log
    where kind = 'track-started'
),
show_transitions as (
    select occurred_at, show_id
    from track_rows
    where show_id is not null
      and show_id is distinct from prev_show_id
),
show_transitions_windowed as (
    select
        occurred_at                                  as transition_at,
        show_id,
        lag(occurred_at) over (order by occurred_at)  as prev_transition_at
    from show_transitions
)
select
    transition_at,
    show_id,
    exists (
        select 1
        from station.booth_log sign_on
        where sign_on.kind = 'track-started'
          and sign_on.segment_kind = 'SignOn'
          and sign_on.show_id = show_transitions_windowed.show_id
          and sign_on.occurred_at <= show_transitions_windowed.transition_at
          and (
                show_transitions_windowed.prev_transition_at is null
                or sign_on.occurred_at > show_transitions_windowed.prev_transition_at
              )
    ) as has_show_stamped_sign_on
from show_transitions_windowed
order by transition_at;

-- Half (b): every FULL HOUR inside a show has >= 1 show-stamped `StationId` row.
--
-- "Full hour inside a show" is read from booth_log's own air-time stamp alone (never
-- segment_schedule): an hour whose `track-started` rows are ALL stamped with the SAME `show_id` — no
-- showless row, no show change — within that hour. An hour with no track-started rows at all is
-- structurally absent from this result (nothing to group), the same "silence isn't this gate's
-- concern" posture the F113.2 header above already states.
--
-- `bool_or` returns NULL, not `false`, when every input row's own predicate evaluates to NULL rather
-- than false — exactly what an all-music show hour does, since `segment_kind = 'StationId'` is itself
-- NULL (SQL three-valued logic, not false) on every row where `segment_kind` is NULL. That is the EXACT
-- failure case this half exists to catch, so a bare `bool_or` renders it as a blank cell instead of the
-- `f` a human running this manually needs to see — indistinguishable from "not applicable" rather than
-- "gate failed here". `coalesce(..., false)` closes that gap. `has_show_stamped_station_id = false` on
-- any row here is the gate failing this half.
with hourly_show_occupancy as (
    select
        date_trunc('hour', occurred_at)                                     as broadcast_hour,
        count(*) filter (where show_id is null)                             as showless_row_count,
        count(distinct show_id) filter (where show_id is not null)          as distinct_show_count,
        min(show_id)                                                        as show_id,
        coalesce(bool_or(segment_kind = 'StationId' and show_id is not null), false)
                                                                             as has_show_stamped_station_id
    from station.booth_log
    where kind = 'track-started'
    group by date_trunc('hour', occurred_at)
)
select broadcast_hour, show_id, has_show_stamped_station_id
from hourly_show_occupancy
where showless_row_count = 0
  and distinct_show_count = 1
order by broadcast_hour;

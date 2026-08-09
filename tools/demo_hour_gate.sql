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

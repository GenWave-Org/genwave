#!/bin/bash
# 44-near-duplicates-imaging-fence-migration.sh — idempotent in-place upgrade for existing DBs.
# T395 review carry-forward, closed at PLAN T406 (SPEC F158.4). db/41's `find_near_duplicates`
# SQL function hand-copies MediaRepository.PlayablePredicate's text as it stood at T354 — it never
# picked up the "and imaging_kind is null" term T395 added to the REAL predicate (SPEC F158.4, the
# rotation fence). A SQL function cannot reference the C# constant, so its own copy needed its own
# edit; T395 deliberately left it (Gardener near-duplicate detection is housekeeping, not a
# selection-path leak, so it was out of that task's scope) — this script is the follow-up.
#
# Net effect: today, an authored imaging row (a liner, a station id, an ad spot) can still surface
# in a near-duplicate finding even though it's already structurally invisible to every real
# selection path (rotation, requests, /media/random). After this migration it's excluded from
# `find_near_duplicates` too — Gardener housekeeping and the rotation fence agree on what counts as
# "real" playable content.
#
# `create or replace function` is naturally idempotent (no existence guard needed, unlike `create
# type`/`add constraint`, which have no "if not exists" form) — running this script twice replaces
# the function with byte-identical text both times. Fresh-init mirror: db/01-library.sh's own
# `find_near_duplicates` gains the identical term (see that script's own remarks) — a box that
# installs clean never sees this numbered script, only 01+06 via docker-entrypoint-initdb.d (the
# gh-#618 lesson every migration in this directory repeats).
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role library_svc;
	set search_path = library;

	-- find_near_duplicates (SPEC F153.5/F158.4): identical to db/41's own version except the
	-- `playable` CTE's where clause gains "and m.imaging_kind is null" — the same fence
	-- MediaRepository.PlayablePredicate carries in C#, closing the PLAN T395 carry-forward.
	create or replace function library.find_near_duplicates(tolerance_ms int)
	returns table (media_id bigint, group_key text, title_variant text)
	language plpgsql
	stable
	security invoker
	set search_path = library, pg_temp
	as $$
	begin
	  return query
	    with playable as (
	      select m.id, m.artist_key, m.title_key, coalesce(m.title_variant, '') as variant,
	             m.title_variant, m.duration_ms
	      from library.media m
	      left join library.media_rating r on r.media_id = m.id
	      where m.state = 'ready' and m.measurable and m.eligible and not coalesce(r.never_play, false)
	        and m.imaging_kind is null
	        and m.artist_key is not null and m.title_key is not null and m.duration_ms is not null
	    ),
	    anchored as (
	      select p.*,
	             min(p.duration_ms) over (partition by p.artist_key, p.title_key, p.variant) as group_min_duration_ms
	      from playable p
	    ),
	    qualifying as (
	      select * from anchored where duration_ms - group_min_duration_ms <= tolerance_ms
	    ),
	    grouped as (
	      select q.*, count(*) over (partition by q.artist_key, q.title_key, q.variant) as group_size
	      from qualifying q
	    )
	    select g.id as media_id,
	           g.artist_key || '|' || g.title_key || '|' || g.variant as group_key,
	           g.title_variant as title_variant
	    from grouped g
	    where g.group_size > 1;
	end;
	$$;
	SQL

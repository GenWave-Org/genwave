#!/bin/bash
# 27-segment-schedule-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds station.segment_schedule — introduced in the Format Clock epoch (SPEC F91.1, F91.6; STORY-240,
# STORY-242; PLAN T118): the weekly grid that replaces the single owner-toggled
# Station:Persona:ActiveId. Mirrors 06-station-settings-migration.sh's own copy of this table (see
# that script's remarks for the full column-by-column rationale) — this file exists so an EXISTING
# database (one that ran db/06 before this release) picks the table up too, and additionally carries
# the one-time F91.6 seed-and-delete data migration a fresh install never needs.
#
# btree_gist: the EXCLUDE constraint below needs a GiST opclass for plain integer equality
# (day_of_week) — int4range's own overlap opclass is already built into core Postgres, but bare
# integer equality is not, without this extension. Installed here too (not only in db/06) so this
# script stands on its own against a database that somehow never picked up db/06's copy.
#
# F91.6 SEED-AND-DELETE: reads the legacy station.settings key 'Station:Persona:ActiveId'. If it holds
# a positive id that still names a real station.persona row, and the grid is currently empty, this
# seeds seven all-day rows (day 0-6, the full 0-1440 range, NULL envelope) for that persona — the
# schedule-shaped statement of "this persona was the only DJ, all day, every day" the old single-owner
# toggle always meant. Either way (seeded or not), the settings key row is then deleted:
# Station:Persona:ActiveId leaves the settings surface entirely (F91.5) — the resolver built in a
# later task (T119/T120) reads the grid, never this key again.
#
# IDEMPOTENCY: the seed step's own guard is "the grid is still empty AND the legacy key row still
# exists" — both conditions together, not just one. The key deletion below is what makes a second run
# naturally inert (the key is gone, so the seed guard's second half already fails), but the guard is
# written explicitly rather than relying on that alone: if some other process ever populated the grid
# before this script's first run reached the key deletion (a partial/interrupted first run, or a
# concurrent write), a second run must still refuse to seed a SECOND set of seven rows on top. The seed
# insert and the key deletion both run inside ONE PL/pgSQL DO block — a single top-level statement, and
# therefore Postgres's own implicit one-statement transaction — so a run either does both or neither,
# never a seeded grid with the old key still present or vice versa.
#
# Safe to run multiple times (CREATE TABLE/EXTENSION IF NOT EXISTS; the seed logic's own explicit
# idempotency guard, described above). Run this script once against any DB initialised before
# 06-station-settings-migration.sh received station.segment_schedule.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	-- Still connected as the bootstrap superuser here (before SET ROLE below) — mirrors db/06's own
	-- ordering for the identical CREATE EXTENSION statement.
	CREATE EXTENSION IF NOT EXISTS btree_gist;

	set role station_svc;
	set search_path = station;

	create table if not exists station.segment_schedule (
	  id           serial      primary key,
	  day_of_week  int         not null check (day_of_week between 0 and 6),
	  start_minute int         not null check (start_minute % 30 = 0 and start_minute between 0 and 1410),
	  end_minute   int         not null check (end_minute   % 30 = 0 and end_minute   between 30 and 1440),
	  persona_id   int         references station.persona (id) on delete restrict,
	  genres       text[],
	  energy_min   double precision,
	  energy_max   double precision,
	  created_at   timestamptz not null default now(),
	  updated_at   timestamptz not null default now(),
	  check (end_minute > start_minute),
	  exclude using gist (day_of_week with =, int4range(start_minute, end_minute) with &&)
	);

	do $$
	declare
	  active_persona_id bigint;
	begin
	  -- Explicit double guard (see this script's own header remarks): only seed when NOTHING has been
	  -- painted yet AND the legacy key row is still there to migrate from.
	  if not exists (select 1 from station.segment_schedule)
	     and exists (select 1 from station.settings where key = 'Station:Persona:ActiveId')
	  then
	    select (value #>> '{}')::bigint
	      into active_persona_id
	      from station.settings
	      where key = 'Station:Persona:ActiveId';

	    if active_persona_id is not null and active_persona_id > 0
	       and exists (select 1 from station.persona where id = active_persona_id)
	    then
	      insert into station.segment_schedule (day_of_week, start_minute, end_minute, persona_id)
	      select day, 0, 1440, active_persona_id
	      from generate_series(0, 6) as day;
	    end if;
	  end if;

	  -- Runs regardless of whether the block above seeded anything — an absent/zero/dangling
	  -- ActiveId still retires the key, leaving an empty (24/7 music-only) grid behind it.
	  delete from station.settings where key = 'Station:Persona:ActiveId';
	end $$;
	SQL

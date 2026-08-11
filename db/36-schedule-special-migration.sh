#!/bin/bash
# 36-schedule-special-migration.sh — creates station.schedule_special. NO db/01/db/06 mirror, by
# design (SPEC F120.1, F120.5; STORY-317; PLAN T258): this is the epic's one ruled-droppable slice,
# and "drop the slice = drop this one file" only holds if nothing else — least of all db/06's own
# fresh-init CREATE — carries a second copy of this table's DDL to go stale/diverge/need reverting.
#
# THE HONEST FRESH-INSTALL MECHANISM: db-compose.yaml / compose.yaml mount ONLY db/01-library.sh and
# db/06-station-settings-migration.sh as Postgres `docker-entrypoint-initdb.d` scripts — those two
# alone run automatically on a brand-new pgdata volume. Every other db/NN-*-migration.sh, this one
# included, reaches a database (fresh volume or an existing one upgrading through a release) the exact
# same way: launch.sh always calls `./migrate.sh --keep-going` immediately after the db service comes
# up and reports healthy, unconditionally, whether the volume was just created or not — see migrate.sh
# itself ("apply the idempotent db/*-migration.sh scripts to a RUNNING db service") and launch.sh's own
# remark just above that call ("applying them on every launch is safe and keeps the schema converged").
# A fresh install therefore gets station.schedule_special the identical way an upgraded install does:
# ONE script, ONE code path, run by the SAME runner either way — there is no db/06 mirror to keep in
# sync in the first place, so "fresh init" and "in-place upgrade" cannot diverge the way an ADD COLUMN
# migration (db/35's own precedent) has to guard against. The nearest existing precedent for a table
# that ships with zero db/01/db/06 mirror is db/34-cue-remeasure-migration.sh's own
# library.one_time_fix ledger table — same mechanism, different table.
#
# F91 mirrored (per F120.1): on_date replaces day_of_week (a specific calendar date instead of a
# weekday number — "shadow the grid for its span," not "repeat every week"); start_minute/end_minute
# keep the exact same 30-minute-step / range / end>start CHECKs station.segment_schedule already
# enforces (db/06's own CREATE, same column shapes/types) — the resolver's specials-first rung (PLAN
# T258, GenWave.Orchestration) treats a special exactly like a schedule block for the span it covers,
# so the two tables' minute arithmetic must never disagree. persona_id/show_id are nullable FKs with
# ON DELETE RESTRICT, mirroring segment_schedule's own persona_id/show_id columns verbatim (db/06 +
# db/35's own remarks): a persona or show still named by a future-dated special can never be deleted
# out from under it. genres/energy_min/energy_max mirror the same optional per-row envelope override
# (NULL = station-default, F91.4's own rule, inherited unchanged).
#
# btree_gist: station.segment_schedule's own EXCLUDE constraint already guarantees this extension is
# installed by the time this script ever runs — db/06 creates it on a fresh install (before
# segment_schedule's own CREATE), db/27 creates it for a pre-existing install upgrading through a
# release that predates db/06's copy, and migrate.sh always runs db/27 (numerically) before this
# script. Installed here too anyway, purely so this script stands on its own against a database that
# somehow never picked either up — the exact defensive posture db/27's own header gives for the
# identical redundant CREATE EXTENSION.
#
# The EXCLUDE constraint below is PER-DATE (on_date WITH =, not day_of_week): two specials may share a
# start/end span on DIFFERENT dates without conflict (a recurring "same slot, different day" special is
# legal), but never overlap on the SAME date — SPEC F120.1's own "per-date EXCLUDE no-overlap" guard.
# station.segment_schedule's own weekly EXCLUDE is untouched by construction — this is a wholly
# separate table with its own separate constraint.
#
# Safe to run multiple times (CREATE EXTENSION/TABLE IF NOT EXISTS both no-ops on an already-migrated
# database).
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	-- Still connected as the bootstrap superuser here (before SET ROLE below) — mirrors db/06's and
	-- db/27's own identical ordering for this statement.
	CREATE EXTENSION IF NOT EXISTS btree_gist;

	set role station_svc;
	set search_path = station;

	create table if not exists station.schedule_special (
	  id           serial      primary key,
	  on_date      date        not null,
	  start_minute int         not null check (start_minute % 30 = 0 and start_minute between 0 and 1410),
	  end_minute   int         not null check (end_minute   % 30 = 0 and end_minute   between 30 and 1440),
	  persona_id   int         references station.persona (id) on delete restrict,
	  show_id      int         references station.show (id) on delete restrict,
	  genres       text[],
	  energy_min   double precision,
	  energy_max   double precision,
	  created_at   timestamptz not null default now(),
	  updated_at   timestamptz not null default now(),
	  check (end_minute > start_minute),
	  exclude using gist (on_date with =, int4range(start_minute, end_minute) with &&)
	);
	SQL

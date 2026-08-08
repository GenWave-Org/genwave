#!/bin/bash
# 19-persona-taste-thumb-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds station.persona_taste_thumb — introduced in SPEC F84.5, STORY-215 (PLAN T70): the durable
# idempotency ledger for operator taste thumbs. Keyed (persona_id, booth_log_id, direction) — a
# double-tap, or a now-playing + booth-log tap on the SAME airing/direction, is the exact same row,
# so `ON CONFLICT ... DO NOTHING` is the entire dedup mechanism (never in-memory, which would forget
# on every restart). Also the durable source T71's "already thumbed" UI state reads.
# Safe to run multiple times: CREATE TABLE IF NOT EXISTS is a no-op on an already-migrated database.
# Run this script once against any DB initialised before 06-station-settings-migration.sh received it.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	-- Identical shape to db/06's fresh-init definition — see that script's comment for the FK/dedup
	-- rationale. FK CASCADE on both columns: a deleted persona or an evicted (retention-swept)
	-- booth-log row makes its own thumb-ledger rows meaningless, so they go with it — unlike
	-- booth_log.persona_id's own ON DELETE SET NULL (F84.6 HISTORY-row survival concern), which does
	-- not apply to this ledger.
	create table if not exists station.persona_taste_thumb (
	  id           bigserial   primary key,
	  persona_id   integer     not null references station.persona (id) on delete cascade,
	  booth_log_id bigint      not null references station.booth_log (id) on delete cascade,
	  direction    text        not null check (direction in ('up', 'down')),
	  created_at   timestamptz not null default now(),
	  unique (persona_id, booth_log_id, direction)
	);
	SQL

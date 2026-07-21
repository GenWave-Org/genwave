#!/bin/bash
# 16-persona-taste-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds station.persona_taste — introduced in SPEC F82.1, F84.1-F84.3, STORY-213 (the persona's
# authored/operator/accrued taste opinions the Q4'26 ranker slice reads and the accrual slice writes).
# Safe to run multiple times: CREATE TABLE IF NOT EXISTS / CREATE INDEX IF NOT EXISTS are no-ops on an
# already-migrated database. Run this script once against any DB initialised before
# 06-station-settings-migration.sh received it.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	-- Persona taste (SPEC F82.1, F84.1-F84.3; STORY-213): identical shape to db/06's fresh-init
	-- definition — see that script's comment for the FK/provenance rationale. No consumer lands with
	-- this table yet: the ranker (T63) and the accrual/eviction write path (T70) are later tasks.
	create table if not exists station.persona_taste (
	  id         serial      primary key,
	  persona_id integer     not null references station.persona (id) on delete cascade,
	  predicate  jsonb       not null,
	  context    jsonb       not null,
	  weight     real        not null check (weight between -1 and 1),
	  source     text        not null check (source in ('authored', 'operator', 'accrued')),
	  created_at timestamptz not null default now(),
	  updated_at timestamptz not null default now()
	);

	create index if not exists persona_taste_persona_source
	  on station.persona_taste (persona_id, source);
	SQL

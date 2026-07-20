#!/bin/bash
# 12-booth-log-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds station.booth_log — introduced in SPEC F72.1-F72.3, STORY-195 (the operator-readable
# "what the DJ did and said" narrative feed: track starts, patter airs, degradation mode changes).
# Safe to run multiple times (CREATE TABLE/INDEX IF NOT EXISTS). Run this script once against any DB
# initialised before 06-station-settings-migration.sh received station.booth_log.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	-- Identical shape to db/06's fresh-init definition — see that script's comment for the
	-- retention/disclosure rationale.
	create table if not exists station.booth_log (
	  id          bigserial   primary key,
	  occurred_at timestamptz not null default now(),
	  kind        text        not null,
	  summary     text        not null
	);

	create index if not exists booth_log_paging
	  on station.booth_log (occurred_at desc, id desc);
	SQL

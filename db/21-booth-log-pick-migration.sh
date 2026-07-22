#!/bin/bash
# 21-booth-log-pick-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds station.booth_log.pick — introduced in SPEC F86.1, STORY-217 (PLAN T73, "Personalities on
# air" persona visibility): the fired-rule summaries + exploration flag stamped from the SAME
# PersonaPickDiagnostics the copywriter reads (F83.1) — one source of truth, no re-derivation.
# Captured by BoothLogWriter.Publish at air time, same discipline persona_id (db/17) and artist
# (db/18) already established for this table. NULL for every non-track row, an engine-initiated
# play, a persona-off pick, or a row that predates this column — never backfilled (F84.6 precedent).
# Scores, pool size, and degradation step are deliberately NOT stored.
# Safe to run multiple times (ADD COLUMN IF NOT EXISTS). Run this script once against any DB
# initialised before 06-station-settings-migration.sh received station.booth_log.pick.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	alter table station.booth_log
	  add column if not exists pick jsonb;
	SQL

#!/bin/bash
# 22-booth-log-media-id-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds station.booth_log.media_id — introduced in gh-#99 (safe-content taste exclusion): the aired
# catalog row's numeric library.media id, captured by BoothLogWriter.Publish at air time, same
# discipline persona_id (db/17), artist (db/18), and pick (db/21) already established for this
# table. NULL for every non-track row, a non-catalog id (e.g. tts:*), or a row that predates this
# column — never backfilled. Deliberately NO foreign key: library.media lives on the other side of
# the schema-role boundary (station_svc has no grant there); the Host resolves safe-scope
# membership for the taste-thumb exclusion via the library connection instead.
# Safe to run multiple times (ADD COLUMN IF NOT EXISTS). Run this script once against any DB
# initialised before 06-station-settings-migration.sh received station.booth_log.media_id.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	alter table station.booth_log
	  add column if not exists media_id bigint;
	SQL

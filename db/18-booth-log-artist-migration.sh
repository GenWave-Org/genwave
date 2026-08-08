#!/bin/bash
# 18-booth-log-artist-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds station.booth_log.artist — introduced in SPEC F84.1, STORY-215 (PLAN T70): the accrual write
# path's "artist rule" needs the STRUCTURED artist name a track aired under, not a regex over the
# narrative Summary text ("Started 'Title' by Artist") — Summary stays human prose, never a machine
# parse target. Captured synchronously by BoothLogWriter.Publish at air time, the exact same
# discipline persona_id (db/17) already established for this column: never re-derived later, NULL
# for every non-track row or a track aired with no known artist. Never surfaced through
# IBoothLogReader/BoothLogEntry — read directly by the accrual write path only, inside the same
# transaction as the nudge it attributes.
# Safe to run multiple times (ADD COLUMN IF NOT EXISTS). Run this script once against any DB
# initialised before 06-station-settings-migration.sh received station.booth_log.artist.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	alter table station.booth_log
	  add column if not exists artist text;
	SQL

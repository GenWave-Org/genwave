#!/bin/bash
# 38-settings-version-migration.sh — idempotent in-place upgrade for existing DBs (gh-#486).
#
# Adds station.settings.version — a per-key optimistic-concurrency counter (bigint, default 1) so
# a whole-array/document settings write (Tts:Pronunciations, Tts:Corrections) can guard against a
# lost update from a concurrent editor: last-write-wins today silently drops whichever revision
# writes second (probed at T144: DELETE || PUT both 2xx, one edit vanished). See
# StationSettingsRepository.WriteIfVersionMatchesAsync/ReadVersionsAsync for the read/write halves
# and db/06-station-settings-migration.sh's own CREATE TABLE (already widened, so a fresh install
# never runs this script to get the same column).
#
# Every existing row backfills to version=1 via the column DEFAULT — safe with no explicit backfill
# statement: DEFAULT applies to every row already present the moment the column is added, the same
# "empty/first-touch table" posture most of this file's siblings document, except here it is the
# DEFAULT clause itself doing the backfill rather than the table being empty.
#
# Safe to run multiple times: ADD COLUMN IF NOT EXISTS is a no-op on an already-migrated database.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	alter table station.settings
	  add column if not exists version bigint not null default 1;
	SQL

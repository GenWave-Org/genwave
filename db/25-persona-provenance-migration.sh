#!/bin/bash
# 25-persona-provenance-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds station.persona.imported_from + imported_at — introduced in SPEC F90.7, STORY-237, PLAN T98
# (db/25: 23/24 were already taken by artwork-token/request). Catalog imports stamp imported_from
# with the entry slug; file imports stamp 'file'; authored-in-place personas (created via the
# CRUD endpoints, never import) keep both columns NULL — PersonaRepository's CreateAsync/UpdateAsync
# never reference either column. A re-import of the same slug refreshes both (PersonaImportRepository
# sets them unconditionally on every upsert, insert or update). Display-only provenance for the
# Admin UI's "Imported · <source> · <date>" badge (T105): no FK, no index — nothing selects, filters,
# or orders on these columns, and no selection/render/spectator path reads them (F90.7's own "nothing
# else reads it" rule).
# Safe to run multiple times (ADD COLUMN IF NOT EXISTS). Run this script once against any DB
# initialised before 06-station-settings-migration.sh received station.persona.imported_from/imported_at.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	alter table station.persona
	  add column if not exists imported_from text;

	alter table station.persona
	  add column if not exists imported_at timestamptz;
	SQL

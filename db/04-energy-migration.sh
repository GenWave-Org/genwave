#!/bin/bash
# 04-energy-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds intro_energy, outro_energy, and energy_analyzed_at to library.media
# introduced in STORY-030. Safe to run multiple times (all DDL uses
# ADD COLUMN IF NOT EXISTS guards).
# Run this script once against any DB initialised before 01-library.sh
# received the energy columns.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role library_svc;
	set search_path = library;

	-- intro_energy: RMS energy level of the intro region; NULL = not yet analyzed.
	alter table library.media
	  add column if not exists intro_energy double precision;

	-- outro_energy: RMS energy level of the outro region; NULL = not yet analyzed.
	alter table library.media
	  add column if not exists outro_energy double precision;

	-- energy_analyzed_at: timestamp of the last energy analysis attempt.
	-- NULL  = never attempted (energy backfill predicate).
	-- Non-NULL with intro_energy/outro_energy NULL = attempted, no energy data found.
	alter table library.media
	  add column if not exists energy_analyzed_at timestamptz;
SQL

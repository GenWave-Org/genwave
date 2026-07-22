#!/bin/bash
# 14-energy-percentile-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds library.media.energy — introduced in SPEC F80.1-F80.2, STORY-211 (the LUFS-percentile energy
# column consumed by the Q4'26 envelope/persona-bias slice). Safe to run multiple times (ADD COLUMN
# IF NOT EXISTS). Run this script once against any DB initialised before 01-library.sh received it.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role library_svc;
	set search_path = library;

	-- energy: percentile rank of integrated_lufs within the READY library (SPEC F80.1) — NOT
	-- track_energy (a fixed per-row linear scale, SPEC F47.1) and NOT intro_energy/outro_energy
	-- (STORY-033 RMS levels). Cannot be a generated column like track_energy: a percentile is
	-- relative to every OTHER ready row, which Postgres generated columns cannot reference. Instead
	-- recomputed by a single set-based UPDATE (MediaRepository.RecomputeEnergyPercentilesAsync)
	-- piggybacked on the enrichment second tier (SPEC F80.2) whenever a ready row's LUFS has moved
	-- since the last recompute (MediaRepository.HasStaleEnergyPercentilesAsync). NULL = not yet
	-- ranked; a fresh install running this migration leaves every row NULL until the next enrichment
	-- pass ticks the piggyback (no backfill-on-migrate step here, mirroring track_energy's own
	-- "re-derives on the next read/pass" convention).
	alter table library.media
	  add column if not exists energy real;
SQL

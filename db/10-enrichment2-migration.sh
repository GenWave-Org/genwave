#!/bin/bash
# 10-enrichment2-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds bpm, bpm_analyzed_at, year_lookup_at, and the track_energy generated column to
# library.media, plus the media_year index — introduced in Epic X / SPEC F46-F49 (gitea-#190, gitea-#208).
# Safe to run multiple times (all DDL uses ADD COLUMN IF NOT EXISTS / IF NOT EXISTS guards).
# Run this script once against any DB initialised before 01-library.sh received these columns.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role library_svc;
	set search_path = library;

	-- bpm: tempo estimate in beats per minute (SPEC F46.1); NULL = not yet analyzed.
	alter table library.media
	  add column if not exists bpm double precision;

	-- bpm_analyzed_at: timestamp of the last BPM analysis attempt.
	-- NULL  = never attempted (F46.3 backfill predicate).
	-- Non-NULL with bpm NULL = attempted, indeterminate tempo (F46.1-F46.2 sentinel semantics).
	alter table library.media
	  add column if not exists bpm_analyzed_at timestamptz;

	-- year_lookup_at: timestamp of the last MusicBrainz year-lookup attempt (SPEC F48.3).
	-- Stamped regardless of outcome (success, low confidence, or network failure) so the claim
	-- predicate `year IS NULL AND year_lookup_at IS NULL` never retries the same row forever.
	alter table library.media
	  add column if not exists year_lookup_at timestamptz;

	-- track_energy: whole-track perceptual energy, derived from integrated_lufs (SPEC F47.1).
	-- A STORED generated column — zero new ffmpeg passes, zero write-path changes, zero sentinel:
	-- it computes for the whole catalog the instant this migration runs and re-derives automatically
	-- whenever a loudness (re-)enrichment rewrites integrated_lufs (F47.2).
	--
	-- Semantics, mirrored 1:1 from FfmpegEnergyAnalyzer.MinLufs/MaxLufs/GateFloor
	-- (src/GenWave.Loudness/FfmpegEnergyAnalyzer.cs) — changing either side means changing both:
	--   integrated_lufs IS NULL      -> NULL (not yet measured)
	--   integrated_lufs <= -70.0     -> 0.0  (gated/silence, GateFloor)
	--   else                         -> clamp((integrated_lufs + 36.0) / 30.0, 0, 1)
	--                                    (MinLufs = -36.0 -> 0.0, MaxLufs = -6.0 -> 1.0)
	alter table library.media
	  add column if not exists track_energy double precision generated always as (
	    case
	      when integrated_lufs is null then null
	      when integrated_lufs <= -70.0 then 0.0
	      else least(1.0, greatest(0.0, (integrated_lufs + 36.0) / 30.0))
	    end
	  ) stored;

	-- media_year: the decade/year filter's spine (SPEC F49.5, F49.1).
	create index if not exists media_year on library.media (year);
SQL

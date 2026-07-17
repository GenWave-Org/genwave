#!/bin/bash
# 03-cue-points-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds cue_in_sec, cue_out_sec, and cue_analyzed_at to library.media
# introduced in v2 (gitea-#161). Safe to run multiple times (all DDL uses
# ADD COLUMN IF NOT EXISTS guards).
# Run this script once against any DB initialised before 01-library.sh
# received the cue-point columns.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role library_svc;
	set search_path = library;

	-- cue_in_sec: offset (seconds) of first non-silent audio; NULL = full-file playback.
	alter table library.media
	  add column if not exists cue_in_sec double precision;

	-- cue_out_sec: offset (seconds) of last non-silent audio; NULL = full-file playback.
	alter table library.media
	  add column if not exists cue_out_sec double precision;

	-- cue_analyzed_at: timestamp of the last cue-point analysis attempt.
	-- NULL  = never attempted (T027 backfill predicate).
	-- Non-NULL with cue_in_sec/cue_out_sec NULL = attempted, no boundaries found.
	alter table library.media
	  add column if not exists cue_analyzed_at timestamptz;
SQL

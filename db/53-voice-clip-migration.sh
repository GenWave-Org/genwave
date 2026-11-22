#!/bin/bash
# 53-voice-clip-migration.sh — idempotent in-place upgrade for existing DBs.
# F219 (STORY-557; PLAN T755): station.voice_clip holds the reference clips a cloning voice engine speaks in,
# and station.voice_pack.engine stops being a closed set. Mirrored in db/06 (fresh installs); this script is the
# upgrade path.
# db/45 created station.voice_pack with an inline `check (engine in ('kokoro'))`, which Postgres names
# voice_pack_engine_check. More engines now exist, so the check is dropped here; VoicePackController
# checks the manifest engine against its SupportedEngines and the active engine instead. db/06 no
# longer creates it (and db/45's CREATE is a no-op after db/06), so a fresh install never has it;
# DROP CONSTRAINT IF EXISTS makes a re-run a no-op.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	-- begin f219-voice-clip
	-- F219 (STORY-557; PLAN T755): one row per reference clip a cloning engine can speak in. The audio file lives under the
	-- voice-clips volume, not in Postgres. Mirrored verbatim between db/06 and db/53 (a spec pins the text equal).
	CREATE TABLE IF NOT EXISTS station.voice_clip (
	  id          serial PRIMARY KEY,
	  slug        text NOT NULL UNIQUE CHECK (slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'),
	  label       text NOT NULL,
	  file_name   text NOT NULL,            -- under the voice-clips volume
	  duration_ms integer NOT NULL CHECK (duration_ms BETWEEN 5000 AND 20000),
	  source      text NOT NULL,            -- 'starter' | 'upload'
	  credit      text,                     -- attribution line, required for 'starter'
	  created_at  timestamptz NOT NULL DEFAULT now()
	);
	-- end f219-voice-clip

	ALTER TABLE station.voice_pack DROP CONSTRAINT IF EXISTS voice_pack_engine_check;
	SQL

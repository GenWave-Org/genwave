#!/bin/bash
# 50-utterance-memory-migration.sh — idempotent in-place upgrade for existing DBs.
# F218 "DJs that remember" (STORY-532): station.utterance logs what each persona said on air (repeat
# suppression + memory extraction); station.memory_event is one shared happening that N personas'
# persona_memory rows point at. persona_memory grows (never reshapes): source widened to
# authored|accrued|extracted|drafted plus occurred_at, event_id, approved_at, forgotten_at.
# memory_event is created BEFORE the persona_memory ALTER because event_id references it.
# Mirrored in db/06 (enum DO block + CREATE TABLEs, fresh installs); this script is the upgrade path.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	DO $$
	BEGIN
	  IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'utterance_kind' AND typnamespace = 'station'::regnamespace) THEN
	    CREATE TYPE station.utterance_kind AS ENUM ('patter', 'crosstalk');
	  END IF;
	END $$;

	CREATE TABLE IF NOT EXISTS station.memory_event (
	  id          serial      PRIMARY KEY,
	  occurred_at timestamptz NOT NULL,
	  kind        text        NOT NULL,               -- 'crosstalk' | 'handoff' | 'outing' (extracted, two DJs)
	  summary     text        NOT NULL
	);

	CREATE TABLE IF NOT EXISTS station.utterance (
	  id            serial      PRIMARY KEY,
	  persona_id    integer     NOT NULL REFERENCES station.persona (id) ON DELETE CASCADE,
	  show_id       integer     REFERENCES station.show (id) ON DELETE SET NULL, -- nullable-fk: unscheduled hours have no show
	  kind          station.utterance_kind NOT NULL,
	  segment_kind  text        NOT NULL,            -- LeadIn / BackAnnounce / SignOn / SignOff / ContextSegment / CrosstalkLine
	  shift_key     text        NOT NULL,            -- gh-#293: persona|day|start|station-local date; date alone when unscheduled
	  text          text        NOT NULL,            -- the LLM's accepted copy, before the TTS normaliser
	  fallback      boolean     NOT NULL DEFAULT false,  -- template line aired instead (truth or repeat gate failed twice)
	  written_at    timestamptz NOT NULL DEFAULT now(),
	  aired_at      timestamptz                      -- null until the engine reports the file started
	);
	CREATE INDEX IF NOT EXISTS utterance_recent ON station.utterance (persona_id, written_at DESC);

	ALTER TABLE station.persona_memory DROP CONSTRAINT IF EXISTS persona_memory_source_check;
	ALTER TABLE station.persona_memory
	  ADD CONSTRAINT persona_memory_source_check CHECK (source IN ('authored', 'accrued', 'extracted', 'drafted'));
	ALTER TABLE station.persona_memory ADD COLUMN IF NOT EXISTS occurred_at  timestamptz;  -- null = timeless
	ALTER TABLE station.persona_memory ADD COLUMN IF NOT EXISTS event_id     integer
	  REFERENCES station.memory_event (id) ON DELETE SET NULL;                             -- nullable-fk: only shared events have one
	ALTER TABLE station.persona_memory ADD COLUMN IF NOT EXISTS approved_at  timestamptz;  -- drafted rows: null = pending, never recalled
	ALTER TABLE station.persona_memory ADD COLUMN IF NOT EXISTS forgotten_at timestamptz;  -- set = never recalled, never-mention for 7 days
	SQL

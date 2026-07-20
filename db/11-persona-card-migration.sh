#!/bin/bash
# 11-persona-card-migration.sh — idempotent in-place upgrade for existing DBs.
# Reconciles station.persona (STORY-118 shape) onto the F71.1 persona-card schema and adds
# station.persona_memory — introduced in SPEC F71.1, STORY-192, Epic (persona foundation).
# Safe to run multiple times (ADD COLUMN IF NOT EXISTS / CREATE TABLE IF NOT EXISTS / CREATE INDEX
# IF NOT EXISTS, plus a DO block that checks information_schema before adding the UNIQUE(slug)
# constraint — the same idempotency idiom db/07-library-management-migration.sh uses).
# Run this script once against any DB initialised before 06-station-settings-migration.sh received
# the slug/definition/enabled columns and the persona_memory table.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	-- slug/definition/enabled (SPEC F71.1): additive so STORY-118's admin CRUD columns
	-- (name/backstory/style/voice) are untouched — see db/06's own comment for the full
	-- reconciliation rationale. A volatile DEFAULT (nextval) on ADD COLUMN backfills every
	-- pre-existing row with a real, unique slug in the same statement; GenWave.MediaLibrary.Station.
	-- PersonaCardMigrator reconciles each row's `definition` from its own name/backstory/style/voice
	-- on the next boot (its sentinel: `definition = '{}'::jsonb` means "not yet reconciled").
	alter table station.persona
	  add column if not exists slug text not null default ('persona-' || nextval('station.persona_id_seq')::text);

	alter table station.persona
	  add column if not exists definition jsonb not null default '{}'::jsonb;

	alter table station.persona
	  add column if not exists enabled boolean not null default true;

	-- Idempotent UNIQUE(slug): ADD CONSTRAINT has no IF NOT EXISTS form, so guard with a DO block
	-- (mirrors db/07-library-management-migration.sh's identical idiom for library.library.name).
	DO $$
	BEGIN
	    IF NOT EXISTS (
	        SELECT 1
	        FROM information_schema.table_constraints
	        WHERE constraint_schema = 'station'
	          AND table_name        = 'persona'
	          AND constraint_name   = 'persona_slug_key'
	          AND constraint_type   = 'UNIQUE'
	    ) THEN
	        ALTER TABLE station.persona ADD CONSTRAINT persona_slug_key UNIQUE (slug);
	    END IF;
	END;
	$$;

	-- Persona memory (SPEC F71.1): identical shape to db/06's fresh-init definition — see that
	-- script's comment for the FK/index rationale.
	create table if not exists station.persona_memory (
	  id            serial      primary key,
	  persona_id    integer     not null references station.persona (id) on delete cascade,
	  kind          text        not null,
	  content       text        not null,
	  source        text        not null check (source in ('authored', 'accrued')),
	  aired_count   integer     not null default 0,
	  last_aired_at timestamptz,
	  created_at    timestamptz not null default now()
	);

	create index if not exists persona_memory_recall
	  on station.persona_memory (persona_id, kind, last_aired_at desc nulls first);
	SQL

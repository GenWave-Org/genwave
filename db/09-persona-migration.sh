#!/bin/bash
# 09-persona-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds station.persona (SPEC F35.1, STORY-118, Epic T): DJ persona storage — backstory/style/voice
# profiles a future orchestrator task blends into TTS patter. Lives in the same station schema/role
# as station.settings (db/06) — no new role, no new schema.
# Safe to run multiple times: CREATE TABLE IF NOT EXISTS is a no-op on an already-migrated database.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	create table if not exists station.persona (
	  id         serial      primary key,
	  name       text        not null unique,
	  backstory  text        not null default '',
	  style      text        not null default '',
	  voice      text        not null default '',  -- '' = station default (Station:Voice)
	  created_at timestamptz not null default now(),
	  updated_at timestamptz not null default now()
	);
SQL

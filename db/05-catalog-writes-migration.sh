#!/bin/bash
# 05-catalog-writes-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds eligible and tags_edited_at to library.media introduced in STORY-039 (Epic I).
# Safe to run multiple times (all DDL uses ADD COLUMN IF NOT EXISTS guards).
# Run this script once against any DB initialised before 01-library.sh received these columns.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role library_svc;
	set search_path = library;

	-- eligible: false = operator has excluded the track from playout.
	-- DEFAULT true ensures existing rows remain in rotation (no behavior change).
	alter table library.media
	  add column if not exists eligible boolean not null default true;

	-- tags_edited_at: NULL = never manually edited by an operator.
	-- Set to now() on each successful PATCH write (Epic I, W2).
	alter table library.media
	  add column if not exists tags_edited_at timestamptz;
SQL

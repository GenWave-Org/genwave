#!/bin/bash
# 02-library-id-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds the library.library table and library_id FK column to library.media
# introduced in v2. Safe to run multiple times (all DDL uses IF NOT EXISTS /
# IF EXISTS guards). Run this script once against any DB that was initialised
# before 01-library.sh received the library table.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role library_svc;
	set search_path = library;

	-- Create the library table if it does not exist yet.
	create table if not exists library.library (
	  id   bigint generated always as identity primary key,
	  name text   not null
	);

	-- Ensure the default library row (id=1) is present; insert only when absent.
	insert into library.library (name)
	select 'default' where not exists (select 1 from library.library where id = 1);

	-- Add library_id column to media if not already present.
	-- Default 1 covers pre-existing rows at backfill time AND new inserts:
	-- the scanner's insert omits library_id (single-library v2), matching
	-- the canonical fresh-install schema in 01-library.sh which keeps the default.
	alter table library.media
	  add column if not exists library_id bigint not null default 1
	    references library.library(id) on delete restrict;

	-- Re-add the default on DBs upgraded by an earlier version of this script
	-- that dropped it (caused 23502 not-null violations on scan inserts).
	alter table library.media alter column library_id set default 1;

	-- Add the composite partial index if not present; drops the old scalar index first.
	drop index if exists library.media_ready;
	create index if not exists media_scope_ready
	  on library.media (library_id, state) where state = 'ready';
SQL

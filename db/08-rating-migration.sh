#!/bin/bash
# 08-rating-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds library.media_rating (SPEC F33, STORY-109): a 1:1 extension table for operator vote/never-play
# state, kept separate from library.media so a vote never bumps that row's xmin (F18.6 ETags survive
# a thumbs-up) and bulk curation (eligibility/PATCH/reassign/re-enrich) stays structurally incapable
# of touching rating state (the gitea-#188 "standalone" guarantee, enforced by schema).
# Safe to run multiple times: CREATE TABLE IF NOT EXISTS is a no-op on an already-migrated database.
# Same library_svc role, same library schema — no new role, no new schema.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role library_svc;
	set search_path = library;

	-- postgres-dba Rule-2 deviation: media_id is the primary key (not a surrogate `id serial`).
	-- This is a 1:1 extension row — a surrogate id would permit duplicate rating rows per media,
	-- which the table exists specifically to prevent. PK = FK is deliberate, not an oversight.
	CREATE TABLE IF NOT EXISTS library.media_rating (
	  media_id   bigint PRIMARY KEY REFERENCES library.media(id) ON DELETE CASCADE,
	  score      int NOT NULL DEFAULT 50 CHECK (score BETWEEN 0 AND 100),
	  never_play boolean NOT NULL DEFAULT false,
	  updated_at timestamptz NOT NULL DEFAULT now()
	);
SQL

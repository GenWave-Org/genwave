#!/bin/bash
# 20-mood-tag-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds library.media.mood_tagged_at / mood_tag_missed_at — introduced in SPEC F85.2-F85.4,
# STORY-216 (the mood-tagger's F76 MusicBrainz-etiquette stamp pair). Safe to run multiple times
# (ADD COLUMN IF NOT EXISTS). Run this script once against any DB initialised before 01-library.sh
# received it. Mirrors 13-year-lookup-etiquette-migration.sh exactly, one column pair later.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role library_svc;
	set search_path = library;

	-- mood_tagged_at: "attempted at" telemetry only (SPEC F85.2) -- stamped unconditionally on every
	-- tagger attempt (success, miss, or endpoint failure); it does not gate re-claiming on its own.
	-- Mirrors year_lookup_at's exact non-gating role.
	--
	-- mood_tag_missed_at: the actual re-claim gate (MediaRepository.ListMoodTagClaimsAsync). Stamped
	-- ONLY for a genuine miss -- a completed round trip that produced zero in-vocabulary survivors
	-- (SPEC F85.4). A failed round trip leaves this NULL, so the row is retried next backfill tick;
	-- only a real "no in-vocabulary tag" answer is ever excluded permanently. Mirrors
	-- year_lookup_missed_at's exact shape -- the reusable "<domain>_missed_at" idiom that column's own
	-- migration comment named for this exact future slice.
	alter table library.media
	  add column if not exists mood_tagged_at     timestamptz,
	  add column if not exists mood_tag_missed_at timestamptz;
SQL

#!/bin/bash
# 15-mood-vocabulary-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds library.media.moods — introduced in SPEC F85.1-F85.2, STORY-216 (the fixed-vocabulary mood
# tag column consumed by the Q4'26 mood-tagger enrichment slice). Safe to run multiple times (ADD
# COLUMN IF NOT EXISTS). Run this script once against any DB initialised before 01-library.sh
# received it. Mirrors 14-energy-percentile-migration.sh exactly.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role library_svc;
	set search_path = library;

	-- moods: up to MoodVocabulary.MaxMoodsPerTrack (3) tags drawn from the fixed vocabulary that
	-- lives in GenWave.Abstractions (SPEC F85.1, F85.2, STORY-216). Populated by a second-tier
	-- enrichment pass (T72, mood tagger); T58 ships storage + the write path only, so a fresh
	-- install leaves every row NULL until that pass runs. The write path itself
	-- (MediaRepository.WriteMoodsAsync) is the vocabulary gate: it rejects, as a whole, any write
	-- naming a term outside the vocabulary (F85.1) BEFORE this UPDATE ever runs — deliberately no
	-- per-term CHECK here, since the vocabulary is versioned in C#, not SQL, and a future term
	-- addition must never require a migration. The count cap IS spec-pinned and version-independent
	-- (F85.2, "≤3"), so it is enforced twice, defense-in-depth: once here, once at the write path.
	alter table library.media
	  add column if not exists moods text[]
	    check (moods is null or cardinality(moods) <= 3);
SQL

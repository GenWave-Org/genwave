#!/bin/bash
# 26-explicit-classification-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds library.media.explicit / explicit_source — introduced in SPEC F95.2, STORY-251 (PLAN T110).
# `explicit` is a plain nullable boolean: NULL = unknown/unclassified, true/false = classified either
# way. `explicit_source` names WHO classified the row, constrained to the three known origins
# (F95.3): 'tag' (advisory flag carried in the file's own metadata, stamped first, PLAN T112),
# 'llm' (the offline sweep asking a model about unclassified rows, PLAN T113), 'operator' (an
# explicit admin override that later sweeps must never touch again, PLAN T115).
#
# Also adds explicit_llm_missed_at — the T113 sweep's own re-claim gate (PLAN T113), extended into
# this same migration rather than shipped as a new numbered one: db/26 has not shipped in a release
# yet (still on an unreleased branch), so it is still this task's schema to grow. Mirrors
# mood_tag_missed_at/year_lookup_missed_at's exact "<domain>_missed_at" idiom (SPEC F76, F85.4): a
# genuine "unknown" verdict (a completed round trip that couldn't tell) stamps this column so the
# sweep never re-asks it; a failed round trip (endpoint unreachable) leaves it NULL, so the row stays
# eligible and is retried on the very next tick. Unlike mood/year, there is no paired unconditional
# "attempted at" telemetry column here — the sweep's own attempt is either a written verdict
# (explicit/explicit_source no longer NULL) or this miss stamp; nothing else currently reads a
# separate "last attempted" marker for this pass.
#
# Safe to run multiple times (ADD COLUMN IF NOT EXISTS). Run this script once against any DB
# initialised before 01-library.sh received these three columns.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role library_svc;
	set search_path = library;

	alter table library.media
	  add column if not exists explicit boolean;

	alter table library.media
	  add column if not exists explicit_source text
	    check (explicit_source is null or explicit_source in ('tag', 'llm', 'operator'));

	alter table library.media
	  add column if not exists explicit_llm_missed_at timestamptz;
	SQL

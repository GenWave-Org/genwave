#!/bin/bash
# 13-year-lookup-etiquette-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds library.media.year_lookup_missed_at — introduced in SPEC F76.1-F76.2, STORY-200 (MusicBrainz
# etiquette: throttle, descriptive User-Agent, miss-stamping). Safe to run multiple times (ADD COLUMN
# IF NOT EXISTS). Run this script once against any DB initialised before 01-library.sh received it.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role library_svc;
	set search_path = library;

	-- year_lookup_missed_at: the actual MusicBrainz re-claim gate (SPEC F76.2). Stamped ONLY for a
	-- genuine miss -- a completed round trip with no confident match above MinScore. An endpoint
	-- failure/timeout leaves this NULL, so the row is retried next backfill tick; only a real
	-- "no such recording" answer is ever excluded permanently.
	--
	-- The older year_lookup_at column (SPEC F48.3) is stamped unconditionally on every attempt --
	-- success, miss, or failure -- and remains an "attempted at" telemetry marker only; it no longer
	-- gates re-claiming on its own (see MediaRepository.ListYearLookupClaimsAsync).
	--
	-- Same reusable idiom a future enrichment slice (e.g. mood tagging) can copy verbatim:
	-- "<domain>_lookup_missed_at".
	alter table library.media
	  add column if not exists year_lookup_missed_at timestamptz;
SQL

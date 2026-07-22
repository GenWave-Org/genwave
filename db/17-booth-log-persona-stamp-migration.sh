#!/bin/bash
# 17-booth-log-persona-stamp-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds station.booth_log.persona_id — introduced in SPEC F84.6, STORY-215 (the Q4'26 taste-accrual
# slice's on-air attribution: the persona active when a track-start row aired, stamped at write time).
# Safe to run multiple times (ADD COLUMN IF NOT EXISTS). Run this script once against any DB
# initialised before 06-station-settings-migration.sh received station.booth_log.persona_id.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	-- nullable-fk: SPEC F84.6 — the persona on air when a TRACK-START row aired, stamped by the
	-- booth-log drain loop at write time (never inferred later, never re-derived from "whichever
	-- persona happens to be active now"). NULL for every non-track row, a persona-less airing, and
	-- every row that predates this column — all three are equally "un-thumbable" for taste accrual
	-- (T70), which is exactly the degraded state F84.6 already defines.
	--
	-- ON DELETE SET NULL, not CASCADE (unlike persona_memory/persona_taste's FKs): deleting a
	-- persona must never delete booth-log HISTORY rows — it only degrades their stamp to unstamped,
	-- the same un-thumbable state above.
	alter table station.booth_log
	  add column if not exists persona_id integer references station.persona (id) on delete set null;
	SQL

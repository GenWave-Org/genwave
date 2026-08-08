#!/bin/bash
# 33-show-and-segment-kind-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds station.show + station.segment_schedule.show_id (SPEC F114, gh-#383 — the later slice, schema
# ruled now) and station.booth_log.segment_kind (SPEC F113, the demo-hour instrument); STORY-304, PLAN
# T219. ARCHITECTURE.md "A station, not a playlist" -> "Data model" has this DDL verbatim.
#
# station.show is singular (station.persona precedent — every table in this schema is), a first-class
# entity rather than a text column on segment_schedule: renaming a show touches one row, and identity
# is what patter/idents/spectator will reference (F114). show_id is a nullable FK (most painted blocks
# are unnamed; NULL = no show branding) with ON DELETE RESTRICT — the same persona_id precedent this
# table's own db/27/db/06 DDL already set: unassign a show from every slot before deleting it, never a
# silent cascade through the format clock.
#
# booth_log.segment_kind is deliberately un-CHECKed (unlike most kind columns in this schema) — the
# SegmentKind token set is a growing C# enum (GenWave.Core.Domain), not a closed set a CHECK could
# pin without churning this migration on every new content kind. NULL for every music row; a
# SegmentKind token for tts:* rows, stamped by the booth-log drain loop at air time (T220) — the
# genuine air-time signal, never inferred from patter-aired (which fires at render time and may never
# actually air).
#
# NO CONSUMER YET for station.show: it stays dormant on purpose until the F114 slice wires a writer/
# reader — the same "seam before consumer" way station.persona_taste (db/06, T59), station.theme
# (db/31, T181), and station.font_pack (db/32, T198) all shipped. show_id and segment_kind are equally
# consumer-less until T220/F114 land.
#
# Safe to run multiple times: CREATE TABLE / ADD COLUMN IF NOT EXISTS are no-ops on an already-migrated
# database.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	-- Shows are entities: rename touches one row; patter/idents/spectator reference identity (F114).
	create table if not exists station.show (
	  id         serial      primary key,
	  name       text        not null check (length(btrim(name)) > 0),
	  created_at timestamptz not null default now(),
	  updated_at timestamptz not null default now()
	);

	-- nullable-fk: most painted blocks are unnamed; NULL = no show branding.
	-- RESTRICT matches the persona_id precedent: unassign first, then delete.
	alter table station.segment_schedule
	  add column if not exists show_id int references station.show(id) on delete restrict;

	-- Air-time kind stamp: the demo-hour instrument (F113).
	-- NULL for music rows; SegmentKind token for tts:* rows, stamped at air time.
	alter table station.booth_log
	  add column if not exists segment_kind text;
	SQL

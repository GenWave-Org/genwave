#!/bin/bash
# 35-show-identity-migration.sh — idempotent in-place upgrade for existing DBs.
# Widens station.show into the F115 identity package (name/slug/tagline/flavor/provenance) plus the
# DORMANT F115.2/F121 bundle columns, stamps station.booth_log and library.media with an air-time
# show_id (F121.1, F119.4); STORY-305, STORY-310, PLAN T238. ARCHITECTURE.md "Dayparting: named
# shows" -> "Data model" has this DDL verbatim.
#
# station.show shipped DORMANT in db/33 (T219) with no writer anywhere — every install has an empty
# table today, which is what makes `slug text NOT NULL` safe to add here without a backfill or a
# DEFAULT. slug is the import identity (a catalog slug for an imported show, the house Slugify output
# for an authored one — T239) and carries a named UNIQUE constraint so a fresh-init table and an
# upgraded one converge on the identical constraint name. tagline is public (broadcast-shaped, the
# spectator DTO and ceremony read it once F115 wires a reader); flavor is prompt-only and NEVER public
# (F115.3 — the persona-soul precedent). imported_from/imported_at mirror station.persona's own db/25
# provenance pair exactly: the catalog entry slug for an import, the literal 'file' for an upload,
# NULL for an authored-in-place show.
#
# persona_id/envelope are the DORMANT bundle columns (ARCHITECTURE.md ruled 2026-08-10): UNREAD until
# the deferred schedulable-bundle slice. Future semantics recorded there, not enforced by this schema:
# effective assignment = block ?? show ?? none, block always wins. persona_id is a plain nullable FK
# (no ON DELETE override needed — the column has no consumer to make deletion a live concern yet);
# envelope is jsonb for the same open-ended-shape reason station.theme/font_pack definitions are.
# NO CONSUMER YET for either column, same "seam before consumer" precedent as station.show itself.
#
# booth_log.show_id is the air-time stamp (F121.1, STORY-310) — the same synchronous-at-write-time
# discipline persona_id/artist/pick/segment_kind on this table already use (db/17/18/33). Deliberately
# NO FK: history must outlive the entity (the exact booth_log.persona_id-would-need vs media_id/
# segment_kind precedent already on this table) — a deleted show must never rewrite or block on past
# airings.
#
# library.media.show_id crosses the db/22 schema-role boundary (station_svc has no grant into
# library) the same way booth_log.media_id already crosses it in the other direction — plain int, NO
# FK, resolved by the app at its own edge, never a cross-schema join. NULL = station-wide (today's
# only meaning); set only on an authored imaging row scoped to a show (T246).
#
# Safe to run multiple times: ADD COLUMN IF NOT EXISTS is a no-op on an already-migrated database.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	-- The identity package + provenance (station.show shipped dormant in db/33; empty everywhere,
	-- so NOT NULL is safe to add with no backfill). Named UNIQUE so a fresh-init table (db/06's own
	-- CREATE, already widened) and this upgraded one land on the identical constraint name.
	alter table station.show
	  add column if not exists slug          text not null constraint show_slug_key unique,
	  add column if not exists tagline       text,
	  add column if not exists flavor        text,
	  add column if not exists imported_from text,
	  add column if not exists imported_at   timestamptz,
	  -- Dormant bundle columns (ruled 2026-08-10): UNREAD until the schedulable-bundle slice.
	  -- Future semantics recorded: effective = block ?? show ?? none; block always wins.
	  add column if not exists persona_id    int references station.persona (id),
	  add column if not exists envelope      jsonb;

	-- Air-time stamp (F121.1). NO FK — history outlives the entity, same as media_id/segment_kind
	-- already on this table.
	alter table station.booth_log
	  add column if not exists show_id int;
	SQL

# library schema (separate connection/grant — the db/22 boundary): its own psql invocation, its own
# role, mirroring how db/34 talks to library.media.
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role library_svc;
	set search_path = library;

	-- NO FK across the grant boundary (db/22 precedent). NULL = station-wide; set only on
	-- authored imaging rows scoped to a show (T246).
	alter table library.media
	  add column if not exists show_id int;
	SQL

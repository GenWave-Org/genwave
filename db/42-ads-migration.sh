#!/bin/bash
# 42-ads-migration.sh — idempotent in-place upgrade for existing DBs.
# The plugin door & the Ads library (gh-#380, SPEC F159.1, STORY-389, PLAN T389). ARCHITECTURE.md
# "The plugin door & the Ads library" -> "Data model (db/42; fresh-init mirrors in db/06 +
# 01-library.sh CHECK widen)" has this DDL's sketch; this script is the refined, idempotent version
# of it, and db/06 + db/01-library.sh carry the schema-only mirror for a fresh install (the same
# "fresh installs never see a numbered upgrade script, only 01+06 through
# docker-entrypoint-initdb.d" rule every earlier migration in this directory follows — and the
# gh-#618 lesson this task exists to not repeat: a migration without its fresh-init mirror haunts
# you).
#
# ad_source / ad_state ARE THE FIRST ENUM TYPES IN THE STATION SCHEMA (library already has five,
# db/41) — `create type` has no `if not exists` clause, so each one is guarded by a `pg_type`
# existence check inside a DO block, the same idiom db/41 established.
#
# station.ad_spot — the spot's whole life (SPEC F159.1): brand/title/brief/script, `source`
# (llm|owner|pack), `pack_slug` (set only for a pack-sourced spot), `spot_seconds` capped to the
# three shipped structures (15|30|60, SPEC F160.2), `voice_plan` (the rendered VoiceSpec cast,
# F161.2), an optional `bed_media_id`, the total state machine (draft -> approved -> rendering ->
# ready|failed; failed -> approved retry; ready -> retired; draft -> retired — SPEC F159.2) with
# `fail_reason` set iff `failed`, `media_id` FK'd to the rendered row once `ready` (F161.3),
# `generation` (bumped on refresh/retry — F159.3's own refresh path re-renders in place rather than
# forking a new row), and the full create/transition/render/retire timestamp quartet. Nothing is
# ever system-deleted (F159.1); `retired` and `failed` are otherwise-terminal states an operator
# leaves in place, the announcements posture (db/40).
#
# `id bigint generated always as identity` — not serial/bigserial — the SAME choice db/40's own
# station.announcement made and this file mirrors verbatim, matching the newest station-schema
# table precedent rather than db/41's library-schema bigserial ones (house rule: consistency with
# the nearest sibling beats a fixed house style).
#
# bed_media_id / media_id are DELIBERATELY PLAIN bigint, NOT a real foreign key into
# library.media(id), despite SPEC F159.1's prose reading "references library.media(id)" — the
# db/22 schema-role boundary (station_svc has no grant into the library schema, library_svc none
# into station) already rules this out for station.booth_log.media_id (db/22) and
# library.media.show_id (db/35's own mirror in this same file's sibling), and station_svc lacks the
# REFERENCES privilege on a library_svc-owned table for the same reason a real constraint would
# need it. A future repository resolves both ids through IMediaCatalog, never a cross-schema join,
# the identical posture db/22's own header remarks spell out. This is the one deliberate deviation
# from SPEC F159.1's literal column list this migration takes.
#
# station.ad_brief — the brand universe (SPEC F159.1, F162.2): pack-installed and owner-authored
# briefs, upsert-keyed `(pack_slug, brand)` so a pack re-install updates rather than duplicates.
# pack_slug is nullable (an owner-authored brief carries none) — plain Postgres UNIQUE treats every
# NULL as distinct, which would let an operator create the SAME brand's owner brief twice with no
# constraint ever catching it. This migration uses `UNIQUE NULLS NOT DISTINCT (pack_slug, brand)`
# (PG15+; this stack pins postgres 16.4, confirmed against a scratch container while building this
# migration) instead, so NULL collapses to one comparable value the same as any other — one
# owner-authored brief per brand, and one pack-installed brief per (pack_slug, brand). Note the
# owner-brief half is an EXTENSION of F162.2 (whose key governs pack installs); the one-owner-brief-
# per-brand cap is a build-time constraint awaiting Dean's ruling — see docs/PLAN.md T398 note.
#
# library.media.imaging_kind's CHECK (db/30's original four values) widens to include 'ad' (SPEC
# F159.1's own third bullet). No "add constraint if not exists" syntax in Postgres, so the widen is
# guarded by comparing the constraint's CURRENT definition (via pg_get_constraintdef, confirmed
# against a scratch container while building this migration) to the widened one — a second run is a
# genuine no-op, not just an error-free drop+recreate.
#
# Safe to run multiple times: every CREATE is IF NOT EXISTS or DO-block-guarded, and the
# imaging_kind widen only fires when the constraint does not already match. Run this script once
# against any DB initialised before 06-station-settings-migration.sh + 01-library.sh received the
# Ads store (db/06's own CHECK) and the widened imaging_kind CHECK (db/01's own mirror) respectively.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

# --- station schema: ad_source/ad_state enums, station.ad_spot, station.ad_brief -------------------
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	do $$
	begin
	  if not exists (select 1 from pg_type where typname = 'ad_source' and typnamespace = 'station'::regnamespace) then
	    create type station.ad_source as enum ('llm', 'owner', 'pack');
	  end if;
	  if not exists (select 1 from pg_type where typname = 'ad_state' and typnamespace = 'station'::regnamespace) then
	    create type station.ad_state as enum ('draft', 'approved', 'rendering', 'ready', 'failed', 'retired');
	  end if;
	end $$;

	create table if not exists station.ad_spot (
	  id               bigint      generated always as identity primary key,
	  brand            text        not null,
	  title            text        not null,
	  brief            text,
	  script           text,
	  source           station.ad_source not null,
	  pack_slug        text,
	  spot_seconds     int         not null default 30 check (spot_seconds in (15, 30, 60)),
	  voice_plan       jsonb,
	  bed_media_id     bigint,     -- NO FK: crosses the db/22 schema-role boundary, see header
	  state            station.ad_state not null default 'draft',
	  fail_reason      text,
	  media_id         bigint,     -- NO FK: crosses the db/22 schema-role boundary, see header
	  generation       int         not null default 1,
	  created_at       timestamptz not null default now(),
	  state_changed_at timestamptz not null default now(),
	  rendered_at      timestamptz,
	  retired_at       timestamptz
	);

	create table if not exists station.ad_brief (
	  id         bigint      generated always as identity primary key,
	  pack_slug  text,
	  brand      text        not null,
	  premise    text,
	  tone       text,
	  structure  text,
	  enabled    boolean     not null default true,
	  created_at timestamptz not null default now(),
	  -- NULLS NOT DISTINCT (PG15+, this stack pins 16.4): pack_slug is nullable for an owner-authored
	  -- brief, and plain UNIQUE treats every NULL as distinct — this is the F162.2 upsert key made
	  -- to actually catch an owner-brief collision on brand alone (see header remarks).
	  constraint ad_brief_pack_slug_brand_key unique nulls not distinct (pack_slug, brand)
	);
	SQL

# --- library schema: library.media.imaging_kind CHECK widen to include 'ad' (SPEC F159.1) ----------
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role library_svc;
	set search_path = library;

	-- No "add constraint if not exists" in Postgres — guarded by comparing the constraint's CURRENT
	-- definition to the widened one (confirmed against a scratch container while building this
	-- migration), so a second run performs no DDL at all rather than an error-free drop+recreate.
	do $$
	begin
	  if not exists (
	    select 1 from pg_constraint
	    where conname = 'media_imaging_kind_check'
	      and conrelid = 'library.media'::regclass
	      and pg_get_constraintdef(oid) = $DEF$CHECK (((imaging_kind IS NULL) OR (imaging_kind = ANY (ARRAY['liner'::text, 'station_id'::text, 'jingle'::text, 'promo'::text, 'ad'::text]))))$DEF$
	  ) then
	    alter table library.media drop constraint if exists media_imaging_kind_check;
	    alter table library.media
	      add constraint media_imaging_kind_check
	      check (imaging_kind is null or imaging_kind in ('liner', 'station_id', 'jingle', 'promo', 'ad'));
	  end if;
	end $$;
	SQL

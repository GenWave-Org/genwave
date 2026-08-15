#!/bin/bash
# 37-visual-layer-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds station.persona_avatar, station.avatar_pack(+_item), station.icon_pack, station.station_image
# (SPEC F128-F131, "The visual layer"; STORY-332, STORY-333, STORY-337, STORY-339, PLAN T290).
# ARCHITECTURE.md "The visual layer" -> "Data model" has this DDL verbatim. Lives in the same station
# schema/role as station.theme/station.font_pack — no new role, no new schema.
#
# station.persona_avatar is the worn face: a 1:1 persona extension (the F33 media_rating precedent —
# bytes live off the hot persona row, so a card/prompt read never drags image bytes along). token is
# UNIQUE and ROTATED on every write (F129.1) — the F88 opaque-token art-transport idiom, so replacing a
# face revokes the old URL and an `immutable` year-cache is safe. source is CHECK-constrained to
# ('upload','catalog') — no open-ended kind churn expected here, unlike booth_log.segment_kind's
# deliberately un-CHECKed token set.
#
# station.avatar_pack(+_item) is the installed-pack library store, mirroring station.font_pack(+_face)'s
# own shape (db/32) almost verbatim: imported_from is NOT NULL (a pack has no authored-in-place path,
# the catalog install route is the only door), avatar_pack_item.pack_id is ON DELETE CASCADE (deleting
# a pack removes its own items with it, never orphaned rows), and (pack_id, name) is UNIQUE — SCOPED
# per-pack, unlike font_pack_face.file's own globally-unique serving key, because an avatar_pack_item
# has no cross-pack serving-key role to protect. suggested_persona is an OFFER (a slug hint the picker
# highlights, T296) never an auto-write — no FK, since the hint may name a persona that does not exist
# or gets renamed/deleted without invalidating the pack.
#
# station.icon_pack is pure jsonb, no binary assets (SPEC F130.1's constrained vector document) —
# mirrors station.theme's own shape (db/31) with font_pack's NOT NULL imported_from (packs only ever
# arrive via catalog install, the same reasoning as avatar_pack above).
#
# station.station_image is a deliberate single-row deviation from every other table in this file: `id
# int primary key default 1 check (id = 1)` makes a second row structurally impossible — the row IS the
# station's own image (gh-#15). token is NOT unique (unlike persona_avatar's) — there is only ever one
# row, so a UNIQUE constraint would be a no-op; it still rotates on every write for the same
# immutable-cache-busting reason.
#
# NO CONSUMER YET for any of the four tables (T290's own scope: schema + thin repositories only, no
# endpoints/controllers) — the same "seam before consumer" way station.theme (db/31, T181) and
# station.font_pack (db/32, T198) already shipped. T291-T296+ wire the image-normalize service and the
# write/read routes.
#
# Safe to run multiple times: CREATE TABLE IF NOT EXISTS is a no-op on an already-migrated database.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	-- the worn face: 1:1 persona extension (F33 media_rating precedent — bytes off the
	-- hot persona row; prompt/card reads never drag image bytes)
	create table if not exists station.persona_avatar (
	  id            serial      primary key,
	  persona_id    int         not null unique references station.persona(id) on delete cascade,
	  bytes         bytea       not null,        -- 512x512 normalized PNG, metadata-free
	  byte_size     int         not null,
	  sha256        text        not null,
	  token         text        not null unique, -- 128-bit hex; ROTATED on every write (F129.1)
	  source        text        not null check (source in ('upload','catalog')),
	  imported_from text,                        -- pack slug or persona-entry slug when source='catalog'
	  updated_at    timestamptz not null default now()
	);

	-- installed avatar packs: the library store (F104 font_pack shape)
	create table if not exists station.avatar_pack (
	  id            serial      primary key,
	  slug          text        not null unique,
	  definition    jsonb       not null,        -- the pack manifest
	  imported_from text        not null,        -- catalog slug (catalog is the only door)
	  imported_at   timestamptz not null default now()
	);
	create table if not exists station.avatar_pack_item (
	  id                serial primary key,
	  pack_id           int    not null references station.avatar_pack(id) on delete cascade,
	  name              text   not null,
	  suggested_persona text,                    -- slug hint; an OFFER, never an auto-write
	  bytes             bytea  not null,
	  byte_size         int    not null,
	  sha256            text   not null,
	  unique (pack_id, name)
	);

	-- installed icon packs: pure jsonb, no binary assets
	create table if not exists station.icon_pack (
	  id            serial      primary key,
	  slug          text        not null unique,
	  definition    jsonb       not null,        -- the constrained vector document (F130.1)
	  imported_from text        not null,
	  imported_at   timestamptz not null default now()
	);

	-- the station image: deliberate single-row deviation from serial pk — the row IS the image
	create table if not exists station.station_image (
	  id         int         primary key default 1 check (id = 1),
	  bytes      bytea       not null,
	  byte_size  int         not null,
	  sha256     text        not null,
	  token      text        not null,           -- rotated on write; busts immutable caches
	  updated_at timestamptz not null default now()
	);
	SQL

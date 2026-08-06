#!/bin/bash
# 32-font-pack-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds station.font_pack + station.font_pack_face (SPEC F104 "The wardrobe workshop"; STORY-282,
# PLAN T198): the library's first per-kind store, Dean-curated font packs installed from the
# Community Catalog's `font` kind (ARCHITECTURE.md "The wardrobe workshop" -> "Data model" has this
# DDL verbatim). Lives in the same station schema/role as station.theme/station.persona/
# station.settings — no new role, no new schema. definition holds the raw catalog pack manifest
# jsonb a caller (GenWave.Host, downstream of this GenWave.Core seam) (de)serializes at its own
# edge, mirroring station.theme's own definition column (db/31) exactly. imported_from is NOT NULL
# here (unlike station.theme's nullable column) — packs have no authored-in-place path, the catalog
# install route is the only door a pack ever arrives through.
#
# NO CONSUMER YET: IFontPackStore ships dark — POST /api/fonts/{slug}/install (T199) is the first
# write consumer, InstalledFontCatalog (T199/T200) and the library page (T203) the first read
# consumers. The same "seam before consumer" way station.theme (db/31, T181) and
# station.persona_taste (db/06, T59) already shipped.
#
# db/32 follows db/31 (theme store) — the next migration number in the same catalog-v2/wardrobe
# track.
# Safe to run multiple times: CREATE TABLE IF NOT EXISTS is a no-op on an already-migrated database.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	create table if not exists station.font_pack (
	  id            serial      primary key,
	  slug          text        not null unique,        -- catalog entry slug
	  family        text        not null,               -- CSS family name, e.g. 'Space Grotesk'
	  definition    jsonb       not null,                -- the pack manifest (provenance, licence, files)
	  imported_from text        not null,                -- catalog slug (packs ONLY arrive via install)
	  imported_at   timestamptz not null default now(),
	  created_at    timestamptz not null default now()
	);

	create table if not exists station.font_pack_face (
	  id        serial primary key,
	  pack_id   int    not null references station.font_pack(id) on delete cascade,
	  file      text   not null unique,                  -- '/fonts/<file>' basename; the serving key
	  style     text   not null default 'normal',         -- 'normal' | 'italic'
	  bytes     bytea  not null,                          -- the latin-subsetted woff2 (<=200 KiB/pack by CI)
	  byte_size int    not null,
	  sha256    text   not null                           -- pinned at install from the index
	);
	SQL

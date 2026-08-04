#!/bin/bash
# 31-theme-store-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds station.theme (SPEC F103.7, F103.8; STORY-271, PLAN T181): owner-imported theme storage for
# the Community Catalog v2 theme kind (ARCHITECTURE "Community Catalog v2" -> "Data model" has this
# DDL verbatim). Lives in the same station schema/role as station.persona/station.settings — no new
# role, no new schema. definition holds the byte-stable ThemeManifest (GenWave.Host.Theming) a
# caller (ThemeManifestSerializer/ThemeManifestParser) (de)serializes at its own edge; imported_from/
# imported_at mirror station.persona's own db/25 provenance columns exactly (catalog entry slug |
# 'file' | NULL for authored-in-place).
#
# NO CONSUMER YET: ThemeCatalog does not read this table until T182 wires it, and no route writes to
# it until T184 (the import endpoint). The IThemeStore repository this table backs ships the same
# "seam before consumer" way station.persona_taste (db/06, T59) and station.segment_schedule (db/27,
# T118) already did.
#
# db/31 follows db/30 (imaging-kind) — an unrelated audio column; the catalog `kind` seam is a
# distinct track that happens to land the next migration number.
# Safe to run multiple times: CREATE TABLE IF NOT EXISTS is a no-op on an already-migrated database.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	create table if not exists station.theme (
	  id            serial      primary key,
	  slug          text        not null unique,
	  definition    jsonb       not null,
	  imported_from text,
	  imported_at   timestamptz,
	  created_at    timestamptz not null default now()
	);
	SQL

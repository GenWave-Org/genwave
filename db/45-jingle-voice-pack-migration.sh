#!/bin/bash
# 45-jingle-voice-pack-migration.sh — idempotent in-place upgrade for existing DBs.
# Jingle packs + voice packs (gh-#709, SPEC F164-F170, STORY-395..405, PLAN T410). ARCHITECTURE.md
# "The jingle & voice pack epic" -> "Data model (db/45; fresh-init mirrors in db/06 + 01-library.sh)"
# has this DDL verbatim; this script is that DDL made idempotent for an existing box. Fresh installs
# never see this numbered script — they get the identical schema through
# docker-entrypoint-initdb.d via db/06-station-settings-migration.sh (station schema) and
# db/01-library.sh (library schema additions), the gh-#618 lesson every migration in this directory
# repeats: a migration without its fresh-init mirror haunts the next box that installs clean rather
# than upgrades.
#
# ONE reconciliation against ARCHITECTURE.md's own DDL sketch: its `voice_pack_voice.pack_id` is
# written `int`, but the column it references, `voice_pack.id`, is `bigint generated always as
# identity` — a foreign-key column must match the type of the column it references, so `pack_id` is
# `bigint` here (and in both fresh-init mirrors). Every other column, type, default, CHECK, and
# index name below is ARCHITECTURE.md's DDL unchanged.
#
# station.voice_pack — voice-pack metadata (SPEC F164.1); `.pt` bytes live on the shared `voices`
# volume (F166.1), NOT in Postgres — mirrors station.font_pack's shape without a bytes column
# because the volume, not the database, is kokoro's load-bearing store. `engine` is checked against
# the station's active TTS engine at install (F164.2, `not_supported_engine` 400) and widens
# additively at gh-#614; `'kokoro'` is the only accepted value this cycle. `definition` holds the
# byte-stable manifest jsonb (F164.1) so a re-install or rollback never loses data. `imported_from`
# is the catalog slug that produced the row — packs arrive only through install, so it is always
# known (NOT NULL), same posture as station.font_pack.imported_from.
#
# station.voice_pack_voice — the roster of voice_ids a voice pack ships (SPEC F164.1/F164.5): one
# row per voice, FK'd to its owning pack with ON DELETE CASCADE so uninstall (F164.6) removes the
# whole roster with the pack row. `file` is the `/voices/<pack_slug>/<voiceId>.pt` path install
# (PLAN T413) writes; kokoro's own per-request rescan (SPEC F166.3) makes a newly-installed voice
# live on the very next render, no restart. `voice_id` carries the collision fence (SPEC F166.4,
# enforced at install by PLAN T413): a voice_id may live in only ONE installed pack at a time, or
# kokoro would serve whichever `.pt` its directory scan happened to see last — nondeterministic. The
# fence is a separate named UNIQUE INDEX (not an inline column constraint) because ARCHITECTURE.md's
# own DDL names it explicitly (`voice_pack_voice_voice_id_uk`) as a citable schema object.
#
# station.jingle_pack — jingle-pack metadata (SPEC F165.1); audio bytes land as library.media rows
# on the `/authored` volume, not in Postgres. There is deliberately NO `_asset` table: the assets
# ARE library rows, and the ad worker's bed-pool query (F168.1) is a single-schema read against the
# same pool the imaging-kind rotation fence already governs (F158.4) — REJECTED alternative:
# station.jingle_pack_asset (a font_pack_face mirror) with a plain-bigint media_id back into
# library.media, which would add a table AND a cross-schema join the ad worker's pool query would
# then need, when the two library.media columns below already carry everything that join would buy.
# `definition` carries the manifest including each asset's `role` and, for a CC-BY asset, its
# structured per-asset attribution object (SPEC F165.4) — read back by the F169.2 attributions
# endpoint (PLAN T419) with NO separate station.attribution table (pack manifests are the source of
# truth; a mirror table would drift, per that ruling).
#
# library.media.jingle_role / library.media.pack_slug (SPEC F165.2/F165.3, written by jingle-pack
# install PLAN T414) — plain `ADD COLUMN IF NOT EXISTS` (the house idiom this directory already uses
# ~70 times, e.g. db/30-imaging-kind-migration.sh). `jingle_role`'s CHECK is SPEC F165.3's closed set
# (`bed`/`sting`/`station_id`); NULL means "not a jingle-pack row" (every scanned row, every other
# imaging_kind). `pack_slug` carries no CHECK — any slug the catalog transport already validated at
# install is valid here; it is not a REFERENCES constraint either, the same db/22 schema-role
# boundary rule that already keeps show_id and bed_media_id/media_id as plain values (station_svc
# has no grant into the library schema, library_svc none into station).
#
# library.media_pack_slug_title_key — an addition BEYOND ARCHITECTURE.md's own column list, because
# SPEC F165.5 names the jingle-pack install's own upsert key as `(pack_slug, title)`
# (`ON CONFLICT (pack_slug, title)` on re-install) and no unique constraint on that pair existed
# anywhere — without one, T414's own upsert would fail at runtime with "there is no unique or
# exclusion constraint matching the ON CONFLICT specification". Plain UNIQUE, not
# `NULLS NOT DISTINCT`: every non-pack row carries `pack_slug` NULL, and plain UNIQUE already treats
# every NULL as its own distinct value, so two unrelated NULL-pack_slug rows sharing a title never
# collide; only rows sharing a REAL pack_slug are constrained to unique titles within that pack —
# exactly F165.5's key, no broader. The same index also gives F165.6's uninstall-by-pack_slug scan
# something to use.
#
# `slug` on both pack tables and `voice_id` on voice_pack_voice each carry a defense-in-depth CHECK:
# both become filesystem path segments at install (PLAN T413/T414 write
# `/voices/{slug}/{voiceId}.pt` and `/authored/jingle-packs/{slug}/{file}`), so the schema itself
# refuses a value that could ever read as a path separator or a traversal segment. The pattern is
# deliberately NO STRICTER than the app's own gate,
# `GenWave.Host.Api.CatalogInstallShell.SlugFormat` (`\A[a-z0-9]+(-[a-z0-9]+)*\z`, composed from
# `CatalogIndexValidator.SlugSegment`) — the database must never reject a slug the app already
# accepted, so the DB pattern (`^[a-z0-9][a-z0-9-]*$`) is a strictly looser superset (it also admits
# a trailing hyphen or a doubled hyphen the app-side regex itself refuses, which is fine — a belt,
# not a second identical buckle). `voice_id` has no existing app-side regex to match against yet
# (PLAN T413, the first voice-pack install route, has not landed) — the CHECK admits kokoro's own
# stock id shape (`af_nova`, `am_adam`: lowercase, digits, underscore, hyphen).
#
# Safe to run multiple times: every CREATE is IF NOT EXISTS, both ADD COLUMNs are IF NOT EXISTS, and
# the new UNIQUE constraint is guarded by a pg_constraint existence check inside a DO block — the
# same idiom db/43-ad-spot-invariants-migration.sh's own two ALTER TABLE ADD CONSTRAINTs already
# establish (Postgres has no `ADD CONSTRAINT IF NOT EXISTS`).
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

# --- station schema: voice_pack, voice_pack_voice, jingle_pack ----------------------------------
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	create table if not exists station.voice_pack (
	  id            bigint generated always as identity primary key,
	  slug          text not null unique check (slug ~ '^[a-z0-9][a-z0-9-]*$'),
	  engine        text not null check (engine in ('kokoro')),   -- widens additively at gh-#614
	  definition    jsonb not null,                                -- the byte-stable manifest
	  imported_from text not null,                                 -- catalog slug (packs only via install)
	  imported_at   timestamptz not null default now(),
	  created_at    timestamptz not null default now()
	);

	create table if not exists station.voice_pack_voice (
	  id           bigint generated always as identity primary key,
	  pack_id      bigint not null references station.voice_pack(id) on delete cascade,
	  voice_id     text not null check (voice_id ~ '^[a-z0-9][a-z0-9_-]*$'), -- kokoro id, unique across ALL installed
	  file         text not null,                       -- /voices/<pack_slug>/<voice_id>.pt
	  gender_hint  text,
	  age_hint     text,
	  preview_sha  text                                 -- clip hash, informational (bytes live in catalog)
	);
	create unique index if not exists voice_pack_voice_voice_id_uk on station.voice_pack_voice(voice_id);

	create table if not exists station.jingle_pack (
	  id            bigint generated always as identity primary key,
	  slug          text not null unique check (slug ~ '^[a-z0-9][a-z0-9-]*$'),
	  definition    jsonb not null,                                -- manifest incl. per-asset attributions
	  imported_from text not null,                                 -- catalog slug
	  imported_at   timestamptz not null default now(),
	  created_at    timestamptz not null default now()
	);
	SQL

# --- library schema: jingle_role + pack_slug columns, and the F165.5 upsert-key unique constraint --
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role library_svc;
	set search_path = library;

	alter table library.media
	  add column if not exists jingle_role text
	    check (jingle_role is null or jingle_role in ('bed', 'sting', 'station_id'));
	alter table library.media
	  add column if not exists pack_slug text;

	-- No "add constraint if not exists" in Postgres — guarded the same pg_catalog-existence way
	-- db/43's own two ALTER TABLE ADD CONSTRAINTs already are (see that script's own header).
	do $$
	begin
	  if not exists (
	    select 1 from pg_constraint
	    where conname = 'media_pack_slug_title_key' and conrelid = 'library.media'::regclass
	  ) then
	    alter table library.media
	      add constraint media_pack_slug_title_key unique (pack_slug, title);
	  end if;
	end $$;
	SQL

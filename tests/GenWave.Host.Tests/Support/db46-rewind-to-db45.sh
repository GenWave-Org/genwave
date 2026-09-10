#!/bin/bash
# TEST-ONLY fixture, not a real migration. db46-rewind-to-db45.sh — takes an EphemeralStationDatabase
# started from db/06's sponsor-era fresh-init (round-2 finding F4) and tears the sponsor-specific
# shape back down to db/45-era: the shape any pre-sponsors box actually has before db/46 ever runs.
# Story414_UpgradePathArc runs this FIRST, seeds a realistic pre-migration fixture into the resulting
# tables, then runs the REAL db/46-sponsor-migration.sh against it — the only way to prove the
# upgrade path (as opposed to the end-state shape Story406 already pins) without hand-rolling a
# second, drift-prone copy of db/46's own DDL inside a temp table.
#
# Exact inverse of db/46's own steps 0/1..14, run in dependency order (children before parents, FKs/
# indexes before the columns/tables they reference): station.show's sponsor_id first (index -> FK ->
# column), then ad_spot's preview/job columns and its sponsor_id/sponsor_name (rename back to
# brand), then ad_brief's sponsor_id/premise_key (restore brand + the OLD
# ad_brief_pack_slug_brand_key constraint db/46 step 3 drops), then station.sponsor itself, then
# station.sponsor_fold(text) (db/46 step 0) last of all, once nothing references it any more.
#
# ad_brief.brand is re-added as NOT NULL directly (no DEFAULT): a fresh-init EphemeralStationDatabase
# has zero rows in ad_brief/ad_spot at this point, so there is no existing row for a NOT NULL column
# add to violate — confirmed against a real Postgres 16.4 container while building this script.
#
# One BEGIN/COMMIT, matching db/46's own posture (round-2 finding F2): this script only ever runs
# once, at the top of an Arc's InitializeAsync, so an aborted rewind should never leave a half-torn
# schema for the real db/46 run that follows to trip over.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	BEGIN;

	set role station_svc;
	set search_path = station;

	-- station.show: sponsor_id did not exist pre-db/46 (index -> FK -> column).
	DROP INDEX IF EXISTS station.show_sponsor_id;
	ALTER TABLE station.show DROP CONSTRAINT IF EXISTS show_sponsor_id_fkey;
	ALTER TABLE station.show DROP COLUMN IF EXISTS sponsor_id;

	-- station.ad_spot: preview/job columns did not exist pre-db/46.
	ALTER TABLE station.ad_spot DROP COLUMN IF EXISTS preview_path;
	ALTER TABLE station.ad_spot DROP COLUMN IF EXISTS preview_at;
	ALTER TABLE station.ad_spot DROP COLUMN IF EXISTS preview_key;
	ALTER TABLE station.ad_spot DROP COLUMN IF EXISTS job_kind;
	ALTER TABLE station.ad_spot DROP COLUMN IF EXISTS job_started_at;
	ALTER TABLE station.ad_spot DROP COLUMN IF EXISTS job_error;

	-- sponsor_name did not exist pre-db/46 — it is db/46's own rename of the original brand column.
	DO $$
	BEGIN
	  IF EXISTS (
	    SELECT 1 FROM pg_attribute
	    WHERE attrelid = 'station.ad_spot'::regclass
	      AND attname = 'sponsor_name' AND attnum > 0 AND NOT attisdropped
	  ) THEN
	    ALTER TABLE station.ad_spot RENAME COLUMN sponsor_name TO brand;
	  END IF;
	END $$;

	DROP INDEX IF EXISTS station.ad_spot_sponsor_id;
	ALTER TABLE station.ad_spot DROP CONSTRAINT IF EXISTS ad_spot_sponsor_id_fkey;
	ALTER TABLE station.ad_spot DROP COLUMN IF EXISTS sponsor_id;

	-- station.ad_brief: sponsor_id/premise_key/the new unique constraint did not exist pre-db/46 —
	-- restore brand + the OLD ad_brief_pack_slug_brand_key constraint db/46 step 3 dropped.
	DROP INDEX IF EXISTS station.ad_brief_pack_slug_sponsor_id_key;
	ALTER TABLE station.ad_brief DROP CONSTRAINT IF EXISTS ad_brief_sponsor_id_premise_key;
	ALTER TABLE station.ad_brief DROP CONSTRAINT IF EXISTS ad_brief_sponsor_id_fkey;
	ALTER TABLE station.ad_brief DROP COLUMN IF EXISTS sponsor_id;
	ALTER TABLE station.ad_brief DROP COLUMN IF EXISTS premise_key;

	-- The table is empty on a fresh-init ephemeral database (no rows exist yet to violate NOT NULL),
	-- so no DEFAULT is needed to add a NOT NULL column back.
	ALTER TABLE station.ad_brief ADD COLUMN IF NOT EXISTS brand text NOT NULL;

	DO $$
	BEGIN
	  IF NOT EXISTS (
	    SELECT 1 FROM pg_constraint
	    WHERE conname = 'ad_brief_pack_slug_brand_key' AND conrelid = 'station.ad_brief'::regclass
	  ) THEN
	    ALTER TABLE station.ad_brief
	      ADD CONSTRAINT ad_brief_pack_slug_brand_key UNIQUE NULLS NOT DISTINCT (pack_slug, brand);
	  END IF;
	END $$;

	-- station.sponsor did not exist pre-db/46 — drop it last, now that nothing references it.
	DROP TABLE IF EXISTS station.sponsor;

	-- station.sponsor_fold(text) (round-3 finding N1) did not exist pre-db/46 either — drop it only
	-- after every column that calls it (station.sponsor.name_key, station.ad_brief.premise_key) is
	-- already gone above; a STORED generated column referencing this function makes the function
	-- itself a dependency Postgres would otherwise refuse to drop out from under.
	DROP FUNCTION IF EXISTS station.sponsor_fold(text);

	COMMIT;
	SQL

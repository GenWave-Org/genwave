#!/bin/bash
# 46-sponsor-migration.sh — idempotent in-place upgrade for existing DBs.
# Sponsors epic (gh-#714, SPEC F171–F176, STORY-406/411/414/430, PLAN T431). ARCHITECTURE.md
# "# 🤝 Sponsors — the customer behind every ad (gh-#714 · designed 2026-09-08)" -> "## Data
# model (db/46; fresh-init mirrors in db/06)" has this DDL's sketch; this script is the refined,
# idempotent version, and db/06 carries the schema-only mirror for a fresh install — the gh-#618
# lesson every earlier migration in this directory follows: a migration without its fresh-init
# mirror haunts the next box that installs clean rather than upgrades.
#
# THIS IS THE FIRST MIGRATION IN THIS DIRECTORY TO DROP OR RENAME A COLUMN (round-3 finding N5 —
# narrowed from "the first destructive migration": db/27-segment-schedule-migration.sh already
# DELETES a settings row as part of its own seed-and-delete data migration, so "destructive" alone
# was already false). It drops ad_brief.brand outright (step 8) after backfilling sponsor_id from
# it, and renames ad_spot.brand to sponsor_name (step 12); the backfill can itself hit an over-length
# or otherwise-invalid brand mid-run. This migration's OWN steps are what make a partial run
# destructive on its own terms — regardless of any other file in this directory: it DROPs a column,
# RENAMEs another, and rewrites every existing row via UPDATE (the sponsor_id backfill). A crash
# between step 8's `DROP COLUMN ad_brief.brand` and step 9's `ADD CONSTRAINT` would leave `brand`
# gone while the brand-guarded backfill/dedupe pass (step 5) is unreachable on the retry, so the
# unique constraint could never be built — so the whole body below runs inside one BEGIN/COMMIT:
# DDL is fully transactional in Postgres (unlike MySQL), so either every step lands or none do —
# never a torn schema with ad_brief.brand already
# gone and the new unique constraint still unbuildable. `SET ROLE`/`SET search_path` inside a
# transaction are ordinary session-GUC assignments, not DDL, and roll back with everything else if
# the transaction aborts.
#
# station.sponsor_fold(text) (round-3 finding N1) — the ONE fold definition, created once as an
# IMMUTABLE SQL function (step 0, below) rather than hand-copied at every call site. Round-1 shipped
# `lower(regexp_replace(btrim(...), '\s+', ' ', 'g'))` — TRIM FIRST, collapse second. Bare `btrim(x)`
# strips SPACES ONLY (it is exactly `btrim(x, ' ')` — the optional second argument, when omitted,
# still defaults to a single space, never "every Unicode whitespace character"), so a leading TAB
# survives that first btrim untouched, and only THEN gets collapsed by regexp_replace into a leading
# SPACE that nothing trims away afterward — `E'\tAcme Corp'` folded to `' acme corp'` (a second,
# spurious sponsor for the same advertiser), a whitespace-only premise like `E'\t'` folded to `' '`
# (not empty, so `nullif(..., '')` never fired), and two whitespace-only premises under one sponsor
# could collide on that same non-empty `' '` premise_key and abort the whole migration. The fix
# collapses FIRST (`regexp_replace(lower($1), '\s+', ' ', 'g')`) and trims SECOND (the outer
# `btrim`) — the order is the whole point: any run of whitespace touching an edge collapses into a
# single edge-space, which the outer btrim then removes; only a run strictly between two non-
# whitespace characters survives as an internal single space.
#
# station.sponsor — the master entity for every advertiser (SPEC F171.1): one row per distinct
# (pack_slug, folded brand name) pair. `name_key` is a STORED generated column calling
# station.sponsor_fold(name) — the ONE fold definition every backfill join and spec text pin
# reuses — so the UNIQUE NULLS NOT DISTINCT (pack_slug, name_key) constraint collapses
# mixed-case/extra-space variants at the database level, never at the application level. NULL
# pack_slug = owner-created sponsor; non-null = installed by an ad pack (SPEC F171.1).
#
# Changes to station.ad_brief (SPEC F172.1):
#   • ADD sponsor_id bigint → backfill → NOT NULL + FK ON DELETE RESTRICT
#   • ADD premise_key GENERATED ALWAYS AS (nullif(station.sponsor_fold(premise), '')) STORED — NULL
#     when premise is NULL/blank (round-2 finding F2): a brief with no angle carries no premise_key,
#     so the UNIQUE (sponsor_id, premise_key) constraint below never collides two angle-less briefs
#     on the same sponsor. SPEC F171.6: "several angles per sponsor are legal, the same angle twice
#     is a 409" — a NULL premise is not an angle, so it can never BE "the same angle twice".
#   • DROP constraint ad_brief_pack_slug_brand_key (replaced by ad_brief_sponsor_id_premise_key)
#   • DROP brand column (brand now lives only on station.sponsor.name via sponsor_id)
#   • ADD UNIQUE (sponsor_id, premise_key) — plain UNIQUE, deliberately NOT "NULLS NOT DISTINCT":
#     Postgres already treats every NULL premise_key as distinct from every other NULL under a
#     plain UNIQUE, which is exactly the "no angle collides with no angle" behaviour above.
#   • ADD partial UNIQUE INDEX (pack_slug, sponsor_id) WHERE pack_slug IS NOT NULL (round-3 finding
#     R1, SPEC F2/F3): a pack declares exactly ONE brief per sponsor — IAdBriefStore.UpsertAllAsync's
#     own (packSlug, sponsorId) upsert key. A raw index, not a constraint: a CONSTRAINT cannot be
#     partial, and an owner-authored sponsor legitimately keeps several angles (premise_key is what
#     scopes THAT uniqueness), so the cap belongs on pack rows only.
#
# Changes to station.ad_spot (SPEC F171.7; migration order per F172.1):
#   • ADD sponsor_id bigint → backfill → NOT NULL + FK ON DELETE RESTRICT
#   • RENAME brand → sponsor_name (sponsor entity has the canonical name; spot carries a
#     snapshot written at creation and refreshed on PATCH when the sponsor changes, so the
#     booth log, ICY title, and spot title keep saying what aired even after a rename)
#   • ADD preview_path, preview_at, preview_key (SPEC F174.4) and job_kind, job_started_at,
#     job_error (SPEC F174.2) — the guided spot dialog's preview + single-slot job columns.
#
# Changes to station.show (SPEC F175.1, STORY-430):
#   • ADD sponsor_id bigint (nullable FK — shows optionally carry a sponsor)
#
# Backfill: one sponsor per distinct (pack_slug, folded brand) over ad_brief ∪ ad_spot, earliest-
# created spelling wins as the canonical name. `norm_name` — the ONLY place a whitespace-collapsing
# expression appears in this file OUTSIDE station.sponsor_fold itself (round-3 finding N1's third
# consequence / N6's drift tripwire), because it must PRESERVE CASE (it builds the display name, not
# a lookup key) — normalises every source row's brand exactly once, into a session-scoped TEMP
# TABLE, before either the sponsor INSERT or the two backfill UPDATEs run:
# `coalesce(nullif(left(btrim(regexp_replace(brand, '\s+', ' ', 'g')), 120), ''), 'Unnamed
# sponsor')`. Round-2 finding F3: `brand` was bare `text not null` pre-migration (catalog manifests
# allow up to 200 chars; controllers only `.Trim()`), but `station.sponsor.name`'s own CHECK caps at
# 120 (SPEC F171.1) — this makes the backfill TOTAL: over-length brands are truncated (never abort
# the whole migration on one bad row), a whitespace-only brand becomes a placeholder name rather
# than an empty string the CHECK would reject outright, and — round-4 finding P1's third case —
# two briefs that could only ever collide on the OLD, pre-migration LITERAL (pack_slug, brand)
# constraint (never on folded brand+premise, which that old constraint never checked at all) are
# folded onto the very same sponsor and kept alive rather than aborting the whole migration on the
# NEW ad_brief_sponsor_id_premise_key constraint (step 9) — see the dedupe-pass paragraph below.
# Every DISTINCT ON / ORDER BY / UPDATE-join key below folds this SAME normalised `norm_name` — via
# `station.sponsor_fold(norm_name)`, never a second hand-copied collapse expression — so the
# grouping key always matches what was actually inserted. INSERT ON CONFLICT DO NOTHING → UPDATE
# WHERE IS NULL makes every phase idempotent. The backfill DO block guards itself by checking
# whether the `brand` column still exists on station.ad_brief — the one column whose existence
# unambiguously signals "we are in the pre-migration state". On a second run (brand already
# dropped), the DO block is skipped entirely, and all subsequent IF-guarded steps are no-ops too.
#
# Dedupe pass (round-4 finding P1): pre-migration uniqueness on ad_brief lived on LITERAL
# (pack_slug, brand) — it never stopped two briefs like (NULL, 'Al''s Diner', 'Open at six') and
# (NULL, 'AL''S DINER', '  Open  At  Six ') coexisting; both fold onto the SAME sponsor AND the
# SAME premise_key, which step 9's ADD CONSTRAINT ad_brief_sponsor_id_premise_key would otherwise
# refuse, aborting the whole transaction and leaving migrate.sh — and launch.sh's pinned flow —
# stopped before the api ever comes up: exactly the dirty data this epic exists to consolidate.
# Policy: keep, disable, disambiguate — never delete, never abort. Immediately after the
# ad_brief/ad_spot sponsor_id backfill above and before step 9's constraint, every group of briefs
# sharing (sponsor_id, station.sponsor_fold(premise)) — skipping groups where that fold is
# empty/NULL, since a blank premise is no angle (step 2's own note) — keeps its earliest-created
# row (ORDER BY created_at, id) untouched as the "keeper", and disables (enabled = false) every
# later row in the group, suffixing its premise with a note naming the keeper's id AND its own id
# (the keeper's id alone is the SAME for every duplicate in a group of three or more — proven by
# this file's own three-duplicate test fixture, where a keeper-id-only suffix folded two disabled
# rows to the identical premise_key and step 9's own ADD CONSTRAINT refused it; each duplicate's
# own id is what is actually unique per row). The STORED premise_key column then re-derives from
# this new premise text to a value that no longer collides with the keeper's or any sibling
# duplicate's, so step 9's constraint builds cleanly. DELETE was deliberately NOT
# chosen: a customer's own data, present on a box that is merely upgrading, is the owner's to
# delete, not this script's. RAISE EXCEPTION was deliberately NOT chosen either: STORY-414 frames
# this as "the upgrade is a boring restart, not a data-loss incident" — aborting the whole
# migration over one duplicate angle would turn that boring restart into a stuck launch.sh. A
# RAISE NOTICE per disabled row (id, keeper id, sponsor name, original premise) during the run
# makes migrate.sh's own console output say exactly what happened.
#
# Pack-identity dedupe pass (round-4 finding R1', supersedes round-3 finding R1, SPEC F2/F3): runs
# FIRST, immediately after the backfill's two UPDATEs and BEFORE the angle-collision pass above —
# a DIFFERENT policy AND a different grouping key. The angle-collision pass above keeps, disables,
# and disambiguates the OWNER's own data (never delete, never abort — it is the owner's to keep or
# remove). This pass DELETES the losing row outright instead: no disable, no premise suffix. Why a
# different policy for pack rows: a pack brief is catalog content, not owner-authored data — a
# reinstall regenerates it via IAdBriefStore.UpsertAllAsync's own ON CONFLICT (packSlug, sponsorId)
# upsert, and nothing references ad_brief.id by foreign key (ad_spot.brief is a text snapshot taken
# at creation; ad_spot.sponsor_id points at station.sponsor, never at any ad_brief row) — so there
# is no owner-visible state a delete could destroy. More to the point, disabling CANNOT satisfy the
# NEW ad_brief_pack_slug_sponsor_id_key partial index (step 9b, below): that index deliberately
# carries no `enabled` predicate — it can't, because an `enabled`-scoped index would let a pack
# reinstall's ON CONFLICT insert a second, ENABLED row beside a duplicate this pass merely disabled,
# breaking the T405 "reinstall preserves enabled" ruling — so a disabled duplicate would still
# collide on this index exactly as an enabled one would; only removing the row (not disabling it)
# can make two pack rows sharing a (pack_slug, sponsor_id) slot stop colliding on it. Grouped by
# (pack_slug, sponsor_id) alone — WHERE pack_slug IS NOT NULL, ignoring premise entirely, since
# sharing a slot (not identical wording) is what makes two pack rows duplicates here — keeps its
# earliest-created row (ORDER BY created_at, id) as the "keeper", untouched; every later row in the
# group is deleted. Because a deleted row is simply gone, it can never appear in the angle-collision
# pass's own SELECT below, so that pass needs no matching guard against this one; its own `WHERE
# enabled` (below) guards a different thing now — see that pass's own paragraph. Idempotent on a
# second run: with the losing rows already gone, this pass's own SELECT finds nothing left to
# delete.
#
# Idempotency: CREATE TABLE IF NOT EXISTS; ADD COLUMN IF NOT EXISTS; DROP COLUMN IF EXISTS;
# pg_constraint existence checks for every ADD/DROP CONSTRAINT; pg_attribute existence check for
# the RENAME COLUMN (RENAME has no IF EXISTS); is_nullable check for the SET NOT NULL steps;
# ON CONFLICT DO NOTHING + WHERE IS NULL for the backfill; CREATE OR REPLACE for the fold function
# itself. Every step below is idempotent BY ITSELF via these guards, AND the whole run is one
# transaction (above) — belt and braces: the guards make a full second run a clean no-op, the
# transaction makes a FAILED run (first or second) leave nothing torn to guard against in the first
# place.
#
# Safe to run multiple times against any DB initialised before this migration was added, and safe
# as a no-op against any DB initialised FROM db/06's sponsor-era fresh-init mirror (which already
# carries the final schema).
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	BEGIN;

	set role station_svc;
	set search_path = station;

	-- ── Step 0: station.sponsor_fold(text) — the ONE fold definition (round-3 finding N1) ─────────
	-- COLLAPSE FIRST (regexp_replace), TRIM SECOND (the outer btrim) — see this file's own header
	-- for why the order is the whole point. IMMUTABLE + PARALLEL SAFE: required for use inside a
	-- STORED generated column (station.sponsor.name_key, station.ad_brief.premise_key, both below)
	-- and lets the planner use it under parallel query execution. RETURNS NULL ON NULL INPUT: a NULL
	-- name/premise folds to NULL, not the fold of an empty string — a caller that needs '' for a
	-- NOT NULL fold instead wraps with coalesce(station.sponsor_fold(x), '') at its own call site
	-- (name_key does not need to: `name` is itself NOT NULL, so station.sponsor_fold(name) alone can
	-- never see a NULL input). CREATE OR REPLACE: idempotent, same as every other object below.
	-- LANGUAGE sql, not plpgsql: a deliberate, narrow exception — the body is a single SELECT
	-- expression with no branching or set-based logic, and Story414 AC5(a) text-pins this exact
	-- one-line body (a plpgsql RETURN wrapper would add object surface without changing behaviour,
	-- and would break that pin's literal string match for no benefit).
	CREATE OR REPLACE FUNCTION station.sponsor_fold(text) RETURNS text
	  LANGUAGE sql IMMUTABLE PARALLEL SAFE RETURNS NULL ON NULL INPUT
	  AS $$ SELECT btrim(regexp_replace(lower($1), '\s+', ' ', 'g')) $$;

	-- ── Step 1: station.sponsor ──────────────────────────────────────────────────────────────────
	-- name_key is a STORED generated column calling station.sponsor_fold(name) — the fold
	-- definition every spec text pin asserts lives in ONE place (step 0 above). The UNIQUE NULLS NOT
	-- DISTINCT constraint on (pack_slug, name_key) means two owner-authored sponsors with the same
	-- folded name are impossible, and pack-namespaced sponsors are isolated from owner sponsors
	-- (PG15+, this stack pins 16.4). tagline/about/phone/address/website/tone are optional profile
	-- fields (SPEC F171.1).
	CREATE TABLE IF NOT EXISTS station.sponsor (
	  id         bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
	  name       text        NOT NULL CHECK (char_length(trim(name)) BETWEEN 1 AND 120),
	  name_key   text GENERATED ALWAYS AS (station.sponsor_fold(name)) STORED,
	  pack_slug  text,   -- nullable-fk-equivalent: NULL = owner-created, non-null = ad-pack-installed
	  tagline    text CHECK (tagline IS NULL OR char_length(tagline) <= 160),
	  about      text CHECK (about IS NULL OR char_length(about) <= 600),
	  phone      text CHECK (phone IS NULL OR char_length(phone) <= 40),
	  address    text CHECK (address IS NULL OR char_length(address) <= 200),
	  website    text CHECK (website IS NULL OR (website ~ '^https?://' AND char_length(website) <= 200)),
	  tone       text CHECK (tone IS NULL OR char_length(tone) <= 120),
	  paused     boolean     NOT NULL DEFAULT false,
	  paused_at  timestamptz,
	  created_at timestamptz NOT NULL DEFAULT now(),
	  updated_at timestamptz NOT NULL DEFAULT now(),
	  CONSTRAINT sponsor_pack_slug_name_key UNIQUE NULLS NOT DISTINCT (pack_slug, name_key)
	);

	-- ── Step 2: add sponsor_id + premise_key to ad_brief (nullable for now — backfill fills them) ─
	-- premise_key calls the SAME station.sponsor_fold(premise) as name_key above, wrapped in
	-- `nullif(..., '')` so a NULL/blank premise stores a NULL premise_key rather than the fold of an
	-- empty string (round-2 finding F2) — the UNIQUE (sponsor_id, premise_key) constraint below then
	-- only ever collapses two REAL angles that fold the same way, never two angle-less briefs.
	-- Story414 AC5's text-pin spec asserts the exact station.sponsor_fold function body (step 0)
	-- appears literally in this file:
	--   btrim(regexp_replace(lower($1), '\s+', ' ', 'g'))
	ALTER TABLE station.ad_brief
	  ADD COLUMN IF NOT EXISTS sponsor_id bigint;
	ALTER TABLE station.ad_brief
	  ADD COLUMN IF NOT EXISTS premise_key text
	    GENERATED ALWAYS AS (nullif(station.sponsor_fold(premise), '')) STORED;

	-- ── Step 3: drop old unique constraint on ad_brief (replaced in step 9) ─────────────────────
	DO $$
	BEGIN
	  IF EXISTS (
	    SELECT 1 FROM pg_constraint
	    WHERE conname = 'ad_brief_pack_slug_brand_key' AND conrelid = 'station.ad_brief'::regclass
	  ) THEN
	    ALTER TABLE station.ad_brief DROP CONSTRAINT ad_brief_pack_slug_brand_key;
	  END IF;
	END $$;

	-- ── Step 4: add sponsor_id to ad_spot (nullable for now — backfill fills it) ─────────────────
	ALTER TABLE station.ad_spot
	  ADD COLUMN IF NOT EXISTS sponsor_id bigint;

	-- ── Step 5: backfill station.sponsor + ad_brief.sponsor_id + ad_spot.sponsor_id ──────────────
	-- Guarded by checking whether station.ad_brief.brand still exists: the column's presence is the
	-- unambiguous signal that we are in the pre-migration state. On a second run, brand is already
	-- gone (step 8 dropped it) and this entire block is skipped. Every statement in the IF branch
	-- below is PLAIN SQL, not dynamic (round-4 finding P3 — the earlier EXECUTE $backfill$…$backfill$
	-- wrapping was unnecessary and its own justifying comment was false): PL/pgSQL prepares each SQL
	-- statement lazily, on that statement's own first execution, not all at once when the function
	-- itself is parsed — a statement inside a branch that never runs is never prepared, so a
	-- reference to a column that may not exist by then never gets the chance to fail. Proven against
	-- a real Postgres 16.4: `DO $$ BEGIN IF false THEN CREATE TEMP TABLE zzz AS SELECT brand FROM
	-- station.ad_brief; END IF; END $$;` exits 0 on a box where `brand` is already gone.
	DO $$
	DECLARE
	  rec record;  -- round-4 finding P1's dedupe loop below needs an explicit loop variable:
	               -- `FOR … IN SELECT` needs an explicitly declared record variable.
	BEGIN
	  IF EXISTS (
	    SELECT 1 FROM pg_attribute
	    WHERE attrelid = 'station.ad_brief'::regclass
	      AND attname = 'brand' AND attnum > 0 AND NOT attisdropped
	  ) THEN
	    -- Materialise the case-preserving normalised display name for every source row EXACTLY ONCE
	    -- (round-3 finding N1's third consequence / N6's drift tripwire) — the ONLY place a
	    -- whitespace-collapsing expression appears in this file outside station.sponsor_fold itself.
	    -- A TEMP TABLE, not a CTE: the two UPDATE joins below are SEPARATE statements, so a CTE's
	    -- statement-scoped lifetime would not reach them — a temp table's session-scoped lifetime
	    -- does, for the rest of this DO block.
	    -- `coalesce(nullif(left(btrim(regexp_replace(brand, '\s+', ' ', 'g')), 120), ''), 'Unnamed
	    -- sponsor')` truncates an over-length brand to 120 chars (round-2 finding F3) and replaces a
	    -- whitespace-only one with a placeholder, so no single bad row ever aborts the whole backfill
	    -- (the transaction above is the last-resort net, not the plan). Every grouping key below then
	    -- derives from this SAME norm_name via station.sponsor_fold(norm_name) — never a second
	    -- hand-copied collapse.
	    CREATE TEMP TABLE sponsor_backfill_norm AS
	    WITH raw_brands AS (
	      SELECT 'ad_brief'::text AS source_table, id AS source_id, pack_slug, brand, created_at
	        FROM station.ad_brief
	      UNION ALL
	      SELECT 'ad_spot'::text, id, pack_slug, brand, created_at
	        FROM station.ad_spot
	    )
	    SELECT source_table, source_id, pack_slug,
	           coalesce(nullif(left(btrim(regexp_replace(brand, '\s+', ' ', 'g')), 120), ''), 'Unnamed sponsor') AS norm_name,
	           created_at
	    FROM raw_brands;

	    -- Insert one sponsor per distinct (pack_slug, folded norm_name) — earliest-created spelling
	    -- wins as the canonical name (DISTINCT ON + ORDER BY created_at ASC). ON CONFLICT DO
	    -- NOTHING: if a sponsor already exists for this (pack_slug, name_key), skip.
	    INSERT INTO station.sponsor (name, pack_slug)
	    SELECT DISTINCT ON (pack_slug, station.sponsor_fold(norm_name))
	      norm_name, pack_slug
	    FROM sponsor_backfill_norm
	    ORDER BY pack_slug NULLS LAST,
	             station.sponsor_fold(norm_name),
	             created_at ASC
	    ON CONFLICT ON CONSTRAINT sponsor_pack_slug_name_key DO NOTHING;

	    -- Backfill ad_brief.sponsor_id: join through the SAME sponsor_backfill_norm rows this source
	    -- table contributed. IS NOT DISTINCT FROM: handles NULL = NULL correctly for pack_slug
	    -- comparison. WHERE sponsor_id IS NULL: idempotent — only updates rows not yet backfilled.
	    UPDATE station.ad_brief b
	    SET sponsor_id = s.id
	    FROM sponsor_backfill_norm n
	    JOIN station.sponsor s
	      ON s.pack_slug IS NOT DISTINCT FROM n.pack_slug
	     AND s.name_key = station.sponsor_fold(n.norm_name)
	    WHERE b.sponsor_id IS NULL
	      AND n.source_table = 'ad_brief'
	      AND n.source_id = b.id;

	    -- Backfill ad_spot.sponsor_id: same join, the ad_spot slice of sponsor_backfill_norm (brand
	    -- before it is renamed in step 12).
	    UPDATE station.ad_spot a
	    SET sponsor_id = s.id
	    FROM sponsor_backfill_norm n
	    JOIN station.sponsor s
	      ON s.pack_slug IS NOT DISTINCT FROM n.pack_slug
	     AND s.name_key = station.sponsor_fold(n.norm_name)
	    WHERE a.sponsor_id IS NULL
	      AND n.source_table = 'ad_spot'
	      AND n.source_id = a.id;

	    -- ── Pack-identity dedupe pass (round-4 finding R1', supersedes round-3 finding R1) ── see this
	    -- file's own header for the full policy writeup: a DIFFERENT policy from the angle-collision
	    -- pass below (that pass keeps/disables/disambiguates the owner's own data; this pass DELETES
	    -- the losing row outright — no disable, no suffix — because a pack brief is catalog content a
	    -- reinstall regenerates, nothing references ad_brief.id by FK, and the partial index below
	    -- (step 9b) carries no `enabled` predicate for a disabled duplicate to hide behind). Grouped by
	    -- (pack_slug, sponsor_id) alone, WHERE pack_slug IS NOT NULL — a pack brief's own identity
	    -- ignores premise entirely (IAdBriefStore.UpsertAllAsync's own upsert key): two pack rows
	    -- sharing a slot are duplicates here regardless of whether their premises match.
	    CREATE TEMP TABLE sponsor_ad_brief_pack_dupes AS
	    SELECT id, sponsor_id,
	           first_value(id) OVER w AS keeper,
	           row_number()    OVER w AS rn
	    FROM station.ad_brief
	    WHERE pack_slug IS NOT NULL
	    WINDOW w AS (PARTITION BY pack_slug, sponsor_id ORDER BY created_at, id);

	    -- One RAISE NOTICE per deleted row, so the operator's own migrate.sh console output says
	    -- exactly what happened, before the DELETE below actually removes them.
	    FOR rec IN
	      SELECT d.id, d.keeper, b.pack_slug, s.name AS sponsor_name, b.premise
	      FROM sponsor_ad_brief_pack_dupes d
	      JOIN station.ad_brief b ON b.id = d.id
	      JOIN station.sponsor s ON s.id = d.sponsor_id
	      WHERE d.rn > 1
	    LOOP
	      RAISE NOTICE 'db/46: deleting ad_brief % (sponsor "%", pack "%") as a duplicate pack brief of ad_brief % — its premise was: %',
	        rec.id, rec.sponsor_name, rec.pack_slug, rec.keeper, rec.premise;
	    END LOOP;

	    -- No `enabled = false`, no premise suffix: the losing row is catalog content, not owner data
	    -- (see this pass's own header note above) — removing it outright is what actually satisfies
	    -- the partial index below, since that index has no `enabled` predicate to spare a disabled row.
	    DELETE FROM station.ad_brief b
	    USING sponsor_ad_brief_pack_dupes d
	    WHERE d.id = b.id AND d.rn > 1;

	    DROP TABLE sponsor_ad_brief_pack_dupes;

	    -- ── Dedupe pass (round-4 finding P1) ── see this file's own header for the full policy writeup
	    -- (keep, disable, disambiguate; never delete, never abort). Every group of ad_brief rows that
	    -- shares BOTH sponsor_id and a non-empty station.sponsor_fold(premise) is materialised once
	    -- (so the RAISE NOTICE loop below and the UPDATE that follows it see the identical ranked set,
	    -- rather than two independently-evaluated copies of the same window function); rn = 1 is the
	    -- earliest-created row in the group (the keeper), rn > 1 is every later duplicate. `enabled`
	    -- excludes any row a PRIOR run of THIS SAME pass already disabled — idempotency on a second
	    -- migration run, not a guard against the pack-identity pass above: that pass now DELETES its
	    -- losing rows outright (round-4 finding R1'), so a row it removes is simply gone and can never
	    -- reach this pass's own SELECT in the first place.
	    CREATE TEMP TABLE sponsor_ad_brief_dupes AS
	    SELECT id, sponsor_id,
	           first_value(id) OVER w AS keeper,
	           row_number()    OVER w AS rn
	    FROM station.ad_brief
	    WHERE enabled AND nullif(station.sponsor_fold(premise), '') IS NOT NULL
	    WINDOW w AS (PARTITION BY sponsor_id, station.sponsor_fold(premise) ORDER BY created_at, id);

	    -- One RAISE NOTICE per disabled row, so the operator's own migrate.sh console output says
	    -- exactly what happened, before the UPDATE below actually disables them.
	    FOR rec IN
	      SELECT d.id, d.keeper, s.name AS sponsor_name, b.premise
	      FROM sponsor_ad_brief_dupes d
	      JOIN station.ad_brief b ON b.id = d.id
	      JOIN station.sponsor s ON s.id = d.sponsor_id
	      WHERE d.rn > 1
	    LOOP
	      RAISE NOTICE 'db/46: disabling ad_brief % (sponsor "%") as a duplicate of ad_brief % — original premise was: %',
	        rec.id, rec.sponsor_name, rec.keeper, rec.premise;
	    END LOOP;

	    -- Both the duplicate's OWN id and the keeper's id go into the suffix — the keeper's id alone
	    -- is identical for every duplicate in a group of three or more, so it cannot by itself make
	    -- the re-derived premise_key unique per row; the duplicate's own id always is.
	    UPDATE station.ad_brief b
	    SET enabled = false,
	        premise = b.premise ||
	          ' [duplicate #' || b.id || ' of brief #' || d.keeper || ', disabled by db/46]'
	    FROM sponsor_ad_brief_dupes d
	    WHERE d.id = b.id AND d.rn > 1;

	    DROP TABLE sponsor_ad_brief_dupes;
	    DROP TABLE sponsor_backfill_norm;
	  END IF;
	END $$;

	-- ── Step 6: set ad_brief.sponsor_id NOT NULL (now that backfill has populated every row) ──────
	-- Guarded by is_nullable check: the SET NOT NULL is a no-op on a fresh-init DB (db/06 already
	-- defines sponsor_id NOT NULL) and on a second migration run.
	DO $$
	BEGIN
	  IF EXISTS (
	    SELECT 1 FROM information_schema.columns
	    WHERE table_schema = 'station' AND table_name = 'ad_brief'
	      AND column_name = 'sponsor_id' AND is_nullable = 'YES'
	  ) THEN
	    ALTER TABLE station.ad_brief ALTER COLUMN sponsor_id SET NOT NULL;
	  END IF;
	END $$;

	-- ── Step 7: add FK ad_brief.sponsor_id → station.sponsor (IF NOT EXISTS) ───────────────────
	DO $$
	BEGIN
	  IF NOT EXISTS (
	    SELECT 1 FROM pg_constraint
	    WHERE conname = 'ad_brief_sponsor_id_fkey' AND conrelid = 'station.ad_brief'::regclass
	  ) THEN
	    ALTER TABLE station.ad_brief
	      ADD CONSTRAINT ad_brief_sponsor_id_fkey
	        FOREIGN KEY (sponsor_id) REFERENCES station.sponsor(id) ON DELETE RESTRICT;
	  END IF;
	END $$;

	-- ── Step 8: drop brand from ad_brief (replaced by sponsor_id FK; brand lives on sponsor.name) ─
	ALTER TABLE station.ad_brief DROP COLUMN IF EXISTS brand;

	-- ── Step 9: add (sponsor_id, premise_key) unique constraint on ad_brief (IF NOT EXISTS) ───────
	-- Plain UNIQUE, not NULLS NOT DISTINCT — see the header's F2 rationale: a NULL premise_key
	-- (no angle) must never collide with another NULL premise_key.
	DO $$
	BEGIN
	  IF NOT EXISTS (
	    SELECT 1 FROM pg_constraint
	    WHERE conname = 'ad_brief_sponsor_id_premise_key' AND conrelid = 'station.ad_brief'::regclass
	  ) THEN
	    ALTER TABLE station.ad_brief
	      ADD CONSTRAINT ad_brief_sponsor_id_premise_key UNIQUE (sponsor_id, premise_key);
	  END IF;
	END $$;

	-- ── Step 9b: add partial unique index (pack_slug, sponsor_id) on ad_brief ──────────────────────
	-- A CONSTRAINT cannot be partial, so this is a raw CREATE UNIQUE INDEX rather than an
	-- ADD CONSTRAINT UNIQUE like ad_brief_sponsor_id_premise_key above — WHERE pack_slug IS NOT NULL
	-- is what actually needs to be partial: an owner-authored sponsor legitimately keeps several
	-- owner briefs (premise_key above is what scopes THAT uniqueness); only a pack brief is capped at
	-- one per sponsor (round-3 finding R1, SPEC F2/F3). Deliberately no `enabled` predicate here: an
	-- `enabled`-scoped index would let a pack reinstall's ON CONFLICT insert a second, ENABLED row
	-- beside a duplicate the dedupe pass had only disabled, breaking the T405 "reinstall preserves
	-- enabled" ruling — which is exactly why that pass (step 5, above) DELETES its losing rows
	-- outright rather than disabling them (round-4 finding R1'). Safe on an upgrading box only
	-- because that pass deletes — a disabled duplicate would still collide on this very index.
	CREATE UNIQUE INDEX IF NOT EXISTS ad_brief_pack_slug_sponsor_id_key
	  ON station.ad_brief (pack_slug, sponsor_id)
	  WHERE pack_slug IS NOT NULL;

	-- ── Step 10: set ad_spot.sponsor_id NOT NULL ────────────────────────────────────────────────
	DO $$
	BEGIN
	  IF EXISTS (
	    SELECT 1 FROM information_schema.columns
	    WHERE table_schema = 'station' AND table_name = 'ad_spot'
	      AND column_name = 'sponsor_id' AND is_nullable = 'YES'
	  ) THEN
	    ALTER TABLE station.ad_spot ALTER COLUMN sponsor_id SET NOT NULL;
	  END IF;
	END $$;

	-- ── Step 11: add FK ad_spot.sponsor_id → station.sponsor (IF NOT EXISTS) ────────────────────
	DO $$
	BEGIN
	  IF NOT EXISTS (
	    SELECT 1 FROM pg_constraint
	    WHERE conname = 'ad_spot_sponsor_id_fkey' AND conrelid = 'station.ad_spot'::regclass
	  ) THEN
	    ALTER TABLE station.ad_spot
	      ADD CONSTRAINT ad_spot_sponsor_id_fkey
	        FOREIGN KEY (sponsor_id) REFERENCES station.sponsor(id) ON DELETE RESTRICT;
	  END IF;
	END $$;

	-- Index on the FK child column (round-2 finding F7) — ad_brief needs no equivalent, it is
	-- already covered by ad_brief_sponsor_id_premise_key's own leading column.
	CREATE INDEX IF NOT EXISTS ad_spot_sponsor_id ON station.ad_spot (sponsor_id);

	-- ── Step 12: rename ad_spot.brand → sponsor_name ────────────────────────────────────────────
	-- The sponsor entity owns the canonical name; the spot carries sponsor_name as a snapshot
	-- written at creation and refreshed on PATCH when the sponsor changes (SPEC F171.7), so the
	-- booth log, ICY title, and spot title keep saying what aired even after a rename. RENAME
	-- COLUMN has no IF EXISTS clause — guarded via pg_attribute.
	DO $$
	BEGIN
	  IF EXISTS (
	    SELECT 1 FROM pg_attribute
	    WHERE attrelid = 'station.ad_spot'::regclass
	      AND attname = 'brand' AND attnum > 0 AND NOT attisdropped
	  ) THEN
	    ALTER TABLE station.ad_spot RENAME COLUMN brand TO sponsor_name;
	  END IF;
	END $$;

	-- ── Step 13: preview/job columns on ad_spot (SPEC F174.2 job / F174.4 preview) ──────────────
	-- preview_path/preview_at/preview_key: the approved-preview asset the guided spot dialog's
	-- "Hear" step renders (F174.4) — staleness is DERIVED (preview_key recomputed == stored), so
	-- there is no separate preview_stale flag to keep in sync. job_kind/job_started_at/job_error:
	-- the single-slot AdSpotJobService's own state stamps (F174.2) — no job jsonb envelope; kind,
	-- started-at, and the last error are the entire state the row needs to report `job: {kind,
	-- startedAt, error}` back to a refreshed page.
	ALTER TABLE station.ad_spot ADD COLUMN IF NOT EXISTS preview_path    text;
	ALTER TABLE station.ad_spot ADD COLUMN IF NOT EXISTS preview_at      timestamptz;
	ALTER TABLE station.ad_spot ADD COLUMN IF NOT EXISTS preview_key     text;
	ALTER TABLE station.ad_spot ADD COLUMN IF NOT EXISTS job_kind        text
	  CHECK (job_kind IS NULL OR job_kind IN ('write', 'preview'));
	ALTER TABLE station.ad_spot ADD COLUMN IF NOT EXISTS job_started_at  timestamptz;
	ALTER TABLE station.ad_spot ADD COLUMN IF NOT EXISTS job_error       text;

	-- ── Step 14: sponsor_id on station.show (SPEC F175.1, STORY-430) ────────────────────────────
	-- Nullable FK: shows optionally carry a sponsor (nullable-fk: optional sponsorship per show).
	ALTER TABLE station.show ADD COLUMN IF NOT EXISTS sponsor_id bigint;
	DO $$
	BEGIN
	  IF NOT EXISTS (
	    SELECT 1 FROM pg_constraint
	    WHERE conname = 'show_sponsor_id_fkey' AND conrelid = 'station.show'::regclass
	  ) THEN
	    ALTER TABLE station.show
	      ADD CONSTRAINT show_sponsor_id_fkey
	        FOREIGN KEY (sponsor_id) REFERENCES station.sponsor(id) ON DELETE RESTRICT;
	  END IF;
	END $$;

	-- Index on the FK child column (round-2 finding F7).
	CREATE INDEX IF NOT EXISTS show_sponsor_id ON station.show (sponsor_id);

	COMMIT;
	SQL

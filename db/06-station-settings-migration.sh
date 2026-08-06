#!/bin/bash
# 06-station-settings-migration.sh — idempotent bootstrap for the station settings overlay store.
# Creates the station_svc role, station schema, and station.settings table introduced in STORY-042.
# Safe to run multiple times (role existence is checked before CREATE ROLE; all DDL uses IF NOT EXISTS).
# Run via: bash -s < db/06-station-settings-migration.sh   (piped into the Postgres container)
# Or mounted into /docker-entrypoint-initdb.d/ for fresh deployments.
#
# Role-creation idiom: psql variable interpolation (:'var') works in heredoc SQL bodies but NOT in
# -c arguments, and NOT inside dollar-quoted DO $$ blocks (the colon is PL/pgSQL syntax there).
# Solution: use a double-quoted heredoc (shell substitution) for the CREATE ROLE statement only,
# so the shell injects the password as a shell-quoted literal. The rest of the DDL uses a
# single-quoted heredoc (no shell expansion needed — fully idempotent SQL). Consistent with
# db/01-library.sh's role-creation approach.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" \
  "${POSTGRES_DB:?POSTGRES_DB must be set}" \
  "${STATION_DB_PASSWORD:?STATION_DB_PASSWORD must be set for the station_svc role}"

# Step 1: create station_svc role if not already present.
# Shell-level check then CREATE ROLE in a double-quoted heredoc so the shell expands $STATION_DB_PASSWORD.
# The password is embedded as a SQL string literal (single-quoted by the surrounding SQL syntax).
role_exists=$(psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  --tuples-only --no-align \
  -c "SELECT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'station_svc')")

if [ "$role_exists" = "f" ]; then
  # Double-quoted here-doc: shell expands $STATION_DB_PASSWORD into the SQL body.
  # The password is enclosed in single quotes in the SQL; we escape any embedded single quotes.
  escaped_pw="${STATION_DB_PASSWORD//\'/\'\'}"
  psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-SQL
		CREATE ROLE station_svc WITH LOGIN PASSWORD '${escaped_pw}';
	SQL
fi

# Step 2: remaining DDL — all idempotent (IF NOT EXISTS, idempotent ALTER ROLE).
# Single-quoted here-doc: no shell expansion needed.
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	-- Pin search_path so station_svc never accidentally resolves objects in other schemas.
	ALTER ROLE station_svc SET search_path = station;

	-- Schema owned by the service role; subsequent DDL runs as station_svc so it owns everything.
	CREATE SCHEMA IF NOT EXISTS station AUTHORIZATION station_svc;

	-- btree_gist (SPEC F91.1, STORY-240, PLAN T118): station.segment_schedule's EXCLUDE constraint
	-- below needs a GiST opclass for plain integer equality (day_of_week) — int4range's own overlap
	-- opclass is already built into core Postgres, but bare-integer equality is not, without this
	-- extension. Installed once per database while still connected as the bootstrap superuser (this
	-- session has not dropped to station_svc yet) — the default opclass Postgres picks up for the
	-- EXCLUDE constraint is resolved by type+access-method, not by search_path, so which schema the
	-- extension's objects land in does not matter here.
	CREATE EXTENSION IF NOT EXISTS btree_gist;

	-- Switch to the service role so it owns every object it creates (real isolation).
	SET ROLE station_svc;
	SET search_path = station;

	-- Key-value overlay store (STORY-042, Epic I). Keys are allowlisted in the C# provider;
	-- secrets (ConnectionStrings:*, Admin:Password, ICECAST_SOURCE_PASSWORD) are never written here.
	CREATE TABLE IF NOT EXISTS station.settings (
	  key        text        NOT NULL PRIMARY KEY,
	  value      jsonb       NOT NULL,
	  updated_at timestamptz NOT NULL DEFAULT now()
	);

	-- DJ persona storage (SPEC F35.1, STORY-118, Epic T). Lives in this same station schema/role —
	-- same station_svc owner, same isolation guarantee station.settings already has. '' voice is a
	-- deliberate sentinel meaning "use the station's own Station:Voice", not "unset".
	--
	-- slug/definition/enabled (SPEC F71.1, STORY-192): the persona-card foundation, reconciled onto
	-- this same table rather than a second one — name/backstory/style/voice stay exactly as STORY-118
	-- shipped them (PersonaRepository's admin CRUD still reads/writes those columns unchanged) while
	-- slug/definition/enabled are the F71.1 card projection PersonaRepository keeps in sync on every
	-- write (see GenWave.MediaLibrary.Station.LegacyPersonaCardMapper). The DEFAULT expressions below
	-- are a safety net only, for a row created outside PersonaRepository (never the app's own path):
	-- a fresh table has no rows to backfill, so nothing more elaborate is needed here.
	--
	-- imported_from/imported_at (SPEC F90.7, STORY-237, PLAN T98): provenance PersonaImportRepository
	-- stamps on every import — the entry slug for a catalog import, the literal 'file' for a file
	-- upload — and refreshes on every re-import. Both stay NULL for a persona PersonaRepository's own
	-- CRUD ever created or edited (neither column appears in CreateAsync's INSERT or UpdateAsync's
	-- UPDATE). Display-only for the Admin UI's provenance badge (T105): no FK, no index — nothing
	-- selects, filters, or orders on either column. See db/25-persona-provenance-migration.sh for the
	-- in-place upgrade path this table also ships as.
	CREATE TABLE IF NOT EXISTS station.persona (
	  id            serial      PRIMARY KEY,
	  name          text        NOT NULL UNIQUE,
	  backstory     text        NOT NULL DEFAULT '',
	  style         text        NOT NULL DEFAULT '',
	  voice         text        NOT NULL DEFAULT '',
	  slug          text        NOT NULL UNIQUE DEFAULT ('persona-' || nextval('station.persona_id_seq')::text),
	  definition    jsonb       NOT NULL DEFAULT '{}'::jsonb,
	  enabled       boolean     NOT NULL DEFAULT true,
	  imported_from text,
	  imported_at   timestamptz,
	  created_at    timestamptz NOT NULL DEFAULT now(),
	  updated_at    timestamptz NOT NULL DEFAULT now()
	);

	-- Persona memory (SPEC F71.1, STORY-192): accrued/authored bits and callbacks a future
	-- orchestrator task (STORY-194) records/recalls. FK CASCADE — deleting a persona deletes its
	-- memory with it, never orphaned rows. The recall index is the spec'd shape verbatim:
	-- newest-aired-first per (persona, kind), with never-aired (NULL last_aired_at) rows sorted
	-- first so "never aired" beats "aired long ago" for anti-repeat/callback recall (STORY-194).
	CREATE TABLE IF NOT EXISTS station.persona_memory (
	  id            serial      PRIMARY KEY,
	  persona_id    integer     NOT NULL REFERENCES station.persona (id) ON DELETE CASCADE,
	  kind          text        NOT NULL,                                       -- e.g. 'bit', 'callback' — open, evolving set (F71.4)
	  content       text        NOT NULL,
	  source        text        NOT NULL CHECK (source IN ('authored', 'accrued')),  -- F71.6: authored rows are eviction-exempt
	  aired_count   integer     NOT NULL DEFAULT 0,
	  last_aired_at timestamptz,                                                -- null = never aired
	  created_at    timestamptz NOT NULL DEFAULT now()
	);

	CREATE INDEX IF NOT EXISTS persona_memory_recall
	  ON station.persona_memory (persona_id, kind, last_aired_at DESC NULLS FIRST);

	-- Persona taste (SPEC F82.1, F84.1-F84.3; STORY-213; ARCHITECTURE.md "Personalities on air"): the
	-- persona's opinions in one shape across all three provenances — hand-authored (imported with the
	-- card, F79.1), operator-nudged (a direct edit), and accrued (learned from operator thumbs, F84.1,
	-- once F84's guardrails land). FK CASCADE mirrors persona_memory above: deleting a persona deletes
	-- its taste with it, never orphaned rows. predicate/context are JSONB documents
	-- (GenWave.Core.Domain.TastePredicate/TasteContext, T56) rather than relational columns — an
	-- evolving match/gate shape with no query pattern yet justifying first-class columns. The ranker
	-- (T63) and the accrual/eviction write path (T70, F84.3's cap-50-weakest-evicted) are later tasks;
	-- this table has no consumer yet (T59).
	CREATE TABLE IF NOT EXISTS station.persona_taste (
	  id         serial      PRIMARY KEY,
	  persona_id integer     NOT NULL REFERENCES station.persona (id) ON DELETE CASCADE,
	  predicate  jsonb       NOT NULL,
	  context    jsonb       NOT NULL,
	  weight     real        NOT NULL CHECK (weight BETWEEN -1 AND 1),
	  source     text        NOT NULL CHECK (source IN ('authored', 'operator', 'accrued')),
	  created_at timestamptz NOT NULL DEFAULT now(),
	  updated_at timestamptz NOT NULL DEFAULT now()
	);

	CREATE INDEX IF NOT EXISTS persona_taste_persona_source
	  ON station.persona_taste (persona_id, source);

	-- Booth log (SPEC F72.1-F72.3, STORY-195): the operator-readable "what the DJ did and said"
	-- narrative feed — track starts, patter airs, degradation mode changes. Retention (default 14
	-- days, BoothLog:RetentionDays) is enforced at insert time in application code (BoothLogRepository),
	-- not here — this table has no TTL of its own. Never on any spectator/public surface (F72.4).
	CREATE TABLE IF NOT EXISTS station.booth_log (
	  id          bigserial   PRIMARY KEY,
	  occurred_at timestamptz NOT NULL DEFAULT now(),
	  kind        text        NOT NULL,
	  summary     text        NOT NULL,
	  -- nullable-fk (SPEC F84.6, STORY-215): the persona on air when a TRACK-START row aired,
	  -- stamped by the booth-log drain loop at write time — never inferred later. NULL for every
	  -- non-track row, a persona-less airing, or a row that predates this column; all three are
	  -- equally "un-thumbable" for taste accrual (T70). ON DELETE SET NULL, not CASCADE (unlike
	  -- persona_memory/persona_taste below): deleting a persona must never delete booth-log HISTORY
	  -- rows — it only degrades their stamp to unstamped, the same un-thumbable state above.
	  persona_id  integer     REFERENCES station.persona (id) ON DELETE SET NULL,
	  -- SPEC F84.1, STORY-215, PLAN T70: the same track's STRUCTURED artist, captured the same
	  -- synchronous-at-air-time way as persona_id above and for the same reason — the accrual write
	  -- path needs a real artist value to build an artist-predicate rule from, never a regex over
	  -- summary's narrative prose. NULL for every non-track row or a track aired with no known
	  -- artist. Never surfaced through IBoothLogReader/BoothLogEntry — read directly by the accrual
	  -- store only, inside the same transaction as the nudge it attributes.
	  artist      text,
	  -- SPEC F86.1, STORY-217, PLAN T73: the fired-rule summaries + exploration flag from the SAME
	  -- PersonaPickDiagnostics the copywriter reads (F83.1) — one source of truth, no re-derivation.
	  -- Stamped by the booth-log drain loop at write time, same as persona_id/artist above. NULL for
	  -- every non-track row, an engine-initiated play, a persona-off pick, or a row that predates this
	  -- column — never backfilled (F84.6 precedent). Scores, pool size, and degradation step are
	  -- deliberately NOT stored — those rename with ranker tuning; the F82.6 debug log line remains
	  -- their one durable-enough record.
	  pick        jsonb,
	  -- gh-#99: the aired catalog row's numeric library.media id, captured at publish time the same
	  -- way as persona_id/artist above. NULL for every non-track row, a non-catalog id, or a row that
	  -- predates this column. Deliberately NO foreign key — library.media lives on the other side of
	  -- the schema-role boundary (station_svc has no grant there); the Host resolves safe-scope
	  -- membership for the taste-thumb exclusion via the library connection instead.
	  media_id    bigint
	);

	-- Keyset paging spine (SPEC F72.2): newest-first (occurred_at DESC, id DESC) with no OFFSET —
	-- matches BoothLogRepository.ReadAsync's ORDER BY / row-comparison predicate exactly.
	CREATE INDEX IF NOT EXISTS booth_log_paging
	  ON station.booth_log (occurred_at DESC, id DESC);

	-- Persona taste thumb ledger (SPEC F84.5, STORY-215, PLAN T70): the durable idempotency record
	-- for an operator taste thumb, keyed (persona_id, booth_log_id, direction) — a double-tap, or a
	-- now-playing + booth-log tap on the SAME airing/direction, is the exact same row, so
	-- `ON CONFLICT ... DO NOTHING` is the entire dedup mechanism (never in-memory, which would forget
	-- on every restart). Also the durable source T71's "already thumbed" UI state reads. FK CASCADE
	-- on both columns: a deleted persona or an evicted (retention-swept) booth-log row makes its own
	-- thumb-ledger rows meaningless, so they go with it — unlike booth_log.persona_id's own ON DELETE
	-- SET NULL (a HISTORY-row survival concern that does not apply to this ledger).
	CREATE TABLE IF NOT EXISTS station.persona_taste_thumb (
	  id           bigserial   PRIMARY KEY,
	  persona_id   integer     NOT NULL REFERENCES station.persona (id) ON DELETE CASCADE,
	  booth_log_id bigint      NOT NULL REFERENCES station.booth_log (id) ON DELETE CASCADE,
	  direction    text        NOT NULL CHECK (direction IN ('up', 'down')),
	  created_at   timestamptz NOT NULL DEFAULT now(),
	  UNIQUE (persona_id, booth_log_id, direction)
	);

	-- Listener requests (SPEC F87, STORY-224, PLAN T86, gh-#105-era epoch): the first public WRITE.
	-- wish is nulled by an insert-time sweep once received_at is older than Requests:WishRetentionHours
	-- (default 24h) — the SAME "eviction runs inside the insert's own transaction" discipline
	-- booth_log's own retention sweep established above; artist/title/genre/moods (the PARSED
	-- predicates), the picked_* dropdown values (gh-#131 — server-validated list members, never free
	-- text), and the row's outcome (status/matched_media_id/fulfilled_at) are never swept and stay
	-- indefinitely. wish is nullable AND optional (gh-#131): a picker-only request carries no free
	-- text at all, only picked_genre/picked_mood. matched_media_id deliberately carries NO foreign
	-- key: library.media lives on the other side of the schema-role boundary (station_svc has no
	-- grant there) — the exact booth_log.media_id precedent just above. See
	-- db/24-request-migration.sh + db/29-request-genre-migration.sh for the in-place upgrade path
	-- this table also ships as, and their own remarks for the fuller rationale.
	CREATE TABLE IF NOT EXISTS station.request (
	  id               bigserial   PRIMARY KEY,
	  received_at      timestamptz NOT NULL DEFAULT now(),
	  wish             text,
	  picked_genre     text,
	  picked_mood      text,
	  artist           text,
	  title            text,
	  genre            text,
	  moods            text[],
	  status           text        NOT NULL DEFAULT 'pending'
	                     CHECK (status IN ('pending', 'fulfilled', 'expired', 'unmatched')),
	  matched_media_id bigint,
	  fulfilled_at     timestamptz,
	  expires_at       timestamptz NOT NULL
	);

	-- The one query shape every consumer needs: "find the oldest live pending request" (fulfillment,
	-- T90) and "count/evict pending rows" (the PendingCap throttle, T87) both filter on status and
	-- order/compare against expires_at.
	CREATE INDEX IF NOT EXISTS request_pending
	  ON station.request (status, expires_at);

	-- The weekly format-clock grid (SPEC F91.1, F91.2; STORY-240, STORY-242; PLAN T118) that replaces
	-- the single owner-toggled Station:Persona:ActiveId. day_of_week is System.DayOfWeek's own 0-6
	-- numbering (0 = Sunday) — no translation ever happens between this column and the C# side.
	-- start_minute/end_minute are wall-clock minutes since local midnight on 30-minute boundaries
	-- (container TZ, TimeProvider.LocalTimeZone) — DST is free by construction (a spring-forward
	-- segment simply airs an hour short, fall-back an hour long; see ARCHITECTURE.md's own remarks).
	-- persona_id NULL means music-only (a deliberate nullable-FK override); ON DELETE RESTRICT means a
	-- persona holding any slot can never be deleted out from under the schedule (SPEC F91.9) — only a
	-- fully benched persona (zero rows here) is deletable. genres/energy_min/energy_max NULL means "use
	-- the station-default envelope" (F91.4); each is independently nullable, so a segment may override
	-- only one of the three. energy_min/energy_max are double precision, not real: a float4 column
	-- round-trips 0.3 as 0.30000001 (single-precision rounding), which would surface verbatim in the
	-- T129 editor — double precision carries every value the C# double envelope bounds already are
	-- without that loss. The EXCLUDE constraint makes two overlapping rows on the same day IMPOSSIBLE
	-- at the store — the same day + an overlapping [start_minute, end_minute) range can never both
	-- exist, regardless of what any application-side check does or fails to do; a midnight-spanning
	-- show is two rows (one per day) rather than a single wraparound range. See
	-- db/27-segment-schedule-migration.sh for the in-place upgrade path this table also ships as,
	-- including the F91.6 seed-and-delete data migration that only concerns an existing installation
	-- upgrading through this release — a fresh install has no legacy Station:Persona:ActiveId key to
	-- migrate, so nothing here seeds a row.
	CREATE TABLE IF NOT EXISTS station.segment_schedule (
	  id           serial      PRIMARY KEY,
	  day_of_week  int         NOT NULL CHECK (day_of_week BETWEEN 0 AND 6),
	  start_minute int         NOT NULL CHECK (start_minute % 30 = 0 AND start_minute BETWEEN 0 AND 1410),
	  end_minute   int         NOT NULL CHECK (end_minute   % 30 = 0 AND end_minute   BETWEEN 30 AND 1440),
	  persona_id   int         REFERENCES station.persona (id) ON DELETE RESTRICT,
	  genres       text[],
	  energy_min   double precision,
	  energy_max   double precision,
	  created_at   timestamptz NOT NULL DEFAULT now(),
	  updated_at   timestamptz NOT NULL DEFAULT now(),
	  CHECK (end_minute > start_minute),
	  EXCLUDE USING gist (day_of_week WITH =, int4range(start_minute, end_minute) WITH &&)
	);

	-- Owner-imported themes (SPEC F103.7, F103.8; STORY-271, PLAN T181): the Community Catalog v2
	-- theme kind's storage. definition holds the byte-stable ThemeManifest (GenWave.Host.Theming) —
	-- no runtime-only fields, no cached CSS — the same byte-stable-manifest discipline
	-- ThemeManifestSerializer/ThemeManifestParser already enforce for the two embedded defaults; a
	-- caller (ThemeCatalog, T182) reconstitutes a ThemeManifest from this column at its own edge
	-- rather than this table (or GenWave.Core, IThemeStore's own home) knowing that type at all.
	-- imported_from/imported_at mirror station.persona's own db/25 provenance columns exactly: the
	-- catalog entry's slug for a catalog import, 'file' for a direct upload, NULL for an
	-- authored-in-place theme (no writer for that path exists yet, so every row today would carry a
	-- non-null stamp — the NULL case exists for symmetry with persona's and a future Layer B editor,
	-- gh-#206). slug is UNIQUE across every owner theme; F103.8's stronger rule — an import may not
	-- also collide with an EMBEDDED default's slug — needs the shipped catalog to check against and
	-- is enforced by ThemeCatalog/the import route (T182/T184), not this table. NO CONSUMER YET
	-- (T181): ThemeCatalog does not read this table until T182 wires it, and no route writes to it
	-- until T184. See db/31-theme-store-migration.sh for the in-place upgrade path this table also
	-- ships as.
	CREATE TABLE IF NOT EXISTS station.theme (
	  id            serial      PRIMARY KEY,
	  slug          text        NOT NULL UNIQUE,
	  definition    jsonb       NOT NULL,
	  imported_from text,
	  imported_at   timestamptz,
	  created_at    timestamptz NOT NULL DEFAULT now()
	);

	-- Font packs (SPEC F104 "The wardrobe workshop"; STORY-282, PLAN T198): Dean-curated font packs
	-- installed from the Community Catalog's `font` kind — the library's first per-kind store,
	-- mirroring station.theme's own shape immediately above. definition holds the raw catalog pack
	-- manifest jsonb; a caller (GenWave.Host, downstream of this GenWave.Core seam) (de)serializes at
	-- its own edge, exactly like station.theme's own definition column. imported_from is NOT NULL
	-- here (unlike station.theme) — packs have no authored-in-place path, the catalog install route
	-- is the only door a pack ever arrives through. NO CONSUMER YET (T198): IFontPackStore ships
	-- dark — POST /api/fonts/{slug}/install (T199) is the first write consumer,
	-- InstalledFontCatalog (T199/T200) and the library page (T203) the first read consumers. See
	-- db/32-font-pack-migration.sh for the in-place upgrade path this table also ships as.
	CREATE TABLE IF NOT EXISTS station.font_pack (
	  id            serial      PRIMARY KEY,
	  slug          text        NOT NULL UNIQUE,
	  family        text        NOT NULL,
	  definition    jsonb       NOT NULL,
	  imported_from text        NOT NULL,
	  imported_at   timestamptz NOT NULL DEFAULT now(),
	  created_at    timestamptz NOT NULL DEFAULT now()
	);

	-- One row per face (upright/italic) a font_pack ships (SPEC F104): a pack is one family, 1-2
	-- faces, role-agnostic — the editor (a later M2 task) assigns display/sans. FK CASCADE: deleting
	-- a pack deletes its own faces with it, never orphaned rows. file is globally UNIQUE (not scoped
	-- to pack_id) — it is the `/fonts/<file>` serving key the widened route (T200) looks up directly,
	-- with no pack context available at request time.
	CREATE TABLE IF NOT EXISTS station.font_pack_face (
	  id        serial PRIMARY KEY,
	  pack_id   int    NOT NULL REFERENCES station.font_pack(id) ON DELETE CASCADE,
	  file      text   NOT NULL UNIQUE,
	  style     text   NOT NULL DEFAULT 'normal',
	  bytes     bytea  NOT NULL,
	  byte_size int    NOT NULL,
	  sha256    text   NOT NULL
	);
	SQL

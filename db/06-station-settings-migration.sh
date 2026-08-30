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
	--
	-- version (gh-#486): a per-key optimistic-concurrency counter, starting at 1 on first
	-- insert and incremented on every subsequent write (both the unconditional WriteAsync upsert and
	-- the version-guarded WriteIfVersionMatchesAsync path — see StationSettingsRepository). A caller
	-- that read the row at version N may write again only if the row is STILL at version N; a whole-
	-- array/document write (Tts:Pronunciations, Tts:Corrections) that started from a stale read now
	-- 409s instead of silently clobbering a concurrent editor's save. expectedVersion=0 means "no row
	-- existed at read time" — a plain conditional INSERT, not a version comparison, since there is no
	-- row yet to compare against.
	CREATE TABLE IF NOT EXISTS station.settings (
	  key        text        NOT NULL PRIMARY KEY,
	  value      jsonb       NOT NULL,
	  version    bigint      NOT NULL DEFAULT 1,
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
	  media_id    bigint,
	  -- SPEC F113, STORY-304, PLAN T219/T220: the air-time kind stamp — the demo-hour instrument.
	  -- NULL for every music row; a SegmentKind token (GenWave.Core.Domain, a growing C# enum) for
	  -- tts:* rows, stamped by the booth-log drain loop at air time, the same synchronous-at-write
	  -- way as persona_id/artist/pick above. Deliberately un-CHECKed (unlike most kind columns in
	  -- this schema) — the token set churns with every new content kind, which a CHECK constraint
	  -- would otherwise force this migration to keep pace with. NO CONSUMER YET (T219): T220 wires
	  -- the write path. See db/33-show-and-segment-kind-migration.sh for the in-place upgrade path
	  -- this column also ships as.
	  segment_kind text,
	  -- SPEC F121.1, STORY-310, PLAN T238/T242: the air-time show stamp, written the same
	  -- synchronous-at-write-time way as persona_id/artist/pick/segment_kind above. NULL for every
	  -- row aired outside a show or predating this column. Deliberately NO FK — history must outlive
	  -- the entity; a deleted show must never rewrite or block on past airings (the exact media_id/
	  -- segment_kind precedent already on this table). NO CONSUMER YET (T238): T242 wires the write
	  -- path. See db/35-show-identity-migration.sh for the in-place upgrade path this column also
	  -- ships as.
	  show_id int
	);

	-- Keyset paging spine (SPEC F72.2): newest-first (occurred_at DESC, id DESC) with no OFFSET —
	-- matches BoothLogRepository.ReadAsync's ORDER BY / row-comparison predicate exactly.
	CREATE INDEX IF NOT EXISTS booth_log_paging
	  ON station.booth_log (occurred_at DESC, id DESC);

	-- Last-airing lookup spine (SPEC F152.5, STORY-373, PLAN T362 review MED-4): mirrors db/41's own
	-- in-place upgrade addition — BoothLogRepository.GetLastAiringAsync's bounded read needs "this
	-- show's own track-started rows, newest first" fast, not a scan of the whole retention window.
	CREATE INDEX IF NOT EXISTS booth_log_show_track_started
	  ON station.booth_log (show_id, occurred_at)
	  WHERE kind = 'track-started';

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

	-- Shows (SPEC F114/F115, gh-#383 — the later slice, schema ruled at STORY-304/T219 then widened
	-- at STORY-305/STORY-310/T238): a first-class entity, singular like every other table in this
	-- schema (station.persona precedent) — renaming a show touches one row, and identity is what
	-- patter/idents/spectator will reference once the F114/F115 slices land. Defined here, ahead of
	-- station.segment_schedule below, purely so that table's show_id column has something to
	-- reference — the two tables carry no other ordering relationship.
	--
	-- slug is the import identity (a catalog slug for an import, the house Slugify output for an
	-- authored show — T239), UNIQUE and NOT NULL — safe with no backfill because this table is still
	-- empty on every install (NO CONSUMER YET below). tagline is public (broadcast-shaped); flavor is
	-- prompt-only and NEVER public (F115.3 — the persona-soul precedent). imported_from/imported_at
	-- mirror station.persona's own db/25 provenance pair exactly.
	--
	-- persona_id/envelope are DORMANT bundle columns (ARCHITECTURE.md ruled 2026-08-10): UNREAD until
	-- the deferred schedulable-bundle slice. Future semantics recorded there, not enforced here:
	-- effective assignment = block ?? show ?? none, block always wins.
	--
	-- NO CONSUMER YET (T219, still true after T238's widening): station.show stays dormant by design
	-- until F114/F115 wire a writer/reader, the same "seam before consumer" way station.persona_taste
	-- (T59), station.theme (T181), and station.font_pack (T198) all shipped. See
	-- db/33-show-and-segment-kind-migration.sh and db/35-show-identity-migration.sh for the in-place
	-- upgrade paths this table also ships as.
	CREATE TABLE IF NOT EXISTS station.show (
	  id            serial      PRIMARY KEY,
	  name          text        NOT NULL CHECK (length(btrim(name)) > 0),
	  slug          text        NOT NULL CONSTRAINT show_slug_key UNIQUE,
	  tagline       text,
	  flavor        text,
	  imported_from text,
	  imported_at   timestamptz,
	  persona_id    int         REFERENCES station.persona (id),
	  -- envelope-is-object (SPEC F152.3, db/41-gardener-migration.sh's own mirror): envelope.rotation
	  -- is the ONLY field ever read from this column (Deep Cuts, gh-#529), and only ever as a JSON
	  -- object — never a scalar/array. Named so a fresh-init table (this CREATE) and db/41's own
	  -- upgraded one land on the identical constraint name, the show_slug_key precedent immediately
	  -- above (db/35's own remarks).
	  envelope      jsonb        CONSTRAINT show_envelope_is_object
	                     CHECK (envelope IS NULL OR jsonb_typeof(envelope) = 'object'),
	  created_at    timestamptz NOT NULL DEFAULT now(),
	  updated_at    timestamptz NOT NULL DEFAULT now()
	);

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
	  -- SPEC F114 (gh-#383, schema ruled at STORY-304/T219): a nullable FK into station.show, created
	  -- just above this table so this FK resolves at fresh-init time. NULL means "no show branding"
	  -- (most painted blocks are unnamed); ON DELETE RESTRICT matches persona_id's own precedent
	  -- immediately above: unassign a show from every slot before deleting it, never a silent cascade
	  -- through the format clock. NO CONSUMER YET — dormant until the F114 slice.
	  show_id      int         REFERENCES station.show (id) ON DELETE RESTRICT,
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

	-- The visual layer (SPEC F128-F131, "The visual layer"; STORY-332, STORY-333, STORY-337, STORY-339,
	-- PLAN T290). See db/37-visual-layer-migration.sh for the in-place upgrade path these four tables
	-- also ship as, and its own remarks for the fuller rationale on every column below.
	--
	-- The worn face: 1:1 persona extension (F33 media_rating precedent — bytes off the hot persona row).
	-- token is UNIQUE and ROTATED on every write (F129.1, the F88 opaque-token art-transport idiom).
	CREATE TABLE IF NOT EXISTS station.persona_avatar (
	  id            serial      PRIMARY KEY,
	  persona_id    int         NOT NULL UNIQUE REFERENCES station.persona(id) ON DELETE CASCADE,
	  bytes         bytea       NOT NULL,        -- 512x512 normalized PNG, metadata-free
	  byte_size     int         NOT NULL,
	  sha256        text        NOT NULL,
	  token         text        NOT NULL UNIQUE, -- 128-bit hex; ROTATED on every write (F129.1)
	  source        text        NOT NULL CHECK (source IN ('upload','catalog')),
	  imported_from text,                        -- pack slug or persona-entry slug when source='catalog'
	  updated_at    timestamptz NOT NULL DEFAULT now()
	);

	-- Installed avatar packs: the library store (F104 font_pack shape immediately above).
	CREATE TABLE IF NOT EXISTS station.avatar_pack (
	  id            serial      PRIMARY KEY,
	  slug          text        NOT NULL UNIQUE,
	  definition    jsonb       NOT NULL,        -- the pack manifest
	  imported_from text        NOT NULL,        -- catalog slug (catalog is the only door)
	  imported_at   timestamptz NOT NULL DEFAULT now()
	);
	CREATE TABLE IF NOT EXISTS station.avatar_pack_item (
	  id                serial PRIMARY KEY,
	  pack_id           int    NOT NULL REFERENCES station.avatar_pack(id) ON DELETE CASCADE,
	  name              text   NOT NULL,
	  suggested_persona text,                    -- slug hint; an OFFER, never an auto-write
	  bytes             bytea  NOT NULL,
	  byte_size         int    NOT NULL,
	  sha256            text   NOT NULL,
	  UNIQUE (pack_id, name)
	);

	-- Installed icon packs: pure jsonb, no binary assets (SPEC F130.1's constrained vector document).
	CREATE TABLE IF NOT EXISTS station.icon_pack (
	  id            serial      PRIMARY KEY,
	  slug          text        NOT NULL UNIQUE,
	  definition    jsonb       NOT NULL,
	  imported_from text        NOT NULL,
	  imported_at   timestamptz NOT NULL DEFAULT now()
	);

	-- The station image: deliberate single-row deviation from serial pk — the row IS the image.
	CREATE TABLE IF NOT EXISTS station.station_image (
	  id         int         PRIMARY KEY DEFAULT 1 CHECK (id = 1),
	  bytes      bytea       NOT NULL,
	  byte_size  int         NOT NULL,
	  sha256     text        NOT NULL,
	  token      text        NOT NULL,           -- rotated on write; busts immutable caches
	  updated_at timestamptz NOT NULL DEFAULT now()
	);

	-- Announcements: the durable store & lifecycle (SPEC F143, STORY-357, PLAN T337, gh-#384 — the
	-- House Voice epic). A first-class, durable unit of content with a total state machine — never a
	-- fire-and-forget string (ARCHITECTURE.md's own design center). id is `generated always as
	-- identity`, not `serial`/`bigserial` like most tables in this schema — ARCHITECTURE.md's own
	-- data-model block spells the column out this exact way and this migration mirrors it verbatim.
	--
	-- state's five values ARE the whole lifecycle (SPEC F143.2): pending -> claimed -> aired;
	-- claimed -> pending (re-arm, SPEC F144.5); pending|claimed -> expired (TTL passed);
	-- pending|claimed -> declined (decline_reason set). No row is ever deleted by the pipeline; every
	-- transition — including expiry and decline — stamps state_changed_at, so no transition is silent.
	-- decline_reason is set iff state = 'declined'; requested_voice is an optional persona/voice
	-- override (SPEC F144.2); source distinguishes an HA/token-authenticated submission from an admin
	-- session one (SPEC F143.1's "token OR admin session" door). collapse_count starts at 1 and
	-- increments on every case-folded-identical pending duplicate (SPEC F143.5) — AnnouncementRepository
	-- (GenWave.MediaLibrary.Station) is the only writer of this table.
	CREATE TABLE IF NOT EXISTS station.announcement (
	  id               bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
	  message          text        NOT NULL CHECK (char_length(message) <= 280),
	  verbatim         boolean     NOT NULL DEFAULT false,
	  requested_voice  text,
	  source           text        NOT NULL DEFAULT 'token'
	                     CHECK (source IN ('token', 'session')),
	  state            text        NOT NULL DEFAULT 'pending'
	                     CHECK (state IN ('pending', 'claimed', 'aired', 'expired', 'declined')),
	  decline_reason   text,
	  collapse_count   int         NOT NULL DEFAULT 1,
	  created_at       timestamptz NOT NULL DEFAULT now(),
	  expires_at       timestamptz NOT NULL,
	  claimed_at       timestamptz,
	  aired_at         timestamptz,
	  state_changed_at timestamptz NOT NULL DEFAULT now()
	);

	-- The one query shape the vend/claim path needs (SPEC F144.1): the oldest deliverable
	-- (still-pending) announcements. Partial on `state = 'pending'` — every other state is a terminal
	-- or in-flight outcome this index has no reason to carry.
	CREATE INDEX IF NOT EXISTS announcement_deliverable
	  ON station.announcement (created_at)
	  WHERE state = 'pending';
	SQL

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
	CREATE TABLE IF NOT EXISTS station.persona (
	  id         serial      PRIMARY KEY,
	  name       text        NOT NULL UNIQUE,
	  backstory  text        NOT NULL DEFAULT '',
	  style      text        NOT NULL DEFAULT '',
	  voice      text        NOT NULL DEFAULT '',
	  slug       text        NOT NULL UNIQUE DEFAULT ('persona-' || nextval('station.persona_id_seq')::text),
	  definition jsonb       NOT NULL DEFAULT '{}'::jsonb,
	  enabled    boolean     NOT NULL DEFAULT true,
	  created_at timestamptz NOT NULL DEFAULT now(),
	  updated_at timestamptz NOT NULL DEFAULT now()
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
	  pick        jsonb
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
	SQL

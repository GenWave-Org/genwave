#!/bin/bash
# 52-memory-offer-migration.sh — idempotent in-place upgrade for existing DBs.
# F218.40-F218.47 (STORY-550, STORY-551; PLAN T730): station.memory_offer records every callback ask, and view
# station.callback_rate counts asks, uses and hours on air per DJ. Mirrored in db/06 (fresh installs); this
# script is the upgrade path.
# migrate.sh re-runs every db/*-migration.sh on every launch, and db/06 (which now also creates the table)
# runs before this script, so "the table does not exist yet" cannot mark the first run. The one-time data
# reset is guarded by the db/34 pattern instead: a claim row in station.one_time_fix, inserted and acted on in
# ONE statement, so only the run that wins the claim zeroes anything (safe against a concurrent run and against
# later unrelated schema edits). aired_count/last_aired_at on lore/episode/life used to mean "offered" and now
# mean "used" (F218.46). station.one_time_fix is a station-schema twin of library.one_time_fix: station_svc
# has no grant into library (db/22 boundary), so it cannot write that ledger. Fresh installs need no
# pre-claim here: launch.sh/migrate.sh run db/06 then db/52, so the first claim finds zero rows. A raw
# `docker compose up` mounts only db/01 and db/06 and never runs db/52, so db/06 claims the key itself when
# no lore/episode/life row holds a count (an upgraded box with old counts is left unclaimed for this script).
# Quirk spend rows are untouched.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	-- begin f218-memory-offer
	-- F218.40-F218.47 (STORY-550, STORY-551; PLAN T730): one row per ask, written before the model call; used flips
	-- when the line airs with the memory in it. Mirrored verbatim between db/06 and db/52 (a spec pins the text equal).
	CREATE TABLE IF NOT EXISTS station.memory_offer (
	  id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, -- bigint: one row per ask, no cap on the table's growth rate
	  memory_id     integer NOT NULL REFERENCES station.persona_memory(id) ON DELETE CASCADE,
	  persona_id    integer NOT NULL REFERENCES station.persona(id) ON DELETE CASCADE,
	  utterance_id  integer REFERENCES station.utterance(id) ON DELETE SET NULL, -- nullable-fk: unset until the line is written, and a pruned line leaves the ask
	  offered_at    timestamptz NOT NULL DEFAULT now(),
	  used          boolean NOT NULL DEFAULT false,
	  matched_words text[] NOT NULL DEFAULT '{}'
	);
	CREATE INDEX IF NOT EXISTS memory_offer_persona_recent ON station.memory_offer (persona_id, offered_at DESC);
	CREATE INDEX IF NOT EXISTS memory_offer_memory ON station.memory_offer (memory_id, offered_at DESC);

	-- Marker ledger for data-shaped (non-DDL) fixes in the station schema: one row per applied fix key.
	-- Twin of library.one_time_fix (db/34), which station_svc cannot reach.
	CREATE TABLE IF NOT EXISTS station.one_time_fix (
	  key        text        PRIMARY KEY, -- deliberate: same shape as db/34's ledger; the key is the identity, nothing references it
	  applied_at timestamptz NOT NULL DEFAULT now()
	);

	-- callback_rate: per DJ over the last 7 days. asks = offers, used = offers that aired, hours = distinct UTC clock
	-- hours holding a non-fallback line, used_per_hour = used / hours (NULL when the DJ had no hours on air).
	CREATE OR REPLACE VIEW station.callback_rate AS
	WITH offer AS (
	  SELECT persona_id, count(*)::integer AS asks, (count(*) FILTER (WHERE used))::integer AS used
	  FROM station.memory_offer
	  WHERE offered_at >= now() - interval '7 days'
	  GROUP BY persona_id
	),
	airtime AS (
	  SELECT persona_id, count(DISTINCT date_trunc('hour', written_at AT TIME ZONE 'UTC'))::integer AS hours
	  FROM station.utterance
	  WHERE NOT fallback AND written_at >= now() - interval '7 days'
	  GROUP BY persona_id
	)
	SELECT COALESCE(o.persona_id, a.persona_id) AS persona_id,
	       COALESCE(o.asks, 0) AS asks,
	       COALESCE(o.used, 0) AS used,
	       COALESCE(a.hours, 0) AS hours,
	       CASE WHEN COALESCE(a.hours, 0) = 0 THEN NULL
	            ELSE COALESCE(o.used, 0)::numeric / a.hours END AS used_per_hour
	FROM offer o
	FULL JOIN airtime a ON a.persona_id = o.persona_id;
	-- end f218-memory-offer

	-- One-time reset (F218.46): claim and reset in one statement; the CTE yields a row only on the first run.
	WITH claim AS (
	  INSERT INTO station.one_time_fix (key)
	  VALUES ('f218-callback-counts-reset')
	  ON CONFLICT (key) DO NOTHING
	  RETURNING key
	)
	UPDATE station.persona_memory
	   SET aired_count = 0, last_aired_at = NULL
	 WHERE EXISTS (SELECT 1 FROM claim)
	   AND kind IN ('lore','episode','life');
	SQL

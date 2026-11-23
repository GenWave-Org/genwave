#!/bin/bash
# 54-talk-rules-migration.sh — idempotent in-place upgrade for existing DBs.
# F220 (STORY-566; PLAN T778): station.talk_rule_switch (owner on/off overrides per rule and talk kind) and
# station.talk_check (every check attempt, shadow or rules mode) for the talk-rule book. Mirrored in db/06
# (fresh installs); this script is the upgrade path. v_repeat_catches_7d is deliberately not replaced here.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	-- begin f220-talk-rules
	-- F220 (STORY-566; PLAN T778): the talk-rule book's two tables. talk_rule_switch holds one row per rule the owner
	-- switched away from its default; talk_check is the append-only record of every check attempt (shadow and rules
	-- mode). Mirrored verbatim between db/06 and db/54 (a spec pins the text equal). v_repeat_catches_7d is untouched.
	CREATE TABLE IF NOT EXISTS station.talk_rule_switch (
	  rule_id    text        NOT NULL CHECK (rule_id ~ '^[a-z0-9]+(-[a-z0-9]+)*$'),
	  talk_kind  text        NOT NULL CHECK (talk_kind IN ('dj-line','chat','ad')),
	  enabled    boolean     NOT NULL,
	  changed_at timestamptz NOT NULL DEFAULT now(),
	  PRIMARY KEY (rule_id, talk_kind)
	);  -- no row = the rule's OnByDefault; a row for an unknown rule is ignored (rule removed)

	CREATE TABLE IF NOT EXISTS station.talk_check (
	  id             bigserial   PRIMARY KEY,  -- bigserial: ~1,000 rows a day, swept, ids never reused
	  checked_at     timestamptz NOT NULL DEFAULT now(),
	  talk_kind      text        NOT NULL CHECK (talk_kind IN ('dj-line','chat','ad')),
	  segment_kind   text,
	  persona_id     integer,            -- no FK: a deleted DJ must not delete the record
	  attempt        smallint    NOT NULL CHECK (attempt IN (1,2)),
	  mode           text        NOT NULL CHECK (mode IN ('shadow','rules')),
	  raw_reply      text        NOT NULL,
	  cleaned        text,               -- null = clean-up failed
	  snapshot       jsonb       NOT NULL,
	  verdicts       jsonb       NOT NULL, -- {rule_id: {on: bool, flag: {reason, ask, evidence}|null}}
	  legacy_outcome text,               -- shadow only: passed|trimmed|re-asked|template|dropped
	  outcome        text        NOT NULL  -- accepted|re-asked|template|dropped|failed
	);
	CREATE INDEX IF NOT EXISTS talk_check_checked_at ON station.talk_check (checked_at);
	-- end f220-talk-rules
	SQL

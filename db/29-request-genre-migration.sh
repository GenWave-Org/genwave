#!/bin/bash
# 29-request-genre-migration.sh — idempotent in-place upgrade for existing DBs.
# gh-#131 (genre predicate + genre/mood pickers on the request form) widens station.request:
#
#   genre        — the PARSED/merged genre predicate, written by MarkParsedAsync alongside
#                  artist/title/moods; case-insensitive exact match against library.media.genre at
#                  match/fulfillment time. Never swept (it is a parsed predicate, not raw text).
#   picked_genre — the request form's Genre dropdown value, validated by the intake endpoint against
#                  the CURRENT requestable-genre list (canonical catalog casing) before insert.
#   picked_mood  — the request form's Mood dropdown value, validated against MoodVocabulary.Terms
#                  before insert.
#
# Both picked_* columns hold server-validated list members only — never listener free text — so the
# F87.8 wish-retention sweep deliberately does not touch them (nothing to redact). wish becomes
# OPTIONAL at insert (a picker-only request stores null wish + picked values); the column itself was
# always nullable (the sweep nulls it), so no ALTER is needed for that.
#
# Safe to run multiple times (ADD COLUMN IF NOT EXISTS). Run this script once against any DB
# initialised before 06-station-settings-migration.sh received these columns.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	alter table station.request
	  add column if not exists picked_genre text,
	  add column if not exists picked_mood  text,
	  add column if not exists genre        text;
	SQL

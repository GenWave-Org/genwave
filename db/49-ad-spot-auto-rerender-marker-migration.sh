#!/bin/bash
# 49-ad-spot-auto-rerender-marker-migration.sh — idempotent in-place upgrade for existing DBs.
# gh-#865: records when the ad worker's stale pass (gh-#854) swapped a spot's take, and on which
# release, so the ad page and the booth log can say "Re-rendered automatically on vX". Stamped by the
# guarded swap; cleared by every operator-driven render (MarkReady), which makes the note stale.
# Mirrored in db/06's CREATE TABLE.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;
	alter table station.ad_spot
	  add column if not exists auto_rerendered_at timestamptz null;
	alter table station.ad_spot
	  add column if not exists auto_rerendered_on_version text null;
	SQL

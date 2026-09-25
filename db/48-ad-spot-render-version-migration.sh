#!/bin/bash
# 48-ad-spot-render-version-migration.sh — idempotent in-place upgrade for existing DBs.
# gh-#854: stamps the render pipeline version on each ad_spot so the worker can find + re-render
# stale 'ready' spots in the background without ever taking one off air. Also adds
# pending_retire_media_id and pending_confirm_media_id, the durable retry markers for the swap's own
# post-commit best-effort flips (old media turned off, new media confirmed eligible): each media row
# a swap touches converges eventually, even across a crash or a failed flip, because the fact
# survives in these columns until the flip actually lands. Mirrored in db/06's CREATE TABLE.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;
	alter table station.ad_spot
	  add column if not exists render_version int not null default 0;
	alter table station.ad_spot
	  add column if not exists pending_retire_media_id bigint null;
	alter table station.ad_spot
	  add column if not exists pending_confirm_media_id bigint null;
	SQL

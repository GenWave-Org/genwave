#!/bin/bash
# 24-request-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds station.request — introduced in the Listener Requests epic (gh-#105-era, explored/designed
# 2026-07-23), SPEC F87, STORY-224. The first public WRITE: an anonymous, throttled free-text wish
# that is interpreted into structured predicates and NEVER voiced, quoted, or echoed back.
#
# wish (raw listener text) is nulled by an insert-time sweep once received_at is older than
# Requests:WishRetentionHours (default 24h) — the same "eviction runs inside the insert's own
# transaction, in application code, not a separate job or trigger" discipline station.booth_log's
# retention sweep already established (db/06's own remarks). artist/title/moods (the PARSED
# predicates) and the row's outcome (status/matched_media_id/fulfilled_at) are never subject to that
# sweep and stay indefinitely — F87.8's whole point is that only the raw text is short-lived.
#
# matched_media_id deliberately carries NO foreign key: library.media lives on the other side of the
# schema-role boundary (station_svc has no grant there) — the exact station.booth_log.media_id
# precedent (db/22's own header).
#
# The request_pending index (status, expires_at) is the one query shape every consumer of this table
# needs: "find the oldest live pending request" (fulfillment, T90) and "count/evict pending rows"
# (the PendingCap throttle, T87) both filter on status and order/compare against expires_at.
#
# Safe to run multiple times (CREATE TABLE/INDEX IF NOT EXISTS). Run this script once against any DB
# initialised before 06-station-settings-migration.sh received station.request.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	create table if not exists station.request (
	  id               bigserial   primary key,
	  received_at      timestamptz not null default now(),
	  wish             text,                -- raw text; nulled by insert-time sweep after WishRetentionHours
	  artist           text,                -- parsed predicates, never voiced
	  title            text,
	  moods            text[],              -- MoodVocabulary-filtered
	  status           text        not null default 'pending'
	                     check (status in ('pending','fulfilled','expired','unmatched')),
	  matched_media_id bigint,              -- deliberately NO FK — library.media is the other schema
	                                        -- role (station.booth_log.media_id precedent, db/22)
	  fulfilled_at     timestamptz,
	  expires_at       timestamptz not null
	);

	create index if not exists request_pending
	  on station.request (status, expires_at);
	SQL

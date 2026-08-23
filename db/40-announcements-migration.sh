#!/bin/bash
# 40-announcements-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds station.announcement — introduced in the House Voice epic (gh-#384, designed 2026-08-22),
# SPEC F143, STORY-357, PLAN T337. A first-class, durable unit of content with a total state
# machine (pending -> claimed -> aired; claimed -> pending re-arm; pending|claimed -> expired;
# pending|claimed -> declined) — never a fire-and-forget string. No row is ever deleted by the
# pipeline; every transition, including expiry and decline, stamps state_changed_at, so no
# transition is silent (SPEC F143.2).
#
# id is `generated always as identity`, not `serial`/`bigserial` like most tables in this schema —
# ARCHITECTURE.md's own data-model block spells the column out this exact way and this migration
# mirrors it verbatim (and db/06's fresh-init mirror, identically).
#
# decline_reason is set iff state = 'declined'; requested_voice is an optional persona/voice
# override (SPEC F144.2); source distinguishes an HA/token-authenticated submission from an admin
# session one (SPEC F143.1's "token OR admin session" door); collapse_count starts at 1 and
# increments on every case-folded-identical pending duplicate (SPEC F143.5) —
# AnnouncementRepository (GenWave.MediaLibrary.Station) is the only writer of this table.
#
# The announcement_deliverable index is partial (where state = 'pending'): the one query shape the
# vend/claim path needs (SPEC F144.1) is "oldest still-pending announcements" — every other state
# is a terminal or in-flight outcome this index has no reason to carry.
#
# Safe to run multiple times (CREATE TABLE/INDEX IF NOT EXISTS). Run this script once against any
# DB initialised before 06-station-settings-migration.sh received station.announcement.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	create table if not exists station.announcement (
	  id               bigint      generated always as identity primary key,
	  message          text        not null check (char_length(message) <= 280),
	  verbatim         boolean     not null default false,
	  requested_voice  text,
	  source           text        not null default 'token'
	                     check (source in ('token', 'session')),
	  state            text        not null default 'pending'
	                     check (state in ('pending', 'claimed', 'aired', 'expired', 'declined')),
	  decline_reason   text,
	  collapse_count   int         not null default 1,
	  created_at       timestamptz not null default now(),
	  expires_at       timestamptz not null,
	  claimed_at       timestamptz,
	  aired_at         timestamptz,
	  state_changed_at timestamptz not null default now()
	);

	create index if not exists announcement_deliverable
	  on station.announcement (created_at)
	  where state = 'pending';
	SQL

#!/bin/bash
# 39-time-announcement-budget-migration.sh — idempotent in-place upgrade for existing DBs (SPEC
# F141.1, STORY-355, PLAN T326, review round-1 finding F1).
#
# Renames the station.settings row (when one exists) from the retired
# Station:Imaging:TimeAnnouncementStaleMinutes key (minutes) to
# Station:Imaging:TimeAnnouncementBudgetSeconds (seconds, F141.1's own unit change), converting the
# stored value minutes*60 in the SAME statement. Closes review finding F1 on PLAN T326: without this
# script, an operator's persisted override under the OLD key is silently ignored at boot
# (StationSettingsConfigurationProvider.Load skips any key not on StationSettingsAllowlist, gh-#412 —
# no WARN, nothing survives), invisible on GET /api/settings (the allowlist no longer names the old key
# either — SettingsController only ever reads allowlisted keys), and un-deletable through the product
# (no DELETE surface for a key not on the allowlist).
#
# FRESH INSTALLS NEVER RUN THIS SCRIPT AT ALL: db/06-station-settings-migration.sh only CREATEs the
# EMPTY station.settings table — it seeds no row for this (or any) key. A row for the old key exists
# ONLY when an operator actually saved a custom TimeAnnouncementStaleMinutes value through
# PUT /api/settings before this release; this script is therefore a no-op on a fresh install AND on any
# existing install that never touched this particular setting (the common case).
#
# IDEMPOTENCY: the UPDATE's own WHERE clause is "the old key still exists" — the first run renames it
# away, so that clause never matches again on a second run (no double-multiply: 10 minutes becomes 600
# seconds once, never 36000). The `NOT EXISTS (new key)` guard additionally protects station.settings'
# own PRIMARY KEY on `key` from a rename-into-collision, for the otherwise-unreachable pre-feature case
# both rows already exist.
#
# Value extraction/conversion mirrors db/27-segment-schedule-migration.sh's own
# `(value #>> '{}')::bigint` idiom for reading a station.settings JSONB scalar as a plain number (see
# that script's own F91.6 seed-and-delete remarks) — `#>>` with an empty path array returns the
# top-level JSONB scalar as text, which a bare numeric literal (never quoted, unlike a JSON string)
# casts straight through.
#
# Safe to run multiple times.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	update station.settings
	set key   = 'Station:Imaging:TimeAnnouncementBudgetSeconds',
	    value = to_jsonb((value #>> '{}')::int * 60)
	where key = 'Station:Imaging:TimeAnnouncementStaleMinutes'
	  and not exists (
	    select 1 from station.settings
	    where key = 'Station:Imaging:TimeAnnouncementBudgetSeconds'
	  );
	SQL

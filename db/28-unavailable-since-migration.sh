#!/bin/bash
# 28-unavailable-since-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds library.media.unavailable_since — introduced by gh-#113 (hide unavailable rows from the
# catalog view; explicit operator purge). Stamped by the scan when a row transitions
# available→unavailable (MediaRepository.MarkUnavailableAsync), cleared on resurrection
# (MarkDiscoveredAsync / InsertDiscoveredAsync's on-conflict re-discovery, the gh-#112 path).
# NULL = the row is not unavailable, or was flipped before this column existed and has not been
# backfilled. The purge endpoint's "unavailable longer than N days" age filter reads this column
# and deliberately never treats a NULL stamp as purgeable.
#
# Backfill: rows already sitting in state='unavailable' (the 1500-row demo-library shrink that
# motivated gh-#113) predate the column, so how long they have ACTUALLY been unavailable is
# unknowable. They are stamped now() — honest "unavailable since at least this migration" — which
# makes them purge-eligible only after they age past the operator's N-day window from today,
# never retroactively on the first purge after upgrade. The backfill only ever touches NULL
# stamps, so re-running this script cannot reset a real stamp.
#
# Safe to run multiple times (ADD COLUMN IF NOT EXISTS; backfill matches only NULL stamps). Run
# this script once against any DB initialised before 01-library.sh received this column.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role library_svc;
	set search_path = library;

	alter table library.media
	  add column if not exists unavailable_since timestamptz;

	update library.media
	  set unavailable_since = now()
	  where state = 'unavailable' and unavailable_since is null;
	SQL

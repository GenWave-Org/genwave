#!/bin/bash
# 30-imaging-kind-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds library.media.imaging_kind — introduced by gh-#149 (Station Imaging content kinds). The
# Station Imaging content kind of an AUTHORED segment, stamped by
# MediaRepository.InsertAuthoredAsync on every authored insert ('liner' by default, the pre-kind
# behavior; 'station_id'/'jingle'/'promo' when the operator picks one).
#
# NULL means "no kind recorded": every scanned row (the scan/enrichment paths never write this
# column), and every authored row that predates this migration. Deliberately NO backfill — after
# the fact nothing structurally distinguishes an old authored row from a scanned row sharing its
# library, so stamping anything would be a guess; the admin UI displays a NULL kind as the Liner
# default instead.
#
# Read by a real production caller as of PLAN T231/T232 (SPEC F110.2): MediaRepository's
# GetRandomReadyByImagingKindAsync selects on it, and the Orchestrator's top-of-hour StationId
# drain queries it directly (station IDs drawn from the station_id pool). Playout and the
# /internal/safe-track predicate still never read this column (F110.4: only StationId gained a
# selection role this cycle).
#
# Safe to run multiple times (ADD COLUMN IF NOT EXISTS; no backfill to re-run). Run this script
# once against any DB initialised before 01-library.sh received this column.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role library_svc;
	set search_path = library;

	alter table library.media
	  add column if not exists imaging_kind text
	    check (imaging_kind is null or imaging_kind in ('liner', 'station_id', 'jingle', 'promo'));
	SQL

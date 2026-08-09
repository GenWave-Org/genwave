#!/bin/bash
# 34-cue-remeasure-migration.sh — idempotent ONE-TIME cue reset for existing DBs (gh-#424).
#
# Every cue point measured before v3.3.1 came from an analyzer that took the LAST silence region
# as cue_out even when it ended mid-file — so any track with an interior pause (a quiet break, a
# TTS sentence gap in authored imaging) carries a persisted cue_out that truncates it on air. The
# analyzer is fixed (PR #435: trailing must actually extend to EOF); this migration retires the
# stale measurements so no operator has to run a manual bulk re-enrich (the "zero manual release
# steps" rule).
#
# What it does, exactly once: nulls cue_in_sec / cue_out_sec / cue_analyzed_at on every already-
# analyzed library.media row. Nulling is instantly curative — NULL cue = full-file playback, so
# clipping stops at deploy — and the F13 backfill predicate (state='ready' AND cue_analyzed_at IS
# NULL) re-measures every row with the fixed analyzer at the enrichment worker's own pace. The
# brief window before a row re-measures plays untrimmed head/tail silence; the engine's blank.eat
# is the documented backstop.
#
# ONE-TIME, not merely idempotent-by-DDL: a bare UPDATE would re-null freshly re-measured cues on
# every future deploy. The library.one_time_fix marker table (created here; reusable by any later
# data-shaped fix until the real migration runner, gh-#12, lands) guards the UPDATE — the CTE only
# yields a row on the run that first claims the key, so re-runs are provable no-ops.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role library_svc;
	set search_path = library;

	-- Marker ledger for data-shaped (non-DDL) fixes: one row per applied fix key. Survives until
	-- gh-#12 replaces bash-as-baseline with a real migration runner.
	create table if not exists library.one_time_fix (
	  key        text        primary key,
	  applied_at timestamptz not null default now()
	);

	-- Claim-then-fix in one statement: the INSERT yields a row only on the first run, so the
	-- UPDATE runs exactly once per database, ever.
	with claim as (
	  insert into library.one_time_fix (key)
	  values ('gh-424-cue-remeasure')
	  on conflict (key) do nothing
	  returning key
	)
	update library.media m
	set cue_in_sec = null, cue_out_sec = null, cue_analyzed_at = null
	where exists (select 1 from claim)
	  and m.cue_analyzed_at is not null;
SQL

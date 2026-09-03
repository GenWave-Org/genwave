#!/bin/bash
# 43-ad-spot-invariants-migration.sh — idempotent in-place upgrade for existing DBs.
# T389 review carry-forward, closed at PLAN T398 (SPEC F159.2; STORY-389). db/42 narrated two
# invariants of station.ad_spot's state machine in prose but enforced neither in the DDL: "ready
# requires a non-null media_id" and "fail_reason set iff state = 'failed'". T398's own store
# (GenWave.MediaLibrary.Station.AdSpotRepository) now closes both in C# — CreateAsync rejects a
# mismatched (InitialState, FailReason) pair, and MarkReadyAsync's own `long mediaId` parameter (never
# nullable) makes the illegal call impossible to even write — but PLAN T398's own ruling (RULING LEAN)
# was to ALSO add the two invariants as real Postgres CHECK constraints: cheap on a table this size,
# and a genuine backstop against any future write path that bypasses the store entirely (a hand-run
# migration, a different process, a bug in the store itself).
#
# ADD CONSTRAINT has no "IF NOT EXISTS" clause, so each is guarded by a pg_constraint existence check
# inside a DO block — the SAME idiom db/41's own show_envelope_is_object CHECK and db/42's own
# imaging_kind widen already establish (see either script's own header).
#
# station.ad_spot's own query shapes this task's store actually writes (PLAN T398's own design note —
# "design from the queries you actually write, document the choice"): a state-scoped listing ordered
# `state_changed_at desc, id desc` (AdSpotRepository.ListByStateAsync), the render worker's own
# oldest-first approved claim ordered `state_changed_at asc, id asc` (ClaimNextApprovedAsync), and the
# stock pass's own ready-by-age scan, `state = 'ready' and state_changed_at < threshold`
# (ListReadyOlderThanAsync). All three share the SAME two-column shape — an equality (or small IN
# list) on `state` followed by a scan/range on `state_changed_at` — so ONE composite btree index,
# `(state, state_changed_at)`, covers every one of them: a plain btree is bidirectionally scannable,
# so the state-scoped listing's own DESCENDING order rides the same index backwards, and the
# approved-claim/ready-by-age reads ride it forwards. No second index is added for `created_at` (the
# row's insert time) — nothing in this task queries by it, and adding an index nothing reads is exactly
# the footgun the Gardener's own `RotFindingRepository` remarks warn against elsewhere in this
# directory.
#
# Fresh-init mirror: db/06-station-settings-migration.sh's own station.ad_spot CREATE TABLE now
# carries both CHECK constraints inline and the same index (the gh-618 lesson every migration file in
# this directory repeats: a migration without its fresh-init mirror haunts the next box that installs
# clean rather than upgrades).
#
# Safe to run multiple times: both constraint adds are pg_constraint-guarded, and the index uses
# `CREATE INDEX IF NOT EXISTS`.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	-- No "add constraint if not exists" in Postgres — guarded the same pg_catalog-existence way
	-- db/41's own show_envelope_is_object CHECK is.
	do $$
	begin
	  if not exists (
	    select 1 from pg_constraint
	    where conname = 'ad_spot_ready_requires_media_id' and conrelid = 'station.ad_spot'::regclass
	  ) then
	    alter table station.ad_spot
	      add constraint ad_spot_ready_requires_media_id
	      check (state <> 'ready'::station.ad_state or media_id is not null);
	  end if;

	  if not exists (
	    select 1 from pg_constraint
	    where conname = 'ad_spot_fail_reason_iff_failed' and conrelid = 'station.ad_spot'::regclass
	  ) then
	    alter table station.ad_spot
	      add constraint ad_spot_fail_reason_iff_failed
	      check ((state = 'failed'::station.ad_state) = (fail_reason is not null));
	  end if;
	end $$;

	-- One composite index serving every query shape this task's store writes — see this script's
	-- own header for why a single (state, state_changed_at) index covers all three.
	create index if not exists ad_spot_state_changed_at on station.ad_spot (state, state_changed_at);
	SQL

#!/bin/bash
# 47-ad-spot-job-failed-kind-migration.sh — idempotent in-place upgrade for existing DBs.
# STORY-435 (gh-#724), PLAN T462: "A failed job says which step failed." job_kind is nulled the
# moment a job settles (success or failure), so a failed job's own step is lost by the time job_error
# is readable — the two columns never carry the answer at the same moment. job_failed_kind is its own
# column: stamped alongside job_error on failure, nulled when a fresh job is stamped (T463 owns the
# store wiring; this migration only adds the column). The why in full: the header of
# tests/GenWave.Host.Tests/Specs/Story435_FailedJobKind.cs.
#
# ADD COLUMN IF NOT EXISTS with an inline CHECK is idempotent on its own — Postgres does not re-add
# the constraint on a second run once the column exists — so this needs none of db/43's own
# pg_constraint DO-block guard (ADD CONSTRAINT, unlike ADD COLUMN, has no IF NOT EXISTS).
#
# Fresh-init mirror: db/06-station-settings-migration.sh's own station.ad_spot CREATE TABLE carries
# the identical column + CHECK inline (the gh-#618 lesson: a migration without its fresh-init mirror
# haunts the next box that installs clean rather than upgrades). The CHECK predicate text is
# byte-identical between the two files — pinned by ScenarioTheFailedKindColumnIsMirroredInFreshInit
# in the spec file above.
#
# No new table: this migration only ALTERs an existing one, so setup.sh's migration-marker scan
# (verify_derive_migration_marker greps for create-table statements) still stops at db/46, the
# highest migration that creates one. setup.sh's B2 range message therefore reports "Can't verify
# past db/46 — db/47 adds no new table (repo's db/ max)" once this file lands — the correct, honest
# degrade (see verify_migrations), not a bug this migration needs to work around.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;
	alter table station.ad_spot
	  add column if not exists job_failed_kind text
	    check (job_failed_kind IS NULL OR job_failed_kind IN ('write', 'preview'));
	SQL

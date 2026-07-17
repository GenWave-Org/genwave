#!/bin/bash
# 06-station-settings-migration.sh — idempotent bootstrap for the station settings overlay store.
# Creates the station_svc role, station schema, and station.settings table introduced in STORY-042.
# Safe to run multiple times (role existence is checked before CREATE ROLE; all DDL uses IF NOT EXISTS).
# Run via: bash -s < db/06-station-settings-migration.sh   (piped into the Postgres container)
# Or mounted into /docker-entrypoint-initdb.d/ for fresh deployments.
#
# Role-creation idiom: psql variable interpolation (:'var') works in heredoc SQL bodies but NOT in
# -c arguments, and NOT inside dollar-quoted DO $$ blocks (the colon is PL/pgSQL syntax there).
# Solution: use a double-quoted heredoc (shell substitution) for the CREATE ROLE statement only,
# so the shell injects the password as a shell-quoted literal. The rest of the DDL uses a
# single-quoted heredoc (no shell expansion needed — fully idempotent SQL). Consistent with
# db/01-library.sh's role-creation approach.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" \
  "${POSTGRES_DB:?POSTGRES_DB must be set}" \
  "${STATION_DB_PASSWORD:?STATION_DB_PASSWORD must be set for the station_svc role}"

# Step 1: create station_svc role if not already present.
# Shell-level check then CREATE ROLE in a double-quoted heredoc so the shell expands $STATION_DB_PASSWORD.
# The password is embedded as a SQL string literal (single-quoted by the surrounding SQL syntax).
role_exists=$(psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  --tuples-only --no-align \
  -c "SELECT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'station_svc')")

if [ "$role_exists" = "f" ]; then
  # Double-quoted here-doc: shell expands $STATION_DB_PASSWORD into the SQL body.
  # The password is enclosed in single quotes in the SQL; we escape any embedded single quotes.
  escaped_pw="${STATION_DB_PASSWORD//\'/\'\'}"
  psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-SQL
		CREATE ROLE station_svc WITH LOGIN PASSWORD '${escaped_pw}';
	SQL
fi

# Step 2: remaining DDL — all idempotent (IF NOT EXISTS, idempotent ALTER ROLE).
# Single-quoted here-doc: no shell expansion needed.
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	-- Pin search_path so station_svc never accidentally resolves objects in other schemas.
	ALTER ROLE station_svc SET search_path = station;

	-- Schema owned by the service role; subsequent DDL runs as station_svc so it owns everything.
	CREATE SCHEMA IF NOT EXISTS station AUTHORIZATION station_svc;

	-- Switch to the service role so it owns every object it creates (real isolation).
	SET ROLE station_svc;
	SET search_path = station;

	-- Key-value overlay store (STORY-042, Epic I). Keys are allowlisted in the C# provider;
	-- secrets (ConnectionStrings:*, Admin:Password, ICECAST_SOURCE_PASSWORD) are never written here.
	CREATE TABLE IF NOT EXISTS station.settings (
	  key        text        NOT NULL PRIMARY KEY,
	  value      jsonb       NOT NULL,
	  updated_at timestamptz NOT NULL DEFAULT now()
	);

	-- DJ persona storage (SPEC F35.1, STORY-118, Epic T). Lives in this same station schema/role —
	-- same station_svc owner, same isolation guarantee station.settings already has. '' voice is a
	-- deliberate sentinel meaning "use the station's own Station:Voice", not "unset".
	CREATE TABLE IF NOT EXISTS station.persona (
	  id         serial      PRIMARY KEY,
	  name       text        NOT NULL UNIQUE,
	  backstory  text        NOT NULL DEFAULT '',
	  style      text        NOT NULL DEFAULT '',
	  voice      text        NOT NULL DEFAULT '',
	  created_at timestamptz NOT NULL DEFAULT now(),
	  updated_at timestamptz NOT NULL DEFAULT now()
	);
SQL

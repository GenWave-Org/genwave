#!/bin/bash
# 07-library-management-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds UNIQUE constraint on library.library.name introduced in STORY-046 (Epic J).
# Safe to run multiple times: the DO block checks information_schema before issuing DDL.
# Run this script once against any DB initialised before 01-library.sh received this constraint.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role library_svc;
	set search_path = library;

	-- Add UNIQUE(name) on library.library if the constraint does not already exist.
	-- The IF NOT EXISTS guard makes this block safe to re-run (idempotent).
	DO $$
	BEGIN
	    IF NOT EXISTS (
	        SELECT 1
	        FROM information_schema.table_constraints
	        WHERE constraint_schema = 'library'
	          AND table_name        = 'library'
	          AND constraint_name   = 'library_name_key'
	          AND constraint_type   = 'UNIQUE'
	    ) THEN
	        ALTER TABLE library.library
	            ADD CONSTRAINT library_name_key UNIQUE (name);
	    END IF;
	END;
	$$;
SQL

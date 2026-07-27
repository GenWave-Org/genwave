#!/bin/bash
# 26-explicit-classification-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds library.media.explicit / explicit_source — introduced in SPEC F95.2, STORY-251 (PLAN T110).
# `explicit` is a plain nullable boolean: NULL = unknown/unclassified, true/false = classified either
# way. `explicit_source` names WHO classified the row, constrained to the three known origins
# (F95.3): 'tag' (advisory flag carried in the file's own metadata, stamped first), 'llm' (the
# offline sweep asking a model about unclassified rows), 'operator' (an explicit admin override that
# later sweeps must never touch again). This migration ships schema only — no enforcement, no
# classification pipeline; every existing row gets explicit=NULL/explicit_source=NULL until a later
# task (T112/T113/T115) starts writing them. Safe to run multiple times (ADD COLUMN IF NOT EXISTS).
# Run this script once against any DB initialised before 01-library.sh received these two columns.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role library_svc;
	set search_path = library;

	alter table library.media
	  add column if not exists explicit boolean;

	alter table library.media
	  add column if not exists explicit_source text
	    check (explicit_source is null or explicit_source in ('tag', 'llm', 'operator'));
	SQL

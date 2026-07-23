#!/bin/bash
# 23-artwork-token-migration.sh — idempotent in-place upgrade for existing DBs.
# Adds library.media.artwork_token — introduced in gh-#105 (per-track artwork), SPEC F88.2,
# STORY-222. A random 128-bit value (32 lowercase hex chars), generated lazily by
# ArtworkTokenRepository.GetOrCreateTokenAsync on a row's first need — NOT backfilled here or
# anywhere else, since F88.2 only ever emits a token for a track that actually airs. Deliberately
# NO foreign key: this column lives entirely within library.media itself (no cross-schema
# boundary, unlike station.booth_log.media_id in db/22).
#
# The unique index is partial (where artwork_token is not null): Postgres unique indexes already
# treat multiple NULLs as distinct, so the predicate is not required for correctness — it is here
# to keep the index small and honest, since every row starts and typically stays NULL (most tracks
# never air). Non-enumerability (F88.2, F62.9) comes from the token's randomness and the
# resolution seam never exposing media ids, not from this index.
# Safe to run multiple times (ADD COLUMN IF NOT EXISTS, CREATE UNIQUE INDEX IF NOT EXISTS). Run
# this script once against any DB initialised before 01-library.sh received
# library.media.artwork_token.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role library_svc;
	set search_path = library;

	alter table library.media
	  add column if not exists artwork_token text;

	create unique index if not exists media_artwork_token
	  on library.media (artwork_token)
	  where artwork_token is not null;
	SQL

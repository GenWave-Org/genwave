#!/bin/bash
# 41-gardener-migration.sh — idempotent in-place upgrade for existing DBs.
# The Library Gardener's data model (gh-#529, SPEC F149.1-F149.3, F153.1, F153.5, F154.7; STORY-367,
# STORY-376; PLAN T354). ARCHITECTURE.md "The Library Gardener" -> "Data model (db/41; fresh-init
# mirrors in db/06 + 01-library.sh)" has this DDL's sketch; this script is the refined, idempotent
# version of it, and db/06 + db/01-library.sh carry the schema-only mirror for a fresh install (the
# same "fresh installs never see a numbered upgrade script, only 01+06 through
# docker-entrypoint-initdb.d" rule every earlier migration in this directory follows — see db/35's
# own remarks).
#
# ENUM TYPES ARE NEW TO THIS SCHEMA (the first `create type` anywhere under db/) — `create type` has
# no `if not exists` clause, so each one is guarded by a `pg_type` existence check inside a DO block,
# the same idiom the CHECK constraint below reuses for `pg_constraint`.
#
# library.media_rotation is a 1:1 extension of library.media (PK = FK, ON DELETE CASCADE) — the SAME
# postgres-dba Rule-2 deviation library.media_rating (db/01, STORY-109) already ships: a write here
# must never bump library.media's own xmin (F18.6 ETags survive an airing, F149.1).
#
# fold_key/title_variant/title_key (SPEC F153.5, STORY-376 AC1/AC2): fold_key is the general
# normalizer (lower, strip diacritics via a fixed translate() map — no unaccent extension, no
# CREATE EXTENSION — collapse punctuation/whitespace); title_variant extracts a trailing "(...)"/
# "[...]" group's own folded content (null when there is none); title_key folds the title with that
# SAME trailing group stripped first, so "Song", "Song (feat. X)", "Song [Live]", and "Song (2011
# Remaster)" all fold to the identical title_key while their title_variant values diverge. All three
# functions are IMMUTABLE + STRICT (a NULL argument short-circuits to NULL with no body execution) —
# STRICT is also why library.media's artist_key/title_key/title_variant generated columns below stay
# NULL for a NULL artist/title without any CASE in the generation expression itself.
#
# find_near_duplicates(tolerance_ms) (SPEC F153.5): the playable predicate is the FULL
# MediaRepository.PlayablePredicate (src/GenWave.MediaLibrary/Catalog/MediaRepository.cs) —
# "m.state = 'ready' and m.measurable and m.eligible and not coalesce(r.never_play, false)",
# LEFT JOIN library.media_rating included (T354 review MED-1 finding: an earlier draft of this
# function omitted the never_play half on the theory that curation was out of scope for a SQL-only
# pass — wrong, since PlayablePredicate has exactly ONE definition and a near-duplicate finding
# against a track nobody will ever hear is noise, not signal). STABLE, not IMMUTABLE: its result
# depends on the CONTENTS of library.media (and library.media_rating) at call time, not just its
# own arguments. Tolerance never chains transitively (T354 review LOW-2, RULED): every candidate is
# measured against its OWN group's shortest duration (a window function, not the self-join this
# function used before), so 200s/201.5s/203s at a 2s tolerance groups the first two and drops the
# third rather than letting 201.5s bridge them. group_key folds in title_variant (T354 review LOW-1,
# "F153.5 amended at T354 review", RULED) so a studio pair and a live pair of the identical song —
# already kept apart as separate GROUPS by the variant partition — never collide onto one text key.
#
# The one-shot booth-log -> ledger seed (SPEC F149.3, STORY-367 AC5/AC6) runs as the bootstrap
# superuser this whole script is already connected as (no SET ROLE) — the ONE place in this file that
# reads across the station/library role boundary in a single statement, deliberately: station_svc has
# no grant into library and library_svc has no grant into station (db/22's own boundary), and a
# superuser bypasses both by definition. `on conflict (media_id) do nothing` makes a re-run a no-op
# (AC6); the join against library.media (not a bare booth_log scan) is what keeps a media_id that no
# longer exists in the catalog from ever violating media_rotation's own FK. Gardener:RotationSince is
# stamped into station.settings the same key-value shape StationSettingsRepository.WriteAsync already
# writes (key/value jsonb/updated_at), guarded by `where not exists` rather than an upsert — ONLY IF
# ABSENT, since a real re-stamp would move the epoch every never-aired count is read beside (F149.3).
#
# station.show's envelope-is-object CHECK (SPEC F152.3's own read shape: `station.show.envelope` is
# read for `rotation` only, and only ever as a JSON object) has no `add constraint if not exists`
# syntax in Postgres either, so it gets the same pg_constraint-guarded DO block as the enum types.
#
# Safe to run multiple times: every CREATE is IF NOT EXISTS or DO-block-guarded, the seed's own
# idempotency is described above, and CREATE OR REPLACE FUNCTION is naturally idempotent (same body in,
# same body out). Run this script once against any DB initialised before 06-station-settings-migration.sh
# + 01-library.sh received the Gardener's tables (db/06's own CHECK) and library objects (db/01's own
# mirror) respectively.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

# --- library schema: enum types, the four Gardener tables, fold/key functions + generated columns,
#     find_near_duplicates -------------------------------------------------------------------------
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role library_svc;
	set search_path = library;

	do $$
	begin
	  if not exists (select 1 from pg_type where typname = 'thumb_direction' and typnamespace = 'library'::regnamespace) then
	    create type library.thumb_direction as enum ('up', 'down');
	  end if;
	  if not exists (select 1 from pg_type where typname = 'thumb_source' and typnamespace = 'library'::regnamespace) then
	    create type library.thumb_source as enum ('spectator', 'operator');
	  end if;
	  if not exists (select 1 from pg_type where typname = 'rot_kind' and typnamespace = 'library'::regnamespace) then
	    create type library.rot_kind as enum ('dead_file', 'near_duplicate', 'stale_metadata', 'shelf_dust', 'unreachable');
	  end if;
	  if not exists (select 1 from pg_type where typname = 'rot_state' and typnamespace = 'library'::regnamespace) then
	    create type library.rot_state as enum ('open', 'dismissed', 'resolved');
	  end if;
	  if not exists (select 1 from pg_type where typname = 'file_verb' and typnamespace = 'library'::regnamespace) then
	    create type library.file_verb as enum ('retag', 'rename', 'move');
	  end if;
	end $$;

	-- postgres-dba Rule-2 deviation, the media_rating precedent (db/01, STORY-109): PK = FK on
	-- purpose (1:1 extension) — a write here must never bump library.media's own xmin (F18.6, F149.1).
	create table if not exists library.media_rotation (
	  media_id          bigint primary key references library.media(id) on delete cascade,
	  play_count        int  not null default 0 check (play_count >= 0),
	  first_aired_at    timestamptz,            -- null = never aired since the ledger began
	  last_aired_at     timestamptz,
	  thumbs_up         int  not null default 0,
	  thumbs_down       int  not null default 0,
	  nudge             real not null default 0 check (nudge between -1 and 1),
	  nudge_computed_at timestamptz,
	  updated_at        timestamptz not null default now()
	);

	-- One row per thumb per listener (bigserial: unbounded, swept by ThumbRetentionDays once T365
	-- wires it — this migration only creates the table). Idempotent per (media, airing, listener); a
	-- flip is an UPDATE of direction, never a second row.
	create table if not exists library.media_thumb (
	  id                bigserial primary key,
	  media_id          bigint not null references library.media(id) on delete cascade,
	  airing_started_at timestamptz not null,
	  listener_key      text   not null,        -- sha256(cookie token) or 'operator'; never logged
	  direction         library.thumb_direction not null,
	  source            library.thumb_source    not null,
	  created_at        timestamptz not null default now(),
	  unique (media_id, airing_started_at, listener_key)
	);

	-- T366 review MED-1: MediaThumbRepository.CountByListenerSinceAsync filters
	-- (listener_key, created_at) — the unique index above leads with media_id, so that query was a
	-- seq scan on every anonymous write (the F150.5 per-listener daily cap read). Own index, listener
	-- first (the query's own equality predicate) then created_at desc (the >= @since range half).
	create index if not exists media_thumb_listener_created_idx
	  on library.media_thumb (listener_key, created_at desc);

	-- One row per (media, kind) forever (SPEC F153.1): a pass opens/re-opens/resolves it, the owner
	-- dismisses it, `dismissed` is never re-opened — state moves, the row does not multiply.
	create table if not exists library.rot_finding (
	  id          bigserial primary key,
	  media_id    bigint not null references library.media(id) on delete cascade,
	  kind        library.rot_kind  not null,
	  state       library.rot_state not null default 'open',
	  group_key   text,                          -- nullable: only near_duplicate carries a group
	  evidence    jsonb not null default '{}' check (jsonb_typeof(evidence) = 'object'),
	  opened_at   timestamptz not null default now(),
	  resolved_at timestamptz,
	  dismissed_at timestamptz,
	  updated_at  timestamptz not null default now(),
	  unique (media_id, kind)
	);
	create index if not exists rot_finding_state_kind on library.rot_finding (state, kind);
	create index if not exists rot_finding_group_key on library.rot_finding (group_key) where group_key is not null;

	-- The audit of every destructive file action (SPEC F154.7) — verb/from/to/plan token/outcome/
	-- detail, never the booth log. No verb here ever deletes (F154.1: retag, rename, move only).
	create table if not exists library.file_action (
	  id            bigserial primary key,
	  media_id      bigint not null references library.media(id) on delete cascade,
	  verb          library.file_verb not null,
	  from_path     text not null,
	  to_path       text,
	  plan_token    text not null,
	  performed_at  timestamptz not null default now(),
	  outcome       text not null,
	  detail        jsonb not null default '{}'
	);

	-- Rule 6/7 (postgres-dba): the fold is IMMUTABLE plpgsql, the keys are STORED generated columns.
	-- fold_key: lower, trim, strip diacritics (fixed Latin-1/Latin-Extended translate() map — no
	-- unaccent, no CREATE EXTENSION), collapse punctuation/whitespace to single spaces. STRICT: a
	-- NULL argument returns NULL without the body ever running (STORY-376 AC1's "the fold"). A title
	-- or artist with NO Latin/digit content at all (Cyrillic, CJK, Greek, or a blank tag — T354
	-- review HIGH finding) folds down to an EMPTY string, not a NULL one, if left un-guarded — and an
	-- empty string is a valid, EQUAL value, so every such row would land in one bogus duplicate group
	-- keyed on '' downstream. nullif() turns that empty result back into NULL, the honest "no usable
	-- key" answer, which artist_key/title_key/title_variant (STORED below) and
	-- find_near_duplicates's own `is not null` guard then propagate correctly with no other change.
	create or replace function library.fold_key(p_text text)
	returns text
	language plpgsql
	immutable
	strict
	security invoker
	set search_path = library, pg_temp
	as $$
	declare
	  v_folded text;
	begin
	  -- translate() maps each lowercase accented Latin-1/Latin-Extended-A letter to its plain ASCII
	  -- form, one character at a time (translate has no multi-character replacement, so ligatures
	  -- are out of scope here — none appear in the STORY-376 fixtures). lower() runs FIRST (matches
	  -- the SPEC F153.5 order: "lower, trim, strip diacritics"), so only the lowercase accented forms
	  -- need a mapping.
	  v_folded := translate(
	    lower(btrim(p_text)),
	    'àáâãäåçèéêëìíîïñòóôõöøùúûüýÿąćčďęěğłńňőřśšťůűźżžĺľīū',
	    'aaaaaaceeeeiiiinoooooouuuuyyaccdeeglnnorsstuuzzzlliu'
	  );

	  -- Any run of characters that is not a-z0-9 (leftover punctuation, brackets, or whitespace
	  -- itself) collapses to a single space — this is BOTH the punctuation-to-space step and the
	  -- whitespace-collapse step in one pass.
	  v_folded := regexp_replace(v_folded, '[^a-z0-9]+', ' ', 'g');

	  -- nullif(..., '') — T354 review HIGH finding: a fold that survives to nothing (no Latin/digit
	  -- content at all) must come back NULL, never ''. Empty string is a value like any other, and
	  -- would otherwise collide every no-usable-key row onto the same bogus group.
	  return nullif(btrim(v_folded), '');
	end;
	$$;

	-- title_variant: the folded content of a trailing "(...)" or "[...]" group, NULL when there is
	-- none (STORY-376 AC2). Only the LAST such group at the true end of the string matches — the
	-- pattern is anchored ^...$ over the whole input.
	create or replace function library.title_variant(p_title text)
	returns text
	language plpgsql
	immutable
	strict
	security invoker
	set search_path = library, pg_temp
	as $$
	declare
	  v_match text[];
	begin
	  v_match := regexp_match(p_title, '^.*?[\(\[]([^\(\)\[\]]+)[\)\]]\s*$');
	  if v_match is null then
	    return null;
	  end if;
	  return library.fold_key(v_match[1]);
	end;
	$$;

	-- title_key: fold_key of the title with that SAME trailing group (and its own leading
	-- whitespace) stripped first — so "Song", "Song (feat. X)", "Song [Live]", and "Song (2011
	-- Remaster)" all fold to the identical key while title_variant diverges (STORY-376 AC2).
	create or replace function library.title_key(p_title text)
	returns text
	language plpgsql
	immutable
	strict
	security invoker
	set search_path = library, pg_temp
	as $$
	declare
	  v_stripped text;
	begin
	  v_stripped := regexp_replace(p_title, '\s*[\(\[][^\(\)\[\]]+[\)\]]\s*$', '');
	  return library.fold_key(v_stripped);
	end;
	$$;

	alter table library.media
	  add column if not exists artist_key    text generated always as (library.fold_key(artist)) stored,
	  add column if not exists title_key     text generated always as (library.title_key(title))  stored,
	  add column if not exists title_variant text generated always as (library.title_variant(title)) stored;

	create index if not exists media_dup_keys on library.media (artist_key, title_key) where state = 'ready';

	-- find_near_duplicates (SPEC F153.5, amended at T354 review): playable rows are the FULL
	-- MediaRepository.PlayablePredicate (see this file's own header remarks), grouped on
	-- (artist_key, title_key, coalesced title_variant). Each candidate is anchored to its OWN
	-- group's SHORTEST duration via a window function (T354 review LOW-2, RULED) — never a
	-- self-join's pairwise distance, which lets tolerance chain transitively (200s/201.5s/203s at a
	-- 2s tolerance would otherwise all group, since 201.5s is within 2s of BOTH neighbors). Groups of
	-- one are dropped AFTER that anchor filter, via a `count(*) over (...) > 1` on the filtered set
	-- — never before it, or a lone survivor of a filtered-out pair would still count as a "group".
	-- group_key folds in title_variant (T354 review LOW-1, RULED) so two groups sharing an
	-- (artist_key, title_key) but differing in variant never share a group_key text; title_variant
	-- is also returned as its own column for a caller that wants it un-concatenated.
	create or replace function library.find_near_duplicates(tolerance_ms int)
	returns table (media_id bigint, group_key text, title_variant text)
	language plpgsql
	stable
	security invoker
	set search_path = library, pg_temp
	as $$
	begin
	  return query
	    with playable as (
	      select m.id, m.artist_key, m.title_key, coalesce(m.title_variant, '') as variant,
	             m.title_variant, m.duration_ms
	      from library.media m
	      left join library.media_rating r on r.media_id = m.id
	      where m.state = 'ready' and m.measurable and m.eligible and not coalesce(r.never_play, false)
	        and m.artist_key is not null and m.title_key is not null and m.duration_ms is not null
	    ),
	    anchored as (
	      select p.*,
	             min(p.duration_ms) over (partition by p.artist_key, p.title_key, p.variant) as group_min_duration_ms
	      from playable p
	    ),
	    qualifying as (
	      select * from anchored where duration_ms - group_min_duration_ms <= tolerance_ms
	    ),
	    grouped as (
	      select q.*, count(*) over (partition by q.artist_key, q.title_key, q.variant) as group_size
	      from qualifying q
	    )
	    select g.id as media_id,
	           g.artist_key || '|' || g.title_key || '|' || g.variant as group_key,
	           g.title_variant as title_variant
	    from grouped g
	    where g.group_size > 1;
	end;
	$$;

	-- recompute_nudge (SPEC F150.9, STORY-371, PLAN T365): the age-decayed, saturation-clamped thumb
	-- aggregate MediaThumbRepository calls after every Recorded/Flipped write and the gardener's own
	-- hourly RecomputeAllAsync pass applies to every media id that carries a thumb. VOLATILE (it
	-- writes) — the first side-effecting function in this schema, unlike fold_key/title_key/
	-- title_variant/find_near_duplicates above, all IMMUTABLE or STABLE. Each thumb's own weight is
	-- +1 (up) or -1 (down) times an exponential half-life decay on its OWN age
	-- (`extract(epoch from (now() - created_at)) / 86400.0 / p_half_life_days` — age in days divided
	-- by the half-life, so the weight halves every p_half_life_days), summed across every
	-- library.media_thumb row for this media id, then divided by p_saturation and clamped to [-1, 1]
	-- — the exact F150.9 formula. coalesce(..., 0): a media id with zero (post-sweep) thumb rows
	-- nudges to a flat 0, not NULL — a swept-clean row still satisfies the `nudge between -1 and 1`
	-- CHECK. The caller (MediaThumbRepository) is responsible for ensuring a library.media_rotation
	-- row exists before calling this function — a thumbed-but-never-aired track must carry a nudge
	-- too (F150.9), but that INSERT ... ON CONFLICT DO NOTHING belongs at the write site, not
	-- duplicated inside every recompute call (RecomputeAllAsync only ever targets media ids that
	-- RecordAsync has already ensured a row for). Calling this against a media id with no
	-- library.media_rotation row is a harmless no-op UPDATE (zero rows matched), never an error.
	create or replace function library.recompute_nudge(p_media_id bigint, p_half_life_days int, p_saturation int)
	returns void
	language plpgsql
	volatile
	security invoker
	set search_path = library, pg_temp
	as $$
	begin
	  update library.media_rotation
	  set nudge = greatest(-1, least(1, coalesce((
	          select sum(
	                   case direction when 'up' then 1 else -1 end
	                   * power(0.5, extract(epoch from (now() - created_at)) / 86400.0 / p_half_life_days)
	                 )
	          from library.media_thumb
	          where media_id = p_media_id
	        ), 0) / p_saturation)),
	      nudge_computed_at = now(),
	      updated_at = now()
	  where media_id = p_media_id;
	end;
	$$;
	SQL

# --- station schema: the envelope-is-object CHECK (SPEC F152.3's own read shape) -------------------
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	-- No "add constraint if not exists" in Postgres — guarded the same pg_catalog-existence way the
	-- enum types above are.
	do $$
	begin
	  if not exists (
	    select 1 from pg_constraint
	    where conname = 'show_envelope_is_object' and conrelid = 'station.show'::regclass
	  ) then
	    alter table station.show
	      add constraint show_envelope_is_object
	      check (envelope is null or jsonb_typeof(envelope) = 'object');
	  end if;
	end $$;
	SQL

# --- station.booth_log: the last-airing index (SPEC F152.5, STORY-373, PLAN T362 review MED-4) ----
# GetLastAiringAsync's own bounded read (GenWave.MediaLibrary.Station.BoothLogRepository) needs
# "this show's own track-started rows, newest first" fast — this partial index carries exactly that
# access path, idempotent (IF NOT EXISTS) like every other index in this file.
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	create index if not exists booth_log_show_track_started
	  on station.booth_log (show_id, occurred_at)
	  where kind = 'track-started';
	SQL

# --- the one-shot booth-log -> ledger seed (SPEC F149.3, STORY-367 AC5/AC6) ------------------------
# Runs as the bootstrap superuser this script is already connected as — no SET ROLE — the one place
# in this migration that reads across the station/library role boundary in a single statement (see
# this file's own header remarks for why that is safe and deliberate here).
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	insert into library.media_rotation (media_id, play_count, first_aired_at, last_aired_at)
	select bl.media_id, count(*), min(bl.occurred_at), max(bl.occurred_at)
	from station.booth_log bl
	join library.media m on m.id = bl.media_id
	where bl.kind = 'track-started' and bl.media_id is not null
	group by bl.media_id
	on conflict (media_id) do nothing;

	-- ONLY IF ABSENT: a real re-stamp would move the epoch every never-aired count is read beside
	-- (F149.3) — never an upsert. Same key/value/updated_at shape StationSettingsRepository.WriteAsync
	-- already writes; to_jsonb(now()) renders as an ISO-8601 string (Postgres's own json/jsonb output
	-- rule for timestamptz), the same shape a C# reader deserializes into a DateTimeOffset.
	insert into station.settings (key, value, updated_at)
	select 'Gardener:RotationSince', to_jsonb(now()), now()
	where not exists (select 1 from station.settings where key = 'Gardener:RotationSince');
	SQL

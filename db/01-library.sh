#!/bin/bash
# 01-library.sh — initialise the library service's database objects (PRD §6, §9).
# Runs once, on first boot, from /docker-entrypoint-initdb.d/ (Postgres executes *.sh and *.sql here
# in alphabetical order). A shell script — not plain SQL — because the dedicated role's password comes
# from the environment, which init *.sql cannot read.
#
# Data-separation discipline (PRD §9), the rules that make a later split a connection-string change,
# not a code change:
#   * Schema per service — `library` is owned by `library_svc`.
#   * Role per service   — `library_svc` logs in via its own connection string; search_path pinned to
#                          `library`, so it only ever sees its own schema.
#   * No cross-schema FKs/joins — an opaque media id is stored as a plain value across boundaries and
#                          resolved through IMediaCatalog; never a foreign key into another schema.
set -euo pipefail

: "${LIBRARY_DB_PASSWORD:?LIBRARY_DB_PASSWORD must be set for the library_svc role}"

# -v pw=... lets psql quote the literal safely (:'pw'); the heredoc is single-quoted so the shell
# does not touch the SQL body.
psql -v ON_ERROR_STOP=1 -v pw="$LIBRARY_DB_PASSWORD" \
     --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	-- Dedicated role; logs in through ConnectionStrings__Library.
	create role library_svc with login password :'pw';

	-- The schema the role owns; search_path pinned so library_svc only ever resolves `library`.
	create schema library authorization library_svc;
	alter role library_svc set search_path = library;

	-- Build the catalog AS the service role so it owns every object outright (real isolation,
	-- not a naming convention).
	set role library_svc;
	set search_path = library;

	-- Named library instances; v1 ships a single 'default' library (id=1 guaranteed by identity start).
	-- UNIQUE(name): library names are unique station-wide (STORY-046, Epic J).
	create table library.library (
	  id   bigint generated always as identity primary key,
	  name text   not null,
	  constraint library_name_key unique (name)
	);

	-- Default library row; identity column starts at 1 so this row always has id=1.
	insert into library.library (name) values ('default');

	create table library.media (
	  id              bigint generated always as identity primary key,
	  path            text not null unique,        -- engine-visible path under /media (the Locator)
	  format          text not null,               -- 'flac' | 'mp3'
	  size_bytes      bigint not null,             -- change detection
	  mtime           timestamptz not null,        -- change detection
	  state           text not null default 'discovered',  -- discovered | ready | failed | unavailable

	  -- library scope (added v2; all media belongs to exactly one library)
	  library_id      bigint not null default 1 references library.library(id) on delete restrict,

	  -- technical (filled at enrichment)
	  duration_ms     integer,
	  sample_rate     integer,
	  channels        smallint,
	  bitrate_kbps    integer,

	  -- tags (filled at enrichment; normalized across mp3/flac)
	  title           text,
	  artist          text,
	  album           text,
	  album_artist    text,
	  genre           text,
	  track_no        integer,
	  year            integer,

	  -- loudness (filled at enrichment) — what playout consumes
	  integrated_lufs double precision,
	  true_peak_dbtp  double precision,
	  measurable      boolean,

	  -- cue points (filled at enrichment; gitea-#161). NULL = no trim known, full-file playback.
	  -- cue_analyzed_at distinguishes "never attempted" (NULL) from "attempted, no boundaries found"
	  -- (non-NULL timestamp, cue_in_sec/cue_out_sec NULL) — the predicate for T027 backfill.
	  cue_in_sec      double precision,
	  cue_out_sec     double precision,
	  cue_analyzed_at timestamptz,

	  -- energy envelope (filled at enrichment; STORY-030). NULL = not yet analyzed.
	  -- energy_analyzed_at distinguishes "never attempted" (NULL) from "attempted, no energy found"
	  -- (non-NULL timestamp, intro_energy/outro_energy NULL) — the predicate for energy backfill.
	  intro_energy         double precision,
	  outro_energy         double precision,
	  energy_analyzed_at   timestamptz,

	  -- catalog write fields (STORY-039, Epic I).
	  -- eligible: false = operator has excluded the track from playout; default true = no behavior change.
	  -- tags_edited_at: NULL = never manually edited; set to now() on each operator tag patch.
	  eligible             boolean not null default true,
	  tags_edited_at       timestamptz,

	  -- BPM (filled at enrichment; Epic X / SPEC F46, gitea-#190). NULL = not yet analyzed.
	  -- bpm_analyzed_at distinguishes "never attempted" (NULL) from "attempted, indeterminate tempo"
	  -- (non-NULL timestamp, bpm NULL) — the predicate for F46.3 backfill.
	  bpm                  double precision,
	  bpm_analyzed_at      timestamptz,

	  -- year_lookup_at: timestamp of the last MusicBrainz year-lookup attempt (Epic X / SPEC F48,
	  -- gitea-#208). Stamped regardless of outcome (success, miss, or endpoint failure) -- an
	  -- "attempted at" telemetry marker only; it no longer gates re-claiming on its own (SPEC F76.2).
	  year_lookup_at         timestamptz,

	  -- year_lookup_missed_at: the actual MusicBrainz re-claim gate (SPEC F76.2, STORY-200). Stamped
	  -- ONLY for a genuine miss -- a completed round trip with no confident match above MinScore.
	  -- An endpoint failure/timeout leaves this NULL, so the row is retried next backfill tick;
	  -- only a real "no such recording" answer is ever excluded permanently. Same reusable idiom a
	  -- future enrichment slice (e.g. mood tagging) can copy verbatim: "<domain>_lookup_missed_at".
	  year_lookup_missed_at timestamptz,

	  discovered_at   timestamptz not null default now(),
	  enriched_at     timestamptz
	);

	-- track_energy: whole-track perceptual energy, derived from integrated_lufs (SPEC F47.1).
	-- A STORED generated column — zero new ffmpeg passes, zero write-path changes, zero sentinel:
	-- it computes for the whole catalog the instant this column exists and re-derives automatically
	-- whenever a loudness (re-)enrichment rewrites integrated_lufs (F47.2).
	--
	-- Semantics, mirrored 1:1 from FfmpegEnergyAnalyzer.MinLufs/MaxLufs/GateFloor
	-- (src/GenWave.Loudness/FfmpegEnergyAnalyzer.cs) — changing either side means changing both:
	--   integrated_lufs IS NULL      -> NULL (not yet measured)
	--   integrated_lufs <= -70.0     -> 0.0  (gated/silence, GateFloor)
	--   else                         -> clamp((integrated_lufs + 36.0) / 30.0, 0, 1)
	--                                    (MinLufs = -36.0 -> 0.0, MaxLufs = -6.0 -> 1.0)
	alter table library.media
	  add column track_energy double precision generated always as (
	    case
	      when integrated_lufs is null then null
	      when integrated_lufs <= -70.0 then 0.0
	      else least(1.0, greatest(0.0, (integrated_lufs + 36.0) / 30.0))
	    end
	  ) stored;

	-- energy: percentile rank of integrated_lufs within the READY library (SPEC F80.1, STORY-211) —
	-- NOT track_energy above (a fixed per-row linear scale) and NOT intro_energy/outro_energy
	-- (STORY-033 RMS levels). Unlike track_energy this cannot be a generated column: a percentile is
	-- relative to every OTHER ready row, which Postgres generated columns cannot reference. It is
	-- instead recomputed by a single set-based UPDATE
	-- (MediaRepository.RecomputeEnergyPercentilesAsync) piggybacked on the enrichment second tier
	-- (SPEC F80.2) — see MediaRepository.WriteEnrichmentAsync (nulls it on every LUFS write) and
	-- MediaRepository.HasStaleEnergyPercentilesAsync (the piggyback trigger). NULL = not yet ranked.
	alter table library.media
	  add column energy real;

	-- moods: up to MoodVocabulary.MaxMoodsPerTrack (3) tags drawn from the fixed vocabulary that
	-- lives in GenWave.Abstractions (SPEC F85.1, F85.2, STORY-216). Populated by a second-tier
	-- enrichment pass (T72, mood tagger); T58 ships storage + the write path only, so a fresh
	-- install leaves every row NULL until that pass runs (same "re-derives on the next pass"
	-- convention as energy above and track_energy before it). The write path itself
	-- (MediaRepository.WriteMoodsAsync) is the vocabulary gate: it rejects, as a whole, any write
	-- naming a term outside the vocabulary (F85.1) BEFORE this UPDATE ever runs — deliberately no
	-- per-term CHECK here, since the vocabulary is versioned in C#, not SQL, and a future term
	-- addition must never require a migration. The count cap IS spec-pinned and version-independent
	-- (F85.2, "≤3"), so it is enforced twice, defense-in-depth: once here, once at the write path.
	alter table library.media
	  add column moods text[]
	    check (moods is null or cardinality(moods) <= 3);

	-- mood_tagged_at / mood_tag_missed_at (SPEC F85.2, F85.4, STORY-216, T72): the F76 MusicBrainz
	-- etiquette pattern applied to moods -- mirrors year_lookup_at/year_lookup_missed_at exactly.
	-- mood_tagged_at is stamped unconditionally on every tagger attempt (success, miss, or endpoint
	-- failure) as an "attempted at" telemetry marker; it does not gate re-claiming on its own.
	-- mood_tag_missed_at is the actual re-claim gate (MediaRepository.ListMoodTagClaimsAsync): stamped
	-- ONLY for a genuine miss -- a completed round trip that produced zero in-vocabulary survivors
	-- (SPEC F85.4). A failed round trip leaves both moods and mood_tag_missed_at untouched, so the row
	-- stays eligible and is retried on the very next backfill tick -- never re-asking a question
	-- already answered, while never giving up on one that was never actually asked.
	alter table library.media
	  add column mood_tagged_at     timestamptz,
	  add column mood_tag_missed_at timestamptz;

	-- explicit / explicit_source (SPEC F95.2, STORY-251, T110): per-track explicit/advisory
	-- classification, orthogonal to the F95.5 never-play verdict (never-play still gates playout;
	-- this pair only classifies). explicit is a plain nullable boolean -- NULL = unknown/unclassified,
	-- never a sentinel false. explicit_source names WHO classified the row, constrained to the three
	-- known origins (F95.3): 'tag' (an advisory flag already carried in the file's own metadata,
	-- stamped first by Enricher's TagLib read, T112), 'llm' (the offline sweep asking a model about
	-- rows the tag pass left unknown, T113), 'operator' (an explicit admin override -- once stamped,
	-- later sweeps must never overwrite it, T115).
	--
	-- explicit_llm_missed_at (T113): the sweep's own re-claim gate, the same "<domain>_missed_at"
	-- idiom as mood_tag_missed_at/year_lookup_missed_at above -- stamped ONLY for a genuine "unknown"
	-- verdict (a completed round trip that couldn't tell), never for a failed round trip (endpoint
	-- unreachable), so a transient outage is retried next tick while a real "can't tell" answer is
	-- excluded permanently. MediaRepository.ListExplicitClassificationClaimsAsync gates on
	-- `explicit is null and explicit_llm_missed_at is null` -- the same `explicit IS NULL` predicate
	-- also happens to be the entire enforcement of "never re-ask an already-classified row" and
	-- "never overwrite an operator row", since every write path that sets explicit_source also sets
	-- explicit to a real value in the same statement (see MediaRepository.WriteEnrichmentAsync's own
	-- remarks for the canonical statement of this precedence).
	alter table library.media
	  add column explicit               boolean,
	  add column explicit_source        text
	    check (explicit_source is null or explicit_source in ('tag', 'llm', 'operator')),
	  add column explicit_llm_missed_at timestamptz;

	-- unavailable_since (gh-#113): when the row last transitioned available→unavailable, stamped
	-- by the scan's MarkUnavailableAsync write and cleared again on resurrection
	-- (MarkDiscoveredAsync / InsertDiscoveredAsync's on-conflict re-discovery, the gh-#112 path).
	-- NULL for any row that is not unavailable. The explicit operator purge's "unavailable longer
	-- than N days" age filter reads this column; a NULL stamp is never purgeable.
	alter table library.media
	  add column unavailable_since timestamptz;

	-- artwork_token (gh-#105, SPEC F88.2, STORY-222): random 128-bit value (32 lowercase hex
	-- chars), generated lazily by ArtworkTokenRepository.GetOrCreateTokenAsync on a row's first
	-- need. NULL for every row until then -- never backfilled, since a token is only ever minted
	-- for a track that actually airs. No FK: this column lives entirely within library.media.
	alter table library.media
	  add column artwork_token text;

	-- imaging_kind (gh-#149): the Station Imaging content kind of an AUTHORED segment, stamped by
	-- MediaRepository.InsertAuthoredAsync on every authored insert ('liner' by default). NULL for
	-- every scanned row -- the scan/enrichment paths never write it -- and for authored rows that
	-- predate the column (no backfill: nothing distinguishes an old authored row from a scanned one
	-- after the fact, and displays default a NULL kind to Liner anyway). Read by a real production
	-- caller as of PLAN T231/T232 (SPEC F110.2): MediaRepository.GetRandomReadyByImagingKindAsync
	-- selects on it, and the Orchestrator's top-of-hour StationId drain queries it directly --
	-- playout and the /internal/safe-track predicate still never read it (that remains F110.4:
	-- only StationId gained a selection role this cycle, no other kind).
	--
	-- 'ad' (gh-#380, SPEC F159.1, PLAN T389): db/42-ads-migration.sh's fresh-init mirror of its own
	-- CHECK widen -- an authored spot in the seeded 'ads' library stamps this kind so the F158.4
	-- rotation fence (PlayablePredicate's `imaging_kind is null`) makes it structurally invisible to
	-- music rotation the same way every other imaging kind already is.
	alter table library.media
	  add column imaging_kind text
	    check (imaging_kind is null or imaging_kind in ('liner', 'station_id', 'jingle', 'promo', 'ad'));

	-- show_id (SPEC F119.4, STORY-305/STORY-310, PLAN T238): scopes an authored imaging row to a
	-- station.show. Crosses the db/22 schema-role boundary (station_svc has no grant into library) the
	-- same way booth_log.media_id already crosses it in the other direction -- plain int, deliberately
	-- NO FK, resolved by the app at its own edge, never a cross-schema join. NULL = station-wide
	-- (every row today); set only on an authored imaging row scoped to a show once T246 wires the
	-- write path. NO CONSUMER YET (T238): the pool query gains the show filter at T250.
	alter table library.media
	  add column show_id int;

	-- Composite partial index: scope-filtered random-ready pick (replaces scalar media_ready).
	create index media_scope_ready on library.media (library_id, state) where state = 'ready';
	create index media_artist      on library.media (artist);                       -- ready for criteria queries
	create index media_genre       on library.media (genre);
	create index media_year        on library.media (year);                        -- decade/year filter spine (F49.5)

	-- media_artwork_token (gh-#105, SPEC F88.2): partial (see 23-artwork-token-migration.sh for
	-- why partial rather than a plain unique index) -- non-enumerability itself comes from the
	-- token's randomness, not this index.
	create unique index media_artwork_token on library.media (artwork_token) where artwork_token is not null;

	-- Rating (SPEC F33, STORY-109): a 1:1 extension table, deliberately not columns on library.media.
	-- A vote must never bump library.media's xmin, or an open F18.6 tag-edit's If-Match would 409 on
	-- an unrelated thumbs-up; keeping rating state in its own table also enforces the gitea-#188 "standalone
	-- from curation" guarantee by schema — bulk eligibility/PATCH/reassign/re-enrich all write
	-- library.media and are structurally incapable of touching a table they never reference.
	--
	-- postgres-dba Rule-2 deviation: media_id is the primary key (not a surrogate `id serial`).
	-- This is a 1:1 extension row — a surrogate id would permit duplicate rating rows per media,
	-- which the table exists specifically to prevent. PK = FK is deliberate, not an oversight.
	create table library.media_rating (
	  media_id   bigint primary key references library.media(id) on delete cascade,
	  score      int not null default 50 check (score between 0 and 100),
	  never_play boolean not null default false,
	  updated_at timestamptz not null default now()
	);

	-- The Library Gardener (gh-#529, SPEC F149.1-F149.3, F153.1, F153.5, F154.7; STORY-367,
	-- STORY-376; PLAN T354): fresh-init mirror of db/41-gardener-migration.sh's library-schema
	-- objects — see that script's own header for the full column-by-column rationale. This file only
	-- ever runs once (first boot), so every statement below is plain, unconditional DDL: no
	-- IF NOT EXISTS, no pg_type/pg_constraint guard. The one-shot booth-log -> ledger seed is
	-- deliberately NOT mirrored here (db/27's own "seed-and-delete" precedent: a fresh install has
	-- nothing to seed from — an empty station.booth_log yields zero ledger rows either way — and
	-- migrate.sh runs every numbered script, including db/41, against a freshly-initialised database
	-- too, so the seed still runs exactly once on a genuinely fresh box).
	create type library.thumb_direction as enum ('up', 'down');
	create type library.thumb_source    as enum ('spectator', 'operator');
	create type library.rot_kind  as enum ('dead_file', 'near_duplicate', 'stale_metadata', 'shelf_dust', 'unreachable');
	create type library.rot_state as enum ('open', 'dismissed', 'resolved');
	create type library.file_verb as enum ('retag', 'rename', 'move');

	-- postgres-dba Rule-2 deviation, the media_rating precedent immediately above: PK = FK on
	-- purpose (1:1 extension) — a write here must never bump library.media's own xmin (F18.6, F149.1).
	create table library.media_rotation (
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
	-- wires it). Idempotent per (media, airing, listener); a flip is an UPDATE of direction, never a
	-- second row.
	create table library.media_thumb (
	  id                bigserial primary key,
	  media_id          bigint not null references library.media(id) on delete cascade,
	  airing_started_at timestamptz not null,
	  listener_key      text   not null,        -- sha256(cookie token) or 'operator'; never logged
	  direction         library.thumb_direction not null,
	  source            library.thumb_source    not null,
	  created_at        timestamptz not null default now(),
	  unique (media_id, airing_started_at, listener_key)
	);
	-- T366 review MED-1 — db/41's own mirror, see that script's own remarks: the F150.5
	-- per-listener daily cap read filters (listener_key, created_at), not media_id first.
	create index media_thumb_listener_created_idx on library.media_thumb (listener_key, created_at desc);

	-- One row per (media, kind) forever (SPEC F153.1): a pass opens/re-opens/resolves it, the owner
	-- dismisses it, `dismissed` is never re-opened — state moves, the row does not multiply.
	create table library.rot_finding (
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
	create index rot_finding_state_kind on library.rot_finding (state, kind);
	create index rot_finding_group_key on library.rot_finding (group_key) where group_key is not null;
	-- T372 review LOW-2: RotFindingRepository.ListAsync's own access path (an optional kind
	-- filter, an optional state filter, sorted opened_at desc) — the table has no retention
	-- (F153.1: one row per media x kind, forever), so this covers that read as it grows rather
	-- than a sequential scan over an ever-larger table.
	create index rot_finding_kind_state_opened_at on library.rot_finding (kind, state, opened_at desc);

	-- The audit of every destructive file action (SPEC F154.7) — verb/from/to/plan token/outcome/
	-- detail, never the booth log. No verb here ever deletes (F154.1: retag, rename, move only).
	create table library.file_action (
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
	-- NULL argument returns NULL without the body ever running (STORY-376 AC1's "the fold").
	create function library.fold_key(p_text text)
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
	  v_folded := translate(
	    lower(btrim(p_text)),
	    'àáâãäåçèéêëìíîïñòóôõöøùúûüýÿąćčďęěğłńňőřśšťůűźżžĺľīū',
	    'aaaaaaceeeeiiiinoooooouuuuyyaccdeeglnnorsstuuzzzlliu'
	  );
	  v_folded := regexp_replace(v_folded, '[^a-z0-9]+', ' ', 'g');
	  -- nullif(..., ''): a fold with no Latin/digit content at all (Cyrillic, CJK, Greek, blank)
	  -- must come back NULL, not '' — see db/41's own remarks (T354 review HIGH finding).
	  return nullif(btrim(v_folded), '');
	end;
	$$;

	-- title_variant: the folded content of a trailing "(...)" or "[...]" group, NULL when there is
	-- none (STORY-376 AC2). Only the LAST such group at the true end of the string matches — the
	-- pattern is anchored ^...$ over the whole input.
	create function library.title_variant(p_title text)
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
	create function library.title_key(p_title text)
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
	  add column artist_key    text generated always as (library.fold_key(artist)) stored,
	  add column title_key     text generated always as (library.title_key(title))  stored,
	  add column title_variant text generated always as (library.title_variant(title)) stored;

	create index media_dup_keys on library.media (artist_key, title_key) where state = 'ready';

	-- find_near_duplicates (SPEC F153.5, amended at T354 review, F158.4 fence closed at T406):
	-- playable rows are the FULL MediaRepository.PlayablePredicate as of T406, LEFT JOIN
	-- library.media_rating included (T354 review MED-1 finding — see db/41's own header remarks for
	-- why the never_play half is not optional). CLOSED (PLAN T395 review, carried forward as PLAN
	-- T406, landed as db/44): PlayablePredicate gained "and imaging_kind is null" at T395 (SPEC
	-- F158.4, the rotation fence) — a SQL function cannot reference the C# constant, so this
	-- fresh-init mirror carries its own copy of the fence term, kept byte-identical to db/44's
	-- upgrade-path version (see that script's own header for the full T406 rationale) — an authored
	-- imaging row (a liner, a station id, an ad spot) can no longer surface in a near-duplicate
	-- finding. STABLE, not IMMUTABLE: its result depends on library.media's contents, not just its
	-- own argument. Anchored to each group's SHORTEST duration via a window function, not a
	-- self-join's pairwise distance, so tolerance never chains transitively (T354 review LOW-2,
	-- RULED — see db/41's own remarks). group_key folds in title_variant (T354 review LOW-1, RULED)
	-- so two groups sharing an (artist_key, title_key) but differing in variant never share a
	-- group_key text; title_variant is also returned as its own column. Groups of one are dropped
	-- AFTER the tolerance filter.
	create function library.find_near_duplicates(tolerance_ms int)
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
	        and m.imaging_kind is null
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

	-- recompute_nudge (SPEC F150.9, STORY-371, PLAN T365): db/41's own fresh-init mirror — see that
	-- script's own remarks for the full rationale. VOLATILE (it writes); the age-decayed,
	-- saturation-clamped thumb aggregate MediaThumbRepository calls after every write and the
	-- gardener's hourly RecomputeAllAsync pass applies to every thumbed media id. A media id with no
	-- library.media_rotation row is a harmless no-op (zero rows matched) — the caller ensures the row
	-- exists first.
	create function library.recompute_nudge(p_media_id bigint, p_half_life_days int, p_saturation int)
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

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

	-- Composite partial index: scope-filtered random-ready pick (replaces scalar media_ready).
	create index media_scope_ready on library.media (library_id, state) where state = 'ready';
	create index media_artist      on library.media (artist);                       -- ready for criteria queries
	create index media_genre       on library.media (genre);
	create index media_year        on library.media (year);                        -- decade/year filter spine (F49.5)

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
SQL

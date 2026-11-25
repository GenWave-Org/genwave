#!/bin/bash
# 55-repeat-catches-rules-migration.sh — idempotent in-place upgrade for existing DBs.
# F220 (STORY-570; PLAN T800): station.v_repeat_catches_7d also counts the repeat catches rules mode records in
# station.talk_check, because rules mode writes no repeat-caught booth rows. Mirrored in db/06 (fresh installs);
# this script is the upgrade path. A view only: no tables, no data. Needs db/54 (talk_check) and db/51 (the view).
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	-- NOTE: talk_check has no persona_id index; the view's per-persona count scans it, which is fine at 1-90 days' retention.
	-- begin f220-repeat-catches
	-- F220 (STORY-570; PLAN T800): v_repeat_catches_7d learns rules mode, which writes no repeat-caught booth rows.
	-- catches = repeat-caught booth rows (legacy) plus talk_check rows of mode 'rules' whose repeat-phrases verdict was
	-- switched on and flagged (a judge that could not run is not a catch: 'Could not check' is TalkRuleReasons.CouldNotCheck;
	-- a shaper or recorder that threw is stored apart from the flag, so it needs no exclusion); trims = booth rows only (rules
	-- never trims); fallbacks unchanged. NOTE: talk_check rows are swept after TalkRules:CheckRetentionDays, so a value under 7
	-- makes the 7-day catches undercount. Same columns as db/51's view, so CREATE OR REPLACE applies. Mirrored verbatim between
	-- db/06 and db/55 (a spec pins the text equal); in db/06 it sits after talk_check, so a fresh install ends here.
	CREATE OR REPLACE VIEW station.v_repeat_catches_7d AS
	SELECT p.id AS persona_id, p.name AS persona_name,
	       ((SELECT count(*) FROM station.booth_log b
	          WHERE b.persona_id = p.id AND b.kind = 'repeat-caught'
	            AND b.occurred_at >= now() - interval '7 days')
	        + (SELECT count(*) FROM station.talk_check c
	            WHERE c.persona_id = p.id AND c.mode = 'rules'
	              AND c.checked_at >= now() - interval '7 days'
	              AND c.verdicts -> 'repeat-phrases' ->> 'on' = 'true'
	              AND jsonb_typeof(c.verdicts -> 'repeat-phrases' -> 'flag') = 'object'
	              AND c.verdicts -> 'repeat-phrases' -> 'flag' ->> 'reason' <> 'Could not check'))::integer AS catches,
	       (SELECT count(*) FROM station.booth_log b
	         WHERE b.persona_id = p.id AND b.kind = 'repeat-caught' AND b.summary LIKE '% — trimmed'
	           AND b.occurred_at >= now() - interval '7 days')::integer AS trims,
	       (SELECT count(*) FROM station.utterance u
	         WHERE u.persona_id = p.id AND u.fallback
	           AND u.written_at >= now() - interval '7 days')::integer AS fallbacks
	FROM station.persona p;
	-- end f220-repeat-catches
	SQL

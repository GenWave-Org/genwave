#!/bin/bash
# 51-repeat-views-migration.sh — idempotent in-place upgrade for existing DBs.
# F218.29 (STORY-542, STORY-545 AC8; PLAN T708): the two read-only views behind the Status page's Repeats
# panel. station.v_catchphrase_per_shift counts each maxPerShift quirk's key phrase per persona per shift;
# station.v_repeat_catches_7d counts repeat catches, trims and fallbacks per persona over 7 days.
# Mirrored in db/06 (fresh installs); this script is the upgrade path. Views only: no tables, no data.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
	set role station_svc;
	set search_path = station;

	-- begin f218-repeat-views
	-- F218.29 (STORY-542, PLAN T708): the numbers behind the Status page's Repeats panel. Read-only views, so a
	-- rerun is CREATE OR REPLACE. Mirrored verbatim between db/06 and db/51 (a spec pins the text equal).
	--
	-- v_catchphrase_per_shift: per persona, per shift, how often each maxPerShift quirk's key phrase was said.
	-- The key phrase is GenWave.Memory.RepetitionText.KeyPhrase in SQL: the rule's longest quoted span (double
	-- quotes straight or curly, or a single-quoted span that closes on the first quote not followed by a letter
	-- or digit), else its last four words with trailing . ! ? , ; : trimmed. Matching is case-insensitive.
	-- Known gaps vs C# RepetitionText.KeyPhrase: (1) a line break inside a single-quoted span reads differently
	-- (Postgres '.' matches newlines); (2) C# scans both quote styles in one left-to-right pass, this scans them
	-- separately, so a single-quoted span may start inside a double-quoted one ("a 'b" c d e' f); (3) equal-length
	-- ties break by first text position here, by match order in C#.
	CREATE OR REPLACE VIEW station.v_catchphrase_per_shift AS
	WITH rule AS (
	  SELECT p.id AS persona_id, p.name AS persona_name, q.rule ->> 'text' AS rule_text,
	         (q.rule ->> 'maxPerShift')::integer AS max_per_shift
	  FROM station.persona p
	  CROSS JOIN LATERAL jsonb_array_elements(
	    CASE WHEN jsonb_typeof(p.definition -> 'quirkRules') = 'array' THEN p.definition -> 'quirkRules' ELSE '[]'::jsonb END
	  ) AS q(rule)
	  WHERE jsonb_typeof(q.rule -> 'maxPerShift') = 'number'
	    AND jsonb_typeof(q.rule -> 'text') = 'string'
	),
	span AS (
	  SELECT rl.persona_id, rl.rule_text, btrim(m.found, E' \t\r\n') AS phrase
	  FROM rule rl
	  CROSS JOIN LATERAL (
	    SELECT g[1] AS found FROM regexp_matches(rl.rule_text, '["“]([^"“”]+)["”]', 'g') AS g
	    UNION ALL
	    SELECT g[1] FROM regexp_matches(rl.rule_text, '(?<![[:alnum:]])[''‘](.+?)[''’](?![[:alnum:]])', 'g') AS g
	  ) m
	  WHERE btrim(m.found, E' \t\r\n') <> ''
	),
	longest AS (
	  SELECT DISTINCT ON (s.persona_id, s.rule_text) s.persona_id, s.rule_text, s.phrase
	  FROM span s
	  ORDER BY s.persona_id, s.rule_text, length(s.phrase) DESC, strpos(s.rule_text, s.phrase)
	),
	key_phrase AS (
	  SELECT rl.persona_id, rl.persona_name, rl.max_per_shift,
	         COALESCE(
	           l.phrase,
	           rtrim(btrim(substring(regexp_replace(btrim(rl.rule_text, ' '), ' +', ' ', 'g') FROM '(?:[^ ]+ ){0,3}[^ ]+$'), E' \t\r\n'), '.!?,;:'),
	           ''
	         ) AS phrase
	  FROM rule rl
	  LEFT JOIN longest l ON l.persona_id = rl.persona_id AND l.rule_text = rl.rule_text
	),
	quirk AS (
	  SELECT persona_id, persona_name, phrase, min(max_per_shift) AS max_per_shift
	  FROM key_phrase
	  WHERE phrase <> ''
	  GROUP BY persona_id, persona_name, phrase
	),
	said AS (
	  SELECT k.persona_id, k.persona_name, u.shift_key, k.phrase, k.max_per_shift, u.written_at,
	         (length(lower(u.text)) - length(replace(lower(u.text), lower(k.phrase), ''))) / length(k.phrase) AS hits
	  FROM quirk k
	  JOIN station.utterance u ON u.persona_id = k.persona_id
	)
	SELECT persona_id, persona_name, shift_key, phrase, max_per_shift,
	       sum(hits)::integer AS occurrences, max(written_at) AS last_written_at
	FROM said
	WHERE hits > 0
	GROUP BY persona_id, persona_name, shift_key, phrase, max_per_shift;

	-- v_repeat_catches_7d: per persona over the last 7 days. catches = every repeat-caught booth row (SPEC
	-- F218.13); trims = those whose summary ends "— trimmed" (a sentence was cut, the DJ kept; F218.38);
	-- fallbacks = utterances where a template line aired instead. Every persona has a row, zeros included.
	CREATE OR REPLACE VIEW station.v_repeat_catches_7d AS
	SELECT p.id AS persona_id, p.name AS persona_name,
	       (SELECT count(*) FROM station.booth_log b
	         WHERE b.persona_id = p.id AND b.kind = 'repeat-caught'
	           AND b.occurred_at >= now() - interval '7 days')::integer AS catches,
	       (SELECT count(*) FROM station.booth_log b
	         WHERE b.persona_id = p.id AND b.kind = 'repeat-caught' AND b.summary LIKE '% — trimmed'
	           AND b.occurred_at >= now() - interval '7 days')::integer AS trims,
	       (SELECT count(*) FROM station.utterance u
	         WHERE u.persona_id = p.id AND u.fallback
	           AND u.written_at >= now() - interval '7 days')::integer AS fallbacks
	FROM station.persona p;
	-- end f218-repeat-views
	SQL

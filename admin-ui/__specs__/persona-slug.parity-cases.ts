/**
 * Shared `personaSlug` ⇄ `LegacyPersonaCardMapper.Slugify` parity cases — the ONE authored copy.
 *
 * Review finding on PLAN T68: `persona-slug.ts` hand-mirrors the backend's `Slugify` (SPEC F71.1)
 * with no parity guard — drift here silently 404s `GET /api/personas/{slug}/export` or lands
 * `POST /api/personas/{slug}/import` on the WRONG persona row (upsert-by-slug). This file is the
 * single source of truth pinning both sides:
 *   - `persona-slug.spec.ts` imports this array and asserts the REAL `personaSlug` against every row.
 *   - The C# theory `FeaturePersonaSlugParity` (tests/GenWave.MediaLibrary.Tests/Specs/
 *     Story192_PersonaSlugParity.cs — the Story151/FeatureSettingsHelpKeysParity repo-content-fact
 *     idiom; no TS toolchain runs inside xUnit) string-parses THIS array out of this file's text and
 *     asserts the REAL `LegacyPersonaCardMapper.Slugify` against the same rows. It lives in
 *     GenWave.MediaLibrary.Tests, not Host.Tests, because `Slugify` is `internal` and only that test
 *     project carries the `InternalsVisibleTo` grant.
 * A change to EITHER implementation that stops matching a row fails a spec on THAT toolchain — never
 * a silent one-sided drift. Add new rows here only; never hand-duplicate a case into just one side.
 *
 * Each row is `[name, expectedSlug]`. Keep every row a single-line, two-element string-literal array
 * (`["...", "..."]`) — the C# side's regex only recognizes that exact shape.
 */
export const PERSONA_SLUG_PARITY_CASES: ReadonlyArray<readonly [string, string]> = [
  ["DJ Nova", "dj-nova"],
  ["MiXeD CaSe NAME", "mixed-case-name"],
  ["Multiple   Spaces!!  And,,, Punctuation", "multiple-spaces-and-punctuation"],
  ["---Leading And Trailing---", "leading-and-trailing"],
  ["!!!???", "persona"],
  ["   ", "persona"],
  ["🎧🔥💯", "persona"],
] as const;

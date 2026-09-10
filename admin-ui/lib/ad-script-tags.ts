// The client-side script-tag parser shared by `AdSpotEditor.tsx` and `steps/ScriptStep.tsx` (PLAN
// T404, T448) — both render a per-tag voice cast from whatever `TAG: line` prefixes the operator's
// (or the station's own written) script carries, and both need the SAME parse so the two surfaces
// never silently disagree about which tags exist.

/** The server's own tag shape (`GenWave.Ads.AdScriptParser.TagPattern`) — starts with a letter,
 * then any run of uppercase letters/digits. */
export const AD_SCRIPT_TAG_PATTERN = /^[A-Z][A-Z0-9]*$/;

/**
 * Parses `TAG: line` script lines client-side into the distinct tags, in first-appearance order —
 * DISPLAY SUGAR ONLY (PLAN T404's own ruling): this drives which voice pickers render, nothing
 * more. `AdScriptValidator` (`AdScriptParser.ParseLine`) on the server is the real parser and is
 * what actually accepts or refuses the script; a malformed line here simply fails to produce a
 * picker for it — the save-time 400 is what tells the operator why.
 *
 * Aligned to the server's EXACT algorithm (`ParseLine`: split at the FIRST `:`, trim both sides,
 * then match the tag against {@link AD_SCRIPT_TAG_PATTERN}) rather than a shape that requires the
 * colon to sit flush against the tag with no intervening space — a line like `"ANNOUNCER : Hello"`
 * (a space before the colon — the server tolerates it, since it trims after splitting) must still
 * produce a picker, or the operator's only signal that a tag existed would be a silent fallback to
 * the station voice with no visible reason why. Matching the server's own split-then-trim shape here
 * means every tag the server WILL accept gets offered a picker, and every tag the server WOULD
 * refuse (fails {@link AD_SCRIPT_TAG_PATTERN}, e.g. leading digit) gets none here either — the two
 * can no longer silently disagree.
 */
export function parseScriptTags(script: string): string[] {
  const tags: string[] = [];
  for (const rawLine of script.split("\n")) {
    const trimmedLine = rawLine.trim();
    const colonIndex = trimmedLine.indexOf(":");
    if (colonIndex <= 0) continue;

    const tag = trimmedLine.slice(0, colonIndex).trim();
    if (AD_SCRIPT_TAG_PATTERN.test(tag) && !tags.includes(tag)) tags.push(tag);
  }
  return tags;
}

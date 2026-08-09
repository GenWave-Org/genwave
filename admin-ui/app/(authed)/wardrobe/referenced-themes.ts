/**
 * Parses the theme slugs `FontPackController.Uninstall`'s 409 embeds in its `detail` prose (gh-#428)
 * — `DELETE /api/fonts/{slug}` carries no structured `themeSlugs` field the way
 * `LibrariesController`'s `dependentMediaCount` extension does (`ReferencedProblem`,
 * `FontPackController.cs`, sets only `Status`/`Title`/`Detail`); the names live nowhere but inside
 * one sentence: `"<slug>" is still referenced by theme(s) "a", "b" and cannot be uninstalled — …`.
 * This is therefore a same-meaning coupling to that exact sentence shape, not a real wire contract —
 * narrow and documented on both ends so a future prose change on the Host side shows up as a spec
 * failure here rather than silently degrading to the generic-message fallback below.
 */
const REFERENCED_BY_THEMES = /referenced by theme\(s\) (.+?) and cannot be uninstalled/;
const QUOTED_NAME = /"([^"]+)"/g;

/**
 * Extracts every referencing theme slug from a 409 `detail` string, in the order the server named
 * them. Returns `[]` on any shape that doesn't match — including `FontPackDeleteResult.Referenced`'s
 * own documented rare-empty-race sentence (`"… referenced by a theme and cannot be uninstalled."`,
 * no theme(s) clause at all) — never throws on unexpected prose.
 */
export function parseReferencedThemeSlugs(detail: string): string[] {
  const clause = REFERENCED_BY_THEMES.exec(detail)?.[1];
  if (clause === undefined) return [];

  return [...clause.matchAll(QUOTED_NAME)]
    .map((match) => match[1])
    .filter((name): name is string => name !== undefined && name !== "");
}

/**
 * The Wardrobe uninstall button's 409 copy (gh-#428): "In use by: <themes>" when the sentence names
 * at least one theme, falling back to the raw `detail` text (still a real explanation, just not a
 * name list — the rare-empty-race case above, or any other 409 shape) rather than a generic error.
 */
export function formatReferencedThemesMessage(detail: string): string {
  const slugs = parseReferencedThemeSlugs(detail);
  return slugs.length > 0 ? `In use by: ${slugs.join(", ")}` : detail;
}

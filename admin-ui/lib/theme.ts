/**
 * Mode-selection contract shared between the server render (root layout reads
 * the cookie and stamps `data-theme`) and the client toggle (writes the cookie
 * and flips the attribute live). SPEC F28.4.
 *
 * Two independent axes (PLAN T164 ruling, 2026-08-03): `genwave-theme` names
 * the station's THEME (palette) slug — server-resolved by
 * GenWave.Host.Theming.ThemeCatalog.Resolve, not this file's concern. This
 * file owns the OTHER axis: `genwave-mode`, the light/dark MODE within
 * whichever theme is active. The two cookies are deliberately independent —
 * a visitor who picked dark keeps dark when the station's theme changes
 * under them.
 */

/** Name of the cookie that stores the visitor's explicit mode override. */
export const MODE_COOKIE_NAME = "genwave-mode";

/** One year, in seconds — long enough that an explicit choice effectively never expires. */
export const MODE_COOKIE_MAX_AGE_SECONDS = 60 * 60 * 24 * 365;

export type Mode = "light" | "dark";

/**
 * Narrows an arbitrary cookie value to a `Mode`. Anything else — missing,
 * garbage, a value from some future mode — is treated as "no explicit
 * choice" so the caller falls back to `prefers-color-scheme` rather than
 * rendering a broken mode.
 */
export function parseMode(raw: string | undefined): Mode | null {
  return raw === "light" || raw === "dark" ? raw : null;
}

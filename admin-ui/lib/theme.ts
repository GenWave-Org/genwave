/**
 * Theme-selection contract shared between the server render (root layout reads
 * the cookie and stamps `data-theme`) and the client toggle (writes the cookie
 * and flips the attribute live). SPEC F28.4.
 */

/** Name of the cookie that stores the operator's explicit theme override. */
export const THEME_COOKIE_NAME = "genwave-theme";

/** One year, in seconds — long enough that an explicit choice effectively never expires. */
export const THEME_COOKIE_MAX_AGE_SECONDS = 60 * 60 * 24 * 365;

export type Theme = "light" | "dark";

/**
 * Narrows an arbitrary cookie value to a `Theme`. Anything else — missing,
 * garbage, a value from some future theme — is treated as "no explicit
 * choice" so the caller falls back to `prefers-color-scheme` rather than
 * rendering a broken theme.
 */
export function parseTheme(raw: string | undefined): Theme | null {
  return raw === "light" || raw === "dark" ? raw : null;
}

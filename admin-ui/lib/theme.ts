/**
 * Cookie contract shared between the server render (root layout reads the
 * mode cookie and stamps `data-theme`; `GET /api/theme.css` reads the theme
 * cookie) and the client switcher (writes both cookies and flips the mode
 * attribute live). SPEC F28.4, F102.12/F102.13.
 *
 * Two independent axes (PLAN T164 ruling, 2026-08-03), one file owning both
 * cookies' names/parsing so they can never drift from one another or from
 * their C# counterparts:
 *   - `genwave-theme` (mirrors `GenWave.Host.Theming.ThemeCatalog.CookieName`)
 *     names the station's THEME (palette) slug. This file only WRITES/READS
 *     it client-side (PLAN T167's ThemeSwitcher) and validates it against a
 *     dynamic choice list ({@link resolveActiveThemeSlug}) — final resolution
 *     for `/api/theme.css` itself stays server-side, in `ThemeCatalog.Resolve`.
 *   - `genwave-mode` names the light/dark MODE within whichever theme is
 *     active, narrowed by the static {@link parseMode}.
 * The two cookies are deliberately independent — a visitor who picked dark
 * keeps dark when the station's theme changes under them.
 */

/** Name of the cookie that stores the visitor's explicit mode override. */
export const MODE_COOKIE_NAME = "genwave-mode";

/** Name of the cookie that stores the visitor's explicit theme (palette) override — same
 *  literal as `GenWave.Host.Theming.ThemeCatalog.CookieName`, on purpose (PLAN T164). */
export const THEME_COOKIE_NAME = "genwave-theme";

/** One year, in seconds — long enough that an explicit choice effectively never expires.
 *  Shared by both cookies: each axis still gets its OWN named constant below so a future
 *  change to one axis's expiry can't silently retarget the other by editing a shared literal. */
const ONE_YEAR_SECONDS = 60 * 60 * 24 * 365;

export const MODE_COOKIE_MAX_AGE_SECONDS = ONE_YEAR_SECONDS;
export const THEME_COOKIE_MAX_AGE_SECONDS = ONE_YEAR_SECONDS;

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

/**
 * One valid theme choice, as `GET /api/settings` represents the `Station:Theme` row's closed
 * set (`SettingDto.Choices` → `SettingChoice`, T175). Redeclared here rather than imported from
 * the Settings-page module tree (`app/(authed)/settings/settings-types.ts`) — that module is
 * T175's Settings-page control, a different feature (see ThemeSwitcher's own remarks); this
 * cookie-contract file has no dependency on it, only on the same wire shape.
 */
export interface ThemeChoice {
  readonly value: string;
  readonly label: string;
  readonly isDefault?: boolean;
}

/**
 * Resolves which shipped theme slug is active for a visitor — the client-side mirror of
 * `GenWave.Host.Theming.ThemeCatalog.Resolve`'s precedence (SPEC F102.5/F102.6, PLAN T167):
 * an explicit `genwave-theme` cookie wins if it still names a shipped theme; otherwise the
 * station's current `Station:Theme` value; otherwise whichever choice the server flagged
 * `isDefault`. A cookie or station slug naming a theme no longer on the shelf is treated
 * exactly like an absent one — never thrown, never rendered as a selection nothing backs.
 *
 * Falls back to `choices[0]` (load order, `ThemeCatalog.All`) only if NONE of the three rungs
 * above resolve — every shipped catalog names one `isDefault` choice, so this last rung is a
 * belt-and-suspenders guard against a malformed response, not a normal path; it exists so a
 * `<select>` bound to this value never lands on an empty string no `<option>` represents.
 */
export function resolveActiveThemeSlug(
  cookieSlug: string | undefined,
  stationThemeSlug: string,
  choices: readonly ThemeChoice[]
): string {
  if (cookieSlug !== undefined && choices.some((choice) => choice.value === cookieSlug)) {
    return cookieSlug;
  }
  if (stationThemeSlug !== "" && choices.some((choice) => choice.value === stationThemeSlug)) {
    return stationThemeSlug;
  }
  return choices.find((choice) => choice.isDefault === true)?.value ?? choices[0]?.value ?? "";
}

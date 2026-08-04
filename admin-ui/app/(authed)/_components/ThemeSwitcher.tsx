"use client";

import { useEffect, useState, type ChangeEvent, type ReactNode } from "react";
import { Tooltip } from "@/components/ui/tooltip";
import {
  MODE_COOKIE_MAX_AGE_SECONDS,
  MODE_COOKIE_NAME,
  THEME_COOKIE_MAX_AGE_SECONDS,
  THEME_COOKIE_NAME,
  resolveActiveThemeSlug,
  type Mode,
  type ThemeChoice,
} from "@/lib/theme";
import { MoonIcon, SunIcon } from "./icons";

export interface ThemeSwitcherProps {
  /**
   * Every shipped theme, straight off `GET /api/settings`'s `Station:Theme` row
   * (`SettingDto.choices`) — the /design ruling (SPEC F102.12/F102.13, 2026-08-04): this
   * switcher sources its list from the settings response the authed layout already fetches for
   * the Persona Catalog gate, never a second/admin-only endpoint. Symmetric with how the
   * spectator surface's switcher reads its own list (T166).
   */
  choices: readonly ThemeChoice[];
  /**
   * The station's current `Station:Theme` value (`SettingDto.value` for that same row) — the
   * middle rung of {@link resolveActiveThemeSlug}'s cascade, below an explicit cookie and above
   * whichever choice the server flagged `isDefault`.
   */
  stationThemeSlug: string;
}

function readThemeCookie(): string | undefined {
  const match = document.cookie.match(new RegExp(`(?:^|; )${THEME_COOKIE_NAME}=([^;]*)`));
  const raw = match?.[1];
  return raw === undefined ? undefined : decodeURIComponent(raw);
}

/** Reads the mode currently in effect: the explicit data-theme attribute if set (root layout
 *  stamps it server-side from the genwave-mode cookie, SPEC F28.4), otherwise the resolved
 *  prefers-color-scheme default — unchanged from the retired ThemeToggle's own logic. */
function resolveCurrentMode(): Mode {
  const attr = document.documentElement.getAttribute("data-theme");
  if (attr === "light" || attr === "dark") {
    return attr;
  }
  return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

/**
 * Forces the page's OWN `/api/theme.css` `<link>` to refetch once a new theme cookie is written
 * — the restyle mechanism PLAN T167 asks this task to WRITE (live-verified later at T170).
 *
 * Chosen over `router.refresh()`: `AdminThemeEndpoints` resolves fresh from the request's Cookie
 * header on every hit (`Cache-Control: no-cache`), so all that's needed is a genuinely NEW
 * request to the SAME href. But the `<link rel="stylesheet" href="/api/theme.css"
 * precedence="theme">` root layout renders is a React 19 stylesheet Resource, deduplicated by
 * href for the life of the document — re-rendering the Server Component that owns it (which is
 * what `router.refresh()` does) reconciles to the SAME href and does not, by itself, cause the
 * browser to re-fetch an already-mounted link. Cloning-and-replacing the node forces a genuinely
 * new request (never a cache-busting query string — still the SAME same-origin stylesheet
 * reference the root layout already ships); reassigning `href` in place is not reliably honoured
 * by every engine, which is why this mirrors `wwwroot/spectator/switcher.js`'s own
 * `reloadThemeStylesheet` (T166) rather than inventing a second mechanism for the same problem.
 *
 * Located by its stable `rel`/`href` rather than an id: the root layout that renders it
 * (`app/layout.tsx`) is outside this task's owned files, so this deliberately needs no change
 * there to keep working.
 */
function reloadThemeStylesheet(): void {
  const current = document.head.querySelector<HTMLLinkElement>(
    'link[rel="stylesheet"][href="/api/theme.css"]'
  );
  if (!current) return;
  current.replaceWith(current.cloneNode(false));
}

const SELECT_CLASSES =
  "h-9 rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent";

const MODE_BUTTON_CLASSES =
  "flex h-10 w-10 items-center justify-center rounded-[6px] text-mute transition-colors duration-[120ms] ease-out hover:bg-surface-2 hover:text-ink focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent";

/**
 * Replaces the binary ThemeToggle (T167, SPEC F102.12/F102.13, STORY-267) with two independent
 * controls: a theme PICKER (one option per shipped theme, sourced from the settings response the
 * authed layout already fetches — never a second endpoint) and the light/dark MODE control the
 * console has always had. The two axes stay independent, mirroring `lib/theme.ts`'s own
 * two-cookie contract and T166's spectator switcher:
 *
 *   - **Mode:** an explicit choice (the `data-theme` attribute root layout stamped from the
 *     `genwave-mode` cookie, or a click here) outranks `prefers-color-scheme`; absent either, OS
 *     preference picks the mode WITHIN whichever theme is active (AC4/AC5) — never a different
 *     theme. Reuses the retired ThemeToggle's `resolveCurrentMode` logic verbatim.
 *   - **Theme:** an explicit `genwave-theme` cookie outranks the station's `Station:Theme`
 *     value, which outranks the choice the server flagged `isDefault` — the exact cascade
 *     `ThemeCatalog.Resolve` runs server-side (`resolveActiveThemeSlug` mirrors it client-side).
 *
 * Both the initial theme selection and the initial mode start from what the SERVER already knows
 * (the station value / no explicit mode) so the first client render matches the server's markup
 * with no hydration mismatch; an effect then corrects each from `document.cookie`/`matchMedia`
 * once mounted — the same "hydrate after mount" shape the retired ThemeToggle and
 * `usePersistedState` already use.
 */
export function ThemeSwitcher({ choices, stationThemeSlug }: ThemeSwitcherProps): ReactNode {
  const [mode, setMode] = useState<Mode | null>(null);
  const [themeSlug, setThemeSlug] = useState<string>(() =>
    resolveActiveThemeSlug(undefined, stationThemeSlug, choices)
  );

  useEffect(() => {
    setMode(resolveCurrentMode());
    setThemeSlug(resolveActiveThemeSlug(readThemeCookie(), stationThemeSlug, choices));
    // Runs once at mount, same as ThemeToggle's own resolution effect — `choices`/`stationThemeSlug`
    // are server-fetched props stable for the lifetime of a single page load.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function handleModeToggle(): void {
    const next: Mode = mode === "dark" ? "light" : "dark";
    document.documentElement.setAttribute("data-theme", next);
    document.cookie = `${MODE_COOKIE_NAME}=${next}; path=/; max-age=${MODE_COOKIE_MAX_AGE_SECONDS}; SameSite=Lax`;
    setMode(next);
  }

  function handleThemeChange(event: ChangeEvent<HTMLSelectElement>): void {
    const next = event.currentTarget.value;
    document.cookie = `${THEME_COOKIE_NAME}=${next}; path=/; max-age=${THEME_COOKIE_MAX_AGE_SECONDS}; SameSite=Lax`;
    setThemeSlug(next);
    reloadThemeStylesheet();
  }

  const isDark = mode === "dark";
  const modeLabel = isDark ? "Switch to light theme" : "Switch to dark theme";

  return (
    <div className="flex items-center gap-2">
      {choices.length > 0 && (
        <>
          <label htmlFor="theme-select" className="sr-only">
            Theme
          </label>
          <select
            id="theme-select"
            value={themeSlug}
            onChange={handleThemeChange}
            className={SELECT_CLASSES}
          >
            {choices.map((choice) => (
              <option key={choice.value} value={choice.value}>
                {choice.label}
              </option>
            ))}
          </select>
        </>
      )}
      <Tooltip label={modeLabel}>
        <button
          type="button"
          onClick={handleModeToggle}
          aria-label={modeLabel}
          className={MODE_BUTTON_CLASSES}
        >
          {isDark ? <SunIcon /> : <MoonIcon />}
        </button>
      </Tooltip>
    </div>
  );
}

// GenWave spectator theme switcher — the page's FIRST interactive chrome (STORY-266, SPEC
// F102.9-F102.11, PLAN T166). F63's original ruling was "no theme toggle here"; this file is
// what overturns it, so its constraints below are load-bearing, not incidental style choices.
//
// Two independent axes (matching admin-ui/lib/theme.ts and its ThemeToggle.tsx exactly, PLAN
// T164 ruling 2026-08-03 — the same names, the same cookie shape, on purpose, so a visitor's
// choice is legible across both surfaces):
//   - genwave-theme (GenWave.Host.Theming.ThemeCatalog.CookieName): the station's THEME
//     (palette) slug. Server-RESOLVED (visitor cookie > Station:Theme > shipped default) by
//     ThemeCatalog.Resolve — this file only ever WRITES it, never resolves it itself.
//   - genwave-mode: the light/dark MODE within whichever theme is active. Resolution here is
//     the ONLY place it happens — the server can never see prefers-color-scheme (SPEC F102.10:
//     the page stays static, so mode is a purely client concern) — so mode is a purely client
//     concern.
//
// AC4 (calls stay within /spectator/api/*): the theme LIST is read once, on load, via
// GET /spectator/api/themes (SPEC F102.10a) — a read within the spectator API surface, never the
// admin API. Persisting a choice and reflecting a chosen MODE are still never a request: a
// document.cookie write, a DOM attribute flip, or a reload of the /spectator/theme.css <link> the
// page already declares. Reflecting a changed theme SLUG means re-requesting that same
// stylesheet (the server resolves it fresh from the request's Cookie header, T164) rather than
// fetching a per-theme URL that does not exist.
//
// This script is loaded UNDEFERRED, early in <head> (see index.html's own remarks): the mode
// stamp below runs before <body> exists, closing most of the flash-of-wrong-mode window a
// deferred script (like app.js) would leave open. Everything that touches <body> content is
// deferred to DOMContentLoaded — including the themes fetch, since it only ever populates the
// <select> once <body> exists.

const THEME_COOKIE_NAME = "genwave-theme";
const MODE_COOKIE_NAME = "genwave-mode";
const COOKIE_MAX_AGE_SECONDS = 60 * 60 * 24 * 365; // one year — an explicit choice shouldn't expire

function readCookie(name) {
  const match = document.cookie.match(new RegExp(`(?:^|; )${name}=([^;]*)`));
  return match ? decodeURIComponent(match[1]) : null;
}

/** No Secure flag (dev runs over plain http — a Secure-only cookie would silently fail to set
 * there); no HttpOnly (this is a UI preference, not a credential, and must be readable/writable
 * by this very script); SameSite=Lax is enough since nothing here is a state-changing request. */
function writeCookie(name, value) {
  document.cookie = `${name}=${encodeURIComponent(value)}; path=/; max-age=${COOKIE_MAX_AGE_SECONDS}; SameSite=Lax`;
}

// ── Mode (light/dark) ────────────────────────────────────────────────────────

function stampMode(mode) {
  document.documentElement.setAttribute("data-theme", mode);
}

// Runs immediately at parse time (see file header) — before <body>, so no flash between the
// default and an explicit stored choice.
(function applyStoredModeEarly() {
  const stored = readCookie(MODE_COOKIE_NAME);
  if (stored === "light" || stored === "dark") stampMode(stored);
})();

/** The mode currently in effect: the explicit data-theme attribute if this visitor (or the
 * cookie above) already set one, otherwise the resolved prefers-color-scheme default — the same
 * two-step admin-ui's ThemeToggle uses. */
function resolveCurrentMode() {
  const attr = document.documentElement.getAttribute("data-theme");
  if (attr === "light" || attr === "dark") return attr;
  return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

// ── Theme (palette) ──────────────────────────────────────────────────────────

/** GET /spectator/api/themes (SPEC F102.10a) — the ONE read this script makes, on load, to learn
 * the catalog and which slug the server resolved as active for this visitor. Never any other
 * network call: a fetch failure just leaves the <select> empty, same posture as app.js's own
 * poll failures elsewhere on this page. */
async function fetchThemeCatalog() {
  try {
    const response = await fetch("/spectator/api/themes");
    if (!response.ok) return { active: "", options: [] };
    return await response.json();
  } catch (error) {
    console.error(error);
    return { active: "", options: [] };
  }
}

/** Forces the page's OWN /spectator/theme.css <link> to refetch: the endpoint resolves fresh
 * from the request's Cookie header on every hit (Cache-Control: no-cache, T164), so once the
 * cookie above is set, all that's needed is a genuinely NEW fetch of the SAME href — reassigning
 * an unchanged href is not reliably honoured by every engine, so a replacement node (identical
 * href, never a new URL) is used instead of a cache-busting query string. Still the SAME
 * same-origin stylesheet reference the page already ships (AC4) — not a new endpoint, not a new
 * target. */
function reloadThemeStylesheet() {
  const current = document.getElementById("theme-stylesheet");
  if (!current) return;
  current.replaceWith(current.cloneNode(false));
}

function populateThemeSelect(select, catalog) {
  select.textContent = "";
  for (const option of catalog.options || []) {
    const entry = document.createElement("option");
    entry.value = option.slug;
    entry.textContent = option.name;
    select.appendChild(entry);
  }
  if (catalog.active) select.value = catalog.active;
}

// ── Wiring (deferred — needs <body>) ─────────────────────────────────────────

function updateModeButton(button, mode) {
  const next = mode === "dark" ? "light" : "dark";
  button.setAttribute("data-current-mode", mode);
  button.setAttribute("aria-pressed", String(mode === "dark"));
  button.setAttribute("aria-label", `Switch to ${next} theme`);
}

async function initThemeSwitcher() {
  const select = document.getElementById("theme-select");
  const modeButton = document.getElementById("mode-toggle");
  if (!select || !modeButton) return;

  updateModeButton(modeButton, resolveCurrentMode());
  populateThemeSelect(select, await fetchThemeCatalog());

  select.addEventListener("change", () => {
    writeCookie(THEME_COOKIE_NAME, select.value);
    reloadThemeStylesheet();
  });

  modeButton.addEventListener("click", () => {
    const next = resolveCurrentMode() === "dark" ? "light" : "dark";
    stampMode(next);
    writeCookie(MODE_COOKIE_NAME, next);
    updateModeButton(modeButton, next);
  });
}

document.addEventListener("DOMContentLoaded", initThemeSwitcher);

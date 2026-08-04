// STORY-267 — Admin UI theme selection (SPEC F102.12, F102.13, F102.16)
//
// Runner: Jest (jsdom, by file extension — see jest.config.js). The Admin UI already owns a real
// theme mechanism — a `genwave-mode` cookie driving `:root[data-theme="dark"]`, with
// `:root:not([data-theme])` as the system-dark fallback. This story widens that from a binary
// light/dark toggle to theme selection, keeping the two axes separate: the THEME is chosen, the
// MODE within it still follows an explicit choice or, absent one, prefers-color-scheme.
//
// F102.16 (amended 2026-08-04): T166/T167 already banked the real anti-drift win — both surfaces
// load the composer's `theme.css`, so the LIVE tokens have exactly one source. The static
// `globals.css`/`styles.css` token blocks are NOT retired — SPEC F102.7 requires them to stay, as
// the never-unstyled fallback for when `theme.css` is slow, failed or absent. What this file
// guards on that DEGRADED path only is a cross-surface PARITY check: the two fallbacks' shared
// semantic tokens (bg/surface/surface-2/line/ink/mute/accent/accent-ink/accent-2/danger/
// danger-ink/success) must agree, even though the two sheets legitimately differ in structure —
// globals.css also carries `--sched-*` swatches and an explicit `:root[data-theme="dark"]`
// block; styles.css has neither.
//
// AC1–AC5 flip live at T167, against the new `ThemeSwitcher` component (replaces `ThemeToggle`)
// and `app/(authed)/layout.tsx`'s single settings fetch (the /design ruling, 2026-08-04:
// ARCHITECTURE "Theme-list delivery — both surfaces read, neither templates" — the admin switcher
// sources its list from `GET /api/settings`'s `Station:Theme` row, never a second endpoint).
// AC7/AC8 flip live here at T168, to the amended acceptance criteria above. AC6 stays it.todo
// pending T170 (LIVE cross-surface parity against a running stack — Jest cannot reproduce the
// composer plus a real HTTP round trip) — house pattern, see safe-scope-empty-badge.spec.tsx.

jest.mock("next/headers", () => ({
  cookies: jest.fn(),
}));

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { ReactNode } from "react";
import { readdirSync, readFileSync } from "node:fs";
import path from "node:path";
import type { cookies } from "next/headers";
import type { ThemeChoice } from "../lib/theme";

const mockedCookies = jest
  .requireMock<{ cookies: typeof cookies }>("next/headers")
  .cookies as jest.MockedFunction<typeof cookies>;

// ---------------------------------------------------------------------------
// Cookie store + tree-walker fakes (mirror app-shell.spec.tsx / station-wordmark.spec.tsx's
// own house pattern for calling an async server component directly, without RTL).
// ---------------------------------------------------------------------------

interface FakeCookieStore {
  get: (name: string) => { value: string } | undefined;
  toString: () => string;
}

function mockCookieStore(store: FakeCookieStore): void {
  mockedCookies.mockResolvedValue(store as unknown as Awaited<ReturnType<typeof cookies>>);
}

function authedCookieStore(): FakeCookieStore {
  return { get: () => ({ value: "test-session" }), toString: () => "genwave-auth=test-session" };
}

/** Finds the first element of the given component type anywhere in a server-component element
 *  tree (mirrors station-wordmark.spec.tsx / app-shell.spec.tsx's own walker — AuthedLayout is
 *  called directly as a plain function, never rendered through RTL). */
function findElementByType(
  node: ReactNode,
  type: unknown
): { props: Record<string, unknown> } | undefined {
  if (node === null || node === undefined || typeof node !== "object") {
    return undefined;
  }
  if (Array.isArray(node)) {
    for (const child of node) {
      const found = findElementByType(child, type);
      if (found) return found;
    }
    return undefined;
  }
  const el = node as { type?: unknown; props?: Record<string, unknown> };
  if (el.type === type) {
    return el as { props: Record<string, unknown> };
  }
  if (el.props && el.props["children"] !== undefined) {
    return findElementByType(el.props["children"] as ReactNode, type);
  }
  return undefined;
}

function mockMatchMedia(prefersDark: boolean): void {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    configurable: true,
    value: jest.fn().mockImplementation((query: string) => ({
      matches: prefersDark,
      media: query,
      addEventListener: jest.fn(),
      removeEventListener: jest.fn(),
      dispatchEvent: jest.fn(),
    })),
  });
}

// ---------------------------------------------------------------------------
// Fixtures — `Station:Theme`'s `choices` shape (`SettingDto.choices` off `GET /api/settings`).
// ---------------------------------------------------------------------------

const SHIPPED_THEME_CHOICES: ThemeChoice[] = [
  { value: "cats-whisker", label: "Cat's Whisker", isDefault: true },
  { value: "aurora-glow", label: "Aurora Glow" },
  { value: "harbor-static", label: "Harbor Static" },
];

/** `GET /api/settings`'s full row list — only the two keys `AuthedLayout` reads. */
function settingsResponse(themeValue: string, choices: ThemeChoice[] = SHIPPED_THEME_CHOICES): unknown[] {
  return [
    { key: "Community:CatalogIndexUrl", value: "" },
    { key: "Station:Theme", value: themeValue, choices },
  ];
}

function makeLayoutFetchMock(settings: unknown[]): jest.MockedFunction<typeof fetch> {
  const fn = jest.fn<typeof fetch>().mockImplementation(async (input) => {
    const url = String(input);
    if (url.includes("/api/stations")) {
      return {
        ok: true,
        status: 200,
        json: async () => [{ id: 1, name: "WKRP Radio" }],
        headers: new Headers(),
      } as unknown as Response;
    }
    if (url.includes("/api/settings")) {
      return { ok: true, status: 200, json: async () => settings, headers: new Headers() } as unknown as Response;
    }
    return { ok: false, status: 404, json: async () => [], headers: new Headers() } as unknown as Response;
  });
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

// ---------------------------------------------------------------------------
// T168 cross-surface parity guard (SPEC F102.16, ARCHITECTURE "Never-unstyled fallback") — the
// two static fallback sheets legitimately differ in STRUCTURE (globals.css also carries
// --sched-1..6 swatches and an explicit :root[data-theme="dark"] block; styles.css has only
// :root plus a @media (prefers-color-scheme: dark) block, no --sched-*) but must agree on the
// SHARED semantic tokens both surfaces actually paint with. This governs ONLY the degraded
// theme.css-absent path — belt-and-suspenders, never the live render (which the composer already
// makes single-source).
// ---------------------------------------------------------------------------

/** The semantic tokens both fallback sheets are required to share — deliberately excludes
 *  globals.css-only tokens (--sched-1..6, --font-*). */
const SHARED_FALLBACK_TOKENS = [
  "bg",
  "surface",
  "surface-2",
  "line",
  "ink",
  "mute",
  "accent",
  "accent-ink",
  "accent-2",
  "danger",
  "danger-ink",
  "success",
] as const;

type SharedFallbackTokens = Record<(typeof SHARED_FALLBACK_TOKENS)[number], string>;

/**
 * Extracts the body of the first CSS block whose selector text is `header` (searched from
 * `fromIndex`), by counting braces from the block's own opening `{` — robust to a block nested
 * inside another rule (styles.css's dark `:root` sits inside a `@media` query) regardless of
 * indentation, unlike a naive "scan for the next closing brace" approach.
 */
function extractCssBlock(css: string, header: string, fromIndex = 0): string {
  const headerIndex = css.indexOf(header, fromIndex);
  if (headerIndex === -1) {
    throw new Error(`CSS block header not found: ${header}`);
  }
  const openBrace = css.indexOf("{", headerIndex);
  if (openBrace === -1) {
    throw new Error(`No opening brace found for block: ${header}`);
  }
  let depth = 0;
  for (let i = openBrace; i < css.length; i++) {
    if (css[i] === "{") depth++;
    else if (css[i] === "}") {
      depth--;
      if (depth === 0) return css.slice(openBrace + 1, i);
    }
  }
  throw new Error(`Unterminated CSS block: ${header}`);
}

/** Reads each of {@link SHARED_FALLBACK_TOKENS}'s values out of a `:root { ... }` block body.
 *  Requiring the colon immediately after the token name (`--accent:`) is what keeps `--accent`
 *  from also matching `--accent-ink`'s/`--accent-2`'s own declarations. */
function extractSharedTokens(blockBody: string): SharedFallbackTokens {
  const result = {} as SharedFallbackTokens;
  for (const token of SHARED_FALLBACK_TOKENS) {
    const match = new RegExp(`--${token}:\\s*([^;]+);`).exec(blockBody);
    if (!match) {
      throw new Error(`Token --${token} not found in block`);
    }
    result[token] = match[1].trim();
  }
  return result;
}

const GLOBALS_CSS_PATH = path.resolve(__dirname, "..", "app", "globals.css");
const SPECTATOR_CSS_PATH = path.resolve(
  __dirname,
  "..",
  "..",
  "src",
  "GenWave.Host",
  "wwwroot",
  "spectator",
  "styles.css"
);
const SPECTATOR_INDEX_HTML_PATH = path.resolve(
  __dirname,
  "..",
  "..",
  "src",
  "GenWave.Host",
  "wwwroot",
  "spectator",
  "index.html"
);

function readGlobalsCss(): string {
  return readFileSync(GLOBALS_CSS_PATH, "utf8");
}

function readSpectatorCss(): string {
  return readFileSync(SPECTATOR_CSS_PATH, "utf8");
}

/** globals.css's LIGHT fallback block: the file's first bare `:root {` — its attribute-qualified
 *  sibling below (`:root[data-theme="dark"] {`) does not match this literal header text. */
function adminLightTokens(css: string): SharedFallbackTokens {
  return extractSharedTokens(extractCssBlock(css, ":root {"));
}

/** globals.css's DARK fallback block — the explicit `:root[data-theme="dark"]` selector
 *  ARCHITECTURE's "Never-unstyled fallback" note names as one of the two structural differences
 *  from styles.css (the other being --sched-*). */
function adminDarkTokens(css: string): SharedFallbackTokens {
  return extractSharedTokens(extractCssBlock(css, ':root[data-theme="dark"] {'));
}

/** styles.css's LIGHT fallback block: its own first bare `:root {`, before the
 *  prefers-color-scheme media query below it. */
function spectatorLightTokens(css: string): SharedFallbackTokens {
  return extractSharedTokens(extractCssBlock(css, ":root {"));
}

/** styles.css's DARK fallback block: nested inside `@media (prefers-color-scheme: dark)` — there
 *  is no explicit `[data-theme="dark"]` selector on this surface, so the dark tokens are reached
 *  by extracting the media block first, then its own nested `:root { ... }`. */
function spectatorDarkTokens(css: string): SharedFallbackTokens {
  const mediaBlock = extractCssBlock(css, "@media (prefers-color-scheme: dark)");
  return extractSharedTokens(extractCssBlock(mediaBlock, ":root {"));
}

let originalFetch: typeof fetch;

beforeEach(() => {
  originalFetch = global.fetch;
  document.documentElement.removeAttribute("data-theme");
  document.cookie = "genwave-theme=; path=/; max-age=0";
  document.cookie = "genwave-mode=; path=/; max-age=0";
});

afterEach(() => {
  global.fetch = originalFetch;
  document.documentElement.removeAttribute("data-theme");
  jest.clearAllMocks();
});

describe("Feature: Admin UI theme selection", () => {
  // ── HAPPY PATH ──────────────────────────────────────────────────────────

  describe("Scenario: the console offers the same themes as the public page", () => {
    it("renders one selectable option per shipped theme (T167, AC1)", async () => {
      mockMatchMedia(false);
      const { ThemeSwitcher } = await import("../app/(authed)/_components/ThemeSwitcher");

      render(<ThemeSwitcher choices={SHIPPED_THEME_CHOICES} stationThemeSlug="cats-whisker" />);

      const options = screen.getAllByRole("option");
      expect(options).toHaveLength(SHIPPED_THEME_CHOICES.length);
      for (const choice of SHIPPED_THEME_CHOICES) {
        expect(screen.getByRole("option", { name: choice.label })).toBeInTheDocument();
      }
    });
  });

  describe("Scenario: it replaces the binary toggle", () => {
    it("no longer renders today's light/dark-only toggle (T167, AC2)", () => {
      const componentsDir = path.resolve(__dirname, "..", "app", "(authed)", "_components");
      const files = readdirSync(componentsDir);

      expect(files).not.toContain("ThemeToggle.tsx");
    });

    it("renders theme selection in its place (T167, AC2)", async () => {
      mockCookieStore(authedCookieStore());
      makeLayoutFetchMock(settingsResponse("aurora-glow"));

      const { default: AuthedLayout } = await import("../app/(authed)/layout");
      const { ThemeSwitcher } = await import("../app/(authed)/_components/ThemeSwitcher");

      const tree = await AuthedLayout({ children: "content" });
      const switcherEl = findElementByType(tree, ThemeSwitcher);

      // The header now carries the switcher — sourced from the SAME `GET /api/settings` read
      // this layout already made for the Persona Catalog gate (the /design ruling), never a
      // second/templated endpoint.
      expect(switcherEl).toBeDefined();
      expect(switcherEl?.props["choices"]).toEqual(SHIPPED_THEME_CHOICES);
      expect(switcherEl?.props["stationThemeSlug"]).toBe("aurora-glow");
    });
  });

  describe("Scenario: an explicit choice outranks OS preference", () => {
    it("applies the explicitly chosen theme when the OS preference disagrees (T167, AC3)", async () => {
      // The theme axis never reads prefers-color-scheme at all (only mode does, SPEC F102.13) —
      // pinning the OS to "dark" here proves the cookie wins regardless, not merely "wins when OS
      // is silent".
      mockMatchMedia(true);
      document.cookie = "genwave-theme=harbor-static; path=/";
      const { ThemeSwitcher } = await import("../app/(authed)/_components/ThemeSwitcher");

      render(<ThemeSwitcher choices={SHIPPED_THEME_CHOICES} stationThemeSlug="cats-whisker" />);

      expect(screen.getByRole("combobox")).toHaveValue("harbor-static");
    });

    it("applies the explicitly chosen mode when the OS preference disagrees (T167, AC3)", async () => {
      // Simulates root layout's own server-side stamp: an explicit genwave-mode cookie resolves
      // to a data-theme attribute on <html> BEFORE this component ever mounts (SPEC F28.4).
      document.documentElement.setAttribute("data-theme", "light");
      mockMatchMedia(true); // OS prefers dark
      const { ThemeSwitcher } = await import("../app/(authed)/_components/ThemeSwitcher");

      render(<ThemeSwitcher choices={[]} stationThemeSlug="" />);

      expect(screen.getByRole("button", { name: "Switch to dark theme" })).toBeInTheDocument();
    });
  });

  describe("Scenario: OS preference still picks the mode", () => {
    // The two axes stay separate: with no explicit choice, prefers-color-scheme selects
    // the MODE WITHIN the station's theme — it does not select a different theme.
    it("applies the station theme's dark mode when the OS prefers dark and no explicit choice exists (T167, AC4)", async () => {
      mockMatchMedia(true);
      const { ThemeSwitcher } = await import("../app/(authed)/_components/ThemeSwitcher");

      render(<ThemeSwitcher choices={SHIPPED_THEME_CHOICES} stationThemeSlug="cats-whisker" />);

      // Mode resolved dark; the theme picker still names the station's own theme, not a
      // different one — the OS preference never touches this axis.
      expect(screen.getByRole("button", { name: "Switch to light theme" })).toBeInTheDocument();
      expect(screen.getByRole("combobox")).toHaveValue("cats-whisker");
    });

    it("applies the station theme's light mode when the OS prefers light and no explicit choice exists (T167, AC4)", async () => {
      mockMatchMedia(false);
      const { ThemeSwitcher } = await import("../app/(authed)/_components/ThemeSwitcher");

      render(<ThemeSwitcher choices={SHIPPED_THEME_CHOICES} stationThemeSlug="cats-whisker" />);

      expect(screen.getByRole("button", { name: "Switch to dark theme" })).toBeInTheDocument();
      expect(screen.getByRole("combobox")).toHaveValue("cats-whisker");
    });
  });

  describe("Scenario: an OS-dark visitor is never served a light palette", () => {
    // This is why flat one-look themes were rejected at design — they would strand an
    // OS-dark visitor in whichever palette the station happened to pick. Exercised through the
    // FULL pipeline (AuthedLayout's settings fetch through to the rendered switcher) — "no
    // explicit choice ANYWHERE" means neither cookie is set AND the settings response is the only
    // source of the active theme.
    it("resolves the active theme's dark mode with no explicit choice anywhere (T167, AC5)", async () => {
      mockCookieStore(authedCookieStore());
      makeLayoutFetchMock(settingsResponse("harbor-static"));
      mockMatchMedia(true);

      const { default: AuthedLayout } = await import("../app/(authed)/layout");
      const { ThemeSwitcher } = await import("../app/(authed)/_components/ThemeSwitcher");

      const tree = await AuthedLayout({ children: "content" });
      const switcherEl = findElementByType(tree, ThemeSwitcher);
      expect(switcherEl).toBeDefined();

      render(
        <ThemeSwitcher
          choices={switcherEl?.props["choices"] as readonly ThemeChoice[]}
          stationThemeSlug={switcherEl?.props["stationThemeSlug"] as string}
        />
      );

      expect(screen.getByRole("button", { name: "Switch to light theme" })).toBeInTheDocument();
      expect(screen.getByRole("combobox")).toHaveValue("harbor-static");
    });
  });

  describe("Scenario: both surfaces resolve from one source", () => {
    it.todo(
      "resolves token values identical to the spectator surface for the same theme and mode (T170, AC6)",
    );
  });

  describe("Scenario: the static token blocks are the F102.7 fallback, documented as such, not a live mirror", () => {
    // Amended 2026-08-04 (T168): the blocks are NOT retired — F102.7 requires them as the
    // never-unstyled fallback. What flips here is that they stay, and their comments stop
    // claiming to be a live 1:1 mirror of one another.
    it("globals.css still carries the shipped default's fallback tokens for every shared semantic token (T168, AC7)", () => {
      const light = adminLightTokens(readGlobalsCss());

      expect(light).toEqual({
        bg: "#f6efe3",
        surface: "#fdf8ee",
        "surface-2": "#efe5d2",
        line: "#ddd0b8",
        ink: "#2b2320",
        mute: "#706256",
        accent: "#b94f29",
        "accent-ink": "#fdf8ee",
        "accent-2": "#6f632f",
        danger: "#a63325",
        "danger-ink": "#fdf8ee",
        success: "#5c7a3f",
      });
    });

    it("spectator/styles.css still carries the shipped default's fallback tokens for every shared semantic token (T168, AC7)", () => {
      const light = spectatorLightTokens(readSpectatorCss());

      expect(light).toEqual({
        bg: "#f6efe3",
        surface: "#fdf8ee",
        "surface-2": "#efe5d2",
        line: "#ddd0b8",
        ink: "#2b2320",
        mute: "#706256",
        accent: "#b94f29",
        "accent-ink": "#fdf8ee",
        "accent-2": "#6f632f",
        danger: "#a63325",
        "danger-ink": "#fdf8ee",
        success: "#5c7a3f",
      });
    });

    it("neither sheet's own comment still claims a live 1:1 mirror of the other (T168, AC7)", () => {
      const globalsCss = readGlobalsCss();
      const spectatorCss = readSpectatorCss();

      // The stale claim being retired: an unenforced "mirrors ... 1:1" framing (the original
      // styles.css header). Replaced in both files with the honest F102.7 fallback framing.
      expect(spectatorCss).not.toMatch(/mirrors[\s\S]{0,120}1:1/i);
      expect(globalsCss).not.toMatch(/mirrors[\s\S]{0,120}1:1/i);
      expect(spectatorCss.toLowerCase()).toContain("fallback");
      expect(globalsCss.toLowerCase()).toContain("fallback");
    });
  });

  // ── SAD PATH ────────────────────────────────────────────────────────────

  describe("Scenario: the composed sheet is the single live source, with a parity guard on the fallback path", () => {
    it("both surfaces link the composed sheet AFTER their own static fallback block (T168, AC8)", async () => {
      // Admin: app/layout.tsx's own <link precedence="theme">. theme-stylesheet-link.spec.ts
      // pins the full ordering proof (React's precedence-Resource mechanism); this restates the
      // same href/precedence contract as this task's own acceptance.
      mockCookieStore(authedCookieStore());
      const { default: RootLayout } = await import("../app/layout");
      const tree = await RootLayout({ children: "content" });
      const themeLink = findElementByType(tree, "link");

      expect(themeLink?.props["href"]).toBe("/api/theme.css");
      expect(themeLink?.props["precedence"]).toBe("theme");

      // Spectator: index.html declares styles.css then theme.css, in that literal document
      // order — Story264_ComposedStylesheet.cs pins this server-side; restated here as this
      // task's own client-side half of the same contract.
      const indexHtml = readFileSync(SPECTATOR_INDEX_HTML_PATH, "utf8");
      const stylesIndex = indexHtml.indexOf('href="/spectator/styles.css"');
      const themeIndex = indexHtml.indexOf('href="/spectator/theme.css"');

      expect(stylesIndex).toBeGreaterThan(-1);
      expect(themeIndex).toBeGreaterThan(stylesIndex);
    });

    // The cross-surface parity guard (SPEC F102.16's amended scope): belt-and-suspenders on the
    // theme.css-ABSENT degraded path only. It does NOT assert the files are byte-identical or
    // that their token SETS are equal — globals.css also carries --sched-1..6 and an explicit
    // :root[data-theme="dark"] block that styles.css has neither of — only that the tokens both
    // surfaces actually share stay equal, in both modes.
    it("the two fallbacks' shared semantic tokens are equal in LIGHT mode (T168, AC8 parity guard)", () => {
      const globalsCss = readGlobalsCss();
      const spectatorCss = readSpectatorCss();

      expect(adminLightTokens(globalsCss)).toEqual(spectatorLightTokens(spectatorCss));
    });

    it("the two fallbacks' shared semantic tokens are equal in DARK mode (T168, AC8 parity guard)", () => {
      const globalsCss = readGlobalsCss();
      const spectatorCss = readSpectatorCss();

      expect(adminDarkTokens(globalsCss)).toEqual(spectatorDarkTokens(spectatorCss));
    });
  });
});

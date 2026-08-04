// STORY-267 — Admin UI theme selection (SPEC F102.12, F102.13, F102.16)
//
// Runner: Jest (jsdom, by file extension — see jest.config.js). The Admin UI already owns a real
// theme mechanism — a `genwave-mode` cookie driving `:root[data-theme="dark"]`, with
// `:root:not([data-theme])` as the system-dark fallback. This story widens that from a binary
// light/dark toggle to theme selection, keeping the two axes separate: the THEME is chosen, the
// MODE within it still follows an explicit choice or, absent one, prefers-color-scheme.
//
// F102.16 is the quiet win here. Today `wwwroot/spectator/styles.css` claims it "Mirrors
// admin-ui/app/globals.css's token values 1:1" and NOTHING enforces that — there is no
// cross-surface parity spec anywhere in the repo. Once both surfaces read the composed
// stylesheet, the drift is not merely tested against, it is structurally impossible: there
// is no second place a token value could be edited.
//
// AC1–AC5 flip live at T167, against the new `ThemeSwitcher` component (replaces `ThemeToggle`)
// and `app/(authed)/layout.tsx`'s single settings fetch (the /design ruling, 2026-08-04:
// ARCHITECTURE "Theme-list delivery — both surfaces read, neither templates" — the admin switcher
// sources its list from `GET /api/settings`'s `Station:Theme` row, never a second endpoint).
// AC6 stays it.todo pending T170 (cross-surface token parity); AC7/AC8 stay it.todo pending T168
// (retiring the mirrored globals.css/styles.css token blocks) — house pattern, see
// safe-scope-empty-badge.spec.tsx.

jest.mock("next/headers", () => ({
  cookies: jest.fn(),
}));

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { ReactNode } from "react";
import { readdirSync } from "node:fs";
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

  describe("Scenario: the duplicated token blocks are gone", () => {
    it.todo(
      "globals.css carries only the shipped default's fallback tokens, not a full hand-mirrored copy (T168, AC7)",
    );
    it.todo(
      "spectator/styles.css carries only the shipped default's fallback tokens (T168, AC7)",
    );
  });

  // ── SAD PATH ────────────────────────────────────────────────────────────

  describe("Scenario: drift cannot be reintroduced by editing one surface", () => {
    // The assertion that matters: after T168 there is no second place to edit. A spec that
    // merely compared two files would still permit drift and then report it; this one
    // asserts the second copy does not exist.
    it.todo(
      "changing a manifest token value changes both surfaces, with no second location holding that value (T168, AC8)",
    );
  });
});

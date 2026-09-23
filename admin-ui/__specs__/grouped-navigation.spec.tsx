// @jest-environment jsdom
// STORY-471 — Grouped navigation (gh-#779 gh-#707 · SPEC F203 · PLAN T558 T559 T560)
//
// BDD specification — Jest. RED at plan time: every specification is it.todo — turn it into an `it` only in the
// task that makes it green (T558–T560). Each Given comment names the arrange the scenario needs.
//
// next/jest's SWC transform (unlike babel-jest) does not hoist jest.mock() calls above import
// statements (mirrors app-shell.spec.tsx's own header comment), so Sidebar/MobileNav — the
// components that actually call the mocked next/navigation hook — are loaded via a dynamic
// `await import()` inside each test rather than a static top-level import.

jest.mock("next/navigation", () => ({
  usePathname: jest.fn(),
  redirect: jest.fn(),
}));

jest.mock("@/app/login/actions", () => ({
  logout: jest.fn(),
}));

import { describe, it, expect, jest, beforeEach } from "@jest/globals";
import { render, screen, fireEvent, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { readFileSync, readdirSync } from "node:fs";
import path from "node:path";
import type { redirect, usePathname } from "next/navigation";
import {
  NAV_BOTTOM,
  NAV_FOOTER,
  NAV_GROUPS,
  NAV_TOP,
  visibleNavGroups,
  type NavGroup,
} from "../app/(authed)/_components/nav-items";
import { resetNavGroupOpenStateForTests } from "../app/(authed)/_components/useNavGroupOpenState";

const ROOT = path.resolve(__dirname, "..");

// jsdom's `window` (and its `localStorage`) is shared by every `it` in this FILE, not recreated
// per test — a toggle written in one scenario (AC6) would otherwise leak into the next one. Every
// test starts from a clean store, matching SPEC F203.2's "no stored toggles" baseline unless a
// scenario deliberately arranges one.
beforeEach(() => {
  window.localStorage.clear();
});

const mockedNextNavigation = jest.requireMock<{
  usePathname: typeof usePathname;
  redirect: typeof redirect;
}>("next/navigation");
const mockedUsePathname = mockedNextNavigation.usePathname as jest.MockedFunction<typeof usePathname>;
const mockedRedirect = mockedNextNavigation.redirect as jest.MockedFunction<typeof redirect>;

/** Recursively lists files under `dir` with one of `exts`, skipping build/dep dirs (house pattern,
 * see app-shell.spec.tsx's own `collectFiles`). */
function collectFiles(dir: string, exts: string[], out: string[] = []): string[] {
  const SKIP = new Set(["node_modules", ".next"]);
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (SKIP.has(entry.name)) continue;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      collectFiles(full, exts, out);
    } else if (exts.some((ext) => entry.name.endsWith(ext))) {
      out.push(full);
    }
  }
  return out;
}

/** The visible text of a group heading button (its label span, not the decorative chevron —
 * which carries `aria-hidden` but is still part of `textContent`). */
function groupHeadingLabel(button: HTMLElement): string {
  return button.querySelector("span:not([aria-hidden])")?.textContent?.trim() ?? "";
}

/** The labels of every group whose heading currently reports `aria-expanded="true"`, in document
 * order (SPEC F203.2). */
function openGroupLabels(root: ParentNode): string[] {
  return Array.from(root.querySelectorAll<HTMLButtonElement>('nav button[aria-expanded="true"]')).map(
    groupHeadingLabel
  );
}

/** Every group's label mapped to its open/closed state (SPEC F203.6's snapshot target) — small and
 * readable, proving "exactly one open group" without snapshotting the whole DOM tree. */
function groupOpenStates(root: ParentNode): Record<string, boolean> {
  const headings = Array.from(root.querySelectorAll<HTMLButtonElement>("nav button[aria-expanded]"));
  return Object.fromEntries(
    headings.map((button) => [groupHeadingLabel(button), button.getAttribute("aria-expanded") === "true"])
  );
}

/** Every static/dynamic import specifier (`from "..."` / `import("...")`) literally present in
 * `fileText` — used by AC13's source scan below. */
function importSpecifiersFrom(fileText: string): string[] {
  const specifiers: string[] = [];
  for (const re of [/\bfrom\s+["']([^"']+)["']/g, /\bimport\(\s*["']([^"']+)["']\s*\)/g]) {
    for (const match of fileText.matchAll(re)) {
      const specifier = match[1];
      if (specifier !== undefined) specifiers.push(specifier);
    }
  }
  return specifiers;
}

/** Resolves a relative (`./`, `../`) or `@/`-aliased specifier found in `fromFile` to an absolute
 * path with no extension — `null` for a bare package specifier (e.g. `"react"`), which can never
 * resolve inside `app/`. Mirrors `moduleNameMapper`'s own `^@/(.*)$ -> <rootDir>/$1` (jest.config.js). */
function resolveImportSpecifier(fromFile: string, specifier: string): string | null {
  if (specifier.startsWith(".")) return path.resolve(path.dirname(fromFile), specifier);
  if (specifier.startsWith("@/")) return path.join(ROOT, specifier.slice(2));
  return null;
}

/** The ordered shape of a rendered `<nav>`: `[Label]` for a group heading, the link text for every
 * item — independent of open/closed state, since a collapsed group's list stays in the DOM (just
 * `hidden`, SPEC F203.2) rather than unmounting. Used to compare Sidebar and MobileNav structurally
 * (STORY-471 AC8) without requiring every group open first. */
function navShape(nav: HTMLElement): string[] {
  return Array.from(nav.querySelectorAll<HTMLElement>("a, button[aria-expanded]")).map((el) =>
    el.tagName === "A" ? (el.textContent?.trim() ?? "") : `[${groupHeadingLabel(el)}]`
  );
}

describe("Feature: Grouped navigation", () => {
  describe("Scenario: the nav model", () => {
    // Given: NAV_GROUPS, NAV_TOP, NAV_BOTTOM, NAV_FOOTER from nav-items.ts
    it("AC1 — NAV_GROUPS is Station, Media, Tools, Status with the ratified items in order", () => {
      const shape = NAV_GROUPS.map((group) => ({
        label: group.label,
        items: group.items.map((item) => item.label),
      }));

      expect(shape).toEqual([
        { label: "Station", items: ["Station sounds", "Ads", "Personas", "Schedule", "Shows"] },
        { label: "Media", items: ["Catalog", "Announcements", "Community Catalog"] },
        { label: "Tools", items: ["Gardener", "Theme Editor"] },
        { label: "Status", items: ["Booth log", "Health"] },
      ]);
    });

    it("AC2 — NAV_TOP is Dashboard, NAV_BOTTOM is Settings, NAV_FOOTER is About then Sign out", () => {
      const shape = {
        top: NAV_TOP.map((item) => item.label),
        bottom: NAV_BOTTOM.map((item) => item.label),
        footer: NAV_FOOTER.map((entry) => entry.label),
      };

      expect(shape).toEqual({ top: ["Dashboard"], bottom: ["Settings"], footer: ["About", "Sign out"] });
    });

    it("AC3 — the retired flat nav array is imported and exported nowhere", () => {
      // Built from parts, not the literal identifier — this spec proves the retired token is gone
      // from the codebase, so it must not itself reintroduce a source hit of that same token.
      const retiredExportName = ["NAV", "ITEMS"].join("_");

      const files = [
        ...collectFiles(path.join(ROOT, "app"), [".ts", ".tsx"]),
        ...collectFiles(path.join(ROOT, "__specs__"), [".ts", ".tsx"]),
      ];
      const navItemsPath = path.join(ROOT, "app", "(authed)", "_components", "nav-items.ts");
      const offenders = files.filter((f) => readFileSync(f, "utf-8").includes(retiredExportName));

      // Single assertion that still proves the scan wasn't vacuous: it must have covered
      // nav-items.ts, and found zero offenders.
      expect({ scannedModel: files.includes(navItemsPath), offenders }).toEqual({
        scannedModel: true,
        offenders: [],
      });
    });
  });

  describe("Scenario: the sidebar at /ads", () => {
    // Given: Sidebar rendered with usePathname → "/ads", no stored toggles
    it("AC4 — exactly the Station group is open", async () => {
      mockedUsePathname.mockReturnValue("/ads");
      const { Sidebar } = await import("../app/(authed)/_components/Sidebar");

      const { container } = render(<Sidebar />);

      expect(openGroupLabels(container)).toEqual(["Station"]);
    });
  });

  describe("Scenario: the sidebar at /health", () => {
    // Given: usePathname → "/health"
    it("AC5 — exactly the Status group is open", async () => {
      mockedUsePathname.mockReturnValue("/health");
      const { Sidebar } = await import("../app/(authed)/_components/Sidebar");

      const { container } = render(<Sidebar />);

      expect(openGroupLabels(container)).toEqual(["Status"]);
    });
  });

  describe("Scenario: a remembered toggle", () => {
    // Given: Media toggled open at /ads, then a reload — the shared in-memory store is reset
    // (simulating a fresh page load) so the next render can only recover the toggle by reading
    // localStorage, not by surviving in memory across the unmount
    it("AC6 — Media is open alongside Station", async () => {
      mockedUsePathname.mockReturnValue("/ads");
      const { Sidebar } = await import("../app/(authed)/_components/Sidebar");

      const first = render(<Sidebar />);
      fireEvent.click(screen.getByRole("button", { name: "Media" }));
      first.unmount();
      resetNavGroupOpenStateForTests();

      const { container } = render(<Sidebar />);

      expect(openGroupLabels(container)).toEqual(["Station", "Media"]);
    });
  });

  describe("Scenario: the two surfaces share one store", () => {
    // Given: Sidebar and MobileNav mounted together, as the authed layout always does — the
    // persistent Sidebar is merely hidden below 1024px, never unmounted (PLAN T559 fix round,
    // finding #1) — the drawer opened (mounting its own NavSections fresh) and Media toggled open
    // INSIDE the drawer, so only a shared, subscribed store can carry that toggle back to the
    // Sidebar, which has been mounted the whole time and never re-reads storage on its own
    it("the Sidebar, mounted the whole time, reflects a toggle made in the drawer", async () => {
      mockedUsePathname.mockReturnValue("/ads");
      const { Sidebar } = await import("../app/(authed)/_components/Sidebar");
      const { MobileNav } = await import("../app/(authed)/_components/MobileNav");

      const { container: sidebarContainer } = render(<Sidebar />);
      render(<MobileNav />);
      fireEvent.click(screen.getByRole("button", { name: "Open navigation" }));
      const dialog = await screen.findByRole("dialog", { name: "Navigation" });
      fireEvent.click(within(dialog).getByRole("button", { name: "Media" }));

      expect(openGroupLabels(sidebarContainer)).toEqual(["Station", "Media"]);
    });
  });

  describe("Scenario: the drawer", () => {
    // Given: MobileNav rendered at /ads
    it("AC8 — lists the same groups and items in the same order as the Sidebar", async () => {
      mockedUsePathname.mockReturnValue("/ads");
      const { Sidebar } = await import("../app/(authed)/_components/Sidebar");
      const { MobileNav } = await import("../app/(authed)/_components/MobileNav");

      const { container: sidebarContainer } = render(<Sidebar />);
      const sidebarNav = sidebarContainer.querySelector<HTMLElement>("nav[aria-label='Sections']");
      if (sidebarNav === null) {
        throw new Error("expected the Sidebar's <nav aria-label='Sections'> to be present");
      }

      render(<MobileNav />);
      fireEvent.click(screen.getByRole("button", { name: "Open navigation" }));
      const dialog = await screen.findByRole("dialog", { name: "Navigation" });
      const drawerNav = within(dialog).getByRole("navigation", { name: "Sections" });

      expect(navShape(drawerNav)).toEqual(navShape(sidebarNav));
    });
  });

  describe("Scenario: the catalog gate", () => {
    // Given: catalog unavailable
    it("AC9 — Media shows Catalog and Announcements only", () => {
      const media = visibleNavGroups(false).find((group) => group.label === "Media");

      expect(media?.items.map((item) => item.label)).toEqual(["Catalog", "Announcements"]);
    });
  });

  describe("Scenario: an empty group", () => {
    // Given: every Tools item hidden — the house Tools group carries no gate, so this drives
    // visibleNavGroups with its own test group (per nav-items.ts's own remarks on that parameter).
    it("AC10 — no Tools heading renders", () => {
      const groups: NavGroup[] = [
        {
          id: "tools",
          label: "Tools",
          items: [{ href: "/gardener", label: "Gardener", iconName: "restore", requiresCatalog: true }],
        },
      ];

      expect(visibleNavGroups(false, groups)).toEqual([]);
    });
  });

  describe("Scenario: the retired routes", () => {
    // Given: app/(authed)/live/page.tsx and app/(authed)/wardrobe/page.tsx
    it("AC11 — /live redirects to /", async () => {
      const { default: LivePage } = await import("../app/(authed)/live/page");

      LivePage();

      expect(mockedRedirect).toHaveBeenCalledWith("/");
    });

    it("AC12 — /wardrobe redirects to /persona-catalog", async () => {
      const { default: WardrobePage } = await import("../app/(authed)/wardrobe/page");

      WardrobePage();

      expect(mockedRedirect).toHaveBeenCalledWith("/persona-catalog");
    });

    // Given: every source file under app/, and the live/ and wardrobe/ route folders themselves —
    // SPEC F203.4 requires both the dead surface gone AND nothing left importing it (the
    // orchestrator ruling for AC13: PickChips/StationThumbs/PersonaTasteThumbs are NOT retired —
    // BoothLogFeed.tsx keeps importing them from _components/ — so this scan only fails on an
    // import reaching INTO app/(authed)/live/ or app/(authed)/wardrobe/, never on those three
    // surviving components existing elsewhere).
    it("AC13 — no module under app/ imports the retired live/wardrobe surfaces, and both routes keep only their redirect page", () => {
      const liveDir = path.join(ROOT, "app", "(authed)", "live");
      const wardrobeDir = path.join(ROOT, "app", "(authed)", "wardrobe");
      const appFiles = collectFiles(path.join(ROOT, "app"), [".ts", ".tsx"]);
      const offenders: string[] = [];

      for (const file of appFiles) {
        if (file.startsWith(liveDir + path.sep) || file.startsWith(wardrobeDir + path.sep)) continue;
        const text = readFileSync(file, "utf-8");
        for (const specifier of importSpecifiersFrom(text)) {
          const resolved = resolveImportSpecifier(file, specifier);
          if (resolved === null) continue;
          const intoLive = resolved === liveDir || resolved.startsWith(liveDir + path.sep);
          const intoWardrobe = resolved === wardrobeDir || resolved.startsWith(wardrobeDir + path.sep);
          if (intoLive || intoWardrobe) offenders.push(`${path.relative(ROOT, file)} -> ${specifier}`);
        }
      }

      // Non-vacuous proof: BoothLogFeed.tsx is a known real importer of the surviving
      // PickChips/StationThumbs/PersonaTasteThumbs trio — confirm the scan actually walked it.
      const boothLogFeedPath = path.join(ROOT, "app", "(authed)", "booth-log", "BoothLogFeed.tsx");

      expect({
        scannedBoothLogFeed: appFiles.includes(boothLogFeedPath),
        offenders,
        liveDirEntries: readdirSync(liveDir).sort(),
        wardrobeDirEntries: readdirSync(wardrobeDir).sort(),
      }).toEqual({
        scannedBoothLogFeed: true,
        offenders: [],
        liveDirEntries: ["page.tsx"],
        wardrobeDirEntries: ["page.tsx"],
      });
    });
  });

  describe("Scenario: station sounds", () => {
    // Given: the safe-content page source and its nav entry
    it("AC14 — the nav label and both page h1s read \"Station sounds\"", () => {
      const stationGroup = NAV_GROUPS.find((group) => group.label === "Station");
      const navLabel = stationGroup?.items.find((item) => item.href === "/safe-content")?.label ?? null;
      const pageSource = readFileSync(
        path.join(ROOT, "app", "(authed)", "safe-content", "page.tsx"),
        "utf-8"
      );
      const h1Texts = Array.from(pageSource.matchAll(/<h1[^>]*>([^<]+)<\/h1>/g)).map((match) => match[1]);

      expect({ navLabel, h1Texts }).toEqual({
        navLabel: "Station sounds",
        h1Texts: ["Station sounds", "Station sounds"],
      });
    });

    it("AC14 — the one-line explainer paragraph is present", () => {
      const pageSource = readFileSync(
        path.join(ROOT, "app", "(authed)", "safe-content", "page.tsx"),
        "utf-8"
      );

      expect(pageSource).toMatch(/always airable: when the music rotation drains/);
    });
  });

  // ---- sad path ----
  describe("Scenario: storage that throws", () => {
    // Given: localStorage access throws
    it("AC7 — renders with the route rule only", async () => {
      mockedUsePathname.mockReturnValue("/ads");
      const getItemSpy = jest.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
        throw new Error("localStorage blocked");
      });

      try {
        const { Sidebar } = await import("../app/(authed)/_components/Sidebar");
        const { container } = render(<Sidebar />);

        expect(openGroupLabels(container)).toEqual(["Station"]);
      } finally {
        getItemSpy.mockRestore();
      }
    });
  });
});

// ---------------------------------------------------------------------------
// SPEC F203.6 — one open group per active route, proven per route group.
// ---------------------------------------------------------------------------

describe("Feature: Grouped navigation — F203.6 snapshot", () => {
  const ROUTES_BY_GROUP: ReadonlyArray<{ pathname: string; groupLabel: string }> = [
    { pathname: "/ads", groupLabel: "Station" },
    { pathname: "/catalog", groupLabel: "Media" },
    { pathname: "/gardener", groupLabel: "Tools" },
    { pathname: "/health", groupLabel: "Status" },
  ];

  for (const { pathname, groupLabel } of ROUTES_BY_GROUP) {
    it(`shows exactly one open group ("${groupLabel}") at ${pathname}`, async () => {
      mockedUsePathname.mockReturnValue(pathname);
      const { Sidebar } = await import("../app/(authed)/_components/Sidebar");

      const { container } = render(<Sidebar />);

      expect(groupOpenStates(container)).toMatchSnapshot();
    });
  }
});

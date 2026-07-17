// @jest-environment jsdom
// STORY-093 — Responsive + accessibility pass (Epic Q / SPEC F28.13–F28.14, F28.2)
//
// Runner: Jest (jsdom). jsdom can't do real layout (no viewport, no actual
// horizontal-scroll measurement), so this file asserts the *contracts* that
// make the real behavior true: the drawer/hamburger/persistent-sidebar
// breakpoint split, a real Radix focus-trap round trip, overflow-x-auto
// wrapper containers around each wide table, min-h-10 touch-target classes
// on standalone interactive controls, motion-reduce gating in globals.css,
// and computed AA contrast from the token values (the same WCAG helper as
// design-system-foundation.spec.ts, duplicated here — spec files don't
// import from one another in this codebase).

// next/jest's SWC transform (unlike babel-jest) does not hoist jest.mock()
// calls above import statements — ES import declarations are always
// evaluated first regardless of source position (mirrors app-shell.spec.tsx's
// header comment). So Sidebar/MobileNav/CatalogTable — the components that
// actually call a mocked next/navigation hook (usePathname, and CatalogTable's
// own useRouter as of SPEC F33.12's restore control, STORY-115) — are loaded
// via a dynamic `await import()` inside each test/helper instead of a static
// top-level import, which would bind too early to the real "next/navigation".
// CatalogTabs doesn't call any next/navigation hook, so a static import stays
// safe for it.

jest.mock("next/navigation", () => ({
  usePathname: jest.fn(),
  useRouter: jest.fn(),
}));

jest.mock("@/app/login/actions", () => ({
  logout: jest.fn(),
}));

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, fireEvent, act } from "@testing-library/react";
import "@testing-library/jest-dom";
import { readFileSync } from "node:fs";
import path from "node:path";
import type { usePathname, useRouter } from "next/navigation";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { CatalogTabs } from "../app/(authed)/catalog/CatalogTabs";
import { PlayHistoryTable } from "../app/(authed)/live/PlayHistoryTable";
import { RecentPlays } from "../app/(authed)/dashboard/RecentPlays";
import { SafeContentClient } from "../app/(authed)/safe-content/SafeContentClient";
import type { SafeContentClientProps } from "../app/(authed)/safe-content/SafeContentClient";
import type { AdminMediaDto, BulkFilter, Pagination } from "../app/(authed)/catalog/types";
import type { PlayHistoryEntry } from "../lib/broadcast-api";
import type { LibraryDto } from "../lib/library";

const ROOT = path.resolve(__dirname, "..");
const globalsCssPath = path.join(ROOT, "app", "globals.css");
const globalsCss = readFileSync(globalsCssPath, "utf-8");

const mockedUsePathname = jest
  .requireMock<{ usePathname: typeof usePathname }>("next/navigation")
  .usePathname as jest.MockedFunction<typeof usePathname>;
const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const EMPTY_FILTER: BulkFilter = {
  state: null,
  artist: null,
  genre: null,
  libraryId: null,
  q: null,
  eligible: null,
};

const LIBRARIES: LibraryDto[] = [{ id: 1, name: "In Rotation", mediaCount: 50 }];

function makeMedia(): AdminMediaDto[] {
  return [
    {
      mediaId: "1",
      locator: "/media/1.flac",
      format: "flac",
      state: "ready",
      durationMs: 180000,
      title: "Track 1",
      artist: "Test Artist",
      album: "Test Album",
      genre: "Rock",
      year: 2024,
      integratedLufs: -14,
      truePeakDbtp: -1,
      measurable: true,
      cueInSec: null,
      cueOutSec: null,
      eligible: true,
      version: "1",
      score: 50,
      neverPlay: false,
    },
  ];
}

function makePagination(): Pagination {
  return { total: 1, pages: 1, page: 1, limit: 50 };
}

function makeHistoryEntries(): PlayHistoryEntry[] {
  return [
    { mediaId: "1", title: "Track 1", artist: "Test Artist", gainDb: -1.2, startedAt: "2026-01-01T00:00:00Z" },
  ];
}

async function renderCatalogTable() {
  const { CatalogTable } = await import("../app/(authed)/catalog/CatalogTable");
  return render(
    <ConfirmDialogProvider>
      <CatalogTable
        media={makeMedia()}
        pagination={makePagination()}
        libraries={LIBRARIES}
        bulkFilter={EMPTY_FILTER}
        filterActive={false}
        clearFiltersHref="/catalog"
      />
      <Toaster />
    </ConfirmDialogProvider>
  );
}

function renderSafeContentClient() {
  const props: SafeContentClientProps = {
    libraries: [{ id: 7, name: "safe", mediaCount: 1 }],
    initialLibraryId: 7,
    initialSegments: [
      { mediaId: "42", title: "Please Stand By", artist: "GenWave", state: "ready", durationMs: 5000, eligible: true, version: "10" },
    ],
    initialOutOfScope: false,
    defaultText: "You're listening to {StationName}.",
    defaultTitle: "Please Stand By",
  };
  return render(
    <>
      <SafeContentClient {...props} />
      <Toaster />
    </>
  );
}

// ---------------------------------------------------------------------------
// WCAG relative-luminance / contrast-ratio helper — same formula as
// design-system-foundation.spec.ts (duplicated: spec files here don't import
// from one another).
// ---------------------------------------------------------------------------

function extractBlock(css: string, selector: string): string {
  const start = css.indexOf(selector);
  if (start === -1) {
    throw new Error(`selector not found: ${selector}`);
  }
  const braceStart = css.indexOf("{", start);
  let depth = 0;
  let i = braceStart;
  for (; i < css.length; i++) {
    if (css[i] === "{") depth++;
    if (css[i] === "}") {
      depth--;
      if (depth === 0) break;
    }
  }
  return css.slice(braceStart + 1, i);
}

function hexToRgb(hex: string): [number, number, number] {
  const match = /^#([0-9a-fA-F]{2})([0-9a-fA-F]{2})([0-9a-fA-F]{2})$/.exec(hex);
  if (!match) {
    throw new Error(`not a 6-digit hex color: ${hex}`);
  }
  return [parseInt(match[1], 16), parseInt(match[2], 16), parseInt(match[3], 16)];
}

function relativeLuminance([r, g, b]: [number, number, number]): number {
  const linear = (c: number): number => {
    const s = c / 255;
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * linear(r) + 0.7152 * linear(g) + 0.0722 * linear(b);
}

function contrastRatio(hexA: string, hexB: string): number {
  const lA = relativeLuminance(hexToRgb(hexA));
  const lB = relativeLuminance(hexToRgb(hexB));
  const [lighter, darker] = lA >= lB ? [lA, lB] : [lB, lA];
  return (lighter + 0.05) / (darker + 0.05);
}

function tokenValue(block: string, token: string): string {
  const value = block.match(new RegExp(`${token}\\s*:\\s*(#[0-9a-fA-F]{6})`))?.[1];
  if (!value) {
    throw new Error(`token not found: ${token}`);
  }
  return value;
}

const AA_NORMAL_TEXT_MIN_CONTRAST = 4.5;

const lightBlock = extractBlock(globalsCss, ":root {");
const darkBlock = extractBlock(globalsCss, ':root[data-theme="dark"] {');

// ---------------------------------------------------------------------------
// Feature: Responsive and accessible console
// ---------------------------------------------------------------------------

describe("Feature: Responsive and accessible console", () => {
  beforeEach(() => {
    mockedUsePathname.mockReturnValue("/dashboard");
    mockedUseRouter.mockReturnValue({ refresh: jest.fn() } as unknown as ReturnType<typeof useRouter>);
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  describe("Scenario: sidebar collapses below 1024px", () => {
    it("renders the sidebar as a drawer behind a hamburger under 1024px", async () => {
      const { Sidebar } = await import("../app/(authed)/_components/Sidebar");
      const { MobileNav } = await import("../app/(authed)/_components/MobileNav");

      const { container: sidebarContainer } = render(<Sidebar />);
      const aside = sidebarContainer.querySelector("aside");
      expect(aside).not.toBeNull();
      // Hidden by default, restored to a flex column only at the lg breakpoint
      // (persistent sidebar, SPEC F28.13) — MobileNav's drawer covers <1024px.
      expect(aside?.className).toMatch(/\bhidden\b/);
      expect(aside?.className).toMatch(/\blg:flex\b/);

      render(<MobileNav />);
      const trigger = screen.getByRole("button", { name: "Open navigation" });
      expect(trigger.className).toMatch(/\blg:hidden\b/);
    });

    it("traps focus while the drawer is open and restores it on close", async () => {
      const { MobileNav } = await import("../app/(authed)/_components/MobileNav");
      render(<MobileNav />);

      const trigger = screen.getByRole("button", { name: "Open navigation" });
      trigger.focus();
      fireEvent.click(trigger);

      const dialog = await screen.findByRole("dialog", { name: "Navigation" });
      expect(dialog).toContainElement(document.activeElement as HTMLElement);
      // The same section list as the persistent Sidebar (SPEC F28.13: the
      // drawer is the same nav, not a second one that can drift).
      for (const label of ["Dashboard", "Live", "Catalog", "Safe content", "Settings"]) {
        expect(screen.getByRole("link", { name: label })).toBeInTheDocument();
      }

      fireEvent.click(screen.getByRole("button", { name: "Close navigation" }));
      // Radix's FocusScope restores focus in a setTimeout(0) on unmount
      // (mirrors feedback-primitives.spec.tsx's confirm-dialog focus-trap spec).
      await act(async () => {
        await new Promise((resolve) => setTimeout(resolve, 0));
      });

      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      expect(document.activeElement).toBe(trigger);
    });
  });

  describe("Scenario: tables adapt", () => {
    it("scrolls wide tables inside their own container (page body never scrolls sideways)", async () => {
      await renderCatalogTable();
      const catalogTable = screen.getByRole("table");
      expect(catalogTable.parentElement?.className).toMatch(/\boverflow-x-auto\b/);
      catalogTable.closest("div")?.remove(); // isolate from the next render's DOM

      render(
        <PlayHistoryTable
          entries={makeHistoryEntries()}
          error={false}
          timeZone="UTC"
          ratings={new Map()}
          onRatingChange={() => {}}
        />
      );
      const playHistoryTable = screen.getByRole("table");
      expect(playHistoryTable.parentElement?.className).toMatch(/\boverflow-x-auto\b/);

      render(<RecentPlays entries={makeHistoryEntries()} error={false} timeZone="UTC" />);
      const recentPlaysTables = screen.getAllByRole("table");
      const recentPlaysTable = recentPlaysTables[recentPlaysTables.length - 1];
      expect(recentPlaysTable?.parentElement?.className).toMatch(/\boverflow-x-auto\b/);

      renderSafeContentClient();
      const safeTables = screen.getAllByRole("table");
      const safeTable = safeTables[safeTables.length - 1];
      expect(safeTable?.parentElement?.className).toMatch(/\boverflow-x-auto\b/);
    });
  });

  describe("Scenario: keyboard operability", () => {
    it("shows a visible focus state on every interactive element via the global focus-visible baseline", () => {
      const baseRule = extractBlock(
        globalsCss,
        "a:focus-visible,\n  button:focus-visible,\n  input:focus-visible,\n  select:focus-visible,\n  textarea:focus-visible {"
      );
      expect(baseRule).toMatch(/outline:\s*2px solid var\(--accent\)/);
      expect(baseRule).toMatch(/outline-offset:\s*2px/);
    });

    it("gives the tab-strip control (CatalogTabs) real <a> elements (covered by the global a:focus-visible baseline) and the hamburger its own explicit focus-visible treatment", async () => {
      render(<CatalogTabs activeTab="tracks" />);
      const tabLinks = screen.getAllByRole("link");
      expect(tabLinks.length).toBeGreaterThan(0);
      for (const link of tabLinks) {
        // "link" role in the accessibility tree is only ever produced by a
        // real <a> here — the global `a:focus-visible` rule (asserted above)
        // reaches it without a bespoke per-component utility class.
        expect(link.tagName).toBe("A");
      }

      // Buttons (the hamburger, unlike a tab-strip <a>) carry the same
      // explicit focus-visible utility class Button/ThemeToggle already use,
      // matching this codebase's existing convention for icon buttons.
      const { MobileNav } = await import("../app/(authed)/_components/MobileNav");
      render(<MobileNav />);
      expect(screen.getByRole("button", { name: "Open navigation" }).className).toMatch(/focus-visible:outline/);
    });
  });

  describe("Scenario: reduced motion honored", () => {
    it("disables the pulse dot and nonessential transitions under prefers-reduced-motion", () => {
      // The onair-dot's own dedicated gate.
      const onairReduced = extractBlock(globalsCss, "@media (prefers-reduced-motion: reduce) {\n  .onair-dot {");
      expect(onairReduced).toMatch(/animation:\s*none/);

      // The universal reset: every other transition/animation (drawer slide,
      // dialog fade, hover-color transitions, tab-strip underline, ...)
      // collapses under the same media query, in one place.
      const universalReduced = extractBlock(
        globalsCss,
        "@media (prefers-reduced-motion: reduce) {\n    *,\n    ::before,\n    ::after {"
      );
      expect(universalReduced).toMatch(/animation-duration:\s*0\.01ms\s*!important/);
      expect(universalReduced).toMatch(/transition-duration:\s*0\.01ms\s*!important/);
    });

    it("also gates the drawer's own slide/fade transitions explicitly (belt-and-suspenders with the global reset)", async () => {
      const { MobileNav } = await import("../app/(authed)/_components/MobileNav");
      render(<MobileNav />);
      fireEvent.click(screen.getByRole("button", { name: "Open navigation" }));
      const dialog = screen.getByRole("dialog");
      expect(dialog.className).toMatch(/motion-reduce:transition-none/);
    });
  });

  describe("Scenario: touch targets", () => {
    it("keeps interactive targets at 40px or larger at the 390px viewport via the global min-height baseline", () => {
      const baseRule = extractBlock(
        globalsCss,
        'input:not([type="checkbox"]):not([type="radio"]),\n  select,\n  textarea,\n  button {'
      );
      expect(baseRule).toMatch(/min-height:\s*2\.5rem/);
    });

    it("pads standalone checkbox/nav-link touch targets to min-h-10 (excluded from the global rule by design)", async () => {
      await renderCatalogTable();
      const selectAll = screen.getByRole("checkbox", { name: "Select all rows on this page" });
      expect(selectAll.closest("span")?.className).toMatch(/\bmin-h-10\b/);

      const { Sidebar } = await import("../app/(authed)/_components/Sidebar");
      const { container: sidebarContainer } = render(<Sidebar />);
      const navLink = sidebarContainer.querySelector("a");
      expect(navLink?.className).toMatch(/\bmin-h-10\b/);
    });
  });

  describe("Scenario: rejecting low contrast in either theme (sad path)", () => {
    it("meets WCAG AA on body text, muted text, and accent-on-surface in both token sets", () => {
      for (const block of [lightBlock, darkBlock]) {
        const bg = tokenValue(block, "--bg");
        const surface = tokenValue(block, "--surface");
        const ink = tokenValue(block, "--ink");
        const mute = tokenValue(block, "--mute");
        const accent = tokenValue(block, "--accent");

        expect(contrastRatio(ink, bg)).toBeGreaterThanOrEqual(AA_NORMAL_TEXT_MIN_CONTRAST);
        expect(contrastRatio(ink, surface)).toBeGreaterThanOrEqual(AA_NORMAL_TEXT_MIN_CONTRAST);
        expect(contrastRatio(mute, bg)).toBeGreaterThanOrEqual(AA_NORMAL_TEXT_MIN_CONTRAST);
        expect(contrastRatio(mute, surface)).toBeGreaterThanOrEqual(AA_NORMAL_TEXT_MIN_CONTRAST);
        expect(contrastRatio(accent, surface)).toBeGreaterThanOrEqual(AA_NORMAL_TEXT_MIN_CONTRAST);
      }
    });

    it("meets WCAG AA on the shipped badge combination (SourceChip: muted text, line border) in both token sets", () => {
      for (const block of [lightBlock, darkBlock]) {
        const surface = tokenValue(block, "--surface");
        const mute = tokenValue(block, "--mute");
        expect(contrastRatio(mute, surface)).toBeGreaterThanOrEqual(AA_NORMAL_TEXT_MIN_CONTRAST);
      }
    });

    // Q11 review: --accent-2 (brass) is small text everywhere it's used
    // (table headers, section micro-labels, the ApplyModeBadge
    // "engine-restart" variant) — it previously failed AA against every
    // light-theme ground (bg 3.69, surface 3.98, surface-2 3.37). Light was
    // darkened from #8A7B3F to #6F632F to clear all three; dark (#B3A25E)
    // already passed and was left unchanged. This asserts the pairing that
    // an earlier pass of this spec had to skip.
    it("meets WCAG AA on accent-2 (brass) text against every ground it's used on, in both token sets", () => {
      for (const block of [lightBlock, darkBlock]) {
        const bg = tokenValue(block, "--bg");
        const surface = tokenValue(block, "--surface");
        const surface2 = tokenValue(block, "--surface-2");
        const accent2 = tokenValue(block, "--accent-2");

        expect(contrastRatio(accent2, bg)).toBeGreaterThanOrEqual(AA_NORMAL_TEXT_MIN_CONTRAST);
        expect(contrastRatio(accent2, surface)).toBeGreaterThanOrEqual(AA_NORMAL_TEXT_MIN_CONTRAST);
        expect(contrastRatio(accent2, surface2)).toBeGreaterThanOrEqual(AA_NORMAL_TEXT_MIN_CONTRAST);
      }
    });
  });
});

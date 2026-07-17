// @jest-environment jsdom
// STORY-159 — Every icon explains itself (Epic Z / SPEC F62, closes gitea-#234).
//
// Runner: Jest (jsdom) + @testing-library/react. Implemented Z8 (2026-07-15), after Z7 (the
// Catalog-toolbar file-conflict edge; Z7's four new rank actions had to exist to be swept). The
// full inventory of icon-only interactive controls in the console, confirmed by grepping every
// `Icon` usage under app/ and components/ at plan time: `RatingControls` (vote up, vote down,
// never-play/restore — Live page rows + now-playing card), `NeverPlayControl` (never-play/restore
// — Catalog rows), `CatalogToolbar`'s four rank actions (Z7), `ThemeToggle` (light/dark), and
// `MobileNav`'s hamburger + drawer close. Every nav icon (Dashboard/Live/Catalog/…) always renders
// beside its own visible text label (Sidebar/MobileNav), so those are NOT icon-only and out of
// scope for this sweep, same as every other Button/`<button>` in the app that carries visible text
// (Reassign, Re-analyze, Set eligible, Columns, Search, Edit, Delete, …).
//
// The F62.3 parity guard is the F55.3 coverage discipline applied to icon-only controls:
// `assertNoUnlabeledIconOnlyButtons` is a GENERIC walker run against the actual rendered surface,
// not a hand-typed list of the five components above. A future icon-only control added to any
// swept component — or a new component added to the sweep — that forgets its aria-label fails the
// very next run of this spec.
//
// Widened (Z8 review Finding 2): the walker's coverage class was narrower than F62's own
// "icon-only interactive control" — it only inspected `<button>` elements carrying an svg. The
// catalog filter chips' clear link (page.tsx) is an `<a>` whose entire visible content is a text
// glyph (`×`), not an svg — invisible to the old walker regardless of whether it carried an
// aria-label. The walker now inspects `<button>` AND `<a>` elements, and treats a control as
// icon-only when it carries an svg with no visible text OR when its entire visible text is a
// single non-alphanumeric glyph (×, ✕, arrows, …).
//
// The mobile nav drawer's "Close navigation" trigger is exercised in its own isolated render
// rather than folded into the shared sweep surface: Radix's Dialog, while open, marks every DOM
// node OUTSIDE its portal as `aria-hidden="true"` (real modal semantics — background content
// should be invisible to assistive tech while a dialog is open), which would make every OTHER
// control's role-based tooltip query in the same tree fail for a reason that has nothing to do
// with F62 coverage. Testing the drawer's own trigger/close pair against its own render sidesteps
// that entirely while still exercising it through the exact same walker and assertions.

jest.mock("next/navigation", () => ({
  ...jest.requireActual("next/navigation"),
  usePathname: jest.fn(),
  useRouter: jest.fn(),
}));

// Server-component fixture (the CatalogPage full-page coverage scenario below) — mirrors
// catalog-facet-pickers.spec.tsx's mock.
jest.mock("next/headers", () => ({
  cookies: jest.fn().mockResolvedValue({
    toString: () => "session=test-cookie",
  }),
}));

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, fireEvent, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { ReactNode } from "react";
import type { usePathname, useRouter } from "next/navigation";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import type { AdminMediaDto, BulkFilter, Pagination } from "../app/(authed)/catalog/types";
import type { LibraryDto } from "../lib/library";

const mockedUsePathname = jest
  .requireMock<{ usePathname: typeof usePathname }>("next/navigation")
  .usePathname as jest.MockedFunction<typeof usePathname>;
const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;

// ---------------------------------------------------------------------------
// The generic walker — F62.3's coverage-class guard
// ---------------------------------------------------------------------------

/** True when `text` is exactly one character and that character isn't alphanumeric — the
 * glyph-icon shape (×, ✕, ←, →, …) F62's icon-only class covers alongside an svg icon (Z8 review
 * Finding 2). Ordinary short button copy ("OK", "1", "×2") never matches: either it's more than
 * one character, or its one character is alphanumeric. */
function isSingleGlyphText(text: string): boolean {
  return text.length === 1 && !/[a-zA-Z0-9]/.test(text);
}

/** A control whose only visible content is an icon: either svg content with no rendered text, or
 * a lone non-alphanumeric text glyph. Tag-agnostic — the same test applies to a `<button>` and an
 * `<a>` (SPEC F62's "icon-only interactive control" coverage class). */
function isIconOnlyControl(el: HTMLElement): boolean {
  const hasSvg = el.querySelector("svg") !== null;
  const visibleText = (el.textContent ?? "").trim();
  if (hasSvg && visibleText.length === 0) return true;
  return isSingleGlyphText(visibleText);
}

/**
 * Walks every icon-only interactive control — `<button>` and `<a>` elements (SPEC F62.3's
 * coverage class, widened at Z8 review Finding 2 beyond svg-only `<button>`s) — under `root` and
 * throws, naming every offender, the moment one is icon-only (per {@link isIconOnlyControl}) and
 * carries no aria-label. This IS the F62.3 guard: run against a real rendered surface, it makes an
 * unlabeled icon-only control structurally impossible to ship unnoticed — a future addition to any
 * swept surface fails here, whether it's an svg `<button>` or a glyph-text `<a>` like the catalog
 * filter chips' clear link.
 */
function assertNoUnlabeledIconOnlyButtons(root: ParentNode): void {
  const candidates = [
    ...Array.from(root.querySelectorAll<HTMLElement>("button")),
    ...Array.from(root.querySelectorAll<HTMLElement>("a")),
  ];
  const offenders = candidates
    .filter(isIconOnlyControl)
    .filter((el) => (el.getAttribute("aria-label") ?? "").trim().length === 0);

  if (offenders.length > 0) {
    throw new Error(
      `${offenders.length} icon-only control(s) rendered without an aria-label:\n` +
        offenders.map((b) => b.outerHTML).join("\n")
    );
  }
}

/** Reads the hover/focus tooltip copy that's a sibling of `trigger` inside its `Tooltip` wrapper,
 * once `trigger` carries keyboard focus — null when none is showing. */
function tooltipTextFor(trigger: HTMLElement): string | null {
  const tooltip = within(trigger.parentElement as HTMLElement).queryByRole("tooltip");
  return tooltip?.textContent ?? null;
}

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const LIBRARIES: LibraryDto[] = [{ id: 1, name: "In Rotation", mediaCount: 50 }];

const EMPTY_FILTER: BulkFilter = {
  state: null,
  artist: null,
  genre: null,
  libraryId: null,
  q: null,
  eligible: null,
};

function makeRow(overrides: Partial<AdminMediaDto> & { mediaId: string }): AdminMediaDto {
  return {
    locator: `/media/${overrides.mediaId}.flac`,
    format: "flac",
    state: "ready",
    durationMs: 180000,
    title: `Track ${overrides.mediaId}`,
    artist: "Test Artist",
    album: "Test Album",
    genre: "Rock",
    year: 2024,
    bpm: null,
    trackEnergy: null,
    integratedLufs: -14,
    truePeakDbtp: -1,
    measurable: true,
    cueInSec: null,
    cueOutSec: null,
    eligible: true,
    version: "900",
    score: 50,
    neverPlay: false,
    ...overrides,
  };
}

function makePagination(overrides: Partial<Pagination> = {}): Pagination {
  return { total: 1, pages: 1, page: 1, limit: 50, ...overrides };
}

/**
 * Fetch mock for a full CatalogPage render (Z8 review Finding 2's chip-row coverage scenario) —
 * CatalogPage's own two server-side GETs (`/api/libraries`, `/api/media`) plus every
 * FacetFilterControl's on-mount `/api/media/facets` fetch (three instances: artist/album/genre).
 * Facets resolve to an empty list — irrelevant here, no icon-only control lives in
 * FacetFilterControl.
 */
function makeCatalogPageFetchMock(): jest.MockedFunction<typeof fetch> {
  const fn = jest.fn<typeof fetch>().mockImplementation(async (input) => {
    const url = String(input);
    if (url.includes("/api/libraries")) {
      return { ok: true, status: 200, json: async () => LIBRARIES, headers: new Headers() } as unknown as Response;
    }
    if (url.includes("/api/media/facets")) {
      return { ok: true, status: 200, json: async () => [], headers: new Headers() } as unknown as Response;
    }
    return {
      ok: true,
      status: 200,
      json: async () => [makeRow({ mediaId: "1" })],
      headers: new Headers({ "x-pagination": "total=1,pages=1,page=1,limit=50" }),
    } as unknown as Response;
  });
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

/** jsdom has no real `matchMedia` — `ThemeToggle` calls it to resolve the system default (the
 * app-shell.spec.tsx precedent). Pins it to "prefers light" so the toggle's initial render is
 * deterministic across runs. */
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

/**
 * Renders every component known to hold an icon-only control, in one tree, and puts each into the
 * state that surfaces its icon-only button(s): both `RatingControls`/`NeverPlayControl` never-play
 * states (X and restore-arrow glyphs) and the Catalog bulk toolbar in selection mode (so its four
 * rank actions render — SPEC F61.4/Z7). The mobile nav drawer's own trigger renders here too
 * (always visible); its "Close navigation" trigger — only present once the drawer is OPEN — is
 * covered separately (see file header comment on Radix's modal aria-hiding).
 *
 * Total icon-only buttons this surface puts on screen: 15 — RatingControls×2 renders (3 each:
 * vote up, vote down, never-play/restore) + NeverPlayControl×2 renders (1 each) + ThemeToggle (1)
 * + MobileNav's hamburger (1) + the Catalog row's own NeverPlayControl (1) + CatalogToolbar's four
 * rank actions (4).
 */
async function renderSweepSurface(): Promise<HTMLElement> {
  const { RatingControls } = await import("../app/(authed)/_components/RatingControls");
  const { NeverPlayControl } = await import("../app/(authed)/catalog/NeverPlayControl");
  const { ThemeToggle } = await import("../app/(authed)/_components/ThemeToggle");
  const { MobileNav } = await import("../app/(authed)/_components/MobileNav");
  const { CatalogTable } = await import("../app/(authed)/catalog/CatalogTable");

  const media = [makeRow({ mediaId: "1" })];
  const utils = render(
    <ConfirmDialogProvider>
      <RatingControls mediaId="10" value={{ score: 50, neverPlay: false }} onChange={() => {}} />
      <RatingControls mediaId="11" value={{ score: 50, neverPlay: true }} onChange={() => {}} />
      <NeverPlayControl mediaId="12" neverPlay={false} onChange={() => {}} />
      <NeverPlayControl mediaId="13" neverPlay={true} onChange={() => {}} />
      <ThemeToggle />
      <MobileNav />
      <CatalogTable
        media={media}
        pagination={makePagination({ total: media.length })}
        libraries={LIBRARIES}
        bulkFilter={EMPTY_FILTER}
        filterActive={false}
        clearFiltersHref="/catalog"
      />
      <Toaster />
    </ConfirmDialogProvider>
  );

  // Bring the Catalog bulk toolbar (and Z7's four icon-only rank actions) into the DOM — hidden
  // until a selection or an active bulk filter exists (F28.11).
  fireEvent.click(screen.getByRole("checkbox", { name: "Select all rows on this page" }));

  return utils.container;
}

// ---------------------------------------------------------------------------
// Feature: Every icon-only control explains itself
// ---------------------------------------------------------------------------

describe("Feature: Every icon-only control explains itself", () => {
  beforeEach(() => {
    mockedUsePathname.mockReturnValue("/catalog");
    mockedUseRouter.mockReturnValue({ refresh: jest.fn() } as unknown as ReturnType<typeof useRouter>);
    mockMatchMedia(false); // system prefers light — deterministic ThemeToggle initial render
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  describe("Scenario: hover and focus both reveal the tooltip", () => {
    it("hovering an icon-only control renders its Wireless-tokened tooltip (F62.1, F62.2)", async () => {
      const { RatingControls } = await import("../app/(authed)/_components/RatingControls");
      render(<RatingControls mediaId="1" value={{ score: 50, neverPlay: false }} onChange={() => {}} />);

      const voteUp = screen.getByRole("button", { name: "Vote up" });
      expect(screen.queryByRole("tooltip")).not.toBeInTheDocument();

      fireEvent.mouseEnter(voteUp);
      const tooltip = screen.getByRole("tooltip", { name: "Vote up" });
      // Wireless semantic tokens only — no raw hex, no Tailwind stock palette class (design-aesthetic).
      expect(tooltip.className).toMatch(/\bbg-surface\b/);
      expect(tooltip.className).toMatch(/\bborder-line\b/);
      expect(tooltip.className).toMatch(/\btext-ink\b/);
      expect(tooltip.className).not.toMatch(/#[0-9a-fA-F]{3,6}/);

      fireEvent.mouseLeave(voteUp);
      expect(screen.queryByRole("tooltip")).not.toBeInTheDocument();
    });

    it("keyboard focus renders the same tooltip — no pointer required (F62.2)", async () => {
      const { NeverPlayControl } = await import("../app/(authed)/catalog/NeverPlayControl");
      render(<NeverPlayControl mediaId="1" neverPlay={false} onChange={() => {}} />);

      const control = screen.getByRole("button", { name: "Never play" });
      expect(screen.queryByRole("tooltip")).not.toBeInTheDocument();

      fireEvent.focus(control);
      expect(screen.getByRole("tooltip", { name: "Never play" })).toBeInTheDocument();

      fireEvent.blur(control);
      expect(screen.queryByRole("tooltip")).not.toBeInTheDocument();
    });

    it("the mobile nav drawer's icon-only triggers reveal on hover/focus too — including the drawer-only close trigger (F62.1, F62.2)", async () => {
      const { MobileNav } = await import("../app/(authed)/_components/MobileNav");
      render(<MobileNav />);

      const openTrigger = screen.getByRole("button", { name: "Open navigation" });
      fireEvent.focus(openTrigger);
      expect(tooltipTextFor(openTrigger)).toBe("Open navigation");
      fireEvent.blur(openTrigger);

      fireEvent.click(openTrigger);
      await screen.findByRole("dialog", { name: "Navigation" });

      const closeTrigger = screen.getByRole("button", { name: "Close navigation" });
      fireEvent.focus(closeTrigger);
      expect(tooltipTextFor(closeTrigger)).toBe("Close navigation");
      fireEvent.blur(closeTrigger);
    });
  });

  describe("Scenario: accessible label parity", () => {
    it("every icon-only control carries an aria-label with the same copy the tooltip shows (F62.1)", async () => {
      const container = await renderSweepSurface();

      const iconOnlyButtons = Array.from(container.querySelectorAll("button")).filter(isIconOnlyControl);
      // Sanity: the sweep surface actually put every known icon-only control (except the
      // drawer-only close trigger, covered separately above) on screen — see renderSweepSurface's
      // own doc comment for the itemized count.
      expect(iconOnlyButtons).toHaveLength(15);

      for (const button of iconOnlyButtons) {
        const label = button.getAttribute("aria-label");
        expect(label).not.toBeNull();
        expect((label ?? "").trim().length).toBeGreaterThan(0);

        fireEvent.focus(button);
        expect(tooltipTextFor(button)).toBe(label);
        fireEvent.blur(button);
      }
    });

    it("the named gitea-#234 controls are covered: vote ±, never-play X, restore (F62.1)", async () => {
      const container = await renderSweepSurface();
      const buttonNames = Array.from(container.querySelectorAll("button")).map((b) => b.getAttribute("aria-label"));

      for (const name of ["Vote up", "Vote down", "Never play", "Restore to rotation"]) {
        expect(buttonNames).toContain(name);
      }
    });
  });

  describe("Scenario: the coverage class is guarded", () => {
    it("a rendered icon-only button without an aria-label fails the parity sweep (F62.3)", () => {
      // Minimal offending harness — a hand-rolled icon-only button that skips the aria-label the
      // whole sweep exists to require. Proves assertNoUnlabeledIconOnlyButtons (the same walker
      // every other spec in this file runs against every real icon-only control) actually rejects
      // the violation it exists to catch, so a future icon-only button added to ANY swept surface
      // without a matching aria-label fails this exact guard.
      function UnlabeledIconButton(): ReactNode {
        return (
          <button type="button">
            <svg viewBox="0 0 16 16" aria-hidden="true">
              <circle cx="8" cy="8" r="4" />
            </svg>
          </button>
        );
      }

      const { container } = render(<UnlabeledIconButton />);
      expect(() => assertNoUnlabeledIconOnlyButtons(container)).toThrow(/icon-only control/);
    });

    it("a rendered icon-only glyph anchor without an aria-label fails the parity sweep too (F62.3, Z8 review Finding 2)", () => {
      // Same proof as the svg-button harness above, but shaped like the actual miss the widened
      // walker exists to catch: a plain-text glyph inside an `<a>`, not an svg inside a `<button>`
      // — the catalog filter chips' clear link's shape before the Finding-1 Tooltip fix. The old,
      // button-only/svg-only walker was structurally blind to this offender regardless of whether
      // it carried an aria-label; the widened one must reject it.
      function UnlabeledGlyphLink(): ReactNode {
        return (
          <a href="/somewhere" className="hover:text-ink">
            ×
          </a>
        );
      }

      const { container } = render(<UnlabeledGlyphLink />);
      expect(() => assertNoUnlabeledIconOnlyButtons(container)).toThrow(/icon-only control/);
    });

    it("the real console surface passes the same guard the offending harness fails (F62.3)", async () => {
      const container = await renderSweepSurface();
      expect(() => assertNoUnlabeledIconOnlyButtons(container)).not.toThrow();
    });

    it("the Catalog page's filter-chip clear link — a glyph-only <a>, not a button — passes the widened guard now that it carries the Tooltip fix (F62.1–F62.3, Z8 review Finding 1+2)", async () => {
      const originalFetch = global.fetch;
      try {
        makeCatalogPageFetchMock();
        const { default: CatalogPage } = await import("../app/(authed)/catalog/page");
        const node = await CatalogPage({ searchParams: Promise.resolve({ "artist-exact": "Queen" }) });

        const { container } = render(<ConfirmDialogProvider>{node}</ConfirmDialogProvider>);

        // Sanity: the widened walker's actual target — the chip's glyph-only clear link — is
        // really on screen, not an empty/errored catalog page.
        const clearLink = await screen.findByRole("link", { name: "Clear Artist: Queen filter" });
        expect(clearLink.textContent?.trim()).toBe("×");

        // Finding 1: the tooltip now carries the same copy as the aria-label, same as every other
        // swept icon-only control.
        fireEvent.focus(clearLink);
        expect(tooltipTextFor(clearLink)).toBe("Clear Artist: Queen filter");
        fireEvent.blur(clearLink);

        // Finding 2: the widened walker now actually inspects this <a> (invisible to the old,
        // button-only walker) and finds it properly labeled.
        expect(() => assertNoUnlabeledIconOnlyButtons(container)).not.toThrow();
      } finally {
        global.fetch = originalFetch;
      }
    });

    it("the drawer's own icon-only triggers pass the same guard once the dialog is open (F62.3)", async () => {
      const { MobileNav } = await import("../app/(authed)/_components/MobileNav");
      render(<MobileNav />);

      fireEvent.click(screen.getByRole("button", { name: "Open navigation" }));
      await screen.findByRole("dialog", { name: "Navigation" });

      // document.body, not the RTL container: Radix's Dialog portals its content (the close
      // trigger) to document.body rather than the local render root.
      expect(() => assertNoUnlabeledIconOnlyButtons(document.body)).not.toThrow();
    });
  });
});

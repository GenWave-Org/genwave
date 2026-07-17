// @jest-environment jsdom
// STORY-145 — The catalog shows and filters the new signals (Epic X / SPEC F49.3–F49.4,
// closes gitea-#190, gitea-#208) — UI half. The API half lives in
// Host.Tests/Specs/Story145_YearDecadeFiltersAndSignalDto.cs.
//
// Runner: Jest (jsdom) + @testing-library/react for the column-visibility/cell-format scenarios
// (mirrors catalog-selection-toolbar.spec.tsx's harness — CatalogTable mounted directly, no
// ConfirmDialogProvider/Toaster needed here since the bulk toolbar never renders: selection stays
// empty and `filterActive` stays false in every fixture below). The decade/year-missing
// browse-wiring scenarios call the CatalogPage server component directly and tree-walk its
// returned element — the same technique as catalog-f3-filter.spec.ts/catalog-pages.spec.ts (plain
// object introspection, no DOM involved, so it works fine under this file's jsdom environment
// too).

jest.mock("next/navigation", () => ({
  ...jest.requireActual("next/navigation"),
  useRouter: jest.fn(),
}));

jest.mock("next/headers", () => ({
  cookies: jest.fn().mockResolvedValue({
    toString: () => "session=test-cookie",
  }),
}));

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, fireEvent, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { ReactNode } from "react";
import type { useRouter } from "next/navigation";
import type { LibraryDto } from "@/lib/library";
import { CATALOG_COLUMN_VISIBILITY_STORAGE_KEY } from "../app/(authed)/catalog/columnVisibility";
import type { AdminMediaDto, BulkFilter, Pagination } from "../app/(authed)/catalog/types";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;

// ---------------------------------------------------------------------------
// Tree walker (copied from catalog-f3-filter.spec.ts's pattern) — used only by the
// decade/year-missing browse-wiring scenarios.
// ---------------------------------------------------------------------------

function isLeaf(node: ReactNode): boolean {
  return node === null || node === undefined || typeof node === "boolean" || typeof node === "string" || typeof node === "number";
}

function findByAriaLabel(node: ReactNode, label: string): { props: Record<string, unknown> } | null {
  if (isLeaf(node)) return null;
  if (Array.isArray(node)) {
    for (const child of node) {
      const found = findByAriaLabel(child, label);
      if (found !== null) return found;
    }
    return null;
  }
  const el = node as { props?: Record<string, unknown> };
  if (el && typeof el === "object" && el.props) {
    if (el.props["aria-label"] === label) return el as { props: Record<string, unknown> };
    if (el.props["children"] !== undefined) return findByAriaLabel(el.props["children"] as ReactNode, label);
  }
  return null;
}

function makeFetchMock(
  body: unknown,
  status = 200,
  extraHeaders: Record<string, string> = {}
): jest.MockedFunction<typeof fetch> {
  const headers = new Headers({ "content-type": "application/json", ...extraHeaders });
  const fn = jest
    .fn<typeof fetch>()
    .mockResolvedValue({
      ok: status >= 200 && status < 300,
      status,
      json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
      headers,
    } as unknown as Response);
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

function capturedUrls(mockFetch: jest.MockedFunction<typeof fetch>): string[] {
  return mockFetch.mock.calls.map(([url]) => String(url));
}

// ---------------------------------------------------------------------------
// CatalogTable fixtures (mirrors catalog-selection-toolbar.spec.tsx's harness idiom)
// ---------------------------------------------------------------------------

const LIBRARIES: LibraryDto[] = [];

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
    year: 1975,
    bpm: 128,
    trackEnergy: 0.7,
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

async function renderCatalogTable(media: AdminMediaDto[] = [makeRow({ mediaId: "1" })]) {
  const { CatalogTable } = await import("../app/(authed)/catalog/CatalogTable");
  return render(
    <CatalogTable
      media={media}
      pagination={makePagination({ total: media.length })}
      libraries={LIBRARIES}
      bulkFilter={EMPTY_FILTER}
      filterActive={false}
      clearFiltersHref="/catalog"
    />
  );
}

function openColumnsPanel(): void {
  fireEvent.click(screen.getByRole("button", { name: "Columns" }));
}

// ---------------------------------------------------------------------------
// Feature: Year, BPM, and energy in the catalog — on demand
// ---------------------------------------------------------------------------

describe("Feature: Year, BPM, and energy in the catalog — on demand", () => {
  beforeEach(() => {
    window.localStorage.clear();
    mockedUseRouter.mockReturnValue({ refresh: jest.fn() } as unknown as ReturnType<typeof useRouter>);
  });

  // -------------------------------------------------------------------------
  describe("Scenario: columns toggle and persist", () => {
    it("hides Year, BPM, and Energy columns by default — the gitea-#190 'off by default' (F49.3)", async () => {
      await renderCatalogTable();

      expect(screen.queryByRole("columnheader", { name: "Year" })).not.toBeInTheDocument();
      expect(screen.queryByRole("columnheader", { name: "BPM" })).not.toBeInTheDocument();
      expect(screen.queryByRole("columnheader", { name: "Energy" })).not.toBeInTheDocument();
      expect(screen.queryByText("1975")).not.toBeInTheDocument();
    });

    it("shows a toggled-on column immediately (F49.3)", async () => {
      await renderCatalogTable();

      openColumnsPanel();
      fireEvent.click(screen.getByRole("checkbox", { name: "Year" }));

      expect(screen.getByRole("columnheader", { name: "Year" })).toBeInTheDocument();
      expect(screen.getByText("1975")).toBeInTheDocument();
    });

    it("persists the chosen column set to localStorage and restores it on reload (F49.3)", async () => {
      const { unmount } = await renderCatalogTable();

      openColumnsPanel();
      fireEvent.click(screen.getByRole("checkbox", { name: "BPM" }));

      expect(window.localStorage.getItem(CATALOG_COLUMN_VISIBILITY_STORAGE_KEY)).toBe(
        JSON.stringify(["bpm"])
      );
      unmount();

      // Simulates a reload: a fresh CatalogTable instance reading the same localStorage key,
      // with no toggle interaction of its own.
      await renderCatalogTable();
      expect(screen.getByRole("columnheader", { name: "BPM" })).toBeInTheDocument();
    });

    it("leaves the existing columns untouched and always visible (F49.3)", async () => {
      await renderCatalogTable();

      for (const label of ["Title", "Artist", "Genre", "State", "Eligible", "Score", "Rating", "Duration"]) {
        expect(screen.getByRole("columnheader", { name: label })).toBeInTheDocument();
      }

      openColumnsPanel();
      // Only the three optional signals are offered — no existing column is toggleable this phase.
      const panel = screen.getByRole("group", { name: "Toggle columns" });
      expect(within(panel).getAllByRole("checkbox")).toHaveLength(3);
    });

    it("closes the Columns panel on Escape", async () => {
      await renderCatalogTable();

      openColumnsPanel();
      expect(screen.getByRole("group", { name: "Toggle columns" })).toBeInTheDocument();

      fireEvent.keyDown(document, { key: "Escape" });

      expect(screen.queryByRole("group", { name: "Toggle columns" })).not.toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: the new cells format their signals", () => {
    it("renders year plain, bpm to one decimal, and energy to two decimals (F49.2)", async () => {
      await renderCatalogTable([makeRow({ mediaId: "1", year: 1975, bpm: 128, trackEnergy: 0.7 })]);

      openColumnsPanel();
      fireEvent.click(screen.getByRole("checkbox", { name: "Year" }));
      fireEvent.click(screen.getByRole("checkbox", { name: "BPM" }));
      fireEvent.click(screen.getByRole("checkbox", { name: "Energy" }));

      expect(screen.getByText("1975")).toBeInTheDocument();
      expect(screen.getByText("128.0")).toBeInTheDocument();
      expect(screen.getByText("0.70")).toBeInTheDocument();
    });

    it("renders an em-dash for null bpm/energy/year — never a zero (F49.2)", async () => {
      await renderCatalogTable([makeRow({ mediaId: "1", year: null, bpm: null, trackEnergy: null })]);

      openColumnsPanel();
      fireEvent.click(screen.getByRole("checkbox", { name: "Year" }));
      fireEvent.click(screen.getByRole("checkbox", { name: "BPM" }));
      fireEvent.click(screen.getByRole("checkbox", { name: "Energy" }));

      expect(screen.queryByText("0")).not.toBeInTheDocument();
      expect(screen.getAllByText("—")).toHaveLength(3);
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: decade and year filters drive the browse", () => {
    let originalFetch: typeof fetch;

    beforeEach(() => {
      originalFetch = global.fetch;
      jest.resetModules();
    });

    afterEach(() => {
      global.fetch = originalFetch;
      jest.clearAllMocks();
    });

    it("wires the decade control to ?decade= in the existing filter-chip style (F49.1, F49.4)", async () => {
      const mockFetch = makeFetchMock([], 200, { "x-pagination": "total=0,pages=1,page=1,limit=50" });

      const { default: CatalogPage } = await import("../app/(authed)/catalog/page");
      const node = await CatalogPage({ searchParams: Promise.resolve({ decade: "1970" }) });

      expect(capturedUrls(mockFetch).some((u) => u.includes("decade=1970"))).toBe(true);
      expect(findByAriaLabel(node, "Clear 1970s filter")).not.toBeNull();
    });

    it("wires year-missing to ?year-missing=true so unfilled rows are findable (F49.1)", async () => {
      const mockFetch = makeFetchMock([], 200, { "x-pagination": "total=0,pages=1,page=1,limit=50" });

      const { default: CatalogPage } = await import("../app/(authed)/catalog/page");
      const node = await CatalogPage({ searchParams: Promise.resolve({ "year-missing": "true" }) });

      expect(capturedUrls(mockFetch).some((u) => u.includes("year-missing=true"))).toBe(true);
      expect(findByAriaLabel(node, "Clear Missing year filter")).not.toBeNull();
    });

    it("clears the chip and the query param together (F49.4)", async () => {
      makeFetchMock([], 200, { "x-pagination": "total=0,pages=1,page=1,limit=50" });

      const { default: CatalogPage } = await import("../app/(authed)/catalog/page");
      const node = await CatalogPage({
        searchParams: Promise.resolve({ decade: "1970", genre: "Rock" }),
      });

      const clearLink = findByAriaLabel(node, "Clear 1970s filter");
      expect(clearLink).not.toBeNull();
      const href = clearLink?.props["href"] as string;
      expect(href).not.toContain("decade=");
      expect(href).toContain("genre=Rock");
    });
  });
});

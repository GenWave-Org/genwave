// StoryF3 — Admin UI: catalog page ?eligible= filter rendering
//
// Runner: Jest (node environment — .ts extension). Server component called directly as an async
// function. Mirrors the existing catalog-pages.spec.ts style.
//
// Q7 (SPEC F28.11) moved row rendering (eligibility Yes/No badges, aria-labels) into
// CatalogTable, a "use client" component this tree-walker never deep-renders — those
// assertions now check the `media`/`eligible` values handed to CatalogTable as props instead of
// rendered text; the actual badge rendering is covered by RTL in
// catalog-selection-toolbar.spec.tsx. The retired BulkEligibilityControl presence-check is
// replaced by asserting CatalogTable receives the page's pagination/filter (the toolbar's data).

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import type { ReactNode } from "react";

/** Finds the first element in the tree whose function component reference is `type`. */
function findElementByType(
  node: ReactNode,
  type: unknown
): { type: unknown; props: Record<string, unknown> } | null {
  if (node === null || node === undefined || typeof node === "boolean" || typeof node === "string" || typeof node === "number") {
    return null;
  }
  if (Array.isArray(node)) {
    for (const child of node) {
      const found = findElementByType(child, type);
      if (found !== null) return found;
    }
    return null;
  }
  const el = node as { type?: unknown; props?: Record<string, unknown> };
  if (el && typeof el === "object" && el.props !== undefined) {
    if (el.type === type) {
      return el as { type: unknown; props: Record<string, unknown> };
    }
    if (el.props["children"] !== undefined) {
      return findElementByType(el.props["children"] as ReactNode, type);
    }
  }
  return null;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

interface AdminMediaDto {
  mediaId: string;
  locator: string;
  format: string;
  state: string;
  durationMs: number | null;
  title: string | null;
  artist: string | null;
  album: string | null;
  genre: string | null;
  year: number | null;
  integratedLufs: number | null;
  truePeakDbtp: number | null;
  measurable: boolean | null;
  cueInSec: number | null;
  cueOutSec: number | null;
  eligible: boolean;
}

function makeMediaDto(overrides: Partial<AdminMediaDto> & { mediaId: string }): AdminMediaDto {
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
    integratedLufs: -14.0,
    truePeakDbtp: -1.0,
    measurable: true,
    cueInSec: null,
    cueOutSec: null,
    eligible: true,
    ...overrides,
  };
}

function makeFetchMock(
  body: unknown,
  status = 200,
  extraHeaders: Record<string, string> = {}
): jest.MockedFunction<typeof fetch> {
  const headers = new Headers({
    "content-type": "application/json",
    ...extraHeaders,
  });
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

jest.mock("next/headers", () => ({
  cookies: jest.fn<() => Promise<{ toString: () => string }>>().mockResolvedValue({
    toString: () => "session=test-cookie",
  }),
}));

// ---------------------------------------------------------------------------
// Feature: catalog page eligibility filter (F3)
// ---------------------------------------------------------------------------

describe("Feature: catalog page eligibility filter (F3)", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
    jest.resetModules();
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  // -------------------------------------------------------------------------
  describe("Scenario: catalog page passes ?eligible= to the backend", () => {
    it("includes eligible=true in the backend URL when the filter is set to true", async () => {
      const rows: AdminMediaDto[] = [makeMediaDto({ mediaId: "1" })];
      const mockFetch = makeFetchMock(rows, 200, {
        "x-pagination": "total=1,pages=1,page=1,limit=50",
      });

      const { default: CatalogPage } = await import("../app/(authed)/catalog/page");
      await CatalogPage({
        searchParams: Promise.resolve({ eligible: "true" }),
      });

      const urls = capturedUrls(mockFetch);
      expect(urls.some((u) => u.includes("eligible=true"))).toBe(true);
    });

    it("includes eligible=false in the backend URL when the filter is set to false", async () => {
      const rows: AdminMediaDto[] = [makeMediaDto({ mediaId: "2", eligible: false })];
      const mockFetch = makeFetchMock(rows, 200, {
        "x-pagination": "total=1,pages=1,page=1,limit=50",
      });

      const { default: CatalogPage } = await import("../app/(authed)/catalog/page");
      await CatalogPage({
        searchParams: Promise.resolve({ eligible: "false" }),
      });

      const urls = capturedUrls(mockFetch);
      expect(urls.some((u) => u.includes("eligible=false"))).toBe(true);
    });

    it("omits eligible from the backend URL when no filter is set", async () => {
      const rows: AdminMediaDto[] = [makeMediaDto({ mediaId: "3" })];
      const mockFetch = makeFetchMock(rows, 200, {
        "x-pagination": "total=1,pages=1,page=1,limit=50",
      });

      const { default: CatalogPage } = await import("../app/(authed)/catalog/page");
      await CatalogPage({
        searchParams: Promise.resolve({}),
      });

      const urls = capturedUrls(mockFetch);
      expect(urls.some((u) => u.includes("eligible="))).toBe(false);
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: eligibility is shown in the catalog table", () => {
    it("hands CatalogTable an eligible:true row", async () => {
      const rows: AdminMediaDto[] = [makeMediaDto({ mediaId: "4", eligible: true })];
      makeFetchMock(rows, 200, {
        "x-pagination": "total=1,pages=1,page=1,limit=50",
      });

      const { default: CatalogPage } = await import("../app/(authed)/catalog/page");
      const { CatalogTable } = await import("../app/(authed)/catalog/CatalogTable");
      const node = await CatalogPage({
        searchParams: Promise.resolve({}),
      });

      const tableEl = findElementByType(node, CatalogTable);
      const media = tableEl?.props["media"] as AdminMediaDto[];
      expect(media?.[0]?.eligible).toBe(true);
    });

    it("hands CatalogTable an eligible:false row", async () => {
      const rows: AdminMediaDto[] = [makeMediaDto({ mediaId: "5", eligible: false })];
      makeFetchMock(rows, 200, {
        "x-pagination": "total=1,pages=1,page=1,limit=50",
      });

      const { default: CatalogPage } = await import("../app/(authed)/catalog/page");
      const { CatalogTable } = await import("../app/(authed)/catalog/CatalogTable");
      const node = await CatalogPage({
        searchParams: Promise.resolve({}),
      });

      const tableEl = findElementByType(node, CatalogTable);
      const media = tableEl?.props["media"] as AdminMediaDto[];
      expect(media?.[0]?.eligible).toBe(false);
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: the catalog table receives the data the bulk toolbar needs", () => {
    it("the catalog page tree contains a CatalogTable element carrying pagination + bulkFilter (the toolbar's contextual-mode inputs, F28.11)", async () => {
      const rows: AdminMediaDto[] = [makeMediaDto({ mediaId: "6" })];
      makeFetchMock(rows, 200, {
        "x-pagination": "total=1,pages=1,page=1,limit=50",
      });

      const { default: CatalogPage } = await import("../app/(authed)/catalog/page");
      const { CatalogTable } = await import("../app/(authed)/catalog/CatalogTable");
      const node = await CatalogPage({
        searchParams: Promise.resolve({}),
      });

      const tableEl = findElementByType(node, CatalogTable);
      expect(tableEl).not.toBeNull();
      expect((tableEl?.props["pagination"] as { total: number }).total).toBe(1);
      expect(tableEl?.props["bulkFilter"]).toBeDefined();
    });
  });
});

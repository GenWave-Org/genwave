// STORY-044 / STORY-089 — Next.js: catalog + libraries pages (single-station, flat routes)
//
// Runner: Jest (node environment — .ts extension). Server components are called
// directly as async functions; their JSX output is inspected via a custom
// recursive tree-walker (no DOM, no renderToStaticMarkup) that collects all
// string leaves and href props. Fetch is mocked via global to verify that the
// correct backend URLs are used. The single-station deployment has no station
// list and no X-Station-Id header — scope is resolved server-side.
//
// Q7 (SPEC F28.11) moved row rendering (titles, per-row `/catalog/<id>` links, eligibility
// badges, checkboxes) into CatalogTable, a "use client" component — this tree-walker calls the
// server component as a plain function and never deep-renders client children, so those
// assertions moved to catalog-selection-toolbar.spec.tsx (jsdom + RTL, which actually renders
// CatalogTable). What stays testable here: the backend URLs fetched, and which data CatalogTable
// receives as props. `/libraries` no longer renders a page at all — it's a Q7 redirect — so its
// scenario is replaced with a redirect assertion.

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import type { ReactNode } from "react";

// ---------------------------------------------------------------------------
// Tree walker: collect all string leaves and link hrefs from a React element
// tree returned by an async server component.
// ---------------------------------------------------------------------------

function collectStrings(node: ReactNode, out: string[] = []): string[] {
  if (node === null || node === undefined || typeof node === "boolean") {
    return out;
  }
  if (typeof node === "string" || typeof node === "number") {
    out.push(String(node));
    return out;
  }
  if (Array.isArray(node)) {
    for (const child of node) collectStrings(child, out);
    return out;
  }
  // React element: { type, props, ... }
  const el = node as { type?: unknown; props?: Record<string, unknown> };
  if (el && typeof el === "object" && el.props) {
    // Capture href attributes from link elements
    if (typeof el.props["href"] === "string") {
      out.push(el.props["href"] as string);
    }
    if (el.props["children"] !== undefined) {
      collectStrings(el.props["children"] as ReactNode, out);
    }
  }
  return out;
}

function treeContains(node: ReactNode, text: string): boolean {
  return collectStrings(node).some((s) => s.includes(text));
}

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

/** Collects every element in the tree whose plain-string `type` (e.g. "td") matches, in document order. */
function collectElementsByType(
  node: ReactNode,
  type: string,
  out: Array<{ props: Record<string, unknown> }> = []
): Array<{ props: Record<string, unknown> }> {
  if (node === null || node === undefined || typeof node === "boolean" || typeof node === "string" || typeof node === "number") {
    return out;
  }
  if (Array.isArray(node)) {
    for (const child of node) collectElementsByType(child, type, out);
    return out;
  }
  const el = node as { type?: unknown; props?: Record<string, unknown> };
  if (el && typeof el === "object" && el.props !== undefined) {
    if (el.type === type) {
      out.push({ props: el.props });
    }
    if (el.props["children"] !== undefined) {
      collectElementsByType(el.props["children"] as ReactNode, type, out);
    }
  }
  return out;
}

/** True when an element's own rendered text (via collectStrings on its children) equals `text` exactly. */
function elementTextEquals(el: { props: Record<string, unknown> }, text: string): boolean {
  return collectStrings(el.props["children"] as ReactNode).join("") === text;
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

// ---------------------------------------------------------------------------
// Mock next/headers so cookies() resolves without the Next.js request context
// ---------------------------------------------------------------------------

jest.mock("next/headers", () => ({
  cookies: jest.fn().mockResolvedValue({
    toString: () => "session=test-cookie",
  }),
}));

// ---------------------------------------------------------------------------
// Feature: Catalog browse pages (libraries + media)
// ---------------------------------------------------------------------------

describe("Feature: Catalog browse pages (libraries + media)", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
    jest.resetModules();
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: /libraries redirects into the Catalog page's Libraries tab", () => {
    it("throws a NEXT_REDIRECT to /catalog?tab=libraries (SPEC F28.11, STORY-089 AC4)", async () => {
      const { default: LibrariesPage } = await import("../app/(authed)/libraries/page");

      let digest: string | undefined;
      try {
        LibrariesPage();
        throw new Error("expected LibrariesPage() to redirect");
      } catch (err) {
        digest = (err as { digest?: string }).digest;
      }

      expect(digest).toBeDefined();
      expect(digest).toContain("NEXT_REDIRECT");
      expect(digest).toContain("/catalog?tab=libraries");
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: /catalog renders paged media", () => {
    it("page renders 50 rows by default, handed to CatalogTable as props", async () => {
      const rows: AdminMediaDto[] = Array.from({ length: 50 }, (_, i) =>
        makeMediaDto({ mediaId: String(i + 1) })
      );
      const mockFetch = makeFetchMock(rows, 200, {
        "x-pagination": "total=200,pages=4,page=1,limit=50",
      });

      const { default: CatalogPage } = await import("../app/(authed)/catalog/page");
      const { CatalogTable } = await import("../app/(authed)/catalog/CatalogTable");
      const node = await CatalogPage({
        searchParams: Promise.resolve({}),
      });

      // Correct backend URL (no X-Station-Id — single-station scope is server-side)
      const urls = capturedUrls(mockFetch);
      expect(urls.some((u) => u.includes("/api/media"))).toBe(true);

      const tableEl = findElementByType(node, CatalogTable);
      expect(tableEl).not.toBeNull();
      const media = tableEl?.props["media"] as AdminMediaDto[];
      expect(media).toHaveLength(50);
      for (let i = 1; i <= 50; i++) {
        expect(media.some((m) => m.title === `Track ${i}`)).toBe(true);
      }
      expect((tableEl?.props["pagination"] as { total: number }).total).toBe(200);
    });

    it("passes the current search-param filter to CatalogTable as the bulk-action filter (byte-compatible with the retired Bulk*Control shape)", async () => {
      const rows = [makeMediaDto({ mediaId: "1" })];
      makeFetchMock(rows, 200, { "x-pagination": "total=1,pages=1,page=1,limit=50" });

      const { default: CatalogPage } = await import("../app/(authed)/catalog/page");
      const { CatalogTable } = await import("../app/(authed)/catalog/CatalogTable");
      const node = await CatalogPage({
        searchParams: Promise.resolve({ genre: "Rock", state: "ready" }),
      });

      const tableEl = findElementByType(node, CatalogTable);
      expect(tableEl?.props["bulkFilter"]).toEqual({
        state: "ready",
        artist: null,
        genre: "Rock",
        libraryId: null,
        q: null,
        eligible: null,
        // SPEC F52.5 (Epic Y) — page.tsx always sets the three facet-picker exact fields too.
        artistExact: null,
        albumExact: null,
        genresExact: [],
      });
      expect(tableEl?.props["filterActive"]).toBe(true);
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: out-of-scope media id renders access-denied page", () => {
    it("403 from the api renders an 'Access denied' page (no ad-hoc back-link — the shell sidebar owns nav, SPEC F28.5)", async () => {
      makeFetchMock({ message: "Access denied." }, 403);

      const { default: MediaDetailPage } = await import(
        "../app/(authed)/catalog/[mediaId]/page"
      );
      const node = await MediaDetailPage({
        params: Promise.resolve({ mediaId: "99" }),
      });

      expect(treeContains(node, "Access denied")).toBe(true);
      expect(treeContains(node, "Back to catalog")).toBe(false);
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: unknown media id renders a not-found page", () => {
    it("404 from the api renders a 'Not found' page", async () => {
      makeFetchMock({}, 404);

      const { default: MediaDetailPage } = await import(
        "../app/(authed)/catalog/[mediaId]/page"
      );
      const node = await MediaDetailPage({
        params: Promise.resolve({ mediaId: "99999" }),
      });

      expect(treeContains(node, "Not found")).toBe(true);
    });
  });

  // ---------------------------------------------------------------------------
  // Q11 review: the Details table's ID/Locator values are unbreakable strings
  // (an opaque id, a filesystem path with no spaces) — without a wrap
  // contract on those two cells, a long enough value pushes the 2-column
  // table's min-content width past 390px and the *page body* scrolls
  // sideways, violating STORY-093 AC2 (SPEC F28.13). break-all — not an
  // overflow-x-auto wrapper — is the fix here: this table has only two
  // columns, so letting the value wrap in place reads better than a
  // horizontal-scroll affordance for a single long path.
  describe("Scenario: track detail Details table never forces the page body to scroll sideways", () => {
    it("break-alls the ID and Locator value cells so an unbreakable string can't force min-content width past 390px", async () => {
      const longLocator =
        "/media/VariousArtists/SomeReallyLongAlbumNameWithNoSpacesToForceOverflowAtNarrowWidths_2026_Remaster/01-TrackTitle.flac";
      const row = makeMediaDto({ mediaId: "42", locator: longLocator });
      makeFetchMock(row, 200);

      const { default: MediaDetailPage } = await import(
        "../app/(authed)/catalog/[mediaId]/page"
      );
      const node = await MediaDetailPage({
        params: Promise.resolve({ mediaId: "42" }),
      });

      const tds = collectElementsByType(node, "td");
      const idTd = tds.find((td) => elementTextEquals(td, "42"));
      const locatorTd = tds.find((td) => elementTextEquals(td, longLocator));

      expect(idTd).toBeDefined();
      expect(String(idTd?.props["className"])).toMatch(/\bbreak-all\b/);
      expect(locatorTd).toBeDefined();
      expect(String(locatorTd?.props["className"])).toMatch(/\bbreak-all\b/);
    });
  });
});

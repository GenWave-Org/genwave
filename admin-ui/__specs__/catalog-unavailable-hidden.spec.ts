// gh-#113 — Hide unavailable rows from the catalog view (commit 1: the page half).
//
// Runner: Jest (node environment — .ts extension). The catalog server component is called
// directly as an async function and its JSX tree inspected via the directory's established
// tree-walker convention (copied from catalog-pages.spec.ts's pattern, duplicated rather than
// imported per that file's own header comment). Fetch is mocked per-URL so the libraries and
// media requests each get their own response — the media response carries the
// `X-Unavailable-Hidden` header the API sets only when the default view hid rows.

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import type { ReactNode } from "react";

// ---------------------------------------------------------------------------
// Tree walker (directory convention — see header)
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
  const el = node as { type?: unknown; props?: Record<string, unknown> };
  if (el && typeof el === "object" && el.props) {
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

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

interface MediaDtoStub {
  mediaId: string;
  locator: string;
  format: string;
  state: string;
  title: string;
}

function makeMediaDto(mediaId: string): MediaDtoStub {
  return {
    mediaId,
    locator: `/media/${mediaId}.flac`,
    format: "flac",
    state: "ready",
    title: `Track ${mediaId}`,
  };
}

function makeResponse(body: unknown, headers: Record<string, string>): Response {
  return {
    ok: true,
    status: 200,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
    headers: new Headers({ "content-type": "application/json", ...headers }),
  } as unknown as Response;
}

/** Mocks fetch per-URL: /api/libraries gets an empty list; /api/media gets `mediaResponse`. */
function mockFetchWithMediaResponse(mediaResponse: Response): jest.MockedFunction<typeof fetch> {
  const fn = jest.fn<typeof fetch>().mockImplementation((url) => {
    if (String(url).includes("/api/libraries")) {
      return Promise.resolve(makeResponse([], {}));
    }
    return Promise.resolve(mediaResponse);
  });
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

function mediaUrl(mockFetch: jest.MockedFunction<typeof fetch>): string {
  const url = mockFetch.mock.calls.map(([u]) => String(u)).find((u) => u.includes("/api/media"));
  expect(url).toBeDefined();
  return url as string;
}

jest.mock("next/headers", () => ({
  cookies: jest.fn<() => Promise<{ toString: () => string }>>().mockResolvedValue({
    toString: () => "session=test-cookie",
  }),
}));

// ---------------------------------------------------------------------------
// Feature: the catalog view hides unavailable rows, with a count and reveal toggle
// ---------------------------------------------------------------------------

describe("Feature: catalog hides unavailable rows (gh-#113)", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
    jest.resetModules();
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: the default view hid rows", () => {
    it("names the hidden count and offers a Show link carrying include-unavailable=true", async () => {
      const mockFetch = mockFetchWithMediaResponse(
        makeResponse([makeMediaDto("1")], {
          "x-pagination": "total=1,pages=1,page=1,limit=50",
          "x-unavailable-hidden": "12",
        })
      );

      const { default: CatalogPage } = await import("../app/(authed)/catalog/page");
      const node = await CatalogPage({ searchParams: Promise.resolve({}) });

      // The default browse never opts in on the wire — hiding is the API's own default.
      expect(mediaUrl(mockFetch)).not.toContain("include-unavailable");

      expect(treeContains(node, "12")).toBe(true);
      expect(treeContains(node, "unavailable track")).toBe(true);
      expect(treeContains(node, "hidden")).toBe(true);
      const hrefs = collectStrings(node).filter((s) => s.startsWith("/catalog"));
      expect(hrefs.some((h) => h.includes("include-unavailable=true"))).toBe(true);
    });

    it("keeps the active filters on the Show link", async () => {
      mockFetchWithMediaResponse(
        makeResponse([], {
          "x-pagination": "total=0,pages=1,page=1,limit=50",
          "x-unavailable-hidden": "3",
        })
      );

      const { default: CatalogPage } = await import("../app/(authed)/catalog/page");
      const node = await CatalogPage({ searchParams: Promise.resolve({ genre: "Rock" }) });

      const showHref = collectStrings(node).find((s) => s.includes("include-unavailable=true"));
      expect(showHref).toBeDefined();
      expect(showHref).toContain("genre=Rock");
    });
  });

  describe("Scenario: nothing was hidden", () => {
    it("renders no hidden-count line when the header is absent", async () => {
      mockFetchWithMediaResponse(
        makeResponse([makeMediaDto("1")], {
          "x-pagination": "total=1,pages=1,page=1,limit=50",
        })
      );

      const { default: CatalogPage } = await import("../app/(authed)/catalog/page");
      const node = await CatalogPage({ searchParams: Promise.resolve({}) });

      expect(treeContains(node, "unavailable track")).toBe(false);
      expect(treeContains(node, "Show unavailable")).toBe(false);
    });
  });

  describe("Scenario: the operator revealed unavailable rows", () => {
    it("forwards include-unavailable=true to the API and offers the Hide link back", async () => {
      const mockFetch = mockFetchWithMediaResponse(
        makeResponse([makeMediaDto("1")], {
          "x-pagination": "total=13,pages=1,page=1,limit=50",
        })
      );

      const { default: CatalogPage } = await import("../app/(authed)/catalog/page");
      const node = await CatalogPage({
        searchParams: Promise.resolve({ "include-unavailable": "true" }),
      });

      expect(mediaUrl(mockFetch)).toContain("include-unavailable=true");
      expect(treeContains(node, "Showing unavailable tracks")).toBe(true);

      // The Hide link is this same browse minus the reveal param.
      const hrefs = collectStrings(node).filter((s) => s.startsWith("/catalog"));
      expect(hrefs.some((h) => !h.includes("include-unavailable"))).toBe(true);
      expect(treeContains(node, "Hide unavailable")).toBe(true);
    });
  });
});

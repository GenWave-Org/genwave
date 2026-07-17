// @jest-environment jsdom
// STORY-148 — Eligibility curation by exact artist, album, and genre (Epic Y / SPEC F52.5,
// closes gitea-#189) — UI half. The SQL half lives in
// MediaLibrary.Tests/Specs/Story148_FacetsAndExactFilterSql.cs; the API half in
// Host.Tests/Specs/Story148_FacetsEndpointAndExactParams.cs.
//
// Runner: Jest (jsdom) + @testing-library/react. Y3 implements against FacetFilterControl (new,
// unit-tested directly), the Catalog filter form (page.tsx, tree-walked like
// catalog-signal-columns.spec.tsx's decade/year-missing scenarios), CatalogTable's hasBulkFilter
// gate, and the toolbar's by-filter requests (both mirroring catalog-selection-toolbar.spec.tsx's
// harness). Helpers are duplicated rather than imported from sibling spec files — the established
// convention in this directory (see catalog-signal-columns.spec.tsx's own header comment).

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
import { render, screen, fireEvent, waitFor, act, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { ReactNode } from "react";
import type { useRouter } from "next/navigation";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import type { LibraryDto } from "@/lib/library";
import { FacetFilterControl } from "../app/(authed)/catalog/FacetFilterControl";
import type { AdminMediaDto, BulkFilter, Pagination } from "../app/(authed)/catalog/types";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;

// ---------------------------------------------------------------------------
// Tree walker (copied from catalog-signal-columns.spec.tsx's pattern) — used only by the
// page.tsx wiring scenarios.
// ---------------------------------------------------------------------------

function isLeaf(node: ReactNode): boolean {
  return (
    node === null ||
    node === undefined ||
    typeof node === "boolean" ||
    typeof node === "string" ||
    typeof node === "number"
  );
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

/** Finds the first element in the tree whose function-component reference is `type` — duplicated
 * from catalog-pages.spec.ts's own tree walker (this directory's established convention, see this
 * file's header comment) rather than imported, since only the Y3 defect-fix scenario below needs
 * it: extracting the `bulkFilter` prop page.tsx actually built and handed to CatalogTable. */
function findElementByType(
  node: ReactNode,
  type: unknown
): { type: unknown; props: Record<string, unknown> } | null {
  if (isLeaf(node)) return null;
  if (Array.isArray(node)) {
    for (const child of node) {
      const found = findElementByType(child, type);
      if (found !== null) return found;
    }
    return null;
  }
  const el = node as { type?: unknown; props?: Record<string, unknown> };
  if (el && typeof el === "object" && el.props !== undefined) {
    if (el.type === type) return el as { type: unknown; props: Record<string, unknown> };
    if (el.props["children"] !== undefined) return findElementByType(el.props["children"] as ReactNode, type);
  }
  return null;
}

// ---------------------------------------------------------------------------
// Fetch helpers
// ---------------------------------------------------------------------------

interface FacetOption {
  value: string;
  count: number;
}

/** Answers GET /api/media/facets?field=… with a per-field option list; any other request (the
 * catalog browse itself) is answered separately via `browseBody`. */
function makeFieldAwareFetchMock(
  facetsByField: Partial<Record<"artist" | "album" | "genre", FacetOption[]>>,
  browseBody: unknown = [],
  browseHeaders: Record<string, string> = { "x-pagination": "total=0,pages=1,page=1,limit=50" }
): jest.MockedFunction<typeof fetch> {
  const fn = jest.fn<typeof fetch>().mockImplementation(async (input) => {
    const url = String(input);
    if (url.includes("/api/media/facets")) {
      const parsed = new URL(url, "http://localhost");
      const field = parsed.searchParams.get("field") as "artist" | "album" | "genre" | null;
      const options = (field !== null ? facetsByField[field] : undefined) ?? [];
      return {
        ok: true,
        status: 200,
        json: jest.fn<() => Promise<unknown>>().mockResolvedValue(options),
        headers: new Headers({ "content-type": "application/json" }),
      } as unknown as Response;
    }
    return {
      ok: true,
      status: 200,
      json: jest.fn<() => Promise<unknown>>().mockResolvedValue(browseBody),
      headers: new Headers({ "content-type": "application/json", ...browseHeaders }),
    } as unknown as Response;
  });
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

/** GET /api/media/facets fails (or errors) for every field — the sad-path fixture. */
function makeFailingFacetsFetchMock(status = 502): jest.MockedFunction<typeof fetch> {
  const fn = jest.fn<typeof fetch>().mockResolvedValue({
    ok: false,
    status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue({}),
    headers: new Headers(),
  } as unknown as Response);
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

function capturedUrls(mockFetch: jest.MockedFunction<typeof fetch>): string[] {
  return mockFetch.mock.calls.map(([url]) => String(url));
}

function queryParams(url: string): URLSearchParams {
  return new URL(url, "http://localhost").searchParams;
}

// ---------------------------------------------------------------------------
// CatalogTable/CatalogToolbar fixtures (mirrors catalog-selection-toolbar.spec.tsx's harness)
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

function makeBulkActionFetchMock(status: number, body: unknown = {}): jest.MockedFunction<typeof fetch> {
  const fn = jest.fn<typeof fetch>().mockResolvedValue({
    ok: status >= 200 && status < 300,
    status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
    headers: new Headers({ "content-type": "application/json" }),
  } as unknown as Response);
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

interface RenderCatalogTableOptions {
  media?: AdminMediaDto[];
  pagination?: Pagination;
  filterActive?: boolean;
  bulkFilter?: BulkFilter;
}

async function renderCatalogTable(options: RenderCatalogTableOptions = {}): Promise<ReturnType<typeof render>> {
  const { CatalogTable } = await import("../app/(authed)/catalog/CatalogTable");
  const media = options.media ?? [makeRow({ mediaId: "1" })];
  return render(
    <ConfirmDialogProvider>
      <CatalogTable
        media={media}
        pagination={options.pagination ?? makePagination({ total: media.length })}
        libraries={LIBRARIES}
        bulkFilter={options.bulkFilter ?? EMPTY_FILTER}
        filterActive={options.filterActive ?? false}
        clearFiltersHref="/catalog"
      />
      <Toaster />
    </ConfirmDialogProvider>
  );
}

async function confirmDialog(name = "Confirm"): Promise<void> {
  const dialog = await screen.findByRole("dialog");
  await act(async () => {
    fireEvent.click(within(dialog).getByRole("button", { name }));
    await Promise.resolve();
  });
}

// ---------------------------------------------------------------------------
// Feature: Facet pickers drive precise curation
// ---------------------------------------------------------------------------

describe("Feature: Facet pickers drive precise curation", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
    mockedUseRouter.mockReturnValue({ refresh: jest.fn() } as unknown as ReturnType<typeof useRouter>);
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  // -------------------------------------------------------------------------
  describe("Scenario: pickers render from the facets endpoint", () => {
    it("renders artist and album single-selects and a genre multi-select fed by GET /api/media/facets (F52.5)", async () => {
      makeFieldAwareFetchMock({
        artist: [{ value: "Queen", count: 2 }],
        album: [{ value: "A Night at the Opera", count: 1 }],
        genre: [{ value: "Rock", count: 3 }],
      });

      render(
        <>
          <FacetFilterControl
            field="artist"
            label="Artist"
            exactParamName="artist-exact"
            substringParamName="artist"
            multiple={false}
            initialExactValues={[]}
          />
          <FacetFilterControl field="album" label="Album" exactParamName="album-exact" multiple={false} initialExactValues={[]} />
          <FacetFilterControl
            field="genre"
            label="Genre"
            exactParamName="genre-exact"
            substringParamName="genre"
            multiple={true}
            initialExactValues={[]}
          />
        </>
      );

      await waitFor(() => {
        expect(screen.getByLabelText("Artist is exactly").tagName).toBe("SELECT");
      });
      expect(screen.getByLabelText("Album is exactly").tagName).toBe("SELECT");
      const genreSelect = screen.getByLabelText("Genre is exactly") as HTMLSelectElement;
      expect(genreSelect.tagName).toBe("SELECT");
      expect(genreSelect.multiple).toBe(true);
    });

    it("shows row counts in option labels — e.g. 'Queen (37)' (F52.5)", async () => {
      makeFieldAwareFetchMock({ artist: [{ value: "Queen", count: 37 }] });

      render(
        <FacetFilterControl
          field="artist"
          label="Artist"
          exactParamName="artist-exact"
          substringParamName="artist"
          multiple={false}
          initialExactValues={[]}
        />
      );

      await waitFor(() => {
        expect(screen.getByText("Queen (37)")).toBeInTheDocument();
      });
    });

    it("fetches facets on demand, not on every keystroke (F52.5)", async () => {
      const mockFetch = makeFieldAwareFetchMock({ artist: [{ value: "Queen", count: 1 }] });

      render(
        <FacetFilterControl
          field="artist"
          label="Artist"
          exactParamName="artist-exact"
          substringParamName="artist"
          multiple={false}
          initialExactValues={[]}
        />
      );

      await waitFor(() => {
        expect(screen.getByLabelText("Artist is exactly").tagName).toBe("SELECT");
      });

      const substringInput = screen.getByLabelText("Artist contains");
      fireEvent.change(substringInput, { target: { value: "Q" } });
      fireEvent.change(substringInput, { target: { value: "Qu" } });
      fireEvent.change(substringInput, { target: { value: "Que" } });

      expect(mockFetch.mock.calls.filter(([url]) => String(url).includes("/api/media/facets"))).toHaveLength(1);
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: picked values flow as exact params", () => {
    it("a picked artist lands on the browse request as artist-exact, never artist (F52.3, F52.5)", async () => {
      const mockFetch = makeFieldAwareFetchMock({});

      const { default: CatalogPage } = await import("../app/(authed)/catalog/page");
      await CatalogPage({ searchParams: Promise.resolve({ "artist-exact": "Queen" }) });

      const browseUrl = capturedUrls(mockFetch).find((u) => u.includes("/api/media?"));
      expect(browseUrl).toBeDefined();
      const params = queryParams(browseUrl ?? "");
      expect(params.get("artist-exact")).toBe("Queen");
      expect(params.has("artist")).toBe(false);
    });

    it("two picked genres land as repeated genre-exact params (F52.3)", async () => {
      const mockFetch = makeFieldAwareFetchMock({});

      const { default: CatalogPage } = await import("../app/(authed)/catalog/page");
      await CatalogPage({ searchParams: Promise.resolve({ "genre-exact": ["Rock", "Metal"] }) });

      const browseUrl = capturedUrls(mockFetch).find((u) => u.includes("/api/media?"));
      expect(browseUrl).toBeDefined();
      const params = queryParams(browseUrl ?? "");
      expect(params.getAll("genre-exact")).toEqual(["Rock", "Metal"]);
      expect(params.has("genre")).toBe(false);
    });

    it("picked values render as filter chips in the shipped chip style (F52.5)", async () => {
      makeFieldAwareFetchMock({});

      const { default: CatalogPage } = await import("../app/(authed)/catalog/page");
      const node = await CatalogPage({ searchParams: Promise.resolve({ "artist-exact": "Queen" }) });

      expect(findByAriaLabel(node, "Clear Artist: Queen filter")).not.toBeNull();
    });

    it("clearing a chip clears the exact param with it (F52.5)", async () => {
      makeFieldAwareFetchMock({});

      const { default: CatalogPage } = await import("../app/(authed)/catalog/page");
      const node = await CatalogPage({
        searchParams: Promise.resolve({ "artist-exact": "Queen", genre: "Rock" }),
      });

      const clearLink = findByAriaLabel(node, "Clear Artist: Queen filter");
      expect(clearLink).not.toBeNull();
      const href = clearLink?.props["href"] as string;
      expect(href).not.toContain("artist-exact=");
      expect(href).toContain("genre=Rock");
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: by-filter bulk mode arms on exact filters", () => {
    it("hasBulkFilter includes artistExact/albumExact/genresExact so an exact-filtered set can be swept with an empty selection (F52.5)", async () => {
      for (const filter of [
        { ...EMPTY_FILTER, artistExact: "Queen" },
        { ...EMPTY_FILTER, albumExact: "A Night at the Opera" },
        { ...EMPTY_FILTER, genresExact: ["Rock"] },
      ]) {
        const { unmount } = await renderCatalogTable({ bulkFilter: filter, filterActive: true });
        expect(screen.getByRole("region", { name: "Bulk actions" })).toBeInTheDocument();
        unmount();
      }
    });

    it("the bulk request body carries the camelCase exact fields (F52.4)", async () => {
      const mockFetch = makeBulkActionFetchMock(200, { affected: 5 });
      const filter: BulkFilter = {
        ...EMPTY_FILTER,
        artistExact: "Queen",
        albumExact: "A Night at the Opera",
        genresExact: ["Rock", "Metal"],
      };

      await renderCatalogTable({
        pagination: makePagination({ total: 500 }),
        bulkFilter: filter,
        filterActive: true,
      });

      fireEvent.click(screen.getByRole("button", { name: "Set eligible" }));
      await confirmDialog();

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
      const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/media/eligibility");
      const body = JSON.parse(init.body as string) as { filter: BulkFilter; eligible: boolean };
      expect(body.filter.artistExact).toBe("Queen");
      expect(body.filter.albumExact).toBe("A Night at the Opera");
      expect(body.filter.genresExact).toEqual(["Rock", "Metal"]);
    });

    it("strips the URL-parsed empty-sibling shape from the outgoing bulk body, keeping the real exact filter (smoke-caught defect fix, closes the Queen 0-affected repro)", async () => {
      // The exact wire shape the live repro sent: a native GET form submit with only the artist
      // facet picker set carries every OTHER filter field on the URL too, as an empty string —
      // never absent. Before the fix, page.tsx passed "album-exact": "" through to bulkFilter
      // verbatim, and the server's `lower(album) = lower('')` guard matched 0 rows instead of the
      // 2 the browse preview promised.
      const browseRows = [makeRow({ mediaId: "1" }), makeRow({ mediaId: "2" })];
      makeFieldAwareFetchMock({}, browseRows, { "x-pagination": "total=2,pages=1,page=1,limit=50" });

      const { default: CatalogPage } = await import("../app/(authed)/catalog/page");
      const { CatalogTable } = await import("../app/(authed)/catalog/CatalogTable");

      const node = await CatalogPage({
        searchParams: Promise.resolve({
          "artist-exact": "Queen",
          "album-exact": "",
          artist: "",
          genre: "",
          q: "",
          state: "",
          eligible: "",
        }),
      });

      const tableEl = findElementByType(node, CatalogTable);
      expect(tableEl).not.toBeNull();
      const bulkFilter = tableEl?.props["bulkFilter"] as BulkFilter;

      // page.tsx's own bulkFilter construction normalizes every blank sibling to null — never "".
      expect(bulkFilter).toEqual({
        state: null,
        artist: null,
        genre: null,
        libraryId: null,
        q: null,
        eligible: null,
        artistExact: "Queen",
        albumExact: null,
        genresExact: [],
      });

      // And the actual outgoing POST body carries that same shape — the byte-for-byte repro fix.
      const bulkFetch = makeBulkActionFetchMock(200, { affected: 2 });
      await renderCatalogTable({
        pagination: makePagination({ total: 2 }),
        bulkFilter,
        filterActive: true,
      });

      fireEvent.click(screen.getByRole("button", { name: "Set ineligible" }));
      await confirmDialog();

      await waitFor(() => expect(bulkFetch).toHaveBeenCalledTimes(1));
      const [url, init] = bulkFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/media/eligibility");
      const body = JSON.parse(init.body as string) as { filter: BulkFilter; eligible: boolean };
      expect(body.eligible).toBe(false);
      expect(body.filter.artistExact).toBe("Queen");
      expect(body.filter.albumExact).toBeNull();
      expect(body.filter.state).toBeNull();
      expect(body.filter.artist).toBeNull();
      expect(body.filter.genre).toBeNull();
      expect(body.filter.q).toBeNull();
    });

    it("selection-mode per-row fan-out is untouched when rows are checked (F52.5)", async () => {
      const mockFetch = makeBulkActionFetchMock(204, {});
      const filter: BulkFilter = { ...EMPTY_FILTER, artistExact: "Queen" };

      await renderCatalogTable({ bulkFilter: filter, filterActive: true });

      fireEvent.click(screen.getByRole("checkbox", { name: "Select Track 1" }));
      fireEvent.click(screen.getByRole("button", { name: "Set eligible" }));
      await confirmDialog();

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
      const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/media/1");
      expect(init.method).toBe("PATCH");
      expect(mockFetch).not.toHaveBeenCalledWith("/api/media/eligibility", expect.anything());
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: facet fetch failure degrades to typing", () => {
    it("a failed facets fetch renders free-text inputs with an inline notice — the VoiceControl discipline (F52.5)", async () => {
      makeFailingFacetsFetchMock();

      render(
        <FacetFilterControl field="album" label="Album" exactParamName="album-exact" multiple={false} initialExactValues={[]} />
      );

      await waitFor(() => {
        const field = screen.getByLabelText("Album is exactly") as HTMLInputElement;
        expect(field.tagName).toBe("INPUT");
      });
      expect(screen.getByText(/album list unavailable/i)).toBeInTheDocument();
    });

    it("filtering keeps working through the free-text fallback (F52.5)", async () => {
      makeFailingFacetsFetchMock();

      render(
        <FacetFilterControl field="album" label="Album" exactParamName="album-exact" multiple={false} initialExactValues={[]} />
      );

      const fallbackInput = await waitFor(() => {
        const el = screen.getByLabelText("Album is exactly") as HTMLInputElement;
        expect(el.tagName).toBe("INPUT");
        return el;
      });

      expect(fallbackInput).toHaveAttribute("name", "album-exact");
      fireEvent.change(fallbackInput, { target: { value: "A Night at the Opera" } });
      expect(fallbackInput.value).toBe("A Night at the Opera");
    });
  });
});

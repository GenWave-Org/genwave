// @jest-environment jsdom
// STORY-220 — The catalog shows and filters by mood (SPEC F86.8 — UI half, T80)
//
// Runner: Jest + jsdom + @testing-library/react. A Moods column joins the catalog's
// existing column-visibility toggle (mirrors catalog-signal-columns.spec.tsx's CatalogTable
// harness); a mood filter offers the fixed MoodVocabulary terms (imported constant — the control
// issues NO facet/discovery fetch) and drives the repeatable ?mood-exact= query param (mirrors
// catalog-facet-pickers.spec.tsx's CatalogPage-wiring harness). Untagged rows show an empty
// moods cell.
//
// The vocabulary↔C# parity pin lives in its own file (catalog-mood-vocabulary-parity.spec.ts) —
// this file only exercises the UI wiring.

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
import type { useRouter } from "next/navigation";
import type { LibraryDto } from "@/lib/library";
import { MOOD_VOCABULARY } from "../app/(authed)/catalog/moodVocabulary";
import { MoodFilterControl } from "../app/(authed)/catalog/MoodFilterControl";
import type { AdminMediaDto, BulkFilter, Pagination } from "../app/(authed)/catalog/types";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;

// ---------------------------------------------------------------------------
// Fetch helpers (duplicated rather than imported — this directory's established convention,
// see catalog-signal-columns.spec.tsx's own header comment)
// ---------------------------------------------------------------------------

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

function queryParams(url: string): URLSearchParams {
  return new URL(url, "http://localhost").searchParams;
}

// ---------------------------------------------------------------------------
// CatalogTable fixtures (mirrors catalog-signal-columns.spec.tsx's harness)
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

interface RenderCatalogTableOptions {
  media?: AdminMediaDto[];
}

async function renderCatalogTable(options: RenderCatalogTableOptions = {}): Promise<void> {
  const { CatalogTable } = await import("../app/(authed)/catalog/CatalogTable");
  const media = options.media ?? [makeRow({ mediaId: "1" })];
  render(
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
// Feature: catalog moods column and filter
// ---------------------------------------------------------------------------

describe("Feature: catalog moods column and filter", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
    window.localStorage.clear();
    mockedUseRouter.mockReturnValue({ refresh: jest.fn() } as unknown as ReturnType<typeof useRouter>);
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  // -------------------------------------------------------------------------
  describe("Scenario: the moods column", () => {
    it("appears in the column-visibility toggle", async () => {
      await renderCatalogTable();

      openColumnsPanel();

      expect(screen.getByRole("checkbox", { name: "Moods" })).toBeInTheDocument();
    });

    it("renders each tagged row's mood tags when enabled", async () => {
      await renderCatalogTable({ media: [makeRow({ mediaId: "1", moods: ["dreamy", "warm"] })] });

      openColumnsPanel();
      fireEvent.click(screen.getByRole("checkbox", { name: "Moods" }));

      const moodsCell = screen.getByRole("list", { name: "Moods" });
      expect(within(moodsCell).getAllByRole("listitem").map((li) => li.textContent)).toEqual([
        "dreamy",
        "warm",
      ]);
    });

    it("renders an empty cell for untagged rows", async () => {
      await renderCatalogTable({
        media: [makeRow({ mediaId: "1", title: "Untagged Track", moods: null })],
      });

      openColumnsPanel();
      fireEvent.click(screen.getByRole("checkbox", { name: "Moods" }));

      const row = screen.getByText("Untagged Track").closest("tr");
      const cells = within(row as HTMLElement).getAllByRole("cell");
      expect(cells[cells.length - 1]).toHaveTextContent("");
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: the mood filter", () => {
    it("offers exactly the MoodVocabulary terms as choices", () => {
      render(<MoodFilterControl initialValues={[]} />);

      const select = screen.getByLabelText("Mood is exactly") as HTMLSelectElement;
      expect(Array.from(select.options).map((opt) => opt.value)).toEqual(MOOD_VOCABULARY);
    });

    it("issues no facet or discovery request for its choices", () => {
      const fetchSpy = jest.fn<typeof fetch>();
      global.fetch = fetchSpy as unknown as typeof fetch;

      render(<MoodFilterControl initialValues={[]} />);

      expect(fetchSpy).not.toHaveBeenCalled();
    });

    it("applies selections as repeatable mood-exact query params", async () => {
      const mockFetch = makeFetchMock([], 200, { "x-pagination": "total=0,pages=1,page=1,limit=50" });

      const { default: CatalogPage } = await import("../app/(authed)/catalog/page");
      await CatalogPage({ searchParams: Promise.resolve({ "mood-exact": ["dreamy", "warm"] }) });

      const browseUrl = capturedUrls(mockFetch).find((u) => u.includes("/api/media?"));
      expect(queryParams(browseUrl ?? "").getAll("mood-exact")).toEqual(["dreamy", "warm"]);
    });

    it("composes with an active exact filter instead of replacing it", async () => {
      const mockFetch = makeFetchMock([], 200, { "x-pagination": "total=0,pages=1,page=1,limit=50" });

      const { default: CatalogPage } = await import("../app/(authed)/catalog/page");
      await CatalogPage({
        searchParams: Promise.resolve({ "genre-exact": "Rock", "mood-exact": "dreamy" }),
      });

      const browseUrl = capturedUrls(mockFetch).find((u) => u.includes("/api/media?"));
      const params = queryParams(browseUrl ?? "");
      expect({ genreExact: params.get("genre-exact"), moodExact: params.get("mood-exact") }).toEqual({
        genreExact: "Rock",
        moodExact: "dreamy",
      });
    });
  });
});

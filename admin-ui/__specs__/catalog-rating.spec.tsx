// @jest-environment jsdom
// STORY-115 — Rating state in the Catalog page (Epic S / SPEC F33.7, F33.10, F33.12)
//
// Runner: Jest (jsdom) + @testing-library/react + mocked fetch, extending the
// catalog-selection-toolbar.spec.tsx / catalog-f3-filter.spec.ts / live-rating.spec.tsx house
// patterns: CatalogTable is rendered directly via RTL for row-level rating assertions (score
// column, badge, the NeverPlayControl restore/X toggle — CatalogToolbar's own selection-mode/
// by-filter-mode tests already cover that harness), and CatalogPage is called as a plain async
// function (tree-walker-free here — only the backend URL and the CatalogTable `media` prop matter)
// for the never-play query-param plumbing. useConfirm()/toast need their providers, so
// CatalogTable renders wrap in ConfirmDialogProvider + mount Toaster, matching the toolbar spec's
// harness. The F33.7 bulk-toolbar negative is the schema-side standalone guarantee's UI half — the
// backend half lives in Story110/Story116 (already shipped: S3, S5).

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
import { render, screen, fireEvent, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { useRouter } from "next/navigation";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import type { LibraryDto } from "@/lib/library";
import type { AdminMediaDto, BulkFilter, Pagination } from "../app/(authed)/catalog/types";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const LIBRARIES: LibraryDto[] = [
  { id: 1, name: "In Rotation", mediaCount: 50 },
  { id: 2, name: "Archive", mediaCount: 20 },
];

const EMPTY_FILTER: BulkFilter = {
  state: null,
  artist: null,
  genre: null,
  libraryId: null,
  q: null,
  eligible: null,
};

interface RowOverrides {
  mediaId?: string;
  eligible?: boolean;
  score?: number;
  neverPlay?: boolean;
}

function makeRow(overrides: RowOverrides = {}): AdminMediaDto {
  const mediaId = overrides.mediaId ?? "1";
  return {
    mediaId,
    locator: `/media/${mediaId}.flac`,
    format: "flac",
    state: "ready",
    durationMs: 180000,
    title: `Track ${mediaId}`,
    artist: "Test Artist",
    album: "Test Album",
    genre: "Rock",
    year: 2024,
    integratedLufs: -14,
    truePeakDbtp: -1,
    measurable: true,
    cueInSec: null,
    cueOutSec: null,
    eligible: overrides.eligible ?? true,
    version: `${900 + Number(mediaId)}`,
    score: overrides.score ?? 50,
    neverPlay: overrides.neverPlay ?? false,
  };
}

function makePagination(overrides: Partial<Pagination> = {}): Pagination {
  return { total: 1, pages: 1, page: 1, limit: 50, ...overrides };
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

/** Finds the first element in a returned server-component tree whose function-component
 * reference is `type` (copied from catalog-pages.spec.ts / catalog-f3-filter.spec.ts's
 * tree-walker pattern — CatalogPage returns plain React elements, never rendered). */
function findElementByType(node: unknown, type: unknown): { props: Record<string, unknown> } | null {
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
    if (el.type === type) return el as { props: Record<string, unknown> };
    if (el.props["children"] !== undefined) return findElementByType(el.props["children"], type);
  }
  return null;
}

interface RenderCatalogTableOptions {
  media?: AdminMediaDto[];
  pagination?: Pagination;
  libraries?: LibraryDto[];
  bulkFilter?: BulkFilter;
  filterActive?: boolean;
}

async function renderCatalogTable(options: RenderCatalogTableOptions = {}) {
  const { CatalogTable } = await import("../app/(authed)/catalog/CatalogTable");
  const media = options.media ?? [makeRow()];
  return render(
    <ConfirmDialogProvider>
      <CatalogTable
        media={media}
        pagination={options.pagination ?? makePagination({ total: media.length })}
        libraries={options.libraries ?? LIBRARIES}
        bulkFilter={options.bulkFilter ?? EMPTY_FILTER}
        filterActive={options.filterActive ?? false}
        clearFiltersHref="/catalog"
      />
      <Toaster />
    </ConfirmDialogProvider>
  );
}

/** Confirms the currently-open useConfirm() dialog (default label is "Confirm" unless overridden). */
async function confirmDialog(name = "Confirm"): Promise<void> {
  const dialog = await screen.findByRole("dialog");
  fireEvent.click(within(dialog).getByRole("button", { name }));
}

// ---------------------------------------------------------------------------
// Feature: Rating state in the Catalog page
// ---------------------------------------------------------------------------

describe("Feature: Rating state in the Catalog page", () => {
  let originalFetch: typeof fetch;
  let refreshMock: jest.Mock;

  beforeEach(() => {
    originalFetch = global.fetch;
    refreshMock = jest.fn();
    mockedUseRouter.mockReturnValue({ refresh: refreshMock } as unknown as ReturnType<typeof useRouter>);
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: browse rows surface rating state", () => {
    it("shows the score column on every row (50 for unrated)", async () => {
      const media = [makeRow({ mediaId: "1", score: 50 }), makeRow({ mediaId: "2", score: 82 })];
      await renderCatalogTable({ media, pagination: makePagination({ total: 2 }) });

      expect(screen.getByText("50")).toBeInTheDocument();
      expect(screen.getByText("82")).toBeInTheDocument();
    });

    it("renders a never-play badge on flagged rows only (F33.12)", async () => {
      const media = [
        makeRow({ mediaId: "1", neverPlay: false }),
        makeRow({ mediaId: "2", neverPlay: true }),
      ];
      await renderCatalogTable({ media, pagination: makePagination({ total: 2 }) });

      expect(screen.getAllByText("Never play")).toHaveLength(1);
      expect(screen.getByRole("button", { name: "Restore to rotation" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Never play" })).toBeInTheDocument();
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: the never-play filter finds X'd tracks", () => {
    it("enabling the filter requests ?never-play=true (F33.10)", async () => {
      const rows = [makeRow({ mediaId: "9", neverPlay: true })];
      const mockFetch = makeFetchMock(rows, 200, {
        "x-pagination": "total=1,pages=1,page=1,limit=50",
      });

      const { default: CatalogPage } = await import("../app/(authed)/catalog/page");
      await CatalogPage({ searchParams: Promise.resolve({ "never-play": "true" }) });

      const urls = capturedUrls(mockFetch);
      expect(urls.some((u) => u.includes("never-play=true"))).toBe(true);
    });

    it("the filtered view lists only flagged rows", async () => {
      const rows = [makeRow({ mediaId: "9", neverPlay: true }), makeRow({ mediaId: "10", neverPlay: true })];
      makeFetchMock(rows, 200, { "x-pagination": "total=2,pages=1,page=1,limit=50" });

      const { default: CatalogPage } = await import("../app/(authed)/catalog/page");
      const { CatalogTable } = await import("../app/(authed)/catalog/CatalogTable");
      const node = await CatalogPage({ searchParams: Promise.resolve({ "never-play": "true" }) });

      const tableEl = findElementByType(node, CatalogTable);
      expect(tableEl).not.toBeNull();
      expect(tableEl?.props["media"]).toEqual(rows);
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: restoring from the catalog", () => {
    it("the restore control PUTs neverPlay false and clears the badge", async () => {
      const media = [makeRow({ mediaId: "5", neverPlay: true })];
      const mockFetch = makeFetchMock({ neverPlay: false }, 200);
      await renderCatalogTable({ media, pagination: makePagination({ total: 1 }) });

      expect(screen.getByText("Never play")).toBeInTheDocument();

      fireEvent.click(screen.getByRole("button", { name: "Restore to rotation" }));

      await waitFor(() => expect(screen.queryByText("Never play")).not.toBeInTheDocument());
      expect(screen.getByRole("button", { name: "Never play" })).toBeInTheDocument();

      const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/media/5/never-play");
      expect(init.method).toBe("PUT");
      expect(JSON.parse(init.body as string)).toEqual({ neverPlay: false });
      expect(refreshMock).toHaveBeenCalledTimes(1);
    });

    it("a restored row leaves the filtered view", async () => {
      const media = [makeRow({ mediaId: "5", neverPlay: true })];
      makeFetchMock({ neverPlay: false }, 200);

      const { CatalogTable } = await import("../app/(authed)/catalog/CatalogTable");
      const tableProps = {
        libraries: LIBRARIES,
        bulkFilter: EMPTY_FILTER,
        filterActive: true,
        clearFiltersHref: "/catalog",
      };

      const utils = render(
        <ConfirmDialogProvider>
          <CatalogTable media={media} pagination={makePagination({ total: 1 })} {...tableProps} />
          <Toaster />
        </ConfirmDialogProvider>
      );

      // Simulate what router.refresh() does for real: the server component re-fetches with the
      // still-active ?never-play=true filter and hands CatalogTable a fresh `media` array that no
      // longer contains the just-restored row.
      refreshMock.mockImplementation(() => {
        utils.rerender(
          <ConfirmDialogProvider>
            <CatalogTable media={[]} pagination={makePagination({ total: 0 })} {...tableProps} />
            <Toaster />
          </ConfirmDialogProvider>
        );
      });

      fireEvent.click(screen.getByRole("button", { name: "Restore to rotation" }));

      await waitFor(() => expect(refreshMock).toHaveBeenCalledTimes(1));
      expect(screen.queryByRole("button", { name: "Restore to rotation" })).not.toBeInTheDocument();
      expect(screen.getByText("No tracks match this filter")).toBeInTheDocument();
    });
  });

  // ---------------------------------------------------------------------------
  // SAD PATH
  // ---------------------------------------------------------------------------

  describe("Scenario: bulk actions leave ratings alone (sad path)", () => {
    it("a bulk eligibility action issues no rating-endpoint requests (F33.7)", async () => {
      const mockFetch = makeFetchMock({}, 204);
      await renderCatalogTable({ media: [makeRow({ mediaId: "1" })] });

      fireEvent.click(screen.getByRole("checkbox", { name: "Select Track 1" }));
      fireEvent.click(screen.getByRole("button", { name: "Set eligible" }));
      await confirmDialog();

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
      const urls = capturedUrls(mockFetch);
      expect(urls).toEqual(["/api/media/1"]);
      expect(urls.some((u) => u.includes("/vote") || u.includes("/never-play"))).toBe(false);
      const [, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(JSON.parse(init.body as string)).toEqual({ eligible: true });
    });

    it("a bulk reassign action issues no rating-endpoint requests (F33.7)", async () => {
      const mockFetch = makeFetchMock({}, 200);
      await renderCatalogTable({ media: [makeRow({ mediaId: "1" })] });

      fireEvent.click(screen.getByRole("checkbox", { name: "Select Track 1" }));
      fireEvent.change(screen.getByLabelText("Destination library"), { target: { value: "2" } });
      fireEvent.click(screen.getByRole("button", { name: "Reassign" }));
      await confirmDialog();

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
      const urls = capturedUrls(mockFetch);
      expect(urls).toEqual(["/api/media/1"]);
      expect(urls.some((u) => u.includes("/vote") || u.includes("/never-play"))).toBe(false);
      const [, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(JSON.parse(init.body as string)).toEqual({ libraryId: 2 });
    });
  });

  describe("Scenario: failures surface (sad path)", () => {
    it("a failed restore toasts the outcome and keeps the badge (F31.3)", async () => {
      makeFetchMock({}, 401);
      await renderCatalogTable({ media: [makeRow({ mediaId: "7", neverPlay: true })] });

      fireEvent.click(screen.getByRole("button", { name: "Restore to rotation" }));

      await waitFor(() => {
        expect(screen.getByText("Your session has expired — sign in again.")).toBeInTheDocument();
      });
      expect(screen.getByText("Never play")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Restore to rotation" })).toBeInTheDocument();
    });
  });
});

// @jest-environment jsdom
// STORY-158 — Rank artists and albums from the Catalog (Epic Z / SPEC F61, closes gitea-#233) —
// the UI half: vote up / vote down / never-play / restore on the Catalog bulk toolbar.
//
// Runner: Jest (jsdom) + @testing-library/react. Authored PENDING at /plan time (2026-07-15,
// house rule since Epic S) as it.todo entries — Z7 implements. House lesson from Y3 applies:
// feed components the WIRE's shape (URL-parsed searchParams), not the type system's idea of it.
//
// Harness mirrors catalog-selection-toolbar.spec.tsx (selection vs by-filter mode, confirm/toast
// plumbing) and catalog-facet-pickers.spec.tsx (the Y3 blank-exact-field CatalogPage tree-walk).
// The toolbar's four new buttons share aria-labels with the row-level RatingControls/
// NeverPlayControl ("Vote up", "Never play", "Restore to rotation", …) — every query below scopes
// through the "Bulk actions" region via `within` so it can't accidentally hit a row's own control.

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
import { render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { ReactNode } from "react";
import type { useRouter } from "next/navigation";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import type { LibraryDto } from "@/lib/library";
import type { AdminMediaDto, BulkFilter, Pagination } from "../app/(authed)/catalog/types";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;

// ---------------------------------------------------------------------------
// Tree walker (copied from catalog-facet-pickers.spec.tsx's pattern — this directory's
// established convention, duplicated rather than imported per that file's own header comment).
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
// Helpers
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

function makeFetchMock(status: number, body: unknown = {}): jest.MockedFunction<typeof fetch> {
  const fn = jest
    .fn<typeof fetch>()
    .mockResolvedValue({
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

/** Scopes every button query below to the toolbar itself — the toolbar's new icon buttons share
 * aria-labels with the row-level RatingControls/NeverPlayControl ("Vote up", "Never play", …), so
 * an unscoped `screen.getByRole` would be ambiguous the moment both are on screen at once. */
function toolbar(): HTMLElement {
  return screen.getByRole("region", { name: "Bulk actions" });
}

/** Confirms the currently-open useConfirm() dialog (default label is "Confirm" unless overridden). */
async function confirmDialog(name = "Confirm"): Promise<void> {
  const dialog = await screen.findByRole("dialog");
  await act(async () => {
    fireEvent.click(within(dialog).getByRole("button", { name }));
    await Promise.resolve();
  });
}

function bulkVoteCalls(mockFetch: jest.MockedFunction<typeof fetch>): [string, RequestInit][] {
  return mockFetch.mock.calls
    .map((call) => call as [string, RequestInit])
    .filter(([url]) => String(url).includes("/vote"));
}

function neverPlayCalls(mockFetch: jest.MockedFunction<typeof fetch>): [string, RequestInit][] {
  return mockFetch.mock.calls
    .map((call) => call as [string, RequestInit])
    .filter(([url]) => String(url).includes("/never-play"));
}

// ---------------------------------------------------------------------------
// Feature: Rank artists and albums from the Catalog toolbar
// ---------------------------------------------------------------------------

describe("Feature: Rank artists and albums from the Catalog toolbar", () => {
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
  describe("Scenario: the four actions render in both toolbar modes", () => {
    it("vote up / vote down / never-play / restore render beside eligibility/reassign/reenrich in selection mode (F61.4)", async () => {
      makeFetchMock(204, {});
      await renderCatalogTable();

      fireEvent.click(screen.getByRole("checkbox", { name: "Select Track 1" }));

      const region = within(toolbar());
      expect(region.getByRole("button", { name: "Vote up" })).toBeInTheDocument();
      expect(region.getByRole("button", { name: "Vote down" })).toBeInTheDocument();
      expect(region.getByRole("button", { name: "Never play" })).toBeInTheDocument();
      expect(region.getByRole("button", { name: "Restore to rotation" })).toBeInTheDocument();
      // Beside the shipped actions, not instead of them.
      expect(region.getByRole("button", { name: "Reassign" })).toBeInTheDocument();
      expect(region.getByRole("button", { name: "Re-analyze" })).toBeInTheDocument();
      expect(region.getByRole("button", { name: "Set eligible" })).toBeInTheDocument();
      expect(region.getByRole("button", { name: "Set ineligible" })).toBeInTheDocument();
    });

    it("the same four actions render in by-filter mode, gated by hasBulkFilter like the shipped actions (F61.4)", async () => {
      const filter: BulkFilter = { ...EMPTY_FILTER, state: "ready" };
      await renderCatalogTable({
        pagination: makePagination({ total: 500 }),
        bulkFilter: filter,
        filterActive: true,
      });

      const region = within(toolbar());
      expect(region.getByRole("button", { name: "Vote up" })).toBeInTheDocument();
      expect(region.getByRole("button", { name: "Vote down" })).toBeInTheDocument();
      expect(region.getByRole("button", { name: "Never play" })).toBeInTheDocument();
      expect(region.getByRole("button", { name: "Restore to rotation" })).toBeInTheDocument();
    });

    it("hides the toolbar (and its four new actions) when no selection and no active bulk filter, exactly like the shipped actions", async () => {
      await renderCatalogTable({ bulkFilter: EMPTY_FILTER, filterActive: false });

      expect(screen.queryByRole("region", { name: "Bulk actions" })).not.toBeInTheDocument();
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: selection mode fans out per-row", () => {
    it("vote up fans out one POST /api/media/{id}/vote per selected row with direction 'up' (F61.4)", async () => {
      const mockFetch = makeFetchMock(200, { score: 51 });
      await renderCatalogTable({
        media: [makeRow({ mediaId: "1" }), makeRow({ mediaId: "2" })],
        pagination: makePagination({ total: 2 }),
      });

      fireEvent.click(screen.getByRole("checkbox", { name: "Select all rows on this page" }));
      fireEvent.click(within(toolbar()).getByRole("button", { name: "Vote up" }));
      await confirmDialog();

      await waitFor(() => expect(bulkVoteCalls(mockFetch)).toHaveLength(2));
      const calls = bulkVoteCalls(mockFetch);
      const urls = calls.map(([url]) => url).sort();
      expect(urls).toEqual(["/api/media/1/vote", "/api/media/2/vote"]);
      for (const [, init] of calls) {
        expect(init.method).toBe("POST");
        expect(JSON.parse(init.body as string)).toEqual({ direction: "up" });
      }
      expect(mockFetch).not.toHaveBeenCalledWith("/api/media/bulk/vote", expect.anything());
    });

    it("never-play fans out one PUT /api/media/{id}/never-play per selected row (F61.4)", async () => {
      const mockFetch = makeFetchMock(200, { neverPlay: true });
      await renderCatalogTable({
        media: [makeRow({ mediaId: "1" }), makeRow({ mediaId: "2" })],
        pagination: makePagination({ total: 2 }),
      });

      fireEvent.click(screen.getByRole("checkbox", { name: "Select all rows on this page" }));
      fireEvent.click(within(toolbar()).getByRole("button", { name: "Never play" }));
      await confirmDialog();

      await waitFor(() => expect(neverPlayCalls(mockFetch)).toHaveLength(2));
      const calls = neverPlayCalls(mockFetch);
      const urls = calls.map(([url]) => url).sort();
      expect(urls).toEqual(["/api/media/1/never-play", "/api/media/2/never-play"]);
      for (const [, init] of calls) {
        expect(init.method).toBe("PUT");
        expect(JSON.parse(init.body as string)).toEqual({ neverPlay: true });
      }
      expect(mockFetch).not.toHaveBeenCalledWith("/api/media/bulk/never-play", expect.anything());
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: by-filter mode calls the bulk endpoints", () => {
    it("bulk vote POSTs /api/media/bulk/vote with the active filter (exact fields included) and the direction (F61.1, F61.4)", async () => {
      const mockFetch = makeFetchMock(200, { updated: 12 });
      const filter: BulkFilter = {
        ...EMPTY_FILTER,
        artistExact: "Queen",
        albumExact: "A Night at the Opera",
        genresExact: ["Rock"],
      };

      await renderCatalogTable({
        pagination: makePagination({ total: 12 }),
        bulkFilter: filter,
        filterActive: true,
      });

      fireEvent.click(within(toolbar()).getByRole("button", { name: "Vote down" }));
      await confirmDialog();

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
      const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/media/bulk/vote");
      const body = JSON.parse(init.body as string) as { filter: BulkFilter; direction: string };
      expect(body.filter).toEqual(filter);
      expect(body.direction).toBe("down");
    });

    it("bulk never-play POSTs /api/media/bulk/never-play with the active filter and the flag (F61.1, F61.4)", async () => {
      const mockFetch = makeFetchMock(200, { updated: 9 });
      const filter: BulkFilter = { ...EMPTY_FILTER, artistExact: "Queen" };

      await renderCatalogTable({
        pagination: makePagination({ total: 9 }),
        bulkFilter: filter,
        filterActive: true,
      });

      fireEvent.click(within(toolbar()).getByRole("button", { name: "Never play" }));
      await confirmDialog();

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
      const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/media/bulk/never-play");
      const body = JSON.parse(init.body as string) as { filter: BulkFilter; neverPlay: boolean };
      expect(body.filter).toEqual(filter);
      expect(body.neverPlay).toBe(true);
    });

    it("blank exact fields normalize to absent — the Y3 empty-exact lesson (wire-shape fidelity)", async () => {
      // Same URL-parsed shape a native GET form actually submits: only artist-exact filled, every
      // other sibling (including album-exact) arrives as "" — never absent (Y3, closes the Queen
      // 0-affected repro's UI half, reused verbatim for the new bulk endpoints).
      const { default: CatalogPage } = await import("../app/(authed)/catalog/page");
      const { CatalogTable } = await import("../app/(authed)/catalog/CatalogTable");

      makeFetchMock(200, [makeRow({ mediaId: "1" })]);
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
      expect(bulkFilter.artistExact).toBe("Queen");
      expect(bulkFilter.albumExact).toBeNull();

      const bulkFetch = makeFetchMock(200, { updated: 3 });
      await renderCatalogTable({
        pagination: makePagination({ total: 3 }),
        bulkFilter,
        filterActive: true,
      });

      fireEvent.click(within(toolbar()).getByRole("button", { name: "Vote up" }));
      await confirmDialog();

      await waitFor(() => expect(bulkFetch).toHaveBeenCalledTimes(1));
      const [url, init] = bulkFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/media/bulk/vote");
      const body = JSON.parse(init.body as string) as { filter: BulkFilter };
      expect(body.filter.artistExact).toBe("Queen");
      expect(body.filter.albumExact).toBeNull();
      expect(body.filter.state).toBeNull();
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: the operator is informed, never surprised", () => {
    it("a confirm dialog precedes a by-filter sweep, naming the matched count (F61.4)", async () => {
      makeFetchMock(200, { updated: 500 });
      const filter: BulkFilter = { ...EMPTY_FILTER, state: "ready" };
      await renderCatalogTable({
        pagination: makePagination({ total: 500 }),
        bulkFilter: filter,
        filterActive: true,
      });

      fireEvent.click(within(toolbar()).getByRole("button", { name: "Vote up" }));

      const dialog = await screen.findByRole("dialog");
      expect(dialog).toHaveTextContent("500 matching");
    });

    it("a success toast reports the { updated } count (F61.4)", async () => {
      makeFetchMock(200, { updated: 7 });
      const filter: BulkFilter = { ...EMPTY_FILTER, state: "ready" };
      await renderCatalogTable({
        pagination: makePagination({ total: 7 }),
        bulkFilter: filter,
        filterActive: true,
      });

      fireEvent.click(within(toolbar()).getByRole("button", { name: "Vote up" }));
      await confirmDialog();

      await waitFor(() => {
        expect(screen.getByText("7 tracks voted up.")).toBeInTheDocument();
      });
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: restore is always reachable", () => {
    it("never-played rows remain visible and the restore action clears the flag — no one-way door (F61.2)", async () => {
      const mockFetch = makeFetchMock(200, { neverPlay: false });
      const media = [makeRow({ mediaId: "1", neverPlay: true }), makeRow({ mediaId: "2", neverPlay: false })];
      await renderCatalogTable({ media, pagination: makePagination({ total: 2 }) });

      // The restore action is not gated on the selected rows' current flag — it renders and works
      // regardless of whether the selection mixes flagged and playable rows.
      fireEvent.click(screen.getByRole("checkbox", { name: "Select all rows on this page" }));
      fireEvent.click(within(toolbar()).getByRole("button", { name: "Restore to rotation" }));
      await confirmDialog();

      await waitFor(() => expect(neverPlayCalls(mockFetch)).toHaveLength(2));
      for (const [, init] of neverPlayCalls(mockFetch)) {
        expect(JSON.parse(init.body as string)).toEqual({ neverPlay: false });
      }

      // The rows stay on screen — no row disappears as a side effect of the restore action itself
      // (only a subsequent refresh against an active never-play filter would ever drop one, F33.10).
      expect(screen.getByRole("link", { name: "Track 1" })).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Track 2" })).toBeInTheDocument();
    });
  });
});

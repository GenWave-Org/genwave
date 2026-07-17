// @jest-environment jsdom
// STORY-089 — Catalog: selection-model bulk toolbar + Libraries tab (Epic Q / SPEC F28.11)
//
// Runner: Jest (jsdom) + @testing-library/react + mocked fetch. In by-filter mode (empty
// selection, active filter) the retired Bulk*Control specs' request-body assertions are the
// byte-compatibility oracle (PLAN.md "Q7 replaces components, not contracts") — reproduced here
// against CatalogToolbar (driven through CatalogTable, its real integration point) instead of
// the deleted components. In selection mode (Q7 review Finding 1 — selection must actually scope
// the write, not just the confirm copy), the wire contract is the shipped single-row endpoints
// instead: one PATCH /api/media/{id} (with If-Match) or POST /api/media/{id}/reenrich per
// selected row, never a bulk call. useConfirm()/toast need their providers, so every render
// wraps in ConfirmDialogProvider and mounts Toaster (mirrors feedback-primitives.spec.tsx's
// harness pattern). router.refresh() is exercised via a mocked next/navigation useRouter
// (app-shell.spec.tsx mocks usePathname the same way; both live in this file's mock since
// Sidebar is exercised here too).

jest.mock("next/navigation", () => ({
  // `redirect()` is left as the real implementation — it's exercised for real by the
  // "redirects /libraries into the tab" scenario below (it just throws a NEXT_REDIRECT
  // digest error synchronously; no request context is needed to construct that throw).
  ...jest.requireActual("next/navigation"),
  useRouter: jest.fn(),
  usePathname: jest.fn(),
}));

jest.mock("@/app/login/actions", () => ({
  logout: jest.fn(),
}));

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { useRouter, usePathname } from "next/navigation";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import type { LibraryDto } from "@/lib/library";
import type { AdminMediaDto, BulkFilter, Pagination } from "../app/(authed)/catalog/types";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;
const mockedUsePathname = jest
  .requireMock<{ usePathname: typeof usePathname }>("next/navigation")
  .usePathname as jest.MockedFunction<typeof usePathname>;

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

function makeMedia(count: number): AdminMediaDto[] {
  return Array.from({ length: count }, (_, i) => ({
    mediaId: String(i + 1),
    locator: `/media/${i + 1}.flac`,
    format: "flac",
    state: "ready",
    durationMs: 180000,
    title: `Track ${i + 1}`,
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
    // Deliberately distinct from mediaId — a test that asserted If-Match using mediaId by
    // mistake would fail against this fixture.
    version: String(900 + i),
    score: 50,
    neverPlay: false,
  }));
}

function makePagination(overrides: Partial<Pagination> = {}): Pagination {
  return { total: 2, pages: 1, page: 1, limit: 50, ...overrides };
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

async function renderCatalogTable(options: RenderCatalogTableOptions = {}) {
  const { CatalogTable } = await import("../app/(authed)/catalog/CatalogTable");
  const media = options.media ?? makeMedia(2);
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

/** Confirms the currently-open useConfirm() dialog (default label is "Confirm" unless overridden). */
async function confirmDialog(name = "Confirm"): Promise<void> {
  const dialog = await screen.findByRole("dialog");
  await act(async () => {
    fireEvent.click(within(dialog).getByRole("button", { name }));
    await Promise.resolve();
  });
}

// ---------------------------------------------------------------------------
// Feature: Catalog selection toolbar and Libraries tab
// ---------------------------------------------------------------------------

describe("Feature: Catalog selection toolbar and Libraries tab", () => {
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
  describe("Scenario: selection drives the toolbar", () => {
    it("shows the contextual toolbar with the selection count when rows are selected", async () => {
      await renderCatalogTable();

      expect(screen.queryByRole("region", { name: "Bulk actions" })).not.toBeInTheDocument();

      fireEvent.click(screen.getByRole("checkbox", { name: "Select Track 1" }));

      expect(screen.getByRole("region", { name: "Bulk actions" })).toBeInTheDocument();
      expect(screen.getByText("1 selected")).toBeInTheDocument();
    });

    it("select-all selects the current page", async () => {
      await renderCatalogTable({ media: makeMedia(3), pagination: makePagination({ total: 3 }) });

      fireEvent.click(screen.getByRole("checkbox", { name: "Select all rows on this page" }));

      expect(screen.getByText("3 selected")).toBeInTheDocument();
      for (const box of screen.getAllByRole("checkbox", { name: /^Select Track/ })) {
        expect(box).toBeChecked();
      }
    });

    it("hides the toolbar when the selection clears", async () => {
      await renderCatalogTable();

      const box = screen.getByRole("checkbox", { name: "Select Track 1" });
      fireEvent.click(box);
      expect(screen.getByRole("region", { name: "Bulk actions" })).toBeInTheDocument();

      fireEvent.click(box);
      expect(screen.queryByRole("region", { name: "Bulk actions" })).not.toBeInTheDocument();
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: bulk actions confirm and toast", () => {
    it("renders a confirm dialog whose consequence copy includes the count", async () => {
      makeFetchMock(204, {});
      await renderCatalogTable();

      fireEvent.click(screen.getByRole("checkbox", { name: "Select Track 1" }));
      fireEvent.click(screen.getByRole("button", { name: "Set eligible" }));

      const dialog = await screen.findByRole("dialog");
      expect(dialog).toHaveTextContent("1 selected track");
    });

    it("selection scopes the write: PATCHes the selected row with an If-Match version, and never calls the bulk endpoint", async () => {
      const mockFetch = makeFetchMock(204, {});
      await renderCatalogTable();

      fireEvent.click(screen.getByRole("checkbox", { name: "Select Track 1" }));
      fireEvent.click(screen.getByRole("button", { name: "Set eligible" }));
      await confirmDialog();

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
      const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/media/1");
      expect(init.method).toBe("PATCH");
      const headers = init.headers as Record<string, string>;
      expect(headers["Content-Type"]).toBe("application/json");
      expect(headers["If-Match"]).toBe('W/"900"');
      expect(JSON.parse(init.body as string)).toEqual({ eligible: true });
      expect(mockFetch).not.toHaveBeenCalledWith("/api/media/eligibility", expect.anything());
    });

    it("checking 3 rows sends exactly 3 single-row PATCHes — not every filter-matching row", async () => {
      const mockFetch = makeFetchMock(204, {});
      await renderCatalogTable({ media: makeMedia(3), pagination: makePagination({ total: 3 }) });

      fireEvent.click(screen.getByRole("checkbox", { name: "Select all rows on this page" }));
      fireEvent.click(screen.getByRole("button", { name: "Set eligible" }));
      await confirmDialog();

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(3));
      const urls = mockFetch.mock.calls.map((call) => call[0]);
      expect([...urls].sort()).toEqual(["/api/media/1", "/api/media/2", "/api/media/3"]);
    });

    it("toasts the result and refreshes the table on success", async () => {
      makeFetchMock(204, {});
      await renderCatalogTable();

      fireEvent.click(screen.getByRole("checkbox", { name: "Select Track 1" }));
      fireEvent.click(screen.getByRole("button", { name: "Set eligible" }));
      await confirmDialog();

      await waitFor(() => {
        expect(screen.getByText("1 track set eligible.")).toBeInTheDocument();
      });
      expect(refreshMock).toHaveBeenCalledTimes(1);
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: selection-mode reassign", () => {
    it("PATCHes the selected row with libraryId and its If-Match version", async () => {
      const mockFetch = makeFetchMock(200, {});
      await renderCatalogTable();

      fireEvent.click(screen.getByRole("checkbox", { name: "Select Track 1" }));
      fireEvent.change(screen.getByLabelText("Destination library"), { target: { value: "2" } });
      fireEvent.click(screen.getByRole("button", { name: "Reassign" }));
      await confirmDialog();

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
      const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/media/1");
      expect(init.method).toBe("PATCH");
      const headers = init.headers as Record<string, string>;
      expect(headers["If-Match"]).toBe('W/"900"');
      expect(JSON.parse(init.body as string)).toEqual({ libraryId: 2 });
      expect(mockFetch).not.toHaveBeenCalledWith("/api/media/bulk/reassign", expect.anything());
    });

    it("surfaces an out-of-scope move in the summary toast without a second confirm", async () => {
      makeFetchMock(200, { outOfScope: true });
      await renderCatalogTable();

      fireEvent.click(screen.getByRole("checkbox", { name: "Select Track 1" }));
      fireEvent.change(screen.getByLabelText("Destination library"), { target: { value: "2" } });
      fireEvent.click(screen.getByRole("button", { name: "Reassign" }));
      await confirmDialog();

      await waitFor(() => {
        expect(
          screen.getByText("1 track reassigned. 1 left rotation (destination out of scope).")
        ).toBeInTheDocument();
      });
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: selection-mode re-enrich", () => {
    it("POSTs the single-row reenrich endpoint per selected row with the chosen fields as a csv query string", async () => {
      const mockFetch = makeFetchMock(202, {});
      await renderCatalogTable();

      fireEvent.click(screen.getByRole("checkbox", { name: "Select Track 1" }));
      fireEvent.click(screen.getByRole("checkbox", { name: "cue" }));
      fireEvent.click(screen.getByRole("checkbox", { name: "energy" }));
      fireEvent.click(screen.getByRole("button", { name: "Re-analyze" }));
      await confirmDialog();

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
      const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/media/1/reenrich?fields=cue,energy");
      expect(init.method).toBe("POST");
      expect(mockFetch).not.toHaveBeenCalledWith("/api/media/bulk/reenrich", expect.anything());
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: by-filter mode survives", () => {
    it("sends the shipped by-filter body shape when the selection is empty and a filter is active", async () => {
      const mockFetch = makeFetchMock(200, { updated: 42 });
      const filter: BulkFilter = { ...EMPTY_FILTER, state: "ready", genre: "Rock" };

      await renderCatalogTable({
        pagination: makePagination({ total: 500, pages: 10 }),
        bulkFilter: filter,
        filterActive: true,
      });

      // No selection — the toolbar shows because a filter is active (by-filter mode).
      expect(screen.getByText("All 500 matching")).toBeInTheDocument();

      const select = screen.getByLabelText("Destination library");
      fireEvent.change(select, { target: { value: "2" } });
      fireEvent.click(screen.getByRole("button", { name: "Reassign" }));
      await confirmDialog();

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
      const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/media/bulk/reassign");
      const body = JSON.parse(init.body as string) as { filter: BulkFilter; toLibraryId: number };
      expect(body).toEqual({ filter, toLibraryId: 2 });
    });

    it("sends the chosen fields array on a filter-mode bulk re-enrich", async () => {
      const mockFetch = makeFetchMock(200, { scheduled: 12 });
      const filter: BulkFilter = { ...EMPTY_FILTER, state: "ready" };

      await renderCatalogTable({
        pagination: makePagination({ total: 12 }),
        bulkFilter: filter,
        filterActive: true,
      });

      fireEvent.click(screen.getByRole("checkbox", { name: "cue" }));
      fireEvent.click(screen.getByRole("checkbox", { name: "energy" }));
      fireEvent.click(screen.getByRole("button", { name: "Re-analyze" }));
      await confirmDialog();

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
      const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/media/bulk/reenrich");
      const body = JSON.parse(init.body as string) as { filter: BulkFilter; fields: string[] };
      expect(body).toEqual({ filter, fields: ["cue", "energy"] });
    });

    it("normalizes an empty field selection to all six fields (incl. bpm/year, F46.4/F48.6) on a filter-mode bulk re-enrich", async () => {
      const mockFetch = makeFetchMock(200, { scheduled: 12 });
      const filter: BulkFilter = { ...EMPTY_FILTER, state: "ready" };

      await renderCatalogTable({
        pagination: makePagination({ total: 12 }),
        bulkFilter: filter,
        filterActive: true,
      });

      fireEvent.click(screen.getByRole("button", { name: "Re-analyze" }));
      await confirmDialog();

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
      const [, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      const body = JSON.parse(init.body as string) as { fields: string[] };
      expect(body.fields).toEqual(["cue", "energy", "loudness", "tags", "bpm", "year"]);
    });

    it("offers BPM and Year lookup (retry) alongside the four original tokens, with the year token wired as the query field name", async () => {
      const mockFetch = makeFetchMock(202, {});
      await renderCatalogTable();

      fireEvent.click(screen.getByRole("checkbox", { name: "Select Track 1" }));
      fireEvent.click(screen.getByRole("checkbox", { name: "BPM" }));
      fireEvent.click(screen.getByRole("checkbox", { name: "Year lookup (retry)" }));
      fireEvent.click(screen.getByRole("button", { name: "Re-analyze" }));
      await confirmDialog();

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
      const [url] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/media/1/reenrich?fields=bpm,year");
    });
  });

  // ---------------------------------------------------------------------------
  // STORY-115 Q7 review, Finding 1: page.tsx's bulkFilter passes `sp.state ?? null` etc.
  // through verbatim — an empty-but-submitted filter form field arrives here as `""`, not
  // `null` (the catalog filter form always submits state/artist/genre/q as present fields).
  // `hasBulkFilter` must treat `""` the same as `null`, or a bare "Filter" submit with nothing
  // entered would light up the by-filter bulk toolbar over the entire unfiltered catalog.
  describe("Scenario: empty-string filter fields don't trigger by-filter mode", () => {
    it('hides the toolbar when every string filter field is "" (the shape a bare form submit actually produces)', async () => {
      const emptyStringFilter: BulkFilter = {
        state: "",
        artist: "",
        genre: "",
        q: "",
        libraryId: null,
        eligible: null,
      };

      await renderCatalogTable({
        pagination: makePagination({ total: 500, pages: 10 }),
        bulkFilter: emptyStringFilter,
        // Matches production: page.tsx's isFilterActive already guards "" out, so a bare
        // submit computes filterActive: false too — the regression was hasBulkFilter no
        // longer honoring that at all, not filterActive being wrong.
        filterActive: false,
      });

      expect(screen.queryByRole("region", { name: "Bulk actions" })).not.toBeInTheDocument();
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: libraries tab", () => {
    it("renders library CRUD inside the Catalog Libraries tab", async () => {
      const { LibrariesTab } = await import("../app/(authed)/catalog/LibrariesTab");
      render(
        <ConfirmDialogProvider>
          <LibrariesTab initialLibraries={LIBRARIES} />
          <Toaster />
        </ConfirmDialogProvider>
      );

      expect(screen.getByText("In Rotation")).toBeInTheDocument();
      expect(screen.getByRole("form", { name: "Create library" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Delete In Rotation" })).toBeInTheDocument();
    });

    it("keeps the 409 dependentMediaCount delete dialog", async () => {
      makeFetchMock(409, { dependentMediaCount: 7 });
      const { LibrariesTab } = await import("../app/(authed)/catalog/LibrariesTab");
      render(
        <ConfirmDialogProvider>
          <LibrariesTab initialLibraries={LIBRARIES} />
          <Toaster />
        </ConfirmDialogProvider>
      );

      fireEvent.click(screen.getByRole("button", { name: "Delete In Rotation" }));
      await confirmDialog("Delete");

      await waitFor(() => {
        expect(screen.getByText("Library has 7 tracks — reassign them first.")).toBeInTheDocument();
      });
    });

    it("redirects /libraries into the tab", async () => {
      const { default: LibrariesPage } = await import("../app/(authed)/libraries/page");

      let digest: string | undefined;
      try {
        LibrariesPage();
        throw new Error("expected LibrariesPage() to redirect");
      } catch (err) {
        digest = (err as { digest?: string }).digest;
      }

      expect(digest).toContain("NEXT_REDIRECT");
      expect(digest).toContain("/catalog?tab=libraries");
    });

    it("lists no Libraries item in the sidebar", async () => {
      mockedUsePathname.mockReturnValue("/catalog");

      const { Sidebar } = await import("../app/(authed)/_components/Sidebar");
      render(<Sidebar />);

      expect(screen.queryByRole("link", { name: /libraries/i })).not.toBeInTheDocument();
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: loading and empty states", () => {
    it("renders skeleton rows during fetch", async () => {
      const { default: CatalogLoading } = await import("../app/(authed)/catalog/loading");
      render(<CatalogLoading />);

      expect(screen.getAllByRole("status", { name: "Loading" }).length).toBeGreaterThanOrEqual(6);
    });

    it("distinguishes empty-catalog from empty-filter-result", async () => {
      const { unmount } = await renderCatalogTable({
        media: [],
        pagination: makePagination({ total: 0 }),
        filterActive: false,
      });
      expect(screen.getByText("The catalog is empty")).toBeInTheDocument();
      unmount();

      await renderCatalogTable({ media: [], pagination: makePagination({ total: 0 }), filterActive: true });
      expect(screen.getByText("No tracks match this filter")).toBeInTheDocument();
    });

    it("offers a clear-filters CTA on an empty filter result", async () => {
      await renderCatalogTable({ media: [], pagination: makePagination({ total: 0 }), filterActive: true });

      expect(screen.getByRole("link", { name: "Clear filters" })).toHaveAttribute("href", "/catalog");
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: a by-filter bulk call fails outright (sad path)", () => {
    it("toasts the error and does not refresh", async () => {
      makeFetchMock(500, {});
      await renderCatalogTable({
        pagination: makePagination({ total: 500, pages: 10 }),
        bulkFilter: { ...EMPTY_FILTER, state: "ready" },
        filterActive: true,
      });

      fireEvent.click(screen.getByRole("button", { name: "Set eligible" }));
      await confirmDialog();

      await waitFor(() => {
        expect(screen.getByText("Server error (500)")).toBeInTheDocument();
      });
      expect(refreshMock).not.toHaveBeenCalled();
    });
  });

  // ---------------------------------------------------------------------------
  // F45.1 (closes gitea-#201) widened the refresh gate to also fire on an all-conflict failure, so
  // this fixture now uses a non-conflict status (500) to keep asserting the no-refresh half of
  // Q7's semantics — the all-conflict-refreshes half is covered by conflict-retry-fidelity.spec.tsx.
  describe("Scenario: every row in a selection fails with a non-conflict error (sad path)", () => {
    it("toasts a failure summary, keeps the row selected, and skips the refresh (nothing changed server-side)", async () => {
      makeFetchMock(500, {});
      await renderCatalogTable();

      fireEvent.click(screen.getByRole("checkbox", { name: "Select Track 1" }));
      fireEvent.click(screen.getByRole("button", { name: "Set eligible" }));
      await confirmDialog();

      await waitFor(() => {
        expect(screen.getByText("1 of 1 failed.")).toBeInTheDocument();
      });

      expect(screen.getByText("1 selected")).toBeInTheDocument();
      expect(screen.getByRole("checkbox", { name: "Select Track 1" })).toBeChecked();
      // A non-conflict total failure means nothing changed and no version is stale: refresh is
      // skipped (gated on succeeded.length > 0 || any conflict — F45.1), matching the by-filter
      // error path. This is also what keeps the selection intact in production — no refresh
      // means CatalogTable's media-reconcile effect never runs.
      expect(refreshMock).not.toHaveBeenCalled();
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: one row in a selection fails (partial failure, sad path)", () => {
    // This exercises the production wiring end to end, not just the internal onOutcome() seam:
    // router.refresh() is stubbed to simulate what it does for real — deliver a *new* `media`
    // array (fresh identity, same rows) to CatalogTable — so CatalogTable's own [media]-reconcile
    // effect (not a mock) is what ultimately decides which rows stay checked. A router.refresh
    // mock that's a bare no-op would never exercise that effect and would pass even if the
    // selection-wipe regression (Q7 re-review Finding) were still present.
    it("reports an error summary, drops the succeeded row from the selection, and keeps the failed row selected once the refresh lands", async () => {
      const media = makeMedia(2);
      const mockFetch = jest.fn<typeof fetch>().mockImplementation((input) => {
        const failing = String(input) === "/api/media/2";
        return Promise.resolve({
          ok: !failing,
          status: failing ? 409 : 204,
          json: jest.fn<() => Promise<unknown>>().mockResolvedValue({}),
          headers: new Headers(),
        } as unknown as Response);
      });
      global.fetch = mockFetch as unknown as typeof fetch;

      const { CatalogTable } = await import("../app/(authed)/catalog/CatalogTable");
      const tableProps = {
        pagination: makePagination({ total: 2 }),
        libraries: LIBRARIES,
        bulkFilter: EMPTY_FILTER,
        filterActive: false,
        clearFiltersHref: "/catalog",
      };

      const utils = render(
        <ConfirmDialogProvider>
          <CatalogTable media={media} {...tableProps} />
          <Toaster />
        </ConfirmDialogProvider>
      );

      // Simulate what router.refresh() does for real: the server component re-fetches and hands
      // CatalogTable a brand-new `media` array (new identity — same rows, freshly loaded).
      refreshMock.mockImplementation(() => {
        utils.rerender(
          <ConfirmDialogProvider>
            <CatalogTable media={media.map((m) => ({ ...m }))} {...tableProps} />
            <Toaster />
          </ConfirmDialogProvider>
        );
      });

      fireEvent.click(screen.getByRole("checkbox", { name: "Select all rows on this page" }));
      fireEvent.click(screen.getByRole("button", { name: "Set eligible" }));
      await confirmDialog();

      await waitFor(() => {
        expect(screen.getByText("1 of 2 failed.")).toBeInTheDocument();
      });

      expect(refreshMock).toHaveBeenCalledTimes(1);
      expect(screen.getByRole("region", { name: "Bulk actions" })).toBeInTheDocument();
      expect(screen.getByText("1 selected")).toBeInTheDocument();
      expect(screen.getByRole("checkbox", { name: "Select Track 1" })).not.toBeChecked();
      expect(screen.getByRole("checkbox", { name: "Select Track 2" })).toBeChecked();
    });
  });

  // ---------------------------------------------------------------------------
  // Row rendering moved here from catalog-pages.spec.ts (Q7 moved rows into this
  // "use client" component — see that file's header comment).
  describe("Scenario: track rows render links and eligibility badges", () => {
    it("each row's title links to /catalog/<mediaId> and shows a Yes/No eligibility badge", async () => {
      const media = makeMedia(2).map((m, i) => (i === 1 ? { ...m, eligible: false } : m));
      await renderCatalogTable({ media, pagination: makePagination({ total: 2 }) });

      expect(screen.getByRole("link", { name: "Track 1" })).toHaveAttribute("href", "/catalog/1");
      expect(screen.getByRole("link", { name: "Track 2" })).toHaveAttribute("href", "/catalog/2");
      expect(screen.getAllByText("Yes").length).toBeGreaterThan(0);
      expect(screen.getAllByText("No").length).toBeGreaterThan(0);
    });
  });
});

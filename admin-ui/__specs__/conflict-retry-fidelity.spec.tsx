// @jest-environment jsdom
// STORY-140 — Conflict retries succeed in place (Epic V / SPEC F45, closes gitea-#201, gitea-#202)
//
// Runner: Jest (jsdom) + @testing-library/react. Authored PENDING at /plan time (2026-07-14,
// house rule since Epic S) as it.todo entries — V3 implements and converts them to real specs
// against CatalogToolbar and MoveToLibraryAction. V3 also amends exactly ONE existing Q7
// total-failure assertion in catalog-selection-toolbar.spec.tsx (the refresh gate widens to
// conflicts; failed-rows-stay-selected — the part Q7 actually locked — is preserved).
//
// Both components touch next/navigation's useRouter(), so — mirroring
// catalog-selection-toolbar.spec.tsx and catalog-library-actions.spec.tsx — it's mocked up front
// and every component under test is imported dynamically inside the test body (next/jest's SWC
// transform hoists static imports ahead of jest.mock() otherwise).

jest.mock("next/navigation", () => ({
  ...jest.requireActual("next/navigation"),
  useRouter: jest.fn(),
}));

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
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

function makeMedia(version: string): AdminMediaDto[] {
  return [
    {
      mediaId: "1",
      locator: "/media/1.flac",
      format: "flac",
      state: "ready",
      durationMs: 180000,
      title: "Track 1",
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
      version,
      score: 50,
      neverPlay: false,
    },
  ];
}

function makePagination(overrides: Partial<Pagination> = {}): Pagination {
  return { total: 1, pages: 1, page: 1, limit: 50, ...overrides };
}

/** A fetch response resolving to `status`/`body` — mirrors the sibling specs' response shape. */
function jsonResponse(status: number, body: unknown = {}): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
    headers: new Headers(),
  } as unknown as Response;
}

function makeFetchMock(status: number, body: unknown = {}): jest.MockedFunction<typeof fetch> {
  const fn = jest.fn<typeof fetch>().mockResolvedValue(jsonResponse(status, body));
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

interface RenderCatalogTableOptions {
  media: AdminMediaDto[];
}

async function renderCatalogTable(options: RenderCatalogTableOptions) {
  const { CatalogTable } = await import("../app/(authed)/catalog/CatalogTable");
  return render(
    <ConfirmDialogProvider>
      <CatalogTable
        media={options.media}
        pagination={makePagination()}
        libraries={LIBRARIES}
        bulkFilter={EMPTY_FILTER}
        filterActive={false}
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
// Feature: Conflict retries succeed in place
// ---------------------------------------------------------------------------

describe("Feature: Conflict retries succeed in place", () => {
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
  describe("Scenario: an all-conflict batch refreshes versions", () => {
    it("fires router.refresh() when every row in the batch fails with a conflict (409/412) (F45.1)", async () => {
      makeFetchMock(409, {});
      await renderCatalogTable({ media: makeMedia("900") });

      fireEvent.click(screen.getByRole("checkbox", { name: "Select Track 1" }));
      fireEvent.click(screen.getByRole("button", { name: "Set eligible" }));
      await confirmDialog();

      await waitFor(() => {
        expect(screen.getByText("1 of 1 failed.")).toBeInTheDocument();
      });
      expect(refreshMock).toHaveBeenCalledTimes(1);
    });

    it("keeps the failed rows selected after the conflict refresh (F45.1)", async () => {
      makeFetchMock(409, {});
      await renderCatalogTable({ media: makeMedia("900") });

      fireEvent.click(screen.getByRole("checkbox", { name: "Select Track 1" }));
      fireEvent.click(screen.getByRole("button", { name: "Set eligible" }));
      await confirmDialog();

      await waitFor(() => {
        expect(screen.getByText("1 of 1 failed.")).toBeInTheDocument();
      });
      expect(screen.getByRole("checkbox", { name: "Select Track 1" })).toBeChecked();
    });

    it("allows the immediate retry to send fresh versions after the refresh (F45.1)", async () => {
      const initialMedia = makeMedia("900");
      const mockFetch = jest
        .fn<typeof fetch>()
        .mockResolvedValueOnce(jsonResponse(409, {}))
        .mockResolvedValueOnce(jsonResponse(204, {}));
      global.fetch = mockFetch as unknown as typeof fetch;

      const tableProps = {
        pagination: makePagination(),
        libraries: LIBRARIES,
        bulkFilter: EMPTY_FILTER,
        filterActive: false,
        clearFiltersHref: "/catalog",
      };

      const { CatalogTable } = await import("../app/(authed)/catalog/CatalogTable");
      const utils = render(
        <ConfirmDialogProvider>
          <CatalogTable media={initialMedia} {...tableProps} />
          <Toaster />
        </ConfirmDialogProvider>
      );

      // Simulate what router.refresh() does for real: the server component re-fetches and hands
      // CatalogTable a fresh `media` array carrying the row's current (post-conflict) version.
      const refreshedMedia = makeMedia("901");
      refreshMock.mockImplementation(() => {
        utils.rerender(
          <ConfirmDialogProvider>
            <CatalogTable media={refreshedMedia} {...tableProps} />
            <Toaster />
          </ConfirmDialogProvider>
        );
      });

      fireEvent.click(screen.getByRole("checkbox", { name: "Select Track 1" }));
      fireEvent.click(screen.getByRole("button", { name: "Set eligible" }));
      await confirmDialog();

      await waitFor(() => expect(refreshMock).toHaveBeenCalledTimes(1));
      await waitFor(() => expect(screen.getByRole("checkbox", { name: "Select Track 1" })).toBeChecked());

      // Retry — the row is still selected post-refresh.
      fireEvent.click(screen.getByRole("button", { name: "Set eligible" }));
      await confirmDialog();

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(2));
      const [, secondInit] = mockFetch.mock.calls[1] as [string, RequestInit];
      const headers = secondInit.headers as Record<string, string>;
      expect(headers["If-Match"]).toBe('W/"901"');
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: move-to-library recovers in place", () => {
    it("consumes the refreshed server-rendered version after a 409 — no surviving stale local version state (F45.2)", async () => {
      const mockFetch = jest
        .fn<typeof fetch>()
        .mockResolvedValueOnce(jsonResponse(409, {}))
        .mockResolvedValueOnce(jsonResponse(200, {}));
      global.fetch = mockFetch as unknown as typeof fetch;

      const { MoveToLibraryAction } = await import(
        "../app/(authed)/catalog/[mediaId]/MoveToLibraryAction"
      );

      const utils = render(
        <ConfirmDialogProvider>
          <MoveToLibraryAction
            mediaId="track-1"
            etag='W/"900"'
            currentLibraryId={1}
            libraries={LIBRARIES}
            scopeLibraryIds={[]}
          />
          <Toaster />
        </ConfirmDialogProvider>
      );

      // Simulate router.refresh(): the page's server component re-fetches and hands this
      // component a fresh `etag` prop carrying the row's current (post-conflict) version.
      refreshMock.mockImplementation(() => {
        utils.rerender(
          <ConfirmDialogProvider>
            <MoveToLibraryAction
              mediaId="track-1"
              etag='W/"901"'
              currentLibraryId={1}
              libraries={LIBRARIES}
              scopeLibraryIds={[]}
            />
            <Toaster />
          </ConfirmDialogProvider>
        );
      });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /move/i }));
        await Promise.resolve();
      });
      await waitFor(() => expect(refreshMock).toHaveBeenCalledTimes(1));

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /move/i }));
        await Promise.resolve();
      });

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(2));
      const [, secondInit] = mockFetch.mock.calls[1] as [string, RequestInit];
      const headers = secondInit.headers as Record<string, string>;
      expect(headers["If-Match"]).toBe('W/"901"');
    });

    it("sends the fresh If-Match on an immediate in-place retry, which succeeds (F45.2)", async () => {
      const mockFetch = jest
        .fn<typeof fetch>()
        .mockResolvedValueOnce(jsonResponse(409, {}))
        .mockResolvedValueOnce(jsonResponse(200, {}));
      global.fetch = mockFetch as unknown as typeof fetch;

      const { MoveToLibraryAction } = await import(
        "../app/(authed)/catalog/[mediaId]/MoveToLibraryAction"
      );

      const utils = render(
        <ConfirmDialogProvider>
          <MoveToLibraryAction
            mediaId="track-1"
            etag='W/"900"'
            currentLibraryId={1}
            libraries={LIBRARIES}
            scopeLibraryIds={[]}
          />
          <Toaster />
        </ConfirmDialogProvider>
      );

      refreshMock.mockImplementation(() => {
        utils.rerender(
          <ConfirmDialogProvider>
            <MoveToLibraryAction
              mediaId="track-1"
              etag='W/"901"'
              currentLibraryId={1}
              libraries={LIBRARIES}
              scopeLibraryIds={[]}
            />
            <Toaster />
          </ConfirmDialogProvider>
        );
      });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /move/i }));
        await Promise.resolve();
      });
      await waitFor(() => expect(refreshMock).toHaveBeenCalledTimes(1));

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /move/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByText("Library updated.")).toBeInTheDocument();
      });
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario (sad path): non-conflict total failure still skips the refresh", () => {
    it("does not fire router.refresh() when every row fails with a non-conflict error — nothing changed server-side (F45.1)", async () => {
      makeFetchMock(500, {});
      await renderCatalogTable({ media: makeMedia("900") });

      fireEvent.click(screen.getByRole("checkbox", { name: "Select Track 1" }));
      fireEvent.click(screen.getByRole("button", { name: "Set eligible" }));
      await confirmDialog();

      await waitFor(() => {
        expect(screen.getByText("1 of 1 failed.")).toBeInTheDocument();
      });
      expect(refreshMock).not.toHaveBeenCalled();
    });

    it("surfaces the failure toast unchanged per F31.3 (F45.3)", async () => {
      makeFetchMock(500, {});
      await renderCatalogTable({ media: makeMedia("900") });

      fireEvent.click(screen.getByRole("checkbox", { name: "Select Track 1" }));
      fireEvent.click(screen.getByRole("button", { name: "Set eligible" }));
      await confirmDialog();

      await waitFor(() => {
        expect(screen.getByText("1 of 1 failed.")).toBeInTheDocument();
      });
    });
  });
});

// @jest-environment jsdom
// STORY-250/STORY-251 — Admin UI: explicit badge + operator override control in the catalog table
// (Epic F95, PLAN T116).
//
// Runner: Jest (jsdom) + @testing-library/react + mocked fetch, mirroring catalog-rating.spec.tsx's
// house pattern: CatalogTable is rendered directly via RTL for row-level assertions (badge text,
// the tri-state override select's PUT bodies). ConfirmDialogProvider/Toaster wrap every render —
// CatalogTable calls useConfirm() unconditionally via CatalogToolbar's descendants.

jest.mock("next/navigation", () => ({
  ...jest.requireActual("next/navigation"),
  useRouter: jest.fn(),
}));

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, fireEvent, waitFor, act } from "@testing-library/react";
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

const LIBRARIES: LibraryDto[] = [{ id: 1, name: "In Rotation", mediaCount: 50 }];

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
  title?: string;
  explicit?: boolean | null;
  explicitSource?: "tag" | "llm" | "operator" | null;
}

function makeRow(overrides: RowOverrides = {}): AdminMediaDto {
  const mediaId = overrides.mediaId ?? "1";
  return {
    mediaId,
    locator: `/media/${mediaId}.flac`,
    format: "flac",
    state: "ready",
    durationMs: 180000,
    title: overrides.title ?? `Track ${mediaId}`,
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
    version: `${900 + Number(mediaId)}`,
    score: 50,
    neverPlay: false,
    explicit: overrides.explicit,
    explicitSource: overrides.explicitSource,
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

/** A bare `Response`-shaped resolution value for a deferred fetch — same shape `makeFetchMock`
 * builds, but standalone so a test can hand it to a held-open promise instead of a resolved mock. */
function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
    headers: new Headers({ "content-type": "application/json" }),
  } as unknown as Response;
}

/** A promise plus its own resolver, exposed separately — lets a test hold a `fetch()` call open
 * across assertions (review F1: the select must stay on the operator's pick and stay disabled for
 * the whole PUT round trip) rather than resolving immediately like `makeFetchMock`. No `!` —
 * `resolveFn` is guaranteed assigned by the time the `Promise` executor returns (it runs
 * synchronously), but the wrapper still guards it dynamically instead of asserting that past the
 * compiler. */
function deferred<T>(): { promise: Promise<T>; resolve: (value: T) => void } {
  let resolveFn: ((value: T) => void) | undefined;
  const promise = new Promise<T>((res) => {
    resolveFn = res;
  });
  return {
    promise,
    resolve: (value: T) => {
      if (resolveFn === undefined) throw new Error("deferred: resolve called before its executor ran");
      resolveFn(value);
    },
  };
}

interface RenderCatalogTableOptions {
  media?: AdminMediaDto[];
  pagination?: Pagination;
}

async function renderCatalogTable(options: RenderCatalogTableOptions = {}) {
  const { CatalogTable } = await import("../app/(authed)/catalog/CatalogTable");
  const media = options.media ?? [makeRow()];
  return render(
    <ConfirmDialogProvider>
      <CatalogTable
        media={media}
        pagination={options.pagination ?? makePagination({ total: media.length })}
        libraries={LIBRARIES}
        bulkFilter={EMPTY_FILTER}
        filterActive={false}
        clearFiltersHref="/catalog"
      />
      <Toaster />
    </ConfirmDialogProvider>
  );
}

// ---------------------------------------------------------------------------
// Feature: Explicit classification in the Catalog page
// ---------------------------------------------------------------------------

describe("Feature: Explicit classification in the Catalog page", () => {
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
  describe("Scenario: the catalog badges a row's explicit classification", () => {
    it("shows a clear 'Explicit · tag' badge when explicit=true, source=tag (F95.2)", async () => {
      await renderCatalogTable({ media: [makeRow({ explicit: true, explicitSource: "tag" })] });

      expect(screen.getByText("Explicit · tag")).toBeInTheDocument();
    });

    it("shows a subtle 'Clean · llm' affordance when explicit=false, source=llm (F95.2)", async () => {
      await renderCatalogTable({ media: [makeRow({ explicit: false, explicitSource: "llm" })] });

      expect(screen.getByText("Clean · llm")).toBeInTheDocument();
      // Distinct from the "Explicit" chip treatment — never a bordered chip.
      expect(screen.queryByText("Explicit · llm")).not.toBeInTheDocument();
    });

    it("renders no badge at all for an unclassified (null) row (F95.2)", async () => {
      await renderCatalogTable({ media: [makeRow({ explicit: null, explicitSource: null })] });

      expect(screen.queryByTestId("explicit-badge")).not.toBeInTheDocument();
    });

    it("badges an operator-sourced classification the same way (F95.3)", async () => {
      await renderCatalogTable({ media: [makeRow({ explicit: true, explicitSource: "operator" })] });

      expect(screen.getByText("Explicit · operator")).toBeInTheDocument();
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: the operator overrides a row's classification", () => {
    it("renders the tri-state override select preset to the row's current state (F95.3)", async () => {
      await renderCatalogTable({
        media: [makeRow({ mediaId: "5", title: "Loud Track", explicit: true, explicitSource: "tag" })],
      });

      const select = screen.getByRole("combobox", {
        name: "Explicit override for Loud Track",
      }) as HTMLSelectElement;
      expect(select.value).toBe("true");
    });

    it("picking 'Explicit' PUTs { explicit: true } (F95.3)", async () => {
      const mockFetch = makeFetchMock({ explicit: true, explicitSource: "operator" });
      await renderCatalogTable({
        media: [makeRow({ mediaId: "5", title: "Track 5", explicit: null, explicitSource: null })],
      });

      const select = screen.getByRole("combobox", { name: "Explicit override for Track 5" });
      fireEvent.change(select, { target: { value: "true" } });

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
      const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/media/5/explicit");
      expect(init.method).toBe("PUT");
      expect(JSON.parse(init.body as string)).toEqual({ explicit: true });
      await waitFor(() => expect(screen.getByText("Explicit · operator")).toBeInTheDocument());
      expect(refreshMock).toHaveBeenCalledTimes(1);
    });

    it("picking 'Clean' PUTs { explicit: false } (F95.3)", async () => {
      const mockFetch = makeFetchMock({ explicit: false, explicitSource: "operator" });
      await renderCatalogTable({
        media: [makeRow({ mediaId: "6", title: "Track 6", explicit: true, explicitSource: "llm" })],
      });

      const select = screen.getByRole("combobox", { name: "Explicit override for Track 6" });
      fireEvent.change(select, { target: { value: "false" } });

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
      const [, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(JSON.parse(init.body as string)).toEqual({ explicit: false });
      await waitFor(() => expect(screen.getByText("Clean · operator")).toBeInTheDocument());
    });

    it("picking 'Unknown' PUTs { explicit: null } — clear-to-unknown (F95.3, F95.5)", async () => {
      const mockFetch = makeFetchMock({ explicit: null, explicitSource: null });
      await renderCatalogTable({
        media: [makeRow({ mediaId: "7", title: "Track 7", explicit: true, explicitSource: "operator" })],
      });

      const select = screen.getByRole("combobox", { name: "Explicit override for Track 7" });
      fireEvent.change(select, { target: { value: "" } });

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
      const [, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(JSON.parse(init.body as string)).toEqual({ explicit: null });
      await waitFor(() => expect(screen.queryByTestId("explicit-badge")).not.toBeInTheDocument());
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: the select holds the operator's pick for the whole PUT round trip (review F1)", () => {
    it("keeps the picked option selected and the select disabled while pending, then settles on success", async () => {
      const pending = deferred<Response>();
      global.fetch = jest.fn<typeof fetch>().mockReturnValue(pending.promise) as unknown as typeof fetch;
      await renderCatalogTable({
        media: [makeRow({ mediaId: "11", title: "Track 11", explicit: true, explicitSource: "tag" })],
      });

      const select = screen.getByRole("combobox", {
        name: "Explicit override for Track 11",
      }) as HTMLSelectElement;
      fireEvent.change(select, { target: { value: "false" } });

      // Mid-flight: the picked option stays put — it must never snap back to the prior
      // server-truth "true" while the PUT is outstanding — and the select is disabled.
      expect(select.value).toBe("false");
      expect(select).toBeDisabled();

      await act(async () => {
        pending.resolve(jsonResponse({ explicit: false, explicitSource: "operator" }));
        await Promise.resolve();
      });

      await waitFor(() => expect(select).toBeEnabled());
      expect(select.value).toBe("false");
      expect(screen.getByText("Clean · operator")).toBeInTheDocument();
    });

    it("reverts the select to server truth when the PUT fails", async () => {
      const pending = deferred<Response>();
      global.fetch = jest.fn<typeof fetch>().mockReturnValue(pending.promise) as unknown as typeof fetch;
      await renderCatalogTable({
        media: [makeRow({ mediaId: "12", title: "Track 12", explicit: true, explicitSource: "tag" })],
      });

      const select = screen.getByRole("combobox", {
        name: "Explicit override for Track 12",
      }) as HTMLSelectElement;
      fireEvent.change(select, { target: { value: "" } });

      // Mid-flight: still holding the pick, same as the success path above.
      expect(select.value).toBe("");
      expect(select).toBeDisabled();

      await act(async () => {
        pending.resolve(jsonResponse({}, 403));
        await Promise.resolve();
      });

      await waitFor(() => expect(select).toBeEnabled());
      expect(select.value).toBe("true");
      expect(screen.getByText("You don't have permission to make this change.")).toBeInTheDocument();
    });
  });

  // ---------------------------------------------------------------------------
  // SAD PATH
  // ---------------------------------------------------------------------------

  describe("Scenario: a failed override surfaces a toast (sad path)", () => {
    it("toasts the classified failure and leaves the prior state on screen (F31.3)", async () => {
      makeFetchMock({}, 403);
      await renderCatalogTable({
        media: [makeRow({ mediaId: "9", title: "Track 9", explicit: true, explicitSource: "tag" })],
      });

      const select = screen.getByRole("combobox", { name: "Explicit override for Track 9" });
      fireEvent.change(select, { target: { value: "false" } });

      await waitFor(() => {
        expect(screen.getByText("You don't have permission to make this change.")).toBeInTheDocument();
      });
      expect(screen.getByText("Explicit · tag")).toBeInTheDocument();
    });
  });
});

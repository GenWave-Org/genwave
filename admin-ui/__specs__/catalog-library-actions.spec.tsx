// @jest-environment jsdom
// STORY-052 — Admin UI: reassign actions (single-row)
// Re-pointed at Q8 (STORY-090, SPEC F28.9) — the wire contract (PATCH /api/media/{id}, If-Match,
// the X-Out-Of-Scope allow-and-warn body shape) is unchanged; only the presentation moved from
// window.confirm + inline status/alert paragraphs to useConfirm + toasts.
//
// The bulk half of this file (BulkReassignControl) was retired at Q7 (STORY-089, SPEC F28.11) —
// its coverage, including the exact request-body assertions, lives in
// catalog-selection-toolbar.spec.tsx against CatalogToolbar (the byte-compatible replacement).
// See track-detail-redesign.spec.tsx for the STORY-090 acceptance-criteria-level scenario; this
// file keeps the granular field/wire-contract coverage for the single-row MoveToLibraryAction.
//
// R9 (STORY-104, SPEC F31.2-F31.3): the PATCH now runs through the shared row-PATCH hook, which
// calls router.refresh() on a 409/412 conflict so the row is current for an immediate retry —
// MoveToLibraryAction now touches next/navigation, so it moves to a dynamic import behind
// jest.mock() (mirrors edit-track.spec.tsx's pattern; next/jest's SWC transform hoists static
// imports ahead of jest.mock(), unlike babel-jest).

jest.mock("next/navigation", () => ({
  ...jest.requireActual("next/navigation"),
  useRouter: jest.fn(),
}));

import {
  describe,
  it,
  expect,
  beforeEach,
  afterEach,
  jest,
} from "@jest/globals";
import { render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { ComponentProps } from "react";
import type { useRouter } from "next/navigation";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import type { MoveToLibraryAction } from "../app/(authed)/catalog/[mediaId]/MoveToLibraryAction";
import type { LibraryDto } from "../lib/library";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const MEDIA_ID = "track-abc";
const ETAG = 'W/"etag-42"';

const LIBRARIES: LibraryDto[] = [
  { id: 1, name: "In Rotation", mediaCount: 50 },
  { id: 2, name: "Archive", mediaCount: 20 },
];

function makeFetchMock(
  status: number,
  body: unknown = {},
  headers: Record<string, string> = {}
): jest.MockedFunction<typeof fetch> {
  const fn = jest
    .fn<typeof fetch>()
    .mockResolvedValue({
      ok: status >= 200 && status < 300,
      status,
      json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
      headers: new Headers({ "content-type": "application/json", ...headers }),
    } as unknown as Response);
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

/**
 * Dynamically imports MoveToLibraryAction — it now calls `useRouter()` (R9, SPEC F31.3: refresh
 * the row on a conflict), so next/jest's SWC transform would otherwise hoist a static import of
 * it ahead of the jest.mock() registration above and bind to the real next/navigation module
 * (mirrors edit-track.spec.tsx's pattern).
 */
async function renderMoveAction(
  props: Partial<ComponentProps<typeof MoveToLibraryAction>> = {}
): Promise<ReturnType<typeof render>> {
  const { MoveToLibraryAction: Component } = await import(
    "../app/(authed)/catalog/[mediaId]/MoveToLibraryAction"
  );
  return render(
    <ConfirmDialogProvider>
      <Component
        mediaId={MEDIA_ID}
        etag={ETAG}
        currentLibraryId={null}
        libraries={LIBRARIES}
        scopeLibraryIds={[]}
        {...props}
      />
      <Toaster />
    </ConfirmDialogProvider>
  );
}

/** Confirms the currently-open useConfirm() dialog. */
async function confirmDialog(name = "Move"): Promise<void> {
  const dialog = await screen.findByRole("dialog");
  await act(async () => {
    fireEvent.click(within(dialog).getByRole("button", { name }));
    await Promise.resolve();
  });
}

// ---------------------------------------------------------------------------
// Feature: Reassign a single track to a different library
// ---------------------------------------------------------------------------

describe("Feature: Reassign a single track to a different library", () => {
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

  describe("Scenario: in-scope reassign saves cleanly", () => {
    it("changing library_id and saving PATCHes /api/media/{id} with { libraryId } and the loaded If-Match", async () => {
      const mockFetch = makeFetchMock(200, {});

      await renderMoveAction({ currentLibraryId: 1, scopeLibraryIds: [1, 2] });

      // Change destination to "Archive" (id=2)
      const select = screen.getByLabelText("Destination library");
      fireEvent.change(select, { target: { value: "2" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /move/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledTimes(1);
      });

      const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe(`/api/media/${MEDIA_ID}`);
      expect(init.method).toBe("PATCH");

      const headers = init.headers as Record<string, string>;
      expect(headers["If-Match"]).toBe(ETAG);
      expect(headers["Content-Type"]).toBe("application/json");

      const body = JSON.parse(init.body as string) as { libraryId: number };
      expect(body.libraryId).toBe(2);
    });
  });

  describe("Scenario: cross-scope reassign asks the operator to confirm", () => {
    it("an in-scope destination is submitted without opening the confirm dialog", async () => {
      makeFetchMock(200, {});

      await renderMoveAction({ scopeLibraryIds: [1, 2] });

      // Both libraries are in scope — select "Archive" (id=2)
      const select = screen.getByLabelText("Destination library");
      fireEvent.change(select, { target: { value: "2" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /move/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        // fetch was called (not blocked by a cancel)
        expect(global.fetch).toHaveBeenCalledTimes(1);
      });

      // No confirm dialog for an in-scope destination.
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });

    it("a destination not in scope opens a confirm dialog before submitting", async () => {
      const mockFetch = makeFetchMock(200, {});

      await renderMoveAction({ scopeLibraryIds: [1] }); // only lib 1 is in scope

      // Select "Archive" (id=2) — out of scope
      const select = screen.getByLabelText("Destination library");
      fireEvent.change(select, { target: { value: "2" } });

      fireEvent.click(screen.getByRole("button", { name: /move/i }));

      const dialog = await screen.findByRole("dialog");
      expect(dialog).toHaveTextContent(/not in rotation/i);

      // Cancelling the dialog does not fire the PATCH.
      fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));
      await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
      expect(mockFetch).not.toHaveBeenCalled();
    });

    it("confirming the out-of-scope dialog fires the PATCH", async () => {
      const mockFetch = makeFetchMock(200, {});

      await renderMoveAction({ scopeLibraryIds: [1] });

      const select = screen.getByLabelText("Destination library");
      fireEvent.change(select, { target: { value: "2" } });
      fireEvent.click(screen.getByRole("button", { name: /move/i }));
      await confirmDialog();

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
    });

    it("the allow-and-warn notice is driven by outOfScope on the response, not by client-side scope state, and surfaces as a toast", async () => {
      // Response body carries outOfScope: true
      makeFetchMock(200, { outOfScope: true });

      await renderMoveAction({ scopeLibraryIds: [] }); // scope unknown client-side — no pre-submit dialog

      const select = screen.getByLabelText("Destination library");
      fireEvent.change(select, { target: { value: "2" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /move/i }));
        await Promise.resolve();
      });

      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

      // The "left rotation" notice is toast-driven by the response body.
      await waitFor(() => {
        expect(screen.getByText("Library updated. This track has left rotation.")).toBeInTheDocument();
      });
    });
  });

  // -------------------------------------------------------------------------
  // SAD PATH
  // -------------------------------------------------------------------------

  describe("Scenario: errors are surfaced, not swallowed", () => {
    it("a 409 (stale If-Match) toasts a 'changed elsewhere — reload' explanation and refreshes the row (F31.3)", async () => {
      makeFetchMock(409, {});

      await renderMoveAction();

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /move/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByText("This track changed elsewhere — reload to see the latest.")).toBeInTheDocument();
      });
      // The shared hook refreshes the row on a conflict so an immediate retry is possible.
      expect(refreshMock).toHaveBeenCalledTimes(1);
    });

    it("a 400 (unknown library_id) toasts an actionable error", async () => {
      makeFetchMock(400, {});

      await renderMoveAction();

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /move/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByText("Unknown library — check the destination and try again")).toBeInTheDocument();
      });
    });
  });
});

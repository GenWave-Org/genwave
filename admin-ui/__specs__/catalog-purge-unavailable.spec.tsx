// @jest-environment jsdom
// gh-#113 — Explicit operator purge for long-unavailable tracks (commit 2: the UI half).
//
// Runner: Jest (jsdom) + @testing-library/react. Harness mirrors catalog-rating-toolbar.spec.tsx
// (ConfirmDialogProvider + Toaster + mocked useRouter/fetch). The contract under test is the
// destructive-action treatment: a dryRun POST fires FIRST so the confirm dialog NAMES the count,
// the destructive POST fires only on confirm, cancel fires nothing further, and the server's
// mount-outage 409 surfaces its own explanation instead of ever opening the dialog.

jest.mock("next/navigation", () => ({
  ...jest.requireActual<typeof import("next/navigation")>("next/navigation"),
  useRouter: jest.fn(),
}));

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import type { useRouter } from "next/navigation";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

interface StubResponse {
  status: number;
  body: unknown;
}

function toResponse(stub: StubResponse): Response {
  return {
    ok: stub.status >= 200 && stub.status < 300,
    status: stub.status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(stub.body),
    headers: new Headers({ "content-type": "application/json" }),
  } as unknown as Response;
}

/** Queues one response per expected POST, failing loudly on any extra call. */
function makeFetchMock(...stubs: StubResponse[]): jest.MockedFunction<typeof fetch> {
  const queue = [...stubs];
  const fn = jest.fn<typeof fetch>().mockImplementation(() => {
    const next = queue.shift();
    if (next === undefined) {
      throw new Error("unexpected fetch call — no stubbed response left");
    }
    return Promise.resolve(toResponse(next));
  });
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

async function renderAction(): Promise<void> {
  // Dynamic import after the mocks are in place — the directory's established convention
  // (catalog-rating-toolbar.spec.tsx's renderCatalogTable does the same).
  const { PurgeUnavailableAction } = await import("../app/(authed)/catalog/PurgeUnavailableAction");
  render(
    <ConfirmDialogProvider>
      <PurgeUnavailableAction />
      <Toaster />
    </ConfirmDialogProvider>
  );
}

async function clickPurge(): Promise<void> {
  await act(async () => {
    fireEvent.click(screen.getByRole("button", { name: "Purge hidden tracks…" }));
    await Promise.resolve();
  });
}

function requestBody(mockFetch: jest.MockedFunction<typeof fetch>, callIndex: number): unknown {
  const call = mockFetch.mock.calls[callIndex] as unknown as [string, RequestInit];
  return JSON.parse(String(call[1].body));
}

// ---------------------------------------------------------------------------
// Feature: purge hidden tracks from the catalog's unavailable view
// ---------------------------------------------------------------------------

describe("Feature: purge hidden tracks (gh-#113)", () => {
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

  describe("Scenario: the confirm dialog names the count", () => {
    it("fetches a dry-run count first and puts the figure in the dialog's plain-words consequence", async () => {
      const mockFetch = makeFetchMock({ status: 200, body: { wouldDelete: 12 } });
      await renderAction();

      await clickPurge();

      expect(mockFetch).toHaveBeenCalledTimes(1);
      expect(requestBody(mockFetch, 0)).toEqual({ olderThanDays: 7, dryRun: true });

      const dialog = await screen.findByRole("dialog");
      expect(within(dialog).getByText(/permanently deletes 12 tracks/i)).toBeInTheDocument();
      expect(within(dialog).getByRole("button", { name: "Purge 12 tracks" })).toBeInTheDocument();
    });
  });

  describe("Scenario: confirming fires the destructive call", () => {
    it("POSTs the real purge only after confirm, toasts the outcome, and refreshes the page data", async () => {
      const mockFetch = makeFetchMock(
        { status: 200, body: { wouldDelete: 12 } },
        { status: 200, body: { deleted: 12 } }
      );
      await renderAction();

      await clickPurge();
      const dialog = await screen.findByRole("dialog");
      await act(async () => {
        fireEvent.click(within(dialog).getByRole("button", { name: "Purge 12 tracks" }));
        await Promise.resolve();
      });

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(2));
      expect(requestBody(mockFetch, 1)).toEqual({ olderThanDays: 7, dryRun: false });
      expect(await screen.findByText("Purged 12 tracks.")).toBeInTheDocument();
      await waitFor(() => expect(refreshMock).toHaveBeenCalled());
    });
  });

  describe("Scenario: cancelling fires nothing", () => {
    it("leaves the dry-run POST as the only call", async () => {
      const mockFetch = makeFetchMock({ status: 200, body: { wouldDelete: 3 } });
      await renderAction();

      await clickPurge();
      const dialog = await screen.findByRole("dialog");
      await act(async () => {
        fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));
        await Promise.resolve();
      });

      expect(mockFetch).toHaveBeenCalledTimes(1);
      expect(refreshMock).not.toHaveBeenCalled();
    });
  });

  describe("Scenario: nothing qualifies", () => {
    it("names the empty outcome instead of opening a dialog over zero rows", async () => {
      makeFetchMock({ status: 200, body: { wouldDelete: 0 } });
      await renderAction();

      await clickPurge();

      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      expect(
        await screen.findByText(/Nothing to purge — no tracks have been unavailable for more than 7 days\./)
      ).toBeInTheDocument();
    });
  });

  describe("Scenario: the server's mount-outage tripwire refuses", () => {
    it("relays the 409 explanation and never opens the confirm dialog", async () => {
      const detail =
        "1400 of 1450 tracks would be deleted — more than half the library. Check the mount before purging.";
      const mockFetch = makeFetchMock({ status: 409, body: { detail } });
      await renderAction();

      await clickPurge();

      expect(mockFetch).toHaveBeenCalledTimes(1);
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      expect(await screen.findByText(detail)).toBeInTheDocument();
    });
  });
});

// @jest-environment jsdom
// gh-#428 — Wardrobe gets an uninstall affordance per installed font pack. Harness mirrors
// catalog-purge-unavailable.spec.tsx (ConfirmDialogProvider + Toaster + mocked useRouter/fetch): a
// confirm dialog names the pack, DELETE fires only on confirm, 204 toasts success and refreshes the
// server-rendered page, 409 names the referencing themes instead of a generic error, and 404/network
// failures still toast (just not with a theme list).

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

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

interface StubResponse {
  status: number;
  body?: unknown;
}

function toResponse(stub: StubResponse): Response {
  return {
    ok: stub.status >= 200 && stub.status < 300,
    status: stub.status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(stub.body ?? {}),
  } as unknown as Response;
}

/** Queues one response per expected fetch call, failing loudly on any extra call. */
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

async function renderButton(): Promise<void> {
  // Dynamic import after the mocks are in place — the directory's established convention
  // (catalog-purge-unavailable.spec.tsx's renderAction does the same).
  const { UninstallPackButton } = await import("../app/(authed)/wardrobe/UninstallPackButton");
  render(
    <ConfirmDialogProvider>
      <UninstallPackButton slug="space-grotesk" family="Space Grotesk" />
      <Toaster />
    </ConfirmDialogProvider>
  );
}

async function clickUninstallTrigger(): Promise<void> {
  await act(async () => {
    fireEvent.click(screen.getByRole("button", { name: "Uninstall Space Grotesk" }));
    await Promise.resolve();
  });
}

async function confirmInDialog(): Promise<void> {
  const dialog = await screen.findByRole("dialog");
  await act(async () => {
    fireEvent.click(within(dialog).getByRole("button", { name: "Uninstall" }));
    await Promise.resolve();
  });
}

// ---------------------------------------------------------------------------
// Feature: Wardrobe per-pack uninstall (gh-#428)
// ---------------------------------------------------------------------------

describe("Feature: Wardrobe per-pack uninstall (gh-#428)", () => {
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

  describe("Scenario: the confirm dialog names the pack", () => {
    it("states the family name in the consequence copy and issues no request before confirm", async () => {
      const mockFetch = makeFetchMock({ status: 204 });
      await renderButton();

      await clickUninstallTrigger();

      const dialog = await screen.findByRole("dialog");
      expect(within(dialog).getByText(/Uninstall "Space Grotesk"\?/)).toBeInTheDocument();
      expect(mockFetch).not.toHaveBeenCalled();
    });
  });

  describe("Scenario: confirming issues the DELETE to the right slug", () => {
    it("DELETEs /api/fonts/<slug>", async () => {
      const mockFetch = makeFetchMock({ status: 204 });
      await renderButton();

      await clickUninstallTrigger();
      await confirmInDialog();

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
      expect(mockFetch).toHaveBeenCalledWith("/api/fonts/space-grotesk", { method: "DELETE" });
    });
  });

  describe("Scenario: 204 refreshes the list and toasts success", () => {
    it("calls router.refresh() and shows a success toast naming the pack", async () => {
      makeFetchMock({ status: 204 });
      await renderButton();

      await clickUninstallTrigger();
      await confirmInDialog();

      expect(await screen.findByText('"Space Grotesk" uninstalled.')).toBeInTheDocument();
      await waitFor(() => expect(refreshMock).toHaveBeenCalled());
    });
  });

  describe("Scenario: 409 names the referencing themes instead of a generic error", () => {
    it("toasts 'In use by: <themes>' and never refreshes the list", async () => {
      const detail =
        '"space-grotesk" is still referenced by theme(s) "midnight-drive", "sunday-static" and cannot be uninstalled — remove or edit those themes first.';
      makeFetchMock({ status: 409, body: { detail } });
      await renderButton();

      await clickUninstallTrigger();
      await confirmInDialog();

      expect(await screen.findByText("In use by: midnight-drive, sunday-static")).toBeInTheDocument();
      expect(refreshMock).not.toHaveBeenCalled();
    });
  });

  describe("Scenario: 404 toasts the server's own explanation", () => {
    it("relays the ProblemDetails detail instead of a generic message", async () => {
      const detail = 'No installed font pack with slug "space-grotesk" exists.';
      makeFetchMock({ status: 404, body: { detail } });
      await renderButton();

      await clickUninstallTrigger();
      await confirmInDialog();

      expect(await screen.findByText(detail)).toBeInTheDocument();
      expect(refreshMock).not.toHaveBeenCalled();
    });
  });

  describe("Scenario: a network error toasts a failure, not a crash", () => {
    it("shows the network-error toast", async () => {
      global.fetch = jest.fn<typeof fetch>().mockRejectedValue(new Error("network down")) as unknown as typeof fetch;
      await renderButton();

      await clickUninstallTrigger();
      await confirmInDialog();

      expect(await screen.findByText("Network error — check your connection")).toBeInTheDocument();
      expect(refreshMock).not.toHaveBeenCalled();
    });
  });

  describe("Scenario: cancelling fires nothing", () => {
    it("leaves fetch uncalled and the router unrefreshed", async () => {
      const mockFetch = makeFetchMock({ status: 204 });
      await renderButton();

      await clickUninstallTrigger();
      const dialog = await screen.findByRole("dialog");
      await act(async () => {
        fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));
        await Promise.resolve();
      });

      expect(mockFetch).not.toHaveBeenCalled();
      expect(refreshMock).not.toHaveBeenCalled();
    });
  });
});

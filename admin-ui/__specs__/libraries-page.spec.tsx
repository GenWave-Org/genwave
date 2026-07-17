// @jest-environment jsdom
// STORY-052 / STORY-089 — Admin UI: library management (CRUD), now the Catalog page's
// Libraries tab (Q7, SPEC F28.11). Re-pointed from the retired standalone `/libraries` page at
// LibrariesClient to LibrariesTab: `window.confirm` on delete is now `useConfirm()`, and inline
// error paragraphs (including the 409 dependentMediaCount message) are now toasts (F28.9). The
// CRUD wire-behavior assertions (POST/PATCH/DELETE bodies, 409/400 handling) are preserved
// verbatim — only the confirmation/feedback mechanism changed.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import { LibrariesTab } from "../app/(authed)/catalog/LibrariesTab";
import type { LibraryDto } from "../app/(authed)/catalog/LibrariesTab";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeLibraries(overrides: Partial<LibraryDto>[] = []): LibraryDto[] {
  const defaults: LibraryDto[] = [
    { id: 1, name: "Lib Alpha", mediaCount: 10 },
    { id: 2, name: "Lib Beta", mediaCount: 0 },
  ];
  return defaults.map((d, i) => ({ ...d, ...(overrides[i] ?? {}) }));
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

function renderLibrariesTab(libraries: LibraryDto[]) {
  return render(
    <ConfirmDialogProvider>
      <LibrariesTab initialLibraries={libraries} />
      <Toaster />
    </ConfirmDialogProvider>
  );
}

/** Confirms the currently-open useConfirm() dialog (clicks its Confirm/Delete button). */
async function confirmDialog(name: RegExp | string = /confirm|delete/i): Promise<void> {
  const dialog = await screen.findByRole("dialog");
  await act(async () => {
    fireEvent.click(within(dialog).getByRole("button", { name }));
    await Promise.resolve();
  });
}

/** Cancels the currently-open useConfirm() dialog. */
async function cancelDialog(): Promise<void> {
  const dialog = await screen.findByRole("dialog");
  fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));
}

// ---------------------------------------------------------------------------
// Feature: Manage libraries from the Catalog page's Libraries tab
// ---------------------------------------------------------------------------

describe("Feature: Manage libraries from the Catalog page's Libraries tab", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  // -------------------------------------------------------------------------
  describe("Scenario: listing libraries shows every library with its media count", () => {
    it("renders one row per library returned by GET /api/libraries with id, name, and mediaCount", () => {
      const libs = makeLibraries();
      renderLibrariesTab(libs);

      expect(screen.getByText("Lib Alpha")).toBeTruthy();
      expect(screen.getByText("Lib Beta")).toBeTruthy();
      expect(screen.getByText("(10 tracks)")).toBeTruthy();
      expect(screen.getByText("(0 tracks)")).toBeTruthy();
    });
  });

  describe("Scenario: creating a library", () => {
    it("submitting the create form POSTs /api/libraries with the entered name", async () => {
      const newLib: LibraryDto = { id: 3, name: "New Library", mediaCount: 0 };
      const mockFetch = makeFetchMock(201, newLib);

      renderLibrariesTab(makeLibraries());

      const nameInput = screen.getByLabelText("New library name");
      fireEvent.change(nameInput, { target: { value: "New Library" } });

      await act(async () => {
        fireEvent.submit(screen.getByRole("form", { name: "Create library" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledTimes(1);
      });

      const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/libraries");
      expect(init.method).toBe("POST");

      const headers = init.headers as Record<string, string>;
      expect(headers["Content-Type"]).toBe("application/json");

      const body = JSON.parse(init.body as string) as { name: string };
      expect(body.name).toBe("New Library");

      await waitFor(() => {
        expect(screen.getByText("New Library")).toBeTruthy();
      });
      // Mutation outcomes surface as toasts (F28.9), not inline banners.
      await waitFor(() => {
        expect(screen.getByText('"New Library" created.')).toBeInTheDocument();
      });
    });

    it("a blank name disables the create button", () => {
      renderLibrariesTab([]);

      const btn = screen.getByRole("button", { name: /create library/i });
      expect(btn).toBeDisabled();

      const nameInput = screen.getByLabelText("New library name");
      fireEvent.change(nameInput, { target: { value: "   " } });
      expect(btn).toBeDisabled();
    });
  });

  describe("Scenario: renaming a library", () => {
    it("editing a row's name and saving PATCHes /api/libraries/{id} with the new name", async () => {
      const mockFetch = makeFetchMock(200, {});

      renderLibrariesTab(makeLibraries());

      const editButtons = screen.getAllByRole("button", { name: /^Edit$/i });
      fireEvent.click(editButtons[0]!);

      const nameInput = screen.getByLabelText("Library name");
      fireEvent.change(nameInput, { target: { value: "Renamed Library" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save name/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledTimes(1);
      });

      const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/libraries/1");
      expect(init.method).toBe("PATCH");

      const body = JSON.parse(init.body as string) as { name: string };
      expect(body.name).toBe("Renamed Library");

      await waitFor(() => {
        expect(screen.getByText("Renamed Library")).toBeTruthy();
      });
    });
  });

  describe("Scenario: deleting an empty library", () => {
    it("confirms via useConfirm() (no window.confirm), then DELETEs and removes the row", async () => {
      const mockFetch = makeFetchMock(204, null);

      renderLibrariesTab(makeLibraries());

      fireEvent.click(screen.getByRole("button", { name: /delete lib beta/i }));

      // The consequence copy names the library and states the plain-words rule (F28.9).
      const dialog = await screen.findByRole("dialog");
      expect(dialog).toHaveTextContent("Delete library");
      expect(dialog).toHaveTextContent("Lib Beta");

      await act(async () => {
        fireEvent.click(within(dialog).getByRole("button", { name: "Delete" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledTimes(1);
      });

      const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/libraries/2");
      expect(init.method).toBe("DELETE");

      await waitFor(() => {
        expect(screen.queryByText("Lib Beta")).toBeNull();
      });
    });

    it("cancelling the confirm dialog issues no DELETE and keeps the row", async () => {
      const mockFetch = makeFetchMock(204, null);

      renderLibrariesTab(makeLibraries());

      fireEvent.click(screen.getByRole("button", { name: /delete lib beta/i }));
      await cancelDialog();

      expect(mockFetch).not.toHaveBeenCalled();
      expect(screen.getByText("Lib Beta")).toBeTruthy();
    });
  });

  // -------------------------------------------------------------------------
  // SAD PATH
  // -------------------------------------------------------------------------

  describe("Scenario: errors are surfaced, not swallowed", () => {
    it("a 409 + dependentMediaCount on DELETE toasts the count and keeps the row", async () => {
      makeFetchMock(409, { dependentMediaCount: 5 });

      renderLibrariesTab(makeLibraries());

      fireEvent.click(screen.getByRole("button", { name: /delete lib alpha/i }));
      await confirmDialog("Delete");

      await waitFor(() => {
        expect(screen.getByText("Library has 5 tracks — reassign them first.")).toBeInTheDocument();
      });

      // Row stays in the list
      expect(screen.getByText("Lib Alpha")).toBeTruthy();
    });

    it("a 409 NameConflict on POST is toasted as a duplicate-name error and the input value is preserved", async () => {
      makeFetchMock(409, {});

      renderLibrariesTab([]);

      const nameInput = screen.getByLabelText("New library name");
      fireEvent.change(nameInput, { target: { value: "Duplicate Name" } });

      await act(async () => {
        fireEvent.submit(screen.getByRole("form", { name: "Create library" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByText("A library with that name already exists.")).toBeInTheDocument();
      });

      expect((screen.getByLabelText("New library name") as HTMLInputElement).value).toBe("Duplicate Name");
    });

    it("a 400 from POST is toasted as a validation error", async () => {
      makeFetchMock(400, {});

      renderLibrariesTab([]);

      const nameInput = screen.getByLabelText("New library name");
      fireEvent.change(nameInput, { target: { value: "   Bad   " } });

      await act(async () => {
        fireEvent.submit(screen.getByRole("form", { name: "Create library" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByText("Invalid library name.")).toBeInTheDocument();
      });
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: rejecting window.confirm (sad path)", () => {
    it("never calls window.confirm — delete is gated by useConfirm()", async () => {
      const confirmSpy = jest.spyOn(window, "confirm");
      makeFetchMock(204, null);

      renderLibrariesTab(makeLibraries());
      fireEvent.click(screen.getByRole("button", { name: /delete lib beta/i }));
      await screen.findByRole("dialog");

      expect(confirmSpy).not.toHaveBeenCalled();
      confirmSpy.mockRestore();
    });
  });
});

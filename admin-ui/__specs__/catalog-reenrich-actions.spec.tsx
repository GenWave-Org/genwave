// @jest-environment jsdom
// STORY-052 — Admin UI: re-enrich actions (single-row panel)
// Re-pointed at Q8 (STORY-090, SPEC F28.9) — the wire contract (POST /api/media/{id}/reenrich?
// fields=<csv>) is unchanged; only the presentation moved from window.confirm + inline status/alert
// paragraphs to useConfirm + toasts, and the panel now disables briefly after a 202.
//
// The bulk half of this file (BulkReenrichControl) was retired at Q7 (STORY-089, SPEC F28.11) —
// its coverage, including the exact request-body assertions, lives in
// catalog-selection-toolbar.spec.tsx against CatalogToolbar (the byte-compatible replacement).
// See track-detail-redesign.spec.tsx for the STORY-090 acceptance-criteria-level scenario; this
// file keeps the granular field/wire-contract coverage for the single-row ReanalyzePanel.

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
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { ReanalyzePanel } from "../app/(authed)/catalog/[mediaId]/ReanalyzePanel";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const MEDIA_ID = "track-xyz";

function makeFetchMock(
  status: number,
  body: unknown = {}
): jest.MockedFunction<typeof fetch> {
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

function renderPanel(tagsEditedAt: string | null = null) {
  return render(
    <ConfirmDialogProvider>
      <ReanalyzePanel mediaId={MEDIA_ID} tagsEditedAt={tagsEditedAt} />
      <Toaster />
    </ConfirmDialogProvider>
  );
}

/** Confirms the currently-open useConfirm() dialog. */
async function confirmDialog(name = "Re-analyze"): Promise<void> {
  const dialog = await screen.findByRole("dialog");
  await act(async () => {
    fireEvent.click(within(dialog).getByRole("button", { name }));
    await Promise.resolve();
  });
}

// ---------------------------------------------------------------------------
// Feature: Re-analyze a single track from its detail page
// ---------------------------------------------------------------------------

describe("Feature: Re-analyze a single track from its detail page", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: re-analyze with selected fields drives the API", () => {
    it("ticking cue + energy and submitting POSTs /api/media/{id}/reenrich?fields=cue,energy", async () => {
      const mockFetch = makeFetchMock(202, null);

      renderPanel();

      // Tick cue and energy
      const checkboxes = screen.getAllByRole("checkbox");
      const cueBox = checkboxes.find(
        (cb) => cb.closest("label")?.textContent?.includes("cue")
      );
      const energyBox = checkboxes.find(
        (cb) => cb.closest("label")?.textContent?.includes("energy")
      );
      expect(cueBox).toBeDefined();
      expect(energyBox).toBeDefined();
      fireEvent.click(cueBox!);
      fireEvent.click(energyBox!);

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /re-analyze/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledTimes(1);
      });

      const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe(`/api/media/${MEDIA_ID}/reenrich?fields=cue,energy`);
      expect(init.method).toBe("POST");

      await waitFor(() => {
        expect(screen.getByText("Re-analysis scheduled — will complete in a few ticks.")).toBeInTheDocument();
      });
    });

    it("ticking BPM and Year lookup (retry) POSTs fields=bpm,year (F46.4/F48.6 pickers)", async () => {
      const mockFetch = makeFetchMock(202, null);

      renderPanel();

      const checkboxes = screen.getAllByRole("checkbox");
      const bpmBox = checkboxes.find((cb) => cb.closest("label")?.textContent?.includes("BPM"));
      const yearBox = checkboxes.find((cb) =>
        cb.closest("label")?.textContent?.includes("Year lookup (retry)")
      );
      expect(bpmBox).toBeDefined();
      expect(yearBox).toBeDefined();
      fireEvent.click(bpmBox!);
      fireEvent.click(yearBox!);

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /re-analyze/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledTimes(1);
      });

      const [url] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe(`/api/media/${MEDIA_ID}/reenrich?fields=bpm,year`);
    });

    it("submitting with no fields ticked defaults the query to fields=all", async () => {
      const mockFetch = makeFetchMock(202, null);

      renderPanel();

      // No checkboxes ticked
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /re-analyze/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledTimes(1);
      });

      const [url] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe(`/api/media/${MEDIA_ID}/reenrich?fields=all`);
    });

    it("disables the panel until the cooldown after a successful 202", async () => {
      makeFetchMock(202, null);

      renderPanel();
      const button = screen.getByRole("button", { name: /re-analyze/i });

      await act(async () => {
        fireEvent.click(button);
        await Promise.resolve();
      });

      await waitFor(() => expect(button).toBeDisabled());
      await waitFor(() => expect(button).not.toBeDisabled(), { timeout: 3000 });
    });
  });

  describe("Scenario: re-analyzing tags on an edited row requires explicit confirmation", () => {
    it("ticking tags on a row with tags_edited_at set opens a discard-edits confirm dialog before submit", async () => {
      makeFetchMock(202, null);

      renderPanel("2026-01-15T10:00:00Z");

      // Tick the tags checkbox
      const checkboxes = screen.getAllByRole("checkbox");
      const tagsBox = checkboxes.find(
        (cb) => cb.closest("label")?.textContent?.includes("tags")
      );
      expect(tagsBox).toBeDefined();
      fireEvent.click(tagsBox!);

      fireEvent.click(screen.getByRole("button", { name: /re-analyze/i }));

      const dialog = await screen.findByRole("dialog");
      expect(dialog.textContent?.toLowerCase()).toMatch(/tag.*edit.*discard|discard.*tag.*edit/i);
    });

    it("ticking tags on a row with tags_edited_at NULL submits without the confirm dialog", async () => {
      makeFetchMock(202, null);

      renderPanel(null);

      // Tick tags
      const checkboxes = screen.getAllByRole("checkbox");
      const tagsBox = checkboxes.find(
        (cb) => cb.closest("label")?.textContent?.includes("tags")
      );
      expect(tagsBox).toBeDefined();
      fireEvent.click(tagsBox!);

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /re-analyze/i }));
        await Promise.resolve();
      });

      // No confirmation dialog for un-edited tags
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

      await waitFor(() => {
        expect(global.fetch).toHaveBeenCalledTimes(1);
      });
    });

    it("confirming the discard-edits dialog fires the POST", async () => {
      const mockFetch = makeFetchMock(202, null);

      renderPanel("2026-01-15T10:00:00Z");

      const checkboxes = screen.getAllByRole("checkbox");
      const tagsBox = checkboxes.find(
        (cb) => cb.closest("label")?.textContent?.includes("tags")
      );
      fireEvent.click(tagsBox!);
      fireEvent.click(screen.getByRole("button", { name: /re-analyze/i }));
      await confirmDialog();

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
    });
  });

  // -------------------------------------------------------------------------
  // SAD PATH
  // -------------------------------------------------------------------------

  describe("Scenario: errors are surfaced, not swallowed", () => {
    it("a 400 (unknown field) toasts an actionable validation error", async () => {
      makeFetchMock(400, {});

      renderPanel();

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /re-analyze/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByText("Unknown field — check field selection and try again")).toBeInTheDocument();
      });
    });

    it("a 404 on the single-row endpoint toasts 'this row no longer exists'", async () => {
      makeFetchMock(404, {});

      renderPanel();

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /re-analyze/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByText("this row no longer exists")).toBeInTheDocument();
      });
    });
  });
});

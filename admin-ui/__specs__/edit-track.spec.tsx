// @jest-environment jsdom
// STORY-044 — Admin UI: edit-track form (tags + eligibility)
// Re-pointed at Q8 (STORY-090, SPEC F28.9) — the wire contract (PATCH /api/media/{id}, If-Match)
// is unchanged; only the outcome presentation moved from inline status/alert paragraphs to toasts,
// and a successful save now triggers router.refresh() (PATCH returns no ETag, so the page re-fetches
// one to carry into the next save). See track-detail-redesign.spec.tsx for the STORY-090
// acceptance-criteria-level scenarios; this file keeps the granular field/wire-contract coverage.
//
// Runner: Jest + jsdom. Drives EditTrackForm via @testing-library/react with a mocked fetch and a
// mocked next/navigation useRouter (mirrors catalog-selection-toolbar.spec.tsx's pattern).

jest.mock("next/navigation", () => ({
  ...jest.requireActual("next/navigation"),
  useRouter: jest.fn(),
}));

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { useRouter } from "next/navigation";
import { Toaster } from "@/components/ui/toast";
import type { EditableTrackFields } from "../app/(authed)/catalog/[mediaId]/EditTrackForm";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const MEDIA_ID = "abc-123";
const ETAG = 'W/"abc-etag-1"';

function makeInitialValues(overrides: Partial<EditableTrackFields> = {}): EditableTrackFields {
  return {
    title: "Test Track",
    artist: "Test Artist",
    album: "Test Album",
    genre: "Rock",
    year: 2024,
    eligible: true,
    ...overrides,
  };
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

/**
 * Dynamically imports EditTrackForm — next/jest's SWC transform hoists every
 * static ES import to the top of the module ahead of the jest.mock() call
 * above, so a static import of a component that itself imports next/navigation
 * would bind to the real (unmocked) module. A dynamic import, evaluated at
 * call time inside the test, runs after jest.mock() has registered.
 */
async function renderEditForm(props: {
  mediaId: string;
  initialValues: EditableTrackFields;
  etag: string;
}) {
  const { EditTrackForm } = await import("../app/(authed)/catalog/[mediaId]/EditTrackForm");
  return render(
    <>
      <EditTrackForm {...props} />
      <Toaster />
    </>
  );
}

// ---------------------------------------------------------------------------
// Feature: Edit a track from its detail page
// ---------------------------------------------------------------------------

describe("Feature: Edit a track from its detail page", () => {
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

  // -------------------------------------------------------------------------
  describe("Scenario: editing tags and eligibility", () => {
    it("changing a tag and saving PATCHes /api/media/{id} with the loaded row's If-Match", async () => {
      const mockFetch = makeFetchMock(200);

      await renderEditForm({ mediaId: MEDIA_ID, initialValues: makeInitialValues(), etag: ETAG });

      // Change the title field
      const titleInput = screen.getByLabelText("Title");
      fireEvent.change(titleInput, { target: { value: "Updated Title" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledTimes(1);
      });

      const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe(`/api/media/${MEDIA_ID}`);
      expect(init.method).toBe("PATCH");

      const headers = init.headers as Record<string, string>;
      expect(headers["Content-Type"]).toBe("application/json");
      expect(headers["If-Match"]).toBe(ETAG);

      const body = JSON.parse(init.body as string) as Record<string, unknown>;
      expect(body["title"]).toBe("Updated Title");
    });

    it("toggling eligible and saving sends the eligible field in the PATCH body", async () => {
      const mockFetch = makeFetchMock(200);

      await renderEditForm({
        mediaId: MEDIA_ID,
        initialValues: makeInitialValues({ eligible: true }),
        etag: ETAG,
      });

      // Toggle eligible off
      const eligibleCheckbox = screen.getByRole("checkbox", { name: /eligible/i });
      fireEvent.click(eligibleCheckbox);

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledTimes(1);
      });

      const [, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      const body = JSON.parse(init.body as string) as Record<string, unknown>;
      expect(body["eligible"]).toBe(false);
    });

    it("a successful save toasts 'Saved.' and refreshes the page (PATCH returns no ETag of its own)", async () => {
      makeFetchMock(200);

      await renderEditForm({ mediaId: MEDIA_ID, initialValues: makeInitialValues(), etag: ETAG });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByText("Saved.")).toBeInTheDocument();
      });
      expect(refreshMock).toHaveBeenCalledTimes(1);
    });
  });

  // -------------------------------------------------------------------------
  // SAD PATH
  // -------------------------------------------------------------------------

  describe("Scenario: a concurrent change is surfaced", () => {
    it("a 409 from PATCH toasts an explanation and offers an adjacent reload, not a silent loss", async () => {
      makeFetchMock(409);

      await renderEditForm({ mediaId: MEDIA_ID, initialValues: makeInitialValues(), etag: ETAG });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByText("This track changed elsewhere — your edits are unsaved.")).toBeInTheDocument();
      });
      // Reload is offered as a persistent adjacent affordance (the toast itself auto-dismisses).
      expect(screen.getByRole("button", { name: "Reload" })).toBeInTheDocument();
    });
  });
});

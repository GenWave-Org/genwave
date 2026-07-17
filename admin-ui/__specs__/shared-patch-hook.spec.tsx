// @jest-environment jsdom
// STORY-104 — One shared PATCH hook with real feedback (Epic R / SPEC F31.2–F31.3, gitea-#181)
//
// One shared row-PATCH hook (`@/lib/use-row-patch`) consumed by all four sites
// (SafeContentClient eligibility toggle, EditTrackForm, CatalogToolbar per-row actions,
// MoveToLibraryAction): version-from-response-ETag on success, toast naming the outcome on
// failure (F28.9), row refresh on conflict. No silent swallow anywhere.
//
// Runner: Jest (jsdom) + @testing-library/react. Scenarios 1/2/4 drive the hook's real behavior
// through SafeContentClient (the simplest of the four sites, and the one gitea-#181 was filed against)
// with a mocked `fetch` — mirrors safe-content-page.spec.tsx's harness. Scenario 3 ("all four call
// sites share the hook") instead mocks the `@/lib/use-row-patch` module itself, defaulting every
// call to the real implementation (so scenarios 1/2/4 are unaffected) and overriding it per-test
// to a spy — this is the structural proof that each site's PATCH goes through the shared hook's
// `patchRow`, not an inline `fetch()` call, extending this suite's established
// mock-the-module-then-assert-invocation idiom (used throughout for `next/navigation`'s
// `useRouter`/`usePathname`) to an application hook instead of a framework one.
//
// EditTrackForm, MoveToLibraryAction and CatalogToolbar (via CatalogTable) all call `useRouter()`
// (MoveToLibraryAction as of R9 — SPEC F31.3's conflict-refresh), and every one of the four sites
// imports `@/lib/use-row-patch` (mocked below, for Scenario 3) — so all four need the
// dynamic-import treatment behind jest.mock() that edit-track.spec.tsx /
// catalog-library-actions.spec.tsx / catalog-selection-toolbar.spec.tsx already use for
// next/navigation (next/jest's SWC transform hoists static ES imports above jest.mock(), unlike
// babel-jest — a *static* import of SafeContentClient here would bind its `use-row-patch` import
// to the real module before the mock below registers, same failure mode).

jest.mock("next/navigation", () => ({
  ...jest.requireActual("next/navigation"),
  useRouter: jest.fn(),
}));

jest.mock("@/lib/use-row-patch", () => {
  const actual = jest.requireActual("@/lib/use-row-patch");
  return { ...actual, useRowPatch: jest.fn() };
});

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { ComponentProps } from "react";
import type { useRouter } from "next/navigation";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import type { LibraryDto } from "@/lib/library";
import type { useRowPatch, UseRowPatchResult } from "@/lib/use-row-patch";
import type { SafeContentClient } from "../app/(authed)/safe-content/SafeContentClient";
import type { SafeContentClientProps, SafeSegmentDto } from "../app/(authed)/safe-content/SafeContentClient";
import type { EditableTrackFields } from "../app/(authed)/catalog/[mediaId]/EditTrackForm";
import type { MoveToLibraryAction } from "../app/(authed)/catalog/[mediaId]/MoveToLibraryAction";
import type { AdminMediaDto, BulkFilter, Pagination } from "../app/(authed)/catalog/types";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;
const mockedUseRowPatch = jest
  .requireMock<{ useRowPatch: typeof useRowPatch }>("@/lib/use-row-patch")
  .useRowPatch as jest.MockedFunction<typeof useRowPatch>;
const actualUseRowPatch = jest.requireActual<{ useRowPatch: typeof useRowPatch }>(
  "@/lib/use-row-patch"
).useRowPatch;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const SEED_MESSAGE = "You're listening to {StationName}. We'll be right back — stay tuned.";
const DEFAULT_TITLE = "Please Stand By";

function makeLibraries(): LibraryDto[] {
  return [{ id: 7, name: "safe", mediaCount: 2 }];
}

function makeSegment(overrides: Partial<SafeSegmentDto> = {}): SafeSegmentDto {
  return {
    mediaId: "42",
    title: "Please Stand By",
    artist: "GenWave",
    state: "ready",
    durationMs: 5000,
    eligible: true,
    version: "10",
    ...overrides,
  };
}

interface MockResponseSpec {
  status: number;
  body?: unknown;
  headers?: Record<string, string>;
}

/** SafeContentClient's VoiceControl child fetches GET /api/voices once on mount, before any
 * scenario-triggered fetch (mirrors safe-content-page.spec.tsx). */
const VOICES_MOUNT_SPEC: MockResponseSpec = { status: 200, body: [] };

function makeSequencedFetchMock(specs: MockResponseSpec[]): jest.MockedFunction<typeof fetch> {
  const allSpecs = [VOICES_MOUNT_SPEC, ...specs];
  let callIndex = 0;
  const fn = jest.fn<typeof fetch>().mockImplementation(async () => {
    const spec = allSpecs[callIndex] ?? allSpecs[allSpecs.length - 1]!;
    callIndex += 1;
    return {
      ok: spec.status >= 200 && spec.status < 300,
      status: spec.status,
      json: jest.fn<() => Promise<unknown>>().mockResolvedValue(spec.body ?? {}),
      headers: new Headers(spec.headers ?? {}),
    } as unknown as Response;
  });
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

/** A single-response fetch mock — for sites with no mount-time fetch of their own (EditTrackForm,
 * MoveToLibraryAction, CatalogTable), unlike SafeContentClient's VoiceControl mount fetch. */
function makeFetchMock(status: number, body: unknown = {}): jest.MockedFunction<typeof fetch> {
  const fn = jest.fn<typeof fetch>().mockResolvedValue({
    ok: status >= 200 && status < 300,
    status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
    headers: new Headers(),
  } as unknown as Response);
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

/** A sequenced mock whose second call (index 1 — the first scenario-triggered fetch after the
 * voices mount) rejects instead of resolving, to exercise the hook's network-failure path. */
function makeFetchMockRejectingSecondCall(): jest.MockedFunction<typeof fetch> {
  const fn = jest.fn<typeof fetch>();
  fn.mockResolvedValueOnce({
    ok: true,
    status: 200,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue([]),
    headers: new Headers(),
  } as unknown as Response);
  fn.mockRejectedValueOnce(new Error("network down"));
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

/** Dynamically imports SafeContentClient — it (transitively) imports `@/lib/use-row-patch`,
 * mocked below for Scenario 3 (see file header). */
async function renderSafeContent(
  overrides: Partial<SafeContentClientProps> = {}
): Promise<ReturnType<typeof render>> {
  const { SafeContentClient } = await import("../app/(authed)/safe-content/SafeContentClient");
  const props: SafeContentClientProps = {
    libraries: makeLibraries(),
    initialLibraryId: 7,
    initialSegments: [],
    initialOutOfScope: false,
    defaultText: SEED_MESSAGE,
    defaultTitle: DEFAULT_TITLE,
    ...overrides,
  };
  return render(
    <>
      <SafeContentClient {...props} />
      <Toaster />
    </>
  );
}

function eligibleToggle(name: RegExp = /eligible: please stand by/i): HTMLElement {
  return screen.getByRole("checkbox", { name });
}

async function clickAndSettle(el: HTMLElement): Promise<void> {
  await act(async () => {
    fireEvent.click(el);
    await Promise.resolve();
  });
}

/** Dynamically imports EditTrackForm — it calls useRouter() (see file header). */
async function renderEditTrackForm(props: {
  mediaId: string;
  initialValues: EditableTrackFields;
  etag: string;
}): Promise<ReturnType<typeof render>> {
  const { EditTrackForm } = await import("../app/(authed)/catalog/[mediaId]/EditTrackForm");
  return render(
    <>
      <EditTrackForm {...props} />
      <Toaster />
    </>
  );
}

/** Dynamically imports MoveToLibraryAction — it calls useRouter() as of R9 (see file header). */
async function renderMoveToLibraryAction(
  props: ComponentProps<typeof MoveToLibraryAction>
): Promise<ReturnType<typeof render>> {
  const { MoveToLibraryAction: Component } = await import(
    "../app/(authed)/catalog/[mediaId]/MoveToLibraryAction"
  );
  return render(
    <ConfirmDialogProvider>
      <Component {...props} />
      <Toaster />
    </ConfirmDialogProvider>
  );
}

const EMPTY_FILTER: BulkFilter = {
  state: null,
  artist: null,
  genre: null,
  libraryId: null,
  q: null,
  eligible: null,
};

function makeMedia(): AdminMediaDto[] {
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
      version: "900",
    },
  ];
}

function makePagination(): Pagination {
  return { total: 1, pages: 1, page: 1, limit: 50 };
}

/** Dynamically imports CatalogTable (renders CatalogToolbar) — it calls useRouter(). */
async function renderCatalogTable(media: AdminMediaDto[]): Promise<ReturnType<typeof render>> {
  const { CatalogTable } = await import("../app/(authed)/catalog/CatalogTable");
  return render(
    <ConfirmDialogProvider>
      <CatalogTable
        media={media}
        pagination={makePagination()}
        libraries={[]}
        bulkFilter={EMPTY_FILTER}
        filterActive={false}
        clearFiltersHref="/catalog"
      />
      <Toaster />
    </ConfirmDialogProvider>
  );
}

/** Confirms the currently-open useConfirm() dialog (default label "Confirm"). */
async function confirmDialog(name = "Confirm"): Promise<void> {
  const dialog = await screen.findByRole("dialog");
  await act(async () => {
    fireEvent.click(within(dialog).getByRole("button", { name }));
    await Promise.resolve();
  });
}

// ---------------------------------------------------------------------------
// Feature: One shared PATCH hook with real feedback
// ---------------------------------------------------------------------------

describe("Feature: One shared PATCH hook with real feedback", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
    mockedUseRouter.mockReturnValue({ refresh: jest.fn() } as unknown as ReturnType<typeof useRouter>);
    // Every test gets the real hook by default — Scenario 3 below overrides this per-test with a
    // spy to prove invocation, then this reverts on the next test.
    mockedUseRowPatch.mockImplementation(actualUseRowPatch);
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: a PATCH succeeds", () => {
    it("updates the cached row version from the response ETag header", async () => {
      const segment = makeSegment({ version: "10" });
      const mockFetch = makeSequencedFetchMock([
        { status: 204, headers: { etag: 'W/"55"' } },
        { status: 204, headers: { etag: 'W/"56"' } },
      ]);
      await renderSafeContent({ initialSegments: [segment] });

      await clickAndSettle(eligibleToggle());
      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(2));

      // A follow-up PATCH is the only externally observable way to prove the cached version
      // moved — the row's version isn't rendered. Its If-Match must carry the first response's
      // ETag ("55"), not the original prop ("10").
      await clickAndSettle(eligibleToggle());
      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(3));

      const [, secondPatchInit] = mockFetch.mock.calls[2] as [string, RequestInit];
      const headers = secondPatchInit.headers as Record<string, string>;
      expect(headers["If-Match"]).toBe('W/"55"');
    });

    it("reflects the new state without a router refresh or page reload", async () => {
      const segment = makeSegment({ eligible: true });
      makeSequencedFetchMock([{ status: 204, headers: { etag: 'W/"11"' } }]);
      await renderSafeContent({ initialSegments: [segment] });

      await clickAndSettle(eligibleToggle());

      // The checkbox flips from the PATCH response alone — SafeContentClient never imports
      // next/navigation, so there is no router to refresh and no reload to wait on.
      await waitFor(() => expect(eligibleToggle()).not.toBeChecked());
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: the gitea-#181 double-toggle repro", () => {
    it("sends the fresh version as If-Match on an immediate second toggle", async () => {
      const segment = makeSegment({ version: "10", eligible: true });
      const mockFetch = makeSequencedFetchMock([
        { status: 204, headers: { etag: 'W/"11"' } },
        { status: 204, headers: { etag: 'W/"12"' } },
      ]);
      await renderSafeContent({ initialSegments: [segment] });

      await clickAndSettle(eligibleToggle());
      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(2));
      const [, firstInit] = mockFetch.mock.calls[1] as [string, RequestInit];
      expect((firstInit.headers as Record<string, string>)["If-Match"]).toBe('W/"10"');

      await clickAndSettle(eligibleToggle());
      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(3));
      const [, secondInit] = mockFetch.mock.calls[2] as [string, RequestInit];
      expect((secondInit.headers as Record<string, string>)["If-Match"]).toBe('W/"11"');
    });

    it("succeeds on the second toggle without a reload", async () => {
      const segment = makeSegment({ version: "10", eligible: true });
      makeSequencedFetchMock([
        { status: 204, headers: { etag: 'W/"11"' } },
        { status: 204, headers: { etag: 'W/"12"' } },
      ]);
      await renderSafeContent({ initialSegments: [segment] });

      await clickAndSettle(eligibleToggle()); // eligible -> false
      await waitFor(() => expect(eligibleToggle()).not.toBeChecked());

      await clickAndSettle(eligibleToggle()); // eligible -> true again, immediately
      await waitFor(() => expect(eligibleToggle()).toBeChecked());

      // Before F31.1/F31.2 this second PATCH 409'd on the stale version — no error toast means
      // it went through clean instead.
      expect(screen.queryByText(/changed elsewhere/i)).not.toBeInTheDocument();
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: all four call sites share the hook", () => {
    // `mockImplementation` (not `mockReturnValueOnce`) — every one of these sites re-renders
    // itself mid-flow (a `setState` before or around the PATCH), which calls `useRowPatch()`
    // again. The stub must return the same zero-internal-hooks shape on every call for the
    // lifetime of the test, or React's "rendered more hooks than during the previous render"
    // invariant fires the moment a render falls through to the real hook (which calls
    // `useCallback` internally) after a render that didn't.
    function stubHook(patchRow: UseRowPatchResult["patchRow"]): void {
      mockedUseRowPatch.mockImplementation(() => ({ patchRow }));
    }

    it("safe-content eligibility toggle performs its PATCH through the shared hook", async () => {
      const patchRowSpy = jest
        .fn<UseRowPatchResult["patchRow"]>()
        .mockResolvedValue({ ok: true, version: "11", body: null });
      stubHook(patchRowSpy);

      makeSequencedFetchMock([]); // only the VoiceControl mount fetch — the PATCH never reaches fetch()
      await renderSafeContent({ initialSegments: [makeSegment({ version: "10" })] });

      await clickAndSettle(eligibleToggle());

      expect(patchRowSpy).toHaveBeenCalledWith({ mediaId: "42", version: "10" }, { eligible: false });
    });

    it("catalog detail form performs its PATCH through the shared hook", async () => {
      const patchRowSpy = jest
        .fn<UseRowPatchResult["patchRow"]>()
        .mockResolvedValue({ ok: true, version: "2", body: null });
      stubHook(patchRowSpy);

      await renderEditTrackForm({
        mediaId: "abc-123",
        initialValues: {
          title: "Test Track",
          artist: null,
          album: null,
          genre: null,
          year: null,
          eligible: true,
        },
        etag: 'W/"1"',
      });

      await clickAndSettle(screen.getByRole("button", { name: /save/i }));

      expect(patchRowSpy).toHaveBeenCalledWith(
        { mediaId: "abc-123", version: "1" },
        expect.objectContaining({ title: "Test Track" })
      );
    });

    it("selection-toolbar per-row actions perform their PATCHes through the shared hook", async () => {
      const patchRowSpy = jest
        .fn<UseRowPatchResult["patchRow"]>()
        .mockResolvedValue({ ok: true, version: "901", body: {} });
      stubHook(patchRowSpy);

      await renderCatalogTable(makeMedia());

      fireEvent.click(screen.getByRole("checkbox", { name: "Select Track 1" }));
      fireEvent.click(screen.getByRole("button", { name: "Set eligible" }));
      await confirmDialog();

      await waitFor(() => expect(patchRowSpy).toHaveBeenCalledWith({ mediaId: "1", version: "900" }, { eligible: true }));
    });

    it("move-to-library performs its PATCH through the shared hook", async () => {
      const patchRowSpy = jest
        .fn<UseRowPatchResult["patchRow"]>()
        .mockResolvedValue({ ok: true, version: "2", body: {} });
      stubHook(patchRowSpy);

      await renderMoveToLibraryAction({
        mediaId: "abc-123",
        etag: 'W/"1"',
        currentLibraryId: 1,
        libraries: [{ id: 1, name: "In Rotation", mediaCount: 5 }],
        scopeLibraryIds: [],
      });

      await clickAndSettle(screen.getByRole("button", { name: /move/i }));

      expect(patchRowSpy).toHaveBeenCalledWith({ mediaId: "abc-123", version: "1" }, { libraryId: 1 });
    });
  });

  // ---------------------------------------------------------------------------
  // SAD PATH
  // ---------------------------------------------------------------------------

  describe("Scenario: PATCH failures surface (sad path)", () => {
    it("shows a toast naming the outcome on 403", async () => {
      makeSequencedFetchMock([{ status: 403 }]);
      await renderSafeContent({ initialSegments: [makeSegment()] });

      await clickAndSettle(eligibleToggle());

      await waitFor(() => {
        expect(screen.getByText("You don't have permission to make this change.")).toBeInTheDocument();
      });
    });

    it("shows a toast naming the outcome on 409 and refreshes the row for retry", async () => {
      const segment = makeSegment({ version: "10" });
      const refreshed = makeSegment({ version: "77" });
      const mockFetch = makeSequencedFetchMock([
        { status: 409 },
        { status: 200, body: [refreshed] },
      ]);
      await renderSafeContent({ initialSegments: [segment], initialLibraryId: 7 });

      await clickAndSettle(eligibleToggle());

      await waitFor(() => {
        expect(
          screen.getByText("This track changed elsewhere — refreshed so you can retry.")
        ).toBeInTheDocument();
      });

      // The conflict-triggered refresh re-fetches the target library's segment list.
      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(3));
      const [refreshUrl] = mockFetch.mock.calls[2] as [string, RequestInit];
      expect(refreshUrl).toBe("/api/media?library-id=7&limit=200");
    });

    it("shows a toast naming the outcome on 412 and refreshes the row for retry", async () => {
      const segment = makeSegment({ version: "10" });
      const refreshed = makeSegment({ version: "78" });
      const mockFetch = makeSequencedFetchMock([
        { status: 412 },
        { status: 200, body: [refreshed] },
      ]);
      await renderSafeContent({ initialSegments: [segment], initialLibraryId: 7 });

      await clickAndSettle(eligibleToggle());

      await waitFor(() => {
        expect(
          screen.getByText("This track changed elsewhere — refreshed so you can retry.")
        ).toBeInTheDocument();
      });

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(3));
      const [refreshUrl] = mockFetch.mock.calls[2] as [string, RequestInit];
      expect(refreshUrl).toBe("/api/media?library-id=7&limit=200");
    });

    it("shows a toast naming the outcome on 415", async () => {
      makeSequencedFetchMock([{ status: 415 }]);
      await renderSafeContent({ initialSegments: [makeSegment()] });

      await clickAndSettle(eligibleToggle());

      await waitFor(() => {
        expect(
          screen.getByText("That request wasn't understood — this is a bug, not you.")
        ).toBeInTheDocument();
      });
    });

    it("shows a toast naming the outcome on a network error", async () => {
      makeFetchMockRejectingSecondCall();
      await renderSafeContent({ initialSegments: [makeSegment()] });

      await clickAndSettle(eligibleToggle());

      await waitFor(() => {
        expect(screen.getByText("Network error — check your connection")).toBeInTheDocument();
      });
    });

    it("leaves no call site that silently swallows a failure", async () => {
      // safe-content: the gitea-#181 site — used to swallow every failure outright.
      makeSequencedFetchMock([{ status: 403 }]);
      const safeContent = await renderSafeContent({ initialSegments: [makeSegment()] });
      await clickAndSettle(eligibleToggle());
      await waitFor(() => {
        expect(screen.getByText("You don't have permission to make this change.")).toBeInTheDocument();
      });
      safeContent.unmount();

      // catalog detail form.
      makeFetchMock(403);
      const editForm = await renderEditTrackForm({
        mediaId: "abc-123",
        initialValues: {
          title: "Test Track",
          artist: null,
          album: null,
          genre: null,
          year: null,
          eligible: true,
        },
        etag: 'W/"1"',
      });
      await clickAndSettle(screen.getByRole("button", { name: /save/i }));
      await waitFor(() => {
        expect(screen.getByText("You don't have permission to make this change.")).toBeInTheDocument();
      });
      editForm.unmount();

      // move-to-library.
      makeFetchMock(403);
      const moveAction = await renderMoveToLibraryAction({
        mediaId: "abc-123",
        etag: 'W/"1"',
        currentLibraryId: 1,
        libraries: [{ id: 1, name: "In Rotation", mediaCount: 5 }],
        scopeLibraryIds: [],
      });
      await clickAndSettle(screen.getByRole("button", { name: /move/i }));
      await waitFor(() => {
        expect(screen.getByText("You don't have permission to make this change.")).toBeInTheDocument();
      });
      moveAction.unmount();

      // selection-toolbar: aggregates per-row outcomes into one summary toast instead of the
      // hook's own per-row toast (Q7 review) — still a visible, never-silent failure signal.
      makeFetchMock(403);
      await renderCatalogTable(makeMedia());
      fireEvent.click(screen.getByRole("checkbox", { name: "Select Track 1" }));
      fireEvent.click(screen.getByRole("button", { name: "Set eligible" }));
      await confirmDialog();
      await waitFor(() => {
        expect(screen.getByText("1 of 1 failed.")).toBeInTheDocument();
      });
    });
  });
});

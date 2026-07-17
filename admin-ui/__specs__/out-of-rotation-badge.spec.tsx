// @jest-environment jsdom
// STORY-137 — Scope never blocks direct row access (Epic V / SPEC F43.5, closes gitea-#203) — UI half.
// The API half lives in Host.Tests/Specs/Story137_ScopeNeverBlocksRowAccess.cs.
//
// Runner: Jest (jsdom) + @testing-library/react. Authored PENDING at /plan time (2026-07-14,
// house rule since Epic S) as it.todo entries — V5 implements against the catalog detail page
// and the Safe content page.
//
// The catalog detail page's scenarios dynamically import `page.tsx` (mirrors
// catalog-pages.spec.ts's tree-walker suite, but rendered here via @testing-library/react instead
// of a plain tree-walk, since these specs need the mounted EditTrackForm/MoveToLibraryAction —
// both of which call `useRouter()`). next/jest's SWC transform hoists static ES imports above
// jest.mock(), so page.tsx (which statically imports those children) is imported inside each test,
// after the mocks below are registered — same pattern track-detail-redesign.spec.tsx and
// shared-patch-hook.spec.tsx already use.
//
// The Safe-content scenarios drive `SafeContentClient` directly with a mocked fetch, mirroring
// safe-content-page.spec.tsx's harness.

jest.mock("next/navigation", () => ({
  ...jest.requireActual("next/navigation"),
  useRouter: jest.fn(),
}));

jest.mock("next/headers", () => ({
  cookies: jest.fn().mockResolvedValue({
    toString: () => "session=test-cookie",
  }),
}));

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { useRouter } from "next/navigation";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import type { LibraryDto } from "@/lib/library";
import { SafeContentClient } from "../app/(authed)/safe-content/SafeContentClient";
import type { SafeContentClientProps, SafeSegmentDto } from "../app/(authed)/safe-content/SafeContentClient";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;

// ---------------------------------------------------------------------------
// Helpers — catalog detail page
// ---------------------------------------------------------------------------

const MEDIA_ID = "77";

/** Mirrors page.tsx's own (non-exported) `AdminMediaDto` — the GET /api/media/{id} row shape. */
interface MediaDetailDto {
  mediaId: string;
  locator: string;
  format: string;
  state: string;
  durationMs: number | null;
  title: string | null;
  artist: string | null;
  album: string | null;
  genre: string | null;
  year: number | null;
  integratedLufs: number | null;
  truePeakDbtp: number | null;
  measurable: boolean | null;
  cueInSec: number | null;
  cueOutSec: number | null;
  eligible?: boolean;
}

function makeMediaDto(overrides: Partial<MediaDetailDto> = {}): MediaDetailDto {
  return {
    mediaId: MEDIA_ID,
    locator: `/media/${MEDIA_ID}.flac`,
    format: "flac",
    state: "ready",
    durationMs: 180000,
    title: "Parked Track",
    artist: "Test Artist",
    album: "Test Album",
    genre: "Rock",
    year: 2024,
    integratedLufs: -14.0,
    truePeakDbtp: -1.0,
    measurable: true,
    cueInSec: null,
    cueOutSec: null,
    eligible: true,
    ...overrides,
  };
}

interface DetailFetchSpec {
  mediaBody: MediaDetailDto;
  /** True to have the GET carry `X-Out-Of-Scope: true` (SPEC F43.1). */
  outOfScope?: boolean;
  librariesBody?: LibraryDto[];
  /** Status/headers for the PATCH a Save click issues (F43.2 — scope never blocks it). */
  patchStatus?: number;
  patchHeaders?: Record<string, string>;
}

/** A fetch mock that answers GET /api/media/{id}, GET /api/libraries, and PATCH /api/media/{id}
 * by URL/method — all three land in the same render (media + libraries in Promise.all; PATCH on
 * a later Save click). */
function makeDetailFetchMock(spec: DetailFetchSpec): jest.MockedFunction<typeof fetch> {
  const fn = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
    const url = String(input);
    const method = init?.method ?? "GET";

    if (url.includes("/api/libraries")) {
      return {
        ok: true,
        status: 200,
        json: jest.fn<() => Promise<unknown>>().mockResolvedValue(spec.librariesBody ?? []),
        headers: new Headers({ "content-type": "application/json" }),
      } as unknown as Response;
    }

    if (method === "PATCH") {
      const patchStatus = spec.patchStatus ?? 204;
      return {
        ok: patchStatus >= 200 && patchStatus < 300,
        status: patchStatus,
        json: jest.fn<() => Promise<unknown>>().mockResolvedValue(null),
        headers: new Headers(spec.patchHeaders ?? {}),
      } as unknown as Response;
    }

    const mediaHeaders: Record<string, string> = { "content-type": "application/json" };
    if (spec.outOfScope === true) mediaHeaders["x-out-of-scope"] = "true";
    return {
      ok: true,
      status: 200,
      json: jest.fn<() => Promise<unknown>>().mockResolvedValue(spec.mediaBody),
      headers: new Headers(mediaHeaders),
    } as unknown as Response;
  });
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

/** Dynamically imports the detail page (see file header) and renders it in the same providers
 * EditTrackForm/MoveToLibraryAction need (router + confirm dialog + toaster). */
async function renderDetailPage(spec: DetailFetchSpec): Promise<ReturnType<typeof render>> {
  makeDetailFetchMock(spec);
  const { default: MediaDetailPage } = await import("../app/(authed)/catalog/[mediaId]/page");
  const node = await MediaDetailPage({ params: Promise.resolve({ mediaId: MEDIA_ID }) });
  return render(
    <ConfirmDialogProvider>
      {node}
      <Toaster />
    </ConfirmDialogProvider>
  );
}

// ---------------------------------------------------------------------------
// Helpers — Safe content page
// ---------------------------------------------------------------------------

const SEED_MESSAGE = "You're listening to {StationName}. We'll be right back — stay tuned.";
const DEFAULT_TITLE = "Please Stand By";

function makeLibraries(): LibraryDto[] {
  return [{ id: 7, name: "safe", mediaCount: 1 }];
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
 * scenario-triggered fetch (mirrors safe-content-page.spec.tsx / shared-patch-hook.spec.tsx). */
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

function renderSafeContent(overrides: Partial<SafeContentClientProps> = {}): ReturnType<typeof render> {
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

// ---------------------------------------------------------------------------
// Feature: Out-of-rotation rows are labeled, not forbidden
// ---------------------------------------------------------------------------

describe("Feature: Out-of-rotation rows are labeled, not forbidden", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
    mockedUseRouter.mockReturnValue({ refresh: jest.fn() } as unknown as ReturnType<typeof useRouter>);
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: the catalog detail page labels parked rows", () => {
    it("renders a 'not in rotation' badge on the detail page when the row fetch carried X-Out-Of-Scope: true (F43.5)", async () => {
      await renderDetailPage({ mediaBody: makeMediaDto(), outOfScope: true });

      expect(screen.getByText("Not in rotation")).toBeInTheDocument();
    });

    it("renders no badge for an in-scope row (F43.1)", async () => {
      await renderDetailPage({ mediaBody: makeMediaDto(), outOfScope: false });

      expect(screen.queryByText("Not in rotation")).not.toBeInTheDocument();
    });

    it("keeps the edit form fully functional on an out-of-scope row (F43.2)", async () => {
      await renderDetailPage({
        mediaBody: makeMediaDto(),
        outOfScope: true,
        patchStatus: 204,
        patchHeaders: { etag: 'W/"2"' },
      });

      // The row still badges as parked...
      expect(screen.getByText("Not in rotation")).toBeInTheDocument();

      // ...but the edit form's Save is unaffected — no scope check blocks the PATCH (F43.2).
      fireEvent.change(screen.getByLabelText("Title"), { target: { value: "Updated Title" } });
      await clickAndSettle(screen.getByRole("button", { name: /save/i }));

      await waitFor(() => {
        expect(screen.getByText("Saved.")).toBeInTheDocument();
      });
    });
  });

  describe("Scenario: the fresh-deploy Safe page works", () => {
    it("completes the eligibility toggle against the seeded safe library without any scope edit — the gitea-#203 repro (F43.5)", async () => {
      const segment = makeSegment({ eligible: true });
      makeSequencedFetchMock([{ status: 204, headers: { etag: 'W/"11"' } }]);

      // The default fresh-deploy shape: the safe library sits outside main scope, so the initial
      // browse already carried X-Out-Of-Scope: true — no scope edit precedes this toggle.
      renderSafeContent({ initialSegments: [segment], initialOutOfScope: true });

      expect(screen.getByText("Out of rotation")).toBeInTheDocument();

      await clickAndSettle(eligibleToggle());

      // Success — no 403, no error toast, and the row reflects the new state.
      await waitFor(() => expect(eligibleToggle()).not.toBeChecked());
      expect(screen.queryByText(/permission/i)).not.toBeInTheDocument();
    });
  });

  describe("Scenario (sad path): failures still surface", () => {
    it("surfaces a toast on a genuine PATCH failure per F31.3 — no silent swallow", async () => {
      const segment = makeSegment({ eligible: true });
      // A 403 here is a genuine auth failure (F31.3 classifies it as "forbidden") — not a scope
      // signal. F43 only repeals the scope-driven 403; the toast contract for a real failure is
      // unchanged.
      makeSequencedFetchMock([{ status: 403 }]);
      renderSafeContent({ initialSegments: [segment], initialOutOfScope: true });

      await clickAndSettle(eligibleToggle());

      await waitFor(() => {
        expect(screen.getByText("You don't have permission to make this change.")).toBeInTheDocument();
      });
      // The toggle's displayed state is left as-is rather than silently flipped.
      expect(eligibleToggle()).toBeChecked();
    });
  });
});

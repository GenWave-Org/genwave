// @jest-environment jsdom
// STORY-313 — Span-assign & imaging scope (F119.4) — imaging half.
//
// BDD specification — Jest (jsdom) + @testing-library/react + mocked fetch, mirroring
// imaging-kind.spec.tsx's own harness. The scope picker (SPEC F117.1) is a minimal delta on the
// Generate form (F119.4): gated to the station_id kind only (SafeContentClient's own
// SCOPE_GATED_KIND remarks — station_id is what T250's drain will consume; every other kind's
// scoped behavior is undefined until a consumer exists, so the picker stays hidden there to avoid
// dead config). Selecting Station ID reveals the Scope select, whose token rides the POST
// /api/safe-segments body as `showId`; the segment list renders each row's stored scope
// (station-wide | show name) resolved against the already-loaded roster — never a joined
// server-side field.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { SafeContentClient } from "../app/(authed)/safe-content/SafeContentClient";
import type { SafeContentClientProps, SafeSegmentDto } from "../app/(authed)/safe-content/SafeContentClient";
import type { ImagingShowOption } from "../app/(authed)/safe-content/imaging-show-scope";
import type { LibraryDto } from "../lib/library";

// ---------------------------------------------------------------------------
// Helpers (the safe-content-page.spec.tsx / imaging-kind.spec.tsx harness)
// ---------------------------------------------------------------------------

const SEED_MESSAGE = "You're listening to {StationName}. We'll be right back — stay tuned.";
const DEFAULT_TITLE = "Please Stand By";

function makeLibraries(): LibraryDto[] {
  return [
    { id: 7, name: "safe", mediaCount: 2 },
    { id: 1, name: "Main", mediaCount: 120 },
  ];
}

function makeShows(): ImagingShowOption[] {
  return [
    { id: 3, name: "Morning Drive" },
    { id: 9, name: "Night Owl" },
  ];
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
    imagingKind: "station_id",
    ...overrides,
  };
}

function renderClient(overrides: Partial<SafeContentClientProps> = {}): ReturnType<typeof render> {
  const props: SafeContentClientProps = {
    libraries: makeLibraries(),
    initialLibraryId: 7,
    initialSegments: [],
    initialOutOfScope: false,
    defaultText: SEED_MESSAGE,
    defaultTitle: DEFAULT_TITLE,
    shows: makeShows(),
    ...overrides,
  };
  return render(<SafeContentClient {...props} />);
}

interface MockResponseSpec {
  status: number;
  body?: unknown;
  headers?: Record<string, string>;
}

/** VoiceControl fetches GET /api/voices once on mount — call index 0 is always that fetch. */
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

async function clickGenerate(): Promise<void> {
  await act(async () => {
    fireEvent.click(screen.getByRole("button", { name: /generate/i }));
    await Promise.resolve();
  });
}

/** The generate POST is the call after the mount voices fetch. */
function generateBody(mockFetch: jest.MockedFunction<typeof fetch>): Record<string, unknown> {
  const [, init] = mockFetch.mock.calls[1] as [string, RequestInit];
  return JSON.parse(init.body as string) as Record<string, unknown>;
}

/** Arms the scope-gated kind (SafeContentClient's own SCOPE_GATED_KIND) so the Scope picker mounts. */
function selectStationIdKind(): void {
  fireEvent.change(screen.getByLabelText("Kind"), { target: { value: "station_id" } });
}

// ---------------------------------------------------------------------------
// Feature: Imaging show scope
// ---------------------------------------------------------------------------

describe("Feature: Imaging show scope", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: authoring with a scope", () => {
    it("the scope picker defaults to station-wide", () => {
      renderClient();
      selectStationIdKind();

      const picker = screen.getByLabelText("Scope") as HTMLSelectElement;
      expect(picker.value).toBe("");
      expect(within(picker).getByRole("option", { name: "Station-wide" })).toBeInTheDocument();
    });

    it("selecting a show sends the scope with the authored insert", async () => {
      const mockFetch = makeSequencedFetchMock([
        { status: 201, body: makeSegment({ showId: 9 }) },
      ]);
      renderClient();
      selectStationIdKind();

      fireEvent.change(screen.getByLabelText("Scope"), { target: { value: "9" } });
      await clickGenerate();

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(2));
      expect(generateBody(mockFetch)["showId"]).toBe(9);
    });

    it("existing authored rows render their scope (station-wide | show name)", () => {
      renderClient({
        initialSegments: [
          makeSegment({ mediaId: "1", title: "Morning Ident", showId: 3 }),
          makeSegment({ mediaId: "2", title: "General Ident", showId: null }),
        ],
      });

      const scoped = screen.getByRole("row", { name: /morning ident/i });
      expect(within(scoped).getByText("Morning Drive")).toBeInTheDocument();

      const unscoped = screen.getByRole("row", { name: /general ident/i });
      expect(within(unscoped).getByText("Station-wide")).toBeInTheDocument();
    });

    it("a row with a showId absent from the roster renders \"Unknown show\" (orphaned scope, e.g.\n       the show was deleted after authoring — SafeSegmentsController.Create's own TOCTOU remarks)", () => {
      renderClient({
        initialSegments: [makeSegment({ mediaId: "5", title: "Ghost Ident", showId: 404 })],
      });

      const row = screen.getByRole("row", { name: /ghost ident/i });
      expect(within(row).getByText("Unknown show")).toBeInTheDocument();
    });
  });

  // ---------------------------------------------------------------------------
  // SAD PATH — the picker is gated to station_id only (Dean's ratification, 2026-08-10)
  // ---------------------------------------------------------------------------

  describe("Scenario: the Scope picker is gated to the station_id kind", () => {
    it("is absent for the default Liner kind — no dead config for a kind with no scoped consumer", () => {
      renderClient();

      expect(screen.queryByLabelText("Scope")).not.toBeInTheDocument();
    });

    it("is absent for every other kind (Jingle, Promo)", () => {
      renderClient();

      fireEvent.change(screen.getByLabelText("Kind"), { target: { value: "jingle" } });
      expect(screen.queryByLabelText("Scope")).not.toBeInTheDocument();

      fireEvent.change(screen.getByLabelText("Kind"), { target: { value: "promo" } });
      expect(screen.queryByLabelText("Scope")).not.toBeInTheDocument();
    });
  });

  // ---------------------------------------------------------------------------
  // SAD PATH — switching away from station_id resets a picked scope
  // ---------------------------------------------------------------------------

  describe("Scenario: switching the kind away from station_id resets the scope", () => {
    it("hides the picker and re-arms station-wide when switching back to station_id", () => {
      renderClient();
      selectStationIdKind();
      fireEvent.change(screen.getByLabelText("Scope"), { target: { value: "9" } });

      // SafeContentClient's own handleKindChange remarks (Principle of Least Astonishment): a
      // hidden picker can never leave a stale show silently armed underneath it.
      fireEvent.change(screen.getByLabelText("Kind"), { target: { value: "liner" } });
      expect(screen.queryByLabelText("Scope")).not.toBeInTheDocument();

      selectStationIdKind();
      const picker = screen.getByLabelText("Scope") as HTMLSelectElement;
      expect(picker.value).toBe("");
    });

    it("submits no showId once the kind is off station_id, even after a scope was picked first", async () => {
      const mockFetch = makeSequencedFetchMock([
        { status: 201, body: makeSegment({ imagingKind: "liner", showId: null }) },
      ]);
      renderClient();
      selectStationIdKind();
      fireEvent.change(screen.getByLabelText("Scope"), { target: { value: "9" } });
      fireEvent.change(screen.getByLabelText("Kind"), { target: { value: "liner" } });

      await clickGenerate();

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(2));
      // Pins the request-build guard in SafeContentClient.handleGenerate (mirrors
      // SafeSegmentsController.Create's own :214 belt-and-braces posture on the wire side) —
      // showId only ever rides the body for the gated kind, never as a leftover from a prior pick.
      expect(generateBody(mockFetch)["showId"]).toBeUndefined();
    });
  });
});

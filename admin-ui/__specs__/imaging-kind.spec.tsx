// @jest-environment jsdom
// gh-#149 — Authored segments carry a Station Imaging content kind (admin-ui half).
//
// BDD specification — Jest (jsdom) + @testing-library/react + mocked fetch, mirroring
// safe-content-page.spec.tsx's harness. The Generate form grows a Kind picker (Liner default —
// today's behavior) whose token rides the POST /api/safe-segments body as `kind`; the segment
// list badges each row's stored `imagingKind` (NULL displays as the Liner default) and gains a
// client-side kind filter. Kinds are METADATA-ONLY — nothing here asserts any playout change.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import { SafeContentClient } from "../app/(authed)/safe-content/SafeContentClient";
import type { SafeContentClientProps, SafeSegmentDto } from "../app/(authed)/safe-content/SafeContentClient";
import type { LibraryDto } from "../lib/library";

// ---------------------------------------------------------------------------
// Helpers (the safe-content-page.spec.tsx harness)
// ---------------------------------------------------------------------------

const SEED_MESSAGE = "You're listening to {StationName}. We'll be right back — stay tuned.";
const DEFAULT_TITLE = "Please Stand By";

function makeLibraries(): LibraryDto[] {
  return [
    { id: 7, name: "safe", mediaCount: 2 },
    { id: 1, name: "Main", mediaCount: 120 },
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
    imagingKind: "liner",
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
    shows: [],
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

// ---------------------------------------------------------------------------
// Feature: Station Imaging content kinds
// ---------------------------------------------------------------------------

describe("Feature: Station Imaging content kinds", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: the Generate form carries a kind picker", () => {
    it("renders the Kind picker with Liner selected by default", () => {
      renderClient();

      const picker = screen.getByLabelText("Kind") as HTMLSelectElement;
      expect(picker.value).toBe("liner");
      for (const label of ["Liner", "Station ID", "Jingle", "Promo"]) {
        expect(within(picker).getByRole("option", { name: label })).toBeInTheDocument();
      }
    });

    it("submits kind: liner when the picker is untouched (today's behavior)", async () => {
      const mockFetch = makeSequencedFetchMock([{ status: 201, body: makeSegment() }]);
      renderClient();

      await clickGenerate();

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(2));
      expect(generateBody(mockFetch)["kind"]).toBe("liner");
    });

    it("submits the picked kind token", async () => {
      const mockFetch = makeSequencedFetchMock([
        { status: 201, body: makeSegment({ imagingKind: "station_id" }) },
      ]);
      renderClient();

      fireEvent.change(screen.getByLabelText("Kind"), { target: { value: "station_id" } });
      await clickGenerate();

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(2));
      expect(generateBody(mockFetch)["kind"]).toBe("station_id");
    });

    it("disables the picker while a render is pending", async () => {
      // Call 0 (mount voices fetch) resolves; the generate POST never resolves — in-flight state.
      const fn = jest.fn<typeof fetch>()
        .mockImplementationOnce(async () =>
          ({ ok: true, status: 200, json: async () => [], headers: new Headers() }) as unknown as Response)
        .mockImplementation(() => new Promise<Response>(() => {}));
      global.fetch = fn as unknown as typeof fetch;
      renderClient();

      await clickGenerate();

      expect(screen.getByLabelText("Kind")).toBeDisabled();
    });
  });

  describe("Scenario: the segment list badges each row's kind", () => {
    it("renders the stored kind's label as a badge on the row", () => {
      renderClient({
        initialSegments: [makeSegment({ mediaId: "1", title: "Top Hour Ident", imagingKind: "jingle" })],
      });

      const row = screen.getByRole("row", { name: /top hour ident/i });
      expect(within(row).getByText("Jingle")).toBeInTheDocument();
    });

    it("badges a row with no stored kind as the Liner default (pre-#149 rows)", () => {
      renderClient({
        initialSegments: [makeSegment({ mediaId: "1", title: "Old Announcement", imagingKind: null })],
      });

      const row = screen.getByRole("row", { name: /old announcement/i });
      expect(within(row).getByText("Liner")).toBeInTheDocument();
    });
  });

  describe("Scenario: the list filters by kind", () => {
    const mixed = [
      makeSegment({ mediaId: "1", title: "Ident", imagingKind: "station_id" }),
      makeSegment({ mediaId: "2", title: "Stand By", imagingKind: "liner" }),
      makeSegment({ mediaId: "3", title: "Legacy Row", imagingKind: null }),
    ];

    it("narrows the table to the picked kind", () => {
      renderClient({ initialSegments: mixed });

      fireEvent.change(screen.getByLabelText("Filter by kind"), { target: { value: "station_id" } });

      expect(screen.getByRole("row", { name: /ident/i })).toBeInTheDocument();
      expect(screen.queryByRole("row", { name: /stand by/i })).not.toBeInTheDocument();
    });

    it("counts a NULL-kind row as Liner when filtering (matching its badge)", () => {
      renderClient({ initialSegments: mixed });

      fireEvent.change(screen.getByLabelText("Filter by kind"), { target: { value: "liner" } });

      expect(screen.getByRole("row", { name: /stand by/i })).toBeInTheDocument();
      expect(screen.getByRole("row", { name: /legacy row/i })).toBeInTheDocument();
      expect(screen.queryByRole("row", { name: /ident/i })).not.toBeInTheDocument();
    });

    it("returns the full list on All kinds", () => {
      renderClient({ initialSegments: mixed });
      const filter = screen.getByLabelText("Filter by kind");

      fireEvent.change(filter, { target: { value: "promo" } });
      fireEvent.change(filter, { target: { value: "all" } });

      expect(screen.getByRole("row", { name: /ident/i })).toBeInTheDocument();
      expect(screen.getByRole("row", { name: /stand by/i })).toBeInTheDocument();
      expect(screen.getByRole("row", { name: /legacy row/i })).toBeInTheDocument();
    });

    it("says the filter (not the library) is empty when no row matches — no generate CTA", () => {
      renderClient({ initialSegments: mixed });

      fireEvent.change(screen.getByLabelText("Filter by kind"), { target: { value: "promo" } });

      expect(screen.getByRole("status")).toHaveTextContent("No segments of this kind");
      expect(screen.queryByText("No imaging segments yet")).not.toBeInTheDocument();
    });
  });
});

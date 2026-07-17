// @jest-environment jsdom
// STORY-092 — Safe content page in the new identity (Epic Q / SPEC F28.9–F28.10)
//
// Runner: Jest (jsdom) + @testing-library/react + mocked fetch. Mirrors
// track-detail-redesign.spec.tsx's harness pattern: toast() needs its viewport, so every render
// mounts <Toaster/> alongside SafeContentClient and toast assertions read the rendered toast
// text rather than spying on the module. Presentation-only coverage — the wire contract (POST
// body shape, content-type, 201/400/502 handling, eligibility PATCH) is exhaustively covered by
// safe-content-page.spec.tsx and is NOT re-asserted here beyond what each AC needs.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import { Toaster } from "@/components/ui/toast";
import { SafeContentClient } from "../app/(authed)/safe-content/SafeContentClient";
import type { SafeContentClientProps, SafeSegmentDto } from "../app/(authed)/safe-content/SafeContentClient";
import type { LibraryDto } from "../lib/library";

// ---------------------------------------------------------------------------
// Helpers
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
    ...overrides,
  };
  return render(
    <>
      <SafeContentClient {...props} />
      <Toaster />
    </>
  );
}

interface MockResponseSpec {
  status: number;
  body?: unknown;
  headers?: Record<string, string>;
}

/**
 * SafeContentClient's VoiceControl child fetches GET /api/voices once on mount (STORY-098 /
 * F29.5), before any scenario-triggered fetch — every sequenced mock is prefixed with this
 * benign response so scenario call-index assertions land on the fetch each scenario actually
 * drives. The voice dropdown itself is covered by voice-dropdown.spec.tsx, not re-tested here.
 */
const VOICES_MOUNT_SPEC: MockResponseSpec = { status: 200, body: [] };

/** A fetch mock that replays one response per call, in order (last spec repeats if exhausted). */
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

function makeFetchMock(status: number, body: unknown = {}): jest.MockedFunction<typeof fetch> {
  return makeSequencedFetchMock([{ status, body }]);
}

/** A fetch mock whose promise never resolves — used to assert in-flight disabled state. */
function makeNeverResolvingFetchMock(): jest.MockedFunction<typeof fetch> {
  const fn = jest.fn<typeof fetch>().mockImplementation(() => new Promise<Response>(() => {}));
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

async function clickGenerate(): Promise<void> {
  await act(async () => {
    fireEvent.click(screen.getByRole("button", { name: /generate/i }));
    await Promise.resolve();
  });
}

// ---------------------------------------------------------------------------
// Feature: Safe content redesign
// ---------------------------------------------------------------------------

describe("Feature: Safe content redesign", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: generate flow with toast and disabled state", () => {
    it("disables the form while rendering", async () => {
      makeNeverResolvingFetchMock();
      renderClient();

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /generate/i }));
        await Promise.resolve();
      });

      // Covers the BedPicker's disabled wiring specifically — the Text/Title disable is already
      // exercised by safe-content-page.spec.tsx.
      expect(screen.getByRole("button", { name: /search/i })).toBeDisabled();
    });

    it("toasts success on 201", async () => {
      makeFetchMock(201, makeSegment());
      renderClient();

      await clickGenerate();

      await waitFor(() => {
        expect(screen.getByText("Segment generated.")).toBeInTheDocument();
      });
    });

    it("prepends the new segment to the list", async () => {
      const existing = makeSegment({ mediaId: "1", title: "Old Announcement" });
      makeFetchMock(201, makeSegment({ mediaId: "2", title: "Brand New Announcement" }));
      renderClient({ initialSegments: [existing] });

      await clickGenerate();

      await waitFor(() => {
        const rows = within(screen.getByRole("table")).getAllByRole("row");
        expect(rows[1]).toHaveTextContent("Brand New Announcement");
      });
    });
  });

  describe("Scenario: bed picker in the new identity", () => {
    it("renders the bed search as a styled combobox over the shipped catalog search", async () => {
      const searchResults = [{ mediaId: "5", title: "Jingle One", artist: "House Band" }];
      makeFetchMock(200, searchResults);
      renderClient();

      const input = screen.getByRole("combobox", { name: /bed \(optional\)/i });
      expect(input).toHaveAttribute("aria-expanded", "false");

      fireEvent.change(input, { target: { value: "jingle" } });
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /search/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByRole("listbox", { name: /bed search results/i })).toBeInTheDocument();
      });
      expect(input).toHaveAttribute("aria-expanded", "true");
    });

    it("submits the picked row's id as bedMediaId", async () => {
      const searchResults = [{ mediaId: "5", title: "Jingle One", artist: "House Band" }];
      const mockFetch = makeSequencedFetchMock([
        { status: 200, body: searchResults },
        { status: 201, body: makeSegment() },
      ]);
      renderClient();

      const input = screen.getByRole("combobox", { name: /bed \(optional\)/i });
      fireEvent.change(input, { target: { value: "jingle" } });
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /search/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByRole("option", { name: /jingle one/i })).toBeInTheDocument();
      });

      // Keyboard path: ArrowDown highlights the first result, Enter selects it — the mouse-click
      // "Select" button path is already covered by safe-content-page.spec.tsx.
      fireEvent.keyDown(input, { key: "ArrowDown" });
      fireEvent.keyDown(input, { key: "Enter" });

      await waitFor(() => {
        expect(screen.getByRole("button", { name: /clear/i })).toBeInTheDocument();
      });

      await clickGenerate();

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(3));
      const [, generateInit] = mockFetch.mock.calls[2] as [string, RequestInit];
      const body = JSON.parse(generateInit.body as string) as Record<string, unknown>;
      expect(body["bedMediaId"]).toBe(5);
    });

    it("closes the results on Escape without submitting the outer Generate form", async () => {
      const searchResults = [{ mediaId: "5", title: "Jingle One", artist: "House Band" }];
      const mockFetch = makeFetchMock(200, searchResults);
      renderClient();

      const input = screen.getByRole("combobox", { name: /bed \(optional\)/i });
      fireEvent.change(input, { target: { value: "jingle" } });
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /search/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByRole("listbox")).toBeInTheDocument();
      });

      fireEvent.keyDown(input, { key: "Escape" });

      expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
      // Only the mount voices fetch + the one search fetch happened — Escape never
      // reached/triggered the Generate submit.
      expect(mockFetch).toHaveBeenCalledTimes(2);
    });
  });

  describe("Scenario: empty state invites the first segment", () => {
    it("renders the EmptyState pointing at the Generate form", () => {
      renderClient({ initialSegments: [] });

      expect(
        screen.getByText("Generate the first announcement using the form above.")
      ).toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  // SAD PATH
  // -------------------------------------------------------------------------

  describe("Scenario: rejecting failed generation without a stuck form (sad path)", () => {
    it("toasts the 400/502 error with inline detail where field-specific", async () => {
      makeFetchMock(400, { detail: "text must not be blank or whitespace." });
      renderClient();

      await clickGenerate();

      await waitFor(() => {
        expect(screen.getAllByText("text must not be blank or whitespace.").length).toBe(2);
      });
    });

    it("re-enables the form after the failure", async () => {
      makeFetchMock(502, { detail: "Render failed." });
      renderClient();

      const button = screen.getByRole("button", { name: /generate/i });
      await clickGenerate();

      await waitFor(() => {
        expect(screen.getByRole("alert")).toBeInTheDocument();
      });

      expect(button).not.toBeDisabled();
    });
  });
});

// @jest-environment jsdom
// STORY-081 — Admin UI: Safe content page
//
// BDD specification — jest. SPEC F27.9. Generate form (text pre-filled from
// the Station:Safe:SeedMessage default, title pre-filled "Please Stand By",
// voice, optional bed picker over the shipped catalog search, target library
// defaulting to the safe library) POSTing /api/safe-segments; segment list =
// shipped browse filtered to the target library with the shipped eligibility
// toggle. Drives SafeContentClient via @testing-library/react with a mocked
// fetch — mirrors settings-page.spec.tsx / edit-track.spec.tsx in style. The
// page consumes ONLY STORY-079's shipped endpoint (the W6/L7/K5
// don't-invent-contracts discipline).

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom";
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
  return render(<SafeContentClient {...props} />);
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

// ---------------------------------------------------------------------------
// Feature: Safe content page
// ---------------------------------------------------------------------------

describe("Feature: Safe content page", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: generate round-trips", () => {
    it("pre-fills the text field with the seed-message default", () => {
      renderClient();

      const textField = screen.getByLabelText("Text") as HTMLTextAreaElement;
      expect(textField.value).toBe(SEED_MESSAGE);
    });

    it('pre-fills the title field with "Please Stand By"', () => {
      renderClient();

      const titleField = screen.getByLabelText("Title") as HTMLInputElement;
      expect(titleField.value).toBe(DEFAULT_TITLE);
    });

    it("POSTs /api/safe-segments with the form values on Generate", async () => {
      const mockFetch = makeFetchMock(201, makeSegment());
      renderClient();

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /generate/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledTimes(2);
      });

      const [url, init] = mockFetch.mock.calls[1] as [string, RequestInit];
      expect(url).toBe("/api/safe-segments");
      expect(init.method).toBe("POST");

      const headers = init.headers as Record<string, string>;
      expect(headers["Content-Type"]).toBe("application/json");

      const body = JSON.parse(init.body as string) as Record<string, unknown>;
      expect(body["text"]).toBe(SEED_MESSAGE);
      expect(body["title"]).toBe(DEFAULT_TITLE);
      expect(body["libraryId"]).toBe(7);
      expect(body["voice"]).toBeUndefined();
      expect(body["bedMediaId"]).toBeUndefined();
    });

    it("disables the form while the render is in flight", async () => {
      makeNeverResolvingFetchMock();
      renderClient();

      const button = screen.getByRole("button", { name: /generate/i });

      await act(async () => {
        fireEvent.click(button);
        await Promise.resolve();
      });

      expect(button).toBeDisabled();
      expect(screen.getByLabelText("Text")).toBeDisabled();
      expect(screen.getByLabelText("Title")).toBeDisabled();
    });

    it("appends the created segment to the list on 201", async () => {
      const created = makeSegment({ mediaId: "99", title: "New Announcement", version: "1" });
      makeFetchMock(201, created);
      renderClient();

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /generate/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByText("New Announcement")).toBeInTheDocument();
      });
    });
  });

  describe("Scenario: bed picker searches the catalog", () => {
    it("offers matching catalog rows for a typed search term", async () => {
      const searchResults = [
        { mediaId: "5", title: "Jingle One", artist: "House Band" },
        { mediaId: "6", title: "Jingle Two", artist: null },
      ];
      const mockFetch = makeFetchMock(200, searchResults);
      renderClient();

      fireEvent.change(screen.getByLabelText("Bed (optional)"), { target: { value: "jingle" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /search/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByText(/Jingle One/)).toBeInTheDocument();
        expect(screen.getByText(/Jingle Two/)).toBeInTheDocument();
      });

      const [url] = mockFetch.mock.calls[1] as [string, RequestInit];
      expect(url).toBe("/api/media?q=jingle");
    });

    it("submits the selected row's id as bedMediaId", async () => {
      const searchResults = [{ mediaId: "5", title: "Jingle One", artist: "House Band" }];
      const mockFetch = makeSequencedFetchMock([
        { status: 200, body: searchResults },
        { status: 201, body: makeSegment() },
      ]);
      renderClient();

      fireEvent.change(screen.getByLabelText("Bed (optional)"), { target: { value: "jingle" } });
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /search/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByRole("button", { name: /^select$/i })).toBeInTheDocument();
      });

      fireEvent.click(screen.getByRole("button", { name: /^select$/i }));

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /generate/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledTimes(3);
      });

      const [, generateInit] = mockFetch.mock.calls[2] as [string, RequestInit];
      const body = JSON.parse(generateInit.body as string) as Record<string, unknown>;
      expect(body["bedMediaId"]).toBe(5);
    });

    it("submits no bedMediaId when the bed field is left empty", async () => {
      const mockFetch = makeFetchMock(201, makeSegment());
      renderClient();

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /generate/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledTimes(2);
      });

      const [, init] = mockFetch.mock.calls[1] as [string, RequestInit];
      const body = JSON.parse(init.body as string) as Record<string, unknown>;
      expect(body["bedMediaId"]).toBeUndefined();
    });
  });

  describe("Scenario: eligibility toggle retires a segment", () => {
    it("keeps a toggled-off segment visible in the list", async () => {
      const segment = makeSegment({ mediaId: "42", title: "Please Stand By", eligible: true });
      makeFetchMock(204);
      renderClient({ initialSegments: [segment] });

      const toggle = screen.getByRole("checkbox", { name: /eligible: please stand by/i });

      await act(async () => {
        fireEvent.click(toggle);
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(toggle).not.toBeChecked();
      });
      expect(screen.getByText("Please Stand By")).toBeInTheDocument();
    });

    it("PATCHes eligible=false via the shipped F18 control", async () => {
      const segment = makeSegment({ mediaId: "42", title: "Please Stand By", eligible: true, version: "10" });
      const mockFetch = makeFetchMock(204);
      renderClient({ initialSegments: [segment] });

      const toggle = screen.getByRole("checkbox", { name: /eligible: please stand by/i });

      await act(async () => {
        fireEvent.click(toggle);
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledTimes(2);
      });

      const [url, init] = mockFetch.mock.calls[1] as [string, RequestInit];
      expect(url).toBe("/api/media/42");
      expect(init.method).toBe("PATCH");

      const headers = init.headers as Record<string, string>;
      expect(headers["If-Match"]).toBe('W/"10"');

      const body = JSON.parse(init.body as string) as Record<string, unknown>;
      expect(body["eligible"]).toBe(false);
    });
  });

  // -------------------------------------------------------------------------
  // SAD PATH
  // -------------------------------------------------------------------------

  describe("Scenario: failure re-enables the form with the error", () => {
    it("renders the inline ProblemDetails error on a 400", async () => {
      makeFetchMock(400, { detail: "text must not be blank or whitespace." });
      renderClient();

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /generate/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByRole("alert")).toHaveTextContent(
          "text must not be blank or whitespace."
        );
      });
    });

    it("renders the inline error on a 502", async () => {
      makeFetchMock(502, {
        detail: "The safe-segment could not be generated. Check the server logs for details.",
      });
      renderClient();

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /generate/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByRole("alert")).toHaveTextContent(
          "The safe-segment could not be generated. Check the server logs for details."
        );
      });
    });

    it("re-enables the form after a failure (K5 stuck-Saving regression class)", async () => {
      makeFetchMock(502, { detail: "Render failed." });
      renderClient();

      const button = screen.getByRole("button", { name: /generate/i });

      await act(async () => {
        fireEvent.click(button);
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByRole("alert")).toBeInTheDocument();
      });

      expect(button).not.toBeDisabled();
      expect(screen.getByLabelText("Text")).not.toBeDisabled();
    });
  });
});

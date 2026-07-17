// @jest-environment jsdom
// STORY-098 — Voice dropdown with graceful fallback (Epic R / SPEC F29.5, gitea-#183)
//
// The Safe content Voice control becomes a select fed by GET /api/voices with "Station
// default" first/selected (submits NO `voice` field — the shipped omit-if-blank wire contract
// is unchanged); on listing failure it falls back to the shipped free-text input with a visible
// notice, and generation stays possible either way. Drives SafeContentClient (which composes
// VoiceControl) via @testing-library/react with a mocked fetch — mirrors safe-content-page.spec.tsx
// in style (renderClient helper, makeSequencedFetchMock).

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
const VOICE_IDS = ["af_alloy", "af_aoede"];

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
  networkError?: boolean;
}

/** A fetch mock that replays one response per call, in order (last spec repeats if exhausted). */
function makeSequencedFetchMock(specs: MockResponseSpec[]): jest.MockedFunction<typeof fetch> {
  let callIndex = 0;
  const fn = jest.fn<typeof fetch>().mockImplementation(async () => {
    const spec = specs[callIndex] ?? specs[specs.length - 1]!;
    callIndex += 1;
    if (spec.networkError === true) {
      throw new Error("network error");
    }
    return {
      ok: spec.status >= 200 && spec.status < 300,
      status: spec.status,
      json: jest.fn<() => Promise<unknown>>().mockResolvedValue(spec.body ?? {}),
      headers: new Headers(),
    } as unknown as Response;
  });
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

/** GET /api/voices succeeds with VOICE_IDS, ready for a subsequent POST /api/safe-segments. */
function makeVoicesThenGenerateFetchMock(
  generateSpec: MockResponseSpec = { status: 201, body: makeSegment() }
): jest.MockedFunction<typeof fetch> {
  return makeSequencedFetchMock([{ status: 200, body: VOICE_IDS }, generateSpec]);
}

// ---------------------------------------------------------------------------
// Feature: Voice dropdown with graceful fallback
// ---------------------------------------------------------------------------

describe("Feature: Voice dropdown with graceful fallback", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: the voices listing succeeds", () => {
    it("renders the Voice control as a select", async () => {
      makeSequencedFetchMock([{ status: 200, body: VOICE_IDS }]);
      renderClient();

      await waitFor(() => {
        expect(screen.getByLabelText("Voice").tagName).toBe("SELECT");
      });
    });

    it("lists 'Station default' as the first and selected option", async () => {
      makeSequencedFetchMock([{ status: 200, body: VOICE_IDS }]);
      renderClient();

      await waitFor(() => {
        const select = screen.getByLabelText("Voice") as HTMLSelectElement;
        expect(select.options[0]?.textContent).toBe("Station default");
        expect(select.options[0]?.value).toBe("");
        expect(select.value).toBe("");
      });
    });

    it("lists every voice id returned by /api/voices", async () => {
      makeSequencedFetchMock([{ status: 200, body: VOICE_IDS }]);
      renderClient();

      await waitFor(() => {
        const select = screen.getByLabelText("Voice") as HTMLSelectElement;
        const optionValues = Array.from(select.options).map((o) => o.value);
        expect(optionValues).toEqual(["", ...VOICE_IDS]);
      });
    });
  });

  describe("Scenario: submitting with the dropdown", () => {
    it("omits the voice field from the POST body when 'Station default' is selected", async () => {
      const mockFetch = makeVoicesThenGenerateFetchMock();
      renderClient();

      await waitFor(() => {
        expect(screen.getByLabelText("Voice").tagName).toBe("SELECT");
      });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /generate/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledTimes(2);
      });

      const [, generateInit] = mockFetch.mock.calls[1] as [string, RequestInit];
      const body = JSON.parse(generateInit.body as string) as Record<string, unknown>;
      expect(body["voice"]).toBeUndefined();
    });

    it("submits the chosen voice id when a specific voice is selected", async () => {
      const mockFetch = makeVoicesThenGenerateFetchMock();
      renderClient();

      const select = await screen.findByLabelText("Voice") as HTMLSelectElement;
      await waitFor(() => {
        expect(Array.from(select.options).map((o) => o.value)).toContain("af_aoede");
      });

      fireEvent.change(select, { target: { value: "af_aoede" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /generate/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledTimes(2);
      });

      const [, generateInit] = mockFetch.mock.calls[1] as [string, RequestInit];
      const body = JSON.parse(generateInit.body as string) as Record<string, unknown>;
      expect(body["voice"]).toBe("af_aoede");
    });

    it("keeps the rest of the request body byte-identical to the shipped contract", async () => {
      const mockFetch = makeVoicesThenGenerateFetchMock();
      renderClient();

      const select = await screen.findByLabelText("Voice") as HTMLSelectElement;
      await waitFor(() => {
        expect(Array.from(select.options).map((o) => o.value)).toContain("af_aoede");
      });

      fireEvent.change(select, { target: { value: "af_aoede" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /generate/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledTimes(2);
      });

      const [url, generateInit] = mockFetch.mock.calls[1] as [string, RequestInit];
      expect(url).toBe("/api/safe-segments");
      expect(generateInit.method).toBe("POST");

      const headers = generateInit.headers as Record<string, string>;
      expect(headers["Content-Type"]).toBe("application/json");

      const body = JSON.parse(generateInit.body as string) as Record<string, unknown>;
      expect(Object.keys(body).sort()).toEqual(["libraryId", "text", "title", "voice"].sort());
      expect(body["text"]).toBe(SEED_MESSAGE);
      expect(body["title"]).toBe(DEFAULT_TITLE);
      expect(body["libraryId"]).toBe(7);
      expect(body["voice"]).toBe("af_aoede");
      expect(body["bedMediaId"]).toBeUndefined();
    });
  });

  describe("Scenario: the voices listing fails (sad path)", () => {
    it("falls back to the free-text voice input", async () => {
      makeSequencedFetchMock([{ status: 502, body: { detail: "Kokoro unreachable" } }]);
      renderClient();

      await waitFor(() => {
        const voiceField = screen.getByLabelText("Voice") as HTMLInputElement;
        expect(voiceField.tagName).toBe("INPUT");
        expect(voiceField.type).toBe("text");
      });
    });

    it("shows a visible notice that the voice list is unavailable", async () => {
      makeSequencedFetchMock([{ status: 502, body: { detail: "Kokoro unreachable" } }]);
      renderClient();

      await waitFor(() => {
        expect(screen.getByText(/voice list unavailable/i)).toBeInTheDocument();
      });
    });

    it("still allows generation with the typed or default voice", async () => {
      const mockFetch = makeSequencedFetchMock([
        { status: 502, body: { detail: "Kokoro unreachable" } },
        { status: 201, body: makeSegment() },
      ]);
      renderClient();

      const voiceField = await waitFor(() => {
        const field = screen.getByLabelText("Voice") as HTMLInputElement;
        expect(field.tagName).toBe("INPUT");
        return field;
      });

      fireEvent.change(voiceField, { target: { value: "custom-voice" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /generate/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledTimes(2);
      });

      const [url, generateInit] = mockFetch.mock.calls[1] as [string, RequestInit];
      expect(url).toBe("/api/safe-segments");
      const body = JSON.parse(generateInit.body as string) as Record<string, unknown>;
      expect(body["voice"]).toBe("custom-voice");
    });
  });
});

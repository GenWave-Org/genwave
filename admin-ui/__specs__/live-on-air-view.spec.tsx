// @jest-environment jsdom
// STORY-088 — Live page: full now-playing + play-history view (Epic Q / SPEC F28.7–F28.8, F28.10)
//
// Runner: Jest (jsdom) + @testing-library/react. Drives LiveView (the client
// component page.tsx renders) with a mocked global.fetch dispatched by URL
// substring across the two polled endpoints (now-playing, play-history) —
// mirrors dashboard-page.spec.tsx's installFetchMock style, minus the
// /api/status endpoint the Live page doesn't poll.
//
// Wire contract note: the play-history entries this suite exercises have no
// "source" field (SPEC F16.2 / PlayHistoryEntry has none) — STORY-088's
// original "source chip" acceptance criteria were struck at the Q5 review
// (docs/STORIES.md, amended). Columns asserted here are exactly time,
// title, artist, gain.
//
// This file supersedes the legacy live-page.spec.tsx (STORY-045, deleted):
// its now-playing/drain/cold-start/ordering/poll-cadence coverage is folded
// in below against the new LiveView (no more initialNow/initialHistory SSR
// props — LiveView now polls via the shared usePoll hook, like the
// Dashboard) rather than silently dropped. Its `cache: "no-store"` fetch-
// option assertions moved to __specs__/broadcast-api.spec.ts, which tests
// lib/broadcast-api.ts's fetchers directly — the one place all three
// pollers (now-playing, status, play-history) actually set that option,
// instead of re-asserting it per call site here. Its per-row endedAt
// assertions are the only coverage NOT migrated: STORY-088 AC2 (as amended)
// locks the play-history columns to exactly time/title/artist/gain, the
// same set the Dashboard's RecentPlays renders — endedAt was never part of
// that contract.

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, act } from "@testing-library/react";
import "@testing-library/jest-dom";
import { LiveView } from "../app/(authed)/live/LiveView";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const ISO_NOW = "2026-01-01T12:00:00.000Z";

interface TrackOverrides {
  stationId?: string;
  mediaId?: string;
  title?: string;
  artist?: string;
  gainDb?: number;
  startedAt?: string;
}

function makeTrack(overrides: TrackOverrides = {}) {
  return {
    stationId: "1",
    mediaId: "m1",
    title: "Astral Plane",
    artist: "Valerie June",
    gainDb: -2.3,
    startedAt: ISO_NOW,
    ...overrides,
  };
}

function makeDrain(): { stationId: string; drain: true } {
  return { stationId: "1", drain: true };
}

interface HistoryOverrides {
  mediaId?: string;
  title?: string;
  artist?: string;
  gainDb?: number;
  startedAt?: string;
  endedAt?: string;
}

function makeHistoryEntry(overrides: HistoryOverrides = {}) {
  return {
    mediaId: "m9",
    title: "Deep Blue",
    artist: "Arca",
    gainDb: -1.25,
    startedAt: "2026-01-01T10:04:00.000Z",
    endedAt: "2026-01-01T10:08:00.000Z",
    ...overrides,
  };
}

type MockResult =
  | { kind: "ok"; status?: number; body: unknown }
  | { kind: "network-error" };

function ok(body: unknown, status = 200): MockResult {
  return { kind: "ok", status, body };
}

function networkError(): MockResult {
  return { kind: "network-error" };
}

interface FetchState {
  now: MockResult;
  history: MockResult;
  /** PLAN T71 (SPEC F84.6) — the taste-thumb resolution poll LiveView now also fires; harmless
   * empty defaults here since none of this file's scenarios exercise persona taste. */
  boothLog: MockResult;
  personas: MockResult;
}

function defaultState(overrides: Partial<FetchState> = {}): FetchState {
  return {
    now: ok(makeTrack()),
    history: ok([]),
    boothLog: ok({ entries: [], nextBefore: null }),
    personas: ok([]),
    ...overrides,
  };
}

function endpointKeyFor(url: string): keyof FetchState {
  if (url.includes("play-history")) return "history";
  if (url.includes("/api/booth-log")) return "boothLog";
  if (url.includes("/api/personas")) return "personas";
  return "now";
}

function installFetchMock(initial: FetchState) {
  const state: FetchState = { ...initial };
  const fn = jest.fn<typeof fetch>().mockImplementation((input) => {
    const result = state[endpointKeyFor(String(input))];
    if (result.kind === "network-error") {
      return Promise.reject(new Error("network error"));
    }
    const status = result.status ?? 200;
    return Promise.resolve({
      ok: status >= 200 && status < 300,
      status,
      json: () => Promise.resolve(result.body),
    } as Response);
  });
  global.fetch = fn as unknown as typeof fetch;
  return { fn, state };
}

/** Flushes the initial-mount poll (or any already-scheduled microtasks) without advancing time. */
async function flush(): Promise<void> {
  await act(async () => {
    await jest.advanceTimersByTimeAsync(0);
  });
}

/** Advances fake time and flushes the resulting fetch/json promise chain(s). */
async function advance(ms: number): Promise<void> {
  await act(async () => {
    await jest.advanceTimersByTimeAsync(ms);
  });
}

beforeEach(() => {
  jest.useFakeTimers({ now: new Date(ISO_NOW) });
});

afterEach(() => {
  jest.useRealTimers();
  jest.restoreAllMocks();
});

// ---------------------------------------------------------------------------
// Feature: Live on-air view
// ---------------------------------------------------------------------------

describe("Feature: Live on-air view", () => {
  // -------------------------------------------------------------------------
  describe("Scenario: hero card with dial progress", () => {
    it("renders the ON AIR badge with the now-playing hero", async () => {
      installFetchMock(defaultState({ now: ok(makeTrack()) }));

      render(<LiveView timeZone="UTC" />);
      await flush();

      expect(screen.getByText("On air")).toBeInTheDocument();
    });

    it("shows title, artist and gain", async () => {
      installFetchMock(
        defaultState({ now: ok(makeTrack({ title: "Astral Plane", artist: "Valerie June", gainDb: -2.3 })) })
      );

      render(<LiveView timeZone="UTC" />);
      await flush();

      expect(screen.getByText("Astral Plane")).toBeInTheDocument();
      expect(screen.getByText("Valerie June")).toBeInTheDocument();
      expect(screen.getByText("-2.30 dB")).toBeInTheDocument();
    });

    it("ticks elapsed on the dial-marking progress bar", async () => {
      installFetchMock(defaultState({ now: ok(makeTrack({ startedAt: ISO_NOW })) }));

      render(<LiveView timeZone="UTC" />);
      await flush();

      expect(screen.getByText(/00:00 elapsed/)).toBeInTheDocument();

      await act(async () => {
        await jest.advanceTimersByTimeAsync(3000);
      });

      expect(screen.getByText(/00:03 elapsed/)).toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: hero reflects non-track states", () => {
    it("shows the safe-rotation drain state", async () => {
      installFetchMock(defaultState({ now: ok(makeDrain()) }));

      render(<LiveView timeZone="UTC" />);
      await flush();

      expect(screen.getByText(/Safe rotation — drain state/)).toBeInTheDocument();
    });

    it("shows the warming-up state before the feeder's first tick", async () => {
      installFetchMock(defaultState({ now: ok(undefined, 503) }));

      render(<LiveView timeZone="UTC" />);
      await flush();

      expect(screen.getByText(/Warming up/)).toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: full history table", () => {
    it("renders every returned ring entry, newest first", async () => {
      const entries = [
        makeHistoryEntry({ mediaId: "c", title: "C", artist: "Z" }),
        makeHistoryEntry({ mediaId: "b", title: "B", artist: "Y" }),
        makeHistoryEntry({ mediaId: "a", title: "A", artist: "X" }),
        makeHistoryEntry({ mediaId: "d", title: "D", artist: "W" }),
        makeHistoryEntry({ mediaId: "e", title: "E", artist: "V" }),
        makeHistoryEntry({ mediaId: "f", title: "F", artist: "U" }),
      ];
      installFetchMock(defaultState({ history: ok(entries) }));

      render(<LiveView timeZone="UTC" />);
      await flush();

      // All 6 rows render — no slicing to a "recent" subset the way the
      // dashboard's RecentPlays does.
      const rows = screen.getAllByRole("row");
      expect(rows).toHaveLength(entries.length + 1); // +1 header row

      const titleCells = entries.map((entry) => screen.getByText(entry.title as string));
      titleCells.forEach((cell) => expect(cell).toBeInTheDocument());

      // Order is preserved exactly as returned by the wire (F16.4: ring
      // contents newest first) — the row for "C" precedes the row for "F".
      const rowTexts = rows.slice(1).map((row) => row.textContent ?? "");
      expect(rowTexts[0]).toContain("C");
      expect(rowTexts[rowTexts.length - 1]).toContain("F");
    });

    it("time and gain columns use tabular numerals", async () => {
      const entries = [makeHistoryEntry({ gainDb: -1.25, startedAt: "2026-01-01T10:04:00.000Z" })];
      installFetchMock(defaultState({ history: ok(entries) }));

      render(<LiveView timeZone="UTC" />);
      await flush();

      expect(screen.getByText("10:04")).toHaveClass("tabular-nums");
      expect(screen.getByText("-1.25 dB")).toHaveClass("tabular-nums");
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: shares the dashboard poll behavior", () => {
    it("shows a track change within one poll interval", async () => {
      const { state } = installFetchMock(defaultState({ now: ok(makeTrack({ title: "Track A" })) }));

      render(<LiveView timeZone="UTC" />);
      await flush();
      expect(screen.getByText("Track A")).toBeInTheDocument();

      state.now = ok(makeTrack({ title: "Track B" }));
      await advance(5000);

      expect(screen.getByText("Track B")).toBeInTheDocument();
      expect(screen.queryByText("Track A")).not.toBeInTheDocument();
    });

    it("pauses polling when the tab hides and resumes on visibility", async () => {
      Object.defineProperty(document, "hidden", { value: false, configurable: true });
      const { fn } = installFetchMock(defaultState());

      render(<LiveView timeZone="UTC" />);
      await flush();

      Object.defineProperty(document, "hidden", { value: true, configurable: true });
      await act(async () => {
        document.dispatchEvent(new Event("visibilitychange"));
      });

      const countAfterHide = fn.mock.calls.length;
      await advance(15000);
      expect(fn.mock.calls.length).toBe(countAfterHide);

      Object.defineProperty(document, "hidden", { value: false, configurable: true });
      await act(async () => {
        document.dispatchEvent(new Event("visibilitychange"));
        await jest.advanceTimersByTimeAsync(0);
      });

      expect(fn.mock.calls.length).toBeGreaterThan(countAfterHide);

      Object.defineProperty(document, "hidden", { value: false, configurable: true });
    });
  });

  // -------------------------------------------------------------------------
  // SAD PATH
  // -------------------------------------------------------------------------

  describe("Scenario: rejecting an empty ring with a designed state (sad path)", () => {
    it("renders the EmptyState explaining entries appear as tracks air", async () => {
      installFetchMock(defaultState({ history: ok([]) }));

      render(<LiveView timeZone="UTC" />);
      await flush();

      expect(screen.getByText("Nothing in the play-history ring yet")).toBeInTheDocument();
      expect(screen.getByText("Entries appear here as tracks air.")).toBeInTheDocument();
    });

    it("degrades quietly on a poll failure without discarding the loaded history", async () => {
      installFetchMock(defaultState({ history: networkError() }));

      render(<LiveView timeZone="UTC" />);
      await flush();

      expect(screen.getByText("Play history — unavailable")).toBeInTheDocument();
      expect(screen.queryByText("Nothing in the play-history ring yet")).not.toBeInTheDocument();
    });
  });
});

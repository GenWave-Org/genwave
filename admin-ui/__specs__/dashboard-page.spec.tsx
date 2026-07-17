// @jest-environment jsdom
// STORY-087 — Dashboard: now-playing, health tiles, recent plays, live polling (Epic Q / SPEC F28.7–F28.8)
//
// Runner: Jest (jsdom) + @testing-library/react. Drives DashboardView (the
// client component page.tsx renders) with a mocked global.fetch dispatched
// by URL substring across the three polled endpoints (now-playing, status,
// play-history) — mirrors live-page.spec.tsx's makeFetchMock style, extended
// to a mutable per-endpoint `state` object so a single test can flip one
// endpoint's response mid-flight (track changes, error → recovery) without
// juggling mockImplementationOnce call ordering across three independent
// pollers. Fake timers throughout; jest.advanceTimersByTimeAsync flushes
// both the timer queue and the promise microtasks the fetch mocks create,
// which plain advanceTimersByTime does not reliably do for multi-await
// fetch chains (fetch() then response.json()).
//
// Wire contract note: the play-history entries this suite exercises have no
// "source" field (SPEC F16.2 / PlayHistoryEntry has none) — the original
// it.todo text mentioned "source" but the shipped wire shape and the Q5
// dispatch's column list (time, title, artist, gain) both omit it, so the
// recent-plays specs assert exactly those four columns.

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, within, act } from "@testing-library/react";
import "@testing-library/jest-dom";
import { DashboardView } from "../app/(authed)/dashboard/DashboardView";
import { toast } from "@/components/ui/toast";

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

interface StatusLlmOverrides {
  enabled: boolean;
  model: string | null;
  activePersona: string | null;
  lastOutcome: "ok" | "failed" | null;
  lastAttemptAt: string | null;
}

interface StatusOverrides {
  startedAt?: string;
  catalog?: { ready: number; enriching: number; failed: number; unavailable: number };
  safeScope?: { libraryIds: number[]; playable: number };
  llm?: StatusLlmOverrides;
}

/** Default llm is the disabled/never-attempted state (STORY-125) — no story in this file exercises it. */
function makeStatus(overrides: StatusOverrides = {}) {
  return {
    startedAt: "2026-01-01T08:00:00.000Z",
    catalog: { ready: 120, enriching: 3, failed: 1, unavailable: 2 },
    safeScope: { libraryIds: [1, 7], playable: 45 },
    llm: { enabled: false, model: null, activePersona: null, lastOutcome: null, lastAttemptAt: null },
    ...overrides,
  };
}

type MockResult =
  | { kind: "ok"; status?: number; body: unknown }
  | { kind: "network-error" }
  | { kind: "pending" };

function ok(body: unknown, status = 200): MockResult {
  return { kind: "ok", status, body };
}

function networkError(): MockResult {
  return { kind: "network-error" };
}

function pending(): MockResult {
  return { kind: "pending" };
}

interface FetchState {
  now: MockResult;
  status: MockResult;
  history: MockResult;
}

function defaultState(overrides: Partial<FetchState> = {}): FetchState {
  return {
    now: ok(makeTrack()),
    status: ok(makeStatus()),
    history: ok([]),
    ...overrides,
  };
}

function endpointKeyFor(url: string): keyof FetchState {
  if (url.includes("now-playing")) return "now";
  if (url.includes("play-history")) return "history";
  return "status";
}

function installFetchMock(initial: FetchState) {
  const state: FetchState = { ...initial };
  const fn = jest.fn<typeof fetch>().mockImplementation((input) => {
    const result = state[endpointKeyFor(String(input))];
    if (result.kind === "network-error") {
      return Promise.reject(new Error("network error"));
    }
    if (result.kind === "pending") {
      return new Promise<Response>(() => {});
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
// Feature: Dashboard
// ---------------------------------------------------------------------------

describe("Feature: Dashboard", () => {
  // -------------------------------------------------------------------------
  describe("Scenario: now-playing card renders a track", () => {
    it("shows title, artist and gain", async () => {
      installFetchMock(
        defaultState({ now: ok(makeTrack({ title: "Astral Plane", artist: "Valerie June", gainDb: -2.3 })) })
      );

      render(<DashboardView timeZone="UTC" />);
      await flush();

      expect(screen.getByText("Astral Plane")).toBeInTheDocument();
      expect(screen.getByText("Valerie June")).toBeInTheDocument();
      expect(screen.getByText("-2.30 dB")).toBeInTheDocument();
    });

    it("ticks the elapsed indicator client-side between polls", async () => {
      installFetchMock(defaultState({ now: ok(makeTrack({ startedAt: ISO_NOW })) }));

      render(<DashboardView timeZone="UTC" />);
      await flush();

      expect(screen.getByText(/00:00 elapsed/)).toBeInTheDocument();

      await act(async () => {
        await jest.advanceTimersByTimeAsync(3000);
      });

      expect(screen.getByText(/00:03 elapsed/)).toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: now-playing card renders non-track states", () => {
    it("shows an explicit drain state for { drain: true }", async () => {
      installFetchMock(defaultState({ now: ok(makeDrain()) }));

      render(<DashboardView timeZone="UTC" />);
      await flush();

      expect(screen.getByText(/Safe rotation — drain state/)).toBeInTheDocument();
    });

    it("shows a warming-up state for 503", async () => {
      installFetchMock(defaultState({ now: ok(undefined, 503) }));

      render(<DashboardView timeZone="UTC" />);
      await flush();

      expect(screen.getByText(/Warming up/)).toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: tiles come from /api/status", () => {
    it("shows catalog ready and enriching counts", async () => {
      installFetchMock(
        defaultState({
          status: ok(makeStatus({ catalog: { ready: 120, enriching: 3, failed: 1, unavailable: 2 } })),
        })
      );

      render(<DashboardView timeZone="UTC" />);
      await flush();

      expect(screen.getByText("120")).toBeInTheDocument();
      expect(screen.getByText(/3 enriching/)).toBeInTheDocument();
    });

    it("shows safeScope playable with the library ids", async () => {
      installFetchMock(
        defaultState({ status: ok(makeStatus({ safeScope: { libraryIds: [1, 7], playable: 45 } })) })
      );

      render(<DashboardView timeZone="UTC" />);
      await flush();

      expect(screen.getByText("45")).toBeInTheDocument();
      // SPEC F40.2 (gitea-#214) — labeled shape, not the bare "Libraries: 1, 7" that read as a count.
      expect(screen.getByText("2 libraries (ids 1, 7)")).toBeInTheDocument();
    });

    it("shows api-up-since from startedAt", async () => {
      installFetchMock(defaultState({ status: ok(makeStatus({ startedAt: "2026-01-01T08:05:00.000Z" })) }));

      render(<DashboardView timeZone="UTC" />);
      await flush();

      expect(screen.getByText(/Up since 08:05 · Jan 1/)).toBeInTheDocument();
    });

    it("shows skeletons while the status fetch is in flight", async () => {
      installFetchMock(defaultState({ status: pending() }));

      render(<DashboardView timeZone="UTC" />);
      await flush();

      const statusRegion = screen.getByRole("region", { name: "Station status" });
      expect(within(statusRegion).getAllByRole("status").length).toBeGreaterThan(0);
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: recent plays list", () => {
    it("renders the most recent history entries with time, title, artist and gain", async () => {
      const entries = [
        makeHistoryEntry({
          title: "Deep Blue",
          artist: "Arca",
          gainDb: -1.25,
          startedAt: "2026-01-01T10:04:00.000Z",
        }),
      ];
      installFetchMock(defaultState({ history: ok(entries) }));

      render(<DashboardView timeZone="UTC" />);
      await flush();

      expect(screen.getByText("10:04")).toBeInTheDocument();
      expect(screen.getByText("Deep Blue")).toBeInTheDocument();
      expect(screen.getByText("Arca")).toBeInTheDocument();
      expect(screen.getByText("-1.25 dB")).toBeInTheDocument();
    });

    it("uses tabular numerals for the numeric columns", async () => {
      const entries = [makeHistoryEntry({ gainDb: -1.25, startedAt: "2026-01-01T10:04:00.000Z" })];
      installFetchMock(defaultState({ history: ok(entries) }));

      render(<DashboardView timeZone="UTC" />);
      await flush();

      expect(screen.getByText("10:04")).toHaveClass("tabular-nums");
      expect(screen.getByText("-1.25 dB")).toHaveClass("tabular-nums");
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: poll updates in place and pauses when hidden", () => {
    beforeEach(() => {
      Object.defineProperty(document, "hidden", { value: false, configurable: true });
    });

    afterEach(() => {
      Object.defineProperty(document, "hidden", { value: false, configurable: true });
    });

    it("updates the card within one 5s interval when the track changes", async () => {
      const { state } = installFetchMock(defaultState({ now: ok(makeTrack({ title: "Track A" })) }));

      render(<DashboardView timeZone="UTC" />);
      await flush();
      expect(screen.getByText("Track A")).toBeInTheDocument();

      state.now = ok(makeTrack({ title: "Track B" }));
      await advance(5000);

      expect(screen.getByText("Track B")).toBeInTheDocument();
      expect(screen.queryByText("Track A")).not.toBeInTheDocument();
    });

    it("stops polling when the document becomes hidden", async () => {
      const { fn } = installFetchMock(defaultState());

      render(<DashboardView timeZone="UTC" />);
      await flush();

      Object.defineProperty(document, "hidden", { value: true, configurable: true });
      await act(async () => {
        document.dispatchEvent(new Event("visibilitychange"));
      });

      const countAfterHide = fn.mock.calls.length;
      await advance(15000);

      expect(fn.mock.calls.length).toBe(countAfterHide);
    });

    it("resumes polling on visibility", async () => {
      const { fn } = installFetchMock(defaultState());

      render(<DashboardView timeZone="UTC" />);
      await flush();

      Object.defineProperty(document, "hidden", { value: true, configurable: true });
      await act(async () => {
        document.dispatchEvent(new Event("visibilitychange"));
      });
      const countAfterHide = fn.mock.calls.length;

      Object.defineProperty(document, "hidden", { value: false, configurable: true });
      await act(async () => {
        document.dispatchEvent(new Event("visibilitychange"));
        await jest.advanceTimersByTimeAsync(0);
      });

      expect(fn.mock.calls.length).toBeGreaterThan(countAfterHide);
    });
  });

  // -------------------------------------------------------------------------
  // SAD PATH
  // -------------------------------------------------------------------------

  describe("Scenario: rejecting fetch failures with a quiet degrade (sad path)", () => {
    it("shows a quiet unavailable state on the affected card when a poll errors", async () => {
      installFetchMock(defaultState({ now: networkError() }));

      render(<DashboardView timeZone="UTC" />);
      await flush();

      expect(screen.getByText("Now playing — unavailable")).toBeInTheDocument();
    });

    it("recovers the card on the next successful poll", async () => {
      const { state } = installFetchMock(defaultState({ now: networkError() }));

      render(<DashboardView timeZone="UTC" />);
      await flush();
      expect(screen.getByText("Now playing — unavailable")).toBeInTheDocument();

      state.now = ok(makeTrack({ title: "Astral Plane" }));
      await advance(5000);

      expect(screen.queryByText("Now playing — unavailable")).not.toBeInTheDocument();
      expect(screen.getByText("Astral Plane")).toBeInTheDocument();
    });

    it("does not toast repeat poll failures", async () => {
      const errorSpy = jest.spyOn(toast, "error").mockImplementation(() => {});
      const successSpy = jest.spyOn(toast, "success").mockImplementation(() => {});
      installFetchMock({ now: networkError(), status: networkError(), history: networkError() });

      render(<DashboardView timeZone="UTC" />);
      await flush();
      await advance(5000);
      await advance(5000);
      await advance(5000);

      expect(errorSpy).not.toHaveBeenCalled();
      expect(successSpy).not.toHaveBeenCalled();
    });
  });
});

// @jest-environment jsdom
// STORY-114 — Voting from the Live page (Epic S / SPEC F33.9, F33.11)
//
// Runner: Jest (jsdom) + @testing-library/react. Drives LiveView (the client component
// live/page.tsx renders) with a mocked global.fetch dispatched by URL substring across five
// endpoints — now-playing, play-history, ratings, vote, never-play — extending the
// live-on-air-view.spec.tsx / dashboard-page.spec.tsx installFetchMock style to the extra three.
// Fake timers throughout (jest.advanceTimersByTimeAsync), matching the house pattern for
// usePoll-driven components. Rating writes are ETag-free by design: do NOT wire them through the
// R9 useRowPatch hook (docs/PLAN.md Epic S sequencing notes) — RatingControls calls
// lib/broadcast-api.ts's voteTrack/setNeverPlay directly.
//
// Fixture note: `now` defaults to a `tts:*` mediaId (a patter announcement on air) rather than a
// numeric one, so the now-playing card renders no rating controls by default — this keeps the
// history-table scenarios' button/chip counts exact without every test having to account for the
// card's own controls. Scenario "the now-playing card is votable" overrides `now` with a numeric
// track explicitly.

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, fireEvent, within, act } from "@testing-library/react";
import "@testing-library/jest-dom";
import { LiveView } from "../app/(authed)/live/LiveView";
import { Toaster } from "@/components/ui/toast";

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
    mediaId: "tts:announcer-1",
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
    mediaId: "202",
    title: "Deep Blue",
    artist: "Arca",
    gainDb: -1.25,
    startedAt: "2026-01-01T10:04:00.000Z",
    endedAt: "2026-01-01T10:08:00.000Z",
    ...overrides,
  };
}

interface RatingOverrides {
  mediaId?: string;
  score?: number;
  neverPlay?: boolean;
}

function makeRating(overrides: RatingOverrides = {}) {
  return {
    mediaId: "202",
    score: 50,
    neverPlay: false,
    ...overrides,
  };
}

type MockResult = { kind: "ok"; status?: number; body: unknown } | { kind: "network-error" };

function ok(body: unknown, status = 200): MockResult {
  return { kind: "ok", status, body };
}

function networkError(): MockResult {
  return { kind: "network-error" };
}

interface FetchState {
  now: MockResult;
  history: MockResult;
  ratings: MockResult;
  vote: MockResult;
  neverPlay: MockResult;
  /** PLAN T71 (SPEC F84.6) — the taste-thumb resolution poll and persona directory fetch LiveView
   * now also fires; harmless empty defaults since none of this file's scenarios exercise persona
   * taste (see persona-taste-thumbs.spec.tsx for that coverage). */
  boothLog: MockResult;
  personas: MockResult;
}

function defaultState(overrides: Partial<FetchState> = {}): FetchState {
  return {
    now: ok(makeTrack()),
    history: ok([]),
    ratings: ok([]),
    vote: ok({ score: 51 }),
    neverPlay: ok({ neverPlay: true }),
    boothLog: ok({ entries: [], nextBefore: null }),
    personas: ok([]),
    ...overrides,
  };
}

interface RecordedCall {
  url: string;
  method: string;
  body: unknown;
}

function endpointKeyFor(url: string): keyof FetchState {
  if (url.includes("/api/ratings")) return "ratings";
  if (url.includes("/vote")) return "vote";
  if (url.includes("/never-play")) return "neverPlay";
  if (url.includes("/play-history")) return "history";
  if (url.includes("/api/booth-log")) return "boothLog";
  if (url.includes("/api/personas")) return "personas";
  return "now";
}

function installFetchMock(initial: FetchState) {
  const state: FetchState = { ...initial };
  const calls: RecordedCall[] = [];
  const fn = jest.fn<typeof fetch>().mockImplementation((input, init) => {
    const url = String(input);
    const method = (init?.method ?? "GET").toUpperCase();
    const body = typeof init?.body === "string" ? (JSON.parse(init.body) as unknown) : undefined;
    calls.push({ url, method, body });

    const result = state[endpointKeyFor(url)];
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
  return { fn, state, calls };
}

/** Renders LiveView with a real Toaster mounted alongside it, so failure toasts (F31.3) are
 * queryable the same way shared-patch-hook.spec.tsx asserts them. */
function renderLive(timeZone = "UTC"): ReturnType<typeof render> {
  return render(
    <>
      <LiveView timeZone={timeZone} />
      <Toaster />
    </>
  );
}

/** Flushes the initial-mount polls (or any already-scheduled microtasks) without advancing time. */
async function flush(): Promise<void> {
  await act(async () => {
    await jest.advanceTimersByTimeAsync(0);
  });
}

/** Clicks an element and flushes the resulting fetch/json/state-update microtask chain. */
async function clickAndSettle(el: HTMLElement): Promise<void> {
  await act(async () => {
    fireEvent.click(el);
    await jest.advanceTimersByTimeAsync(0);
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
// Feature: Voting from the Live page
// ---------------------------------------------------------------------------

describe("Feature: Voting from the Live page", () => {
  // -------------------------------------------------------------------------
  describe("Scenario: the play-history ring renders as a votable table", () => {
    it("renders history entries as table rows (the gitea-#188 UX note)", async () => {
      const entries = [makeHistoryEntry({ mediaId: "202" }), makeHistoryEntry({ mediaId: "303", title: "Other" })];
      installFetchMock(defaultState({ history: ok(entries) }));

      renderLive();
      await flush();

      const rows = screen.getAllByRole("row");
      expect(rows).toHaveLength(entries.length + 1); // +1 header row
    });

    it("shows a score chip on every catalog-id row", async () => {
      const entries = [makeHistoryEntry({ mediaId: "202" }), makeHistoryEntry({ mediaId: "303", title: "Other" })];
      installFetchMock(
        defaultState({
          history: ok(entries),
          ratings: ok([makeRating({ mediaId: "202", score: 61 }), makeRating({ mediaId: "303", score: 40 })]),
        })
      );

      renderLive();
      await flush();

      expect(screen.getByLabelText("Score 61")).toBeInTheDocument();
      expect(screen.getByLabelText("Score 40")).toBeInTheDocument();
    });

    it("shows vote-up, vote-down, and never-play controls on every catalog-id row", async () => {
      const entries = [makeHistoryEntry({ mediaId: "202" }), makeHistoryEntry({ mediaId: "303", title: "Other" })];
      installFetchMock(defaultState({ history: ok(entries) }));

      renderLive();
      await flush();

      expect(screen.getAllByRole("button", { name: "Vote up" })).toHaveLength(2);
      expect(screen.getAllByRole("button", { name: "Vote down" })).toHaveLength(2);
      expect(screen.getAllByRole("button", { name: "Never play" })).toHaveLength(2);
    });

    it("composes scores from GET /api/ratings?ids=… on the poll cadence (F33.9)", async () => {
      const entries = [makeHistoryEntry({ mediaId: "202" })];
      const { calls } = installFetchMock(defaultState({ history: ok(entries) }));

      renderLive();
      await flush();

      const ratingsCall = calls.find((call) => call.url.includes("/api/ratings"));
      expect(ratingsCall?.url).toContain("ids=202");
    });

    it("leaves /api/now-playing and /api/play-history requests unchanged (F16.6)", async () => {
      const entries = [makeHistoryEntry({ mediaId: "202" })];
      const { calls } = installFetchMock(defaultState({ history: ok(entries) }));

      renderLive();
      await flush();

      const nowCalls = calls.filter((call) => call.url.includes("/api/now-playing"));
      const historyCalls = calls.filter((call) => call.url.includes("/api/play-history"));
      expect(nowCalls).toEqual([{ url: "/api/now-playing", method: "GET", body: undefined }]);
      expect(historyCalls).toEqual([{ url: "/api/play-history", method: "GET", body: undefined }]);
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: voting updates in place", () => {
    it("clicking vote up updates the chip from the response body's score, no reload", async () => {
      const entries = [makeHistoryEntry({ mediaId: "202" })];
      installFetchMock(
        defaultState({
          history: ok(entries),
          ratings: ok([makeRating({ mediaId: "202", score: 50 })]),
          vote: ok({ score: 63 }),
        })
      );

      renderLive();
      await flush();
      expect(screen.getByLabelText("Score 50")).toBeInTheDocument();

      await clickAndSettle(screen.getByRole("button", { name: "Vote up" }));

      expect(screen.getByLabelText("Score 63")).toBeInTheDocument();
    });

    it("clicking vote down updates the chip from the response body's score", async () => {
      const entries = [makeHistoryEntry({ mediaId: "202" })];
      installFetchMock(
        defaultState({
          history: ok(entries),
          ratings: ok([makeRating({ mediaId: "202", score: 50 })]),
          vote: ok({ score: 40 }),
        })
      );

      renderLive();
      await flush();

      await clickAndSettle(screen.getByRole("button", { name: "Vote down" }));

      expect(screen.getByLabelText("Score 40")).toBeInTheDocument();
    });

    it("does not refetch the row after a vote (the response IS the fresh state)", async () => {
      const entries = [makeHistoryEntry({ mediaId: "202" })];
      const { calls } = installFetchMock(
        defaultState({
          history: ok(entries),
          ratings: ok([makeRating({ mediaId: "202", score: 50 })]),
          vote: ok({ score: 63 }),
        })
      );

      renderLive();
      await flush();
      const ratingsCallsBefore = calls.filter((call) => call.url.includes("/api/ratings")).length;

      await clickAndSettle(screen.getByRole("button", { name: "Vote up" }));

      const ratingsCallsAfter = calls.filter((call) => call.url.includes("/api/ratings")).length;
      expect(ratingsCallsAfter).toBe(ratingsCallsBefore);
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: the never-play toggle reflects state", () => {
    it("clicking X on a playable row PUTs neverPlay true and swaps to the restore icon", async () => {
      const entries = [makeHistoryEntry({ mediaId: "202" })];
      const { calls } = installFetchMock(
        defaultState({
          history: ok(entries),
          ratings: ok([makeRating({ mediaId: "202", neverPlay: false })]),
          neverPlay: ok({ neverPlay: true }),
        })
      );

      renderLive();
      await flush();

      await clickAndSettle(screen.getByRole("button", { name: "Never play" }));

      const putCall = calls.find((call) => call.url.includes("/never-play"));
      expect(putCall).toMatchObject({ method: "PUT", body: { neverPlay: true } });
      expect(screen.getByRole("button", { name: "Restore to rotation" })).toBeInTheDocument();
    });

    it("clicking restore on a flagged row PUTs neverPlay false and swaps back to X", async () => {
      const entries = [makeHistoryEntry({ mediaId: "202" })];
      const { calls } = installFetchMock(
        defaultState({
          history: ok(entries),
          ratings: ok([makeRating({ mediaId: "202", neverPlay: true })]),
          neverPlay: ok({ neverPlay: false }),
        })
      );

      renderLive();
      await flush();

      await clickAndSettle(screen.getByRole("button", { name: "Restore to rotation" }));

      const putCall = calls.find((call) => call.url.includes("/never-play"));
      expect(putCall).toMatchObject({ method: "PUT", body: { neverPlay: false } });
      expect(screen.getByRole("button", { name: "Never play" })).toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: the now-playing card is votable", () => {
    it("shows the score chip and all three controls for a real on-air track", async () => {
      installFetchMock(
        defaultState({
          now: ok(makeTrack({ mediaId: "101" })),
          ratings: ok([makeRating({ mediaId: "101", score: 72 })]),
        })
      );

      renderLive();
      await flush();

      const card = screen.getByRole("region", { name: "Now playing" });
      expect(within(card).getByLabelText("Score 72")).toBeInTheDocument();
      expect(within(card).getByRole("button", { name: "Vote up" })).toBeInTheDocument();
      expect(within(card).getByRole("button", { name: "Vote down" })).toBeInTheDocument();
      expect(within(card).getByRole("button", { name: "Never play" })).toBeInTheDocument();
    });

    it("wires the card's controls to the same vote/never-play endpoints", async () => {
      const { calls } = installFetchMock(
        defaultState({
          now: ok(makeTrack({ mediaId: "101" })),
          ratings: ok([makeRating({ mediaId: "101", score: 50 })]),
          vote: ok({ score: 51 }),
        })
      );

      renderLive();
      await flush();

      const card = screen.getByRole("region", { name: "Now playing" });
      await clickAndSettle(within(card).getByRole("button", { name: "Vote up" }));

      const voteCall = calls.find((call) => call.url === "/api/media/101/vote");
      expect(voteCall).toMatchObject({ method: "POST", body: { direction: "up" } });
      expect(within(card).getByLabelText("Score 51")).toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  // SAD PATH
  // -------------------------------------------------------------------------

  describe("Scenario: unvotable entries render no controls (sad path)", () => {
    it("tts:* history entries render no score chip and no rating controls (F33.11)", async () => {
      const entries = [makeHistoryEntry({ mediaId: "tts:seg-9" })];
      installFetchMock(defaultState({ history: ok(entries) }));

      renderLive();
      await flush();

      expect(screen.queryByRole("button", { name: "Vote up" })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Vote down" })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Never play" })).not.toBeInTheDocument();
    });

    it("the drain-state card renders no rating controls", async () => {
      installFetchMock(defaultState({ now: ok(makeDrain()) }));

      renderLive();
      await flush();

      const card = screen.getByRole("region", { name: "Now playing" });
      expect(within(card).queryByRole("button", { name: "Vote up" })).not.toBeInTheDocument();
    });
  });

  describe("Scenario: failures surface, state stays truthful (sad path)", () => {
    it("a failed vote toasts the outcome and leaves the chip unchanged (F31.3)", async () => {
      const entries = [makeHistoryEntry({ mediaId: "202" })];
      installFetchMock(
        defaultState({
          history: ok(entries),
          ratings: ok([makeRating({ mediaId: "202", score: 50 })]),
          vote: ok(undefined, 401),
        })
      );

      renderLive();
      await flush();

      await clickAndSettle(screen.getByRole("button", { name: "Vote up" }));

      expect(screen.getByText("Your session has expired — sign in again.")).toBeInTheDocument();
      expect(screen.getByLabelText("Score 50")).toBeInTheDocument();
    });

    it("a failed never-play toggle toasts and leaves the icon unchanged", async () => {
      const entries = [makeHistoryEntry({ mediaId: "202" })];
      installFetchMock(
        defaultState({
          history: ok(entries),
          ratings: ok([makeRating({ mediaId: "202", neverPlay: false })]),
          neverPlay: networkError(),
        })
      );

      renderLive();
      await flush();

      await clickAndSettle(screen.getByRole("button", { name: "Never play" }));

      expect(screen.getByText("Network error — check your connection.")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Never play" })).toBeInTheDocument();
    });
  });
});

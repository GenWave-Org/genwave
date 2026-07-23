// @jest-environment jsdom
// STORY-218 — The Live card tells me why this track is playing right now (SPEC F86.4 — T76)
//
// Runner: Jest + jsdom + @testing-library/react. Drives the real Live view wiring (fetch mock ->
// usePoll -> useNowPlayingTasteAttribution -> LiveView -> NowPlayingCard -> PickChips), mirroring
// persona-taste-thumbs.spec.tsx's `installLiveFetchMock` harness (itself extending
// live-on-air-view.spec.tsx's own) and booth-log-pick-chips.spec.tsx's fixture shapes — this
// exercises the same shared booth-log-row data path the taste thumbs already use, not PickChips in
// isolation, so a broken "same row" assumption fails here too, not just a visual regression.

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, within, act } from "@testing-library/react";
import "@testing-library/jest-dom";
import { LiveView } from "../app/(authed)/live/LiveView";
import type { BoothLogPick } from "../lib/booth-log-api";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const ISO_NOW = "2026-01-01T12:00:00.000Z";

function makeTrack() {
  return {
    stationId: "1",
    // Non-numeric on purpose — sidesteps the F33 rating machinery entirely (irrelevant to pick
    // chips, which key off the booth log's own row, never the now-playing mediaId).
    mediaId: "tts:announcer-1",
    title: "Astral Plane",
    artist: "Valerie June",
    gainDb: -2.3,
    startedAt: ISO_NOW,
  };
}

interface BoothLogEntryFixture {
  id?: number;
  personaId?: number | null;
  pick?: BoothLogPick;
}

function makeBoothLogEntry(overrides: BoothLogEntryFixture = {}) {
  return {
    occurredAt: "2026-01-01T10:04:00.000Z",
    kind: "track-started",
    summary: "Started 'Astral Plane' by Valerie June",
    id: 501,
    personaId: 7,
    ...overrides,
  };
}

type MockResult = { kind: "ok"; status?: number; body: unknown } | { kind: "network-error" };

function ok(body: unknown, status = 200): MockResult {
  return { kind: "ok", status, body };
}

interface LiveFetchState {
  now: MockResult;
  history: MockResult;
  boothLog: MockResult;
  personas: MockResult;
}

function defaultLiveState(overrides: Partial<LiveFetchState> = {}): LiveFetchState {
  return {
    now: ok(makeTrack()),
    history: ok([]),
    boothLog: ok({ entries: [makeBoothLogEntry()], nextBefore: null }),
    personas: ok([{ id: 7, name: "Nova" }]),
    ...overrides,
  };
}

function endpointKeyForLive(url: string): keyof LiveFetchState {
  if (url.includes("/api/booth-log")) return "boothLog";
  if (url.includes("/api/personas")) return "personas";
  if (url.includes("/play-history")) return "history";
  return "now";
}

function installLiveFetchMock(initial: LiveFetchState) {
  const state: LiveFetchState = { ...initial };
  const fn = jest.fn<typeof fetch>().mockImplementation((input) => {
    const url = String(input);
    const result = state[endpointKeyForLive(url)];
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

/** Flushes the initial-mount polls (or any already-scheduled microtasks) without advancing time. */
async function flush(): Promise<void> {
  await act(async () => {
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
// Feature: live now-playing why-this-pick chips
// ---------------------------------------------------------------------------

describe("Feature: live now-playing why-this-pick chips", () => {
  describe("Scenario: current airing with a stamped pick", () => {
    // Arrange: the latest track-started row (id 501, the same row PersonaTasteThumbs targets)
    // carries a rule-driven pick.
    const rulePick: BoothLogPick = {
      firedRules: [{ summary: "The Weeknd", weight: 0.6 }],
      isExploration: false,
    };
    const explorationPick: BoothLogPick = { firedRules: [], isExploration: true };

    it("renders the airing's fired-rule chips on the now-playing card", async () => {
      installLiveFetchMock(
        defaultLiveState({ boothLog: ok({ entries: [makeBoothLogEntry({ pick: rulePick })], nextBefore: null }) })
      );

      render(<LiveView timeZone="UTC" />);
      await flush();

      const card = screen.getByRole("region", { name: "Now playing" });
      expect(within(card).getByText("The Weeknd +0.6")).toBeInTheDocument();
    });

    it("renders the exploration badge for a stamped exploration airing", async () => {
      installLiveFetchMock(
        defaultLiveState({
          boothLog: ok({ entries: [makeBoothLogEntry({ pick: explorationPick })], nextBefore: null }),
        })
      );

      render(<LiveView timeZone="UTC" />);
      await flush();

      const card = screen.getByRole("region", { name: "Now playing" });
      expect(within(card).getByText("Exploration pick")).toBeInTheDocument();
    });

    it("sources chips from the same booth-log row the thumbs target", async () => {
      const { fn } = installLiveFetchMock(
        defaultLiveState({
          boothLog: ok({ entries: [makeBoothLogEntry({ id: 501, pick: rulePick })], nextBefore: null }),
        })
      );

      render(<LiveView timeZone="UTC" />);
      await flush();

      // No separate now-playing diagnostics fetch (F86.4) — exactly one GET to the shared
      // booth-log endpoint backs both the taste thumb and the pick chips.
      const boothLogGetCalls = fn.mock.calls.filter(([input]) => String(input) === "/api/booth-log");
      expect(boothLogGetCalls).toHaveLength(1);

      // And it's the SAME resolved row (id 501): its stamped pick renders as chips, and the
      // taste-thumb control attributed to that identical row also renders alongside it — two
      // surfaces reading off one fetched row, not two independently-resolved ones.
      const card = screen.getByRole("region", { name: "Now playing" });
      expect(within(card).getByText("The Weeknd +0.6")).toBeInTheDocument();
      expect(within(card).getByRole("button", { name: "Taste up for Nova" })).toBeInTheDocument();
    });
  });

  describe("Scenario: current airing with a stamped pick that matched no rules (majority shape)", () => {
    it("renders the taste thumb but no chips, no badge, and no stray wrapper node", async () => {
      // Arrange: a present, non-exploration pick with an empty firedRules — the majority
      // production shape (most picks don't happen to match a rule) — `PickChips` renders `null`
      // for this, same as it does for an absent pick, but the SLOT (`{pickChips}`) must be a bare
      // render of that element, not `pickChips && <div>...</div>`, or this element (truthy,
      // regardless of what it renders inside) would leave a stray empty `<div>` in the DOM.
      const noopPick: BoothLogPick = { firedRules: [], isExploration: false };
      installLiveFetchMock(
        defaultLiveState({ boothLog: ok({ entries: [makeBoothLogEntry({ pick: noopPick })], nextBefore: null }) })
      );

      render(<LiveView timeZone="UTC" />);
      await flush();

      const card = screen.getByRole("region", { name: "Now playing" });
      expect(within(card).getByRole("button", { name: "Taste up for Nova" })).toBeInTheDocument();
      expect(within(card).queryByText("Exploration pick")).not.toBeInTheDocument();
      expect(within(card).queryByRole("list", { name: "Fired rules" })).not.toBeInTheDocument();
      // The stray-empty-div regression this pins: nothing sits between the gain line and the
      // dial-marking decoration directly below it — a wrapped-but-null `pickChips` would insert
      // an empty `<div className="mt-2">` right at this exact position.
      const gainLine = within(card).getByText("-2.30 dB");
      expect(gainLine.nextElementSibling).toHaveAttribute("aria-hidden", "true");
    });
  });

  describe("Scenario: current airing without a stamped pick", () => {
    it("renders no chips and no badge", async () => {
      // Arrange: the latest track-started row is persona-stamped (thumbs render) but carries no
      // `pick` field at all — an airing that predates the pick column, or was never scored.
      installLiveFetchMock(defaultLiveState({ boothLog: ok({ entries: [makeBoothLogEntry()], nextBefore: null }) }));

      render(<LiveView timeZone="UTC" />);
      await flush();

      const card = screen.getByRole("region", { name: "Now playing" });
      expect(within(card).queryByText("Exploration pick")).not.toBeInTheDocument();
      expect(within(card).queryByRole("list", { name: "Fired rules" })).not.toBeInTheDocument();
    });

    it("leaves the rest of the card unchanged", async () => {
      installLiveFetchMock(defaultLiveState({ boothLog: ok({ entries: [makeBoothLogEntry()], nextBefore: null }) }));

      render(<LiveView timeZone="UTC" />);
      await flush();

      const card = screen.getByRole("region", { name: "Now playing" });
      expect(within(card).getByText("On air")).toBeInTheDocument();
      expect(within(card).getByText("Astral Plane")).toBeInTheDocument();
      expect(within(card).getByText("Valerie June")).toBeInTheDocument();
      expect(within(card).getByText("-2.30 dB")).toBeInTheDocument();
    });
  });
});

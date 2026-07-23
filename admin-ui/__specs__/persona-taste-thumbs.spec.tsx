// @jest-environment jsdom
// STORY-215 — The persona learns only from me, and can't spiral (UI half — SPEC F84.1, F84.6, F84.7)
//
// Runner: Jest (jsdom) + @testing-library/react. PLAN T71 implements against two existing pages:
// the now-playing surface (`LiveView`, driven exactly like live-rating.spec.tsx's own harness) and
// the booth-log surface (`BoothLogView`, driven like booth-log-page.spec.tsx's own harness) — both
// mirrored here rather than imported, per this directory's established "duplicated rather than
// imported" convention (see e.g. catalog-rating-toolbar.spec.tsx's header comment). The taste thumb
// is a DIFFERENT control from the F33 catalog vote (curation vs character) and must never be
// visually confusable with it (F84.7) — the last scenario below pins that directly against
// `RatingControls`.

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, fireEvent, within, act } from "@testing-library/react";
import "@testing-library/jest-dom";
import { Toaster } from "@/components/ui/toast";
import { LiveView } from "../app/(authed)/live/LiveView";
import { BoothLogView } from "../app/(authed)/booth-log/BoothLogView";
import { PersonaTasteThumbs } from "../app/(authed)/_components/PersonaTasteThumbs";
import { RatingControls } from "../app/(authed)/_components/RatingControls";

// ---------------------------------------------------------------------------
// Shared fixtures
// ---------------------------------------------------------------------------

const ISO_NOW = "2026-01-01T12:00:00.000Z";

interface PersonaFixture {
  id: number;
  name: string;
}

function makePersona(overrides: Partial<PersonaFixture> = {}): PersonaFixture {
  return { id: 7, name: "Nova", ...overrides };
}

interface BoothLogEntryFixture {
  occurredAt?: string;
  kind?: string;
  summary?: string;
  id?: number;
  personaId?: number | null;
  tasteExcluded?: boolean;
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

interface RecordedCall {
  url: string;
  method: string;
  body: unknown;
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

/** Advances fake time and flushes the resulting poll/fetch/json promise chain(s) — mirrors
 * booth-log-page.spec.tsx's own `advance`, needed here for the remount-survival scenario, which
 * has to drive a real head-page poll tick rather than just the initial mount. */
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
// Now-playing surface (LiveView) — extends live-rating.spec.tsx's installFetchMock style with the
// two endpoints T71 adds (booth-log resolution, persona directory) and the taste-thumb POST.
// ---------------------------------------------------------------------------

interface TrackFixture {
  mediaId?: string;
}

function makeTrack(overrides: TrackFixture = {}) {
  return {
    stationId: "1",
    // Non-numeric on purpose — sidesteps the F33 rating machinery entirely (irrelevant to taste
    // thumbs, which key off the booth log's own row, never the now-playing mediaId).
    mediaId: "tts:announcer-1",
    title: "Astral Plane",
    artist: "Valerie June",
    gainDb: -2.3,
    startedAt: ISO_NOW,
    ...overrides,
  };
}

interface LiveFetchState {
  now: MockResult;
  history: MockResult;
  boothLog: MockResult;
  personas: MockResult;
  tasteThumb: MockResult;
}

function defaultLiveState(overrides: Partial<LiveFetchState> = {}): LiveFetchState {
  return {
    now: ok(makeTrack()),
    history: ok([]),
    boothLog: ok({ entries: [makeBoothLogEntry()], nextBefore: null }),
    personas: ok([makePersona()]),
    tasteThumb: ok({ alreadyRecorded: false, weight: 0.2 }),
    ...overrides,
  };
}

function endpointKeyForLive(url: string): keyof LiveFetchState {
  if (url.includes("taste-thumb")) return "tasteThumb";
  if (url.includes("/api/booth-log")) return "boothLog";
  if (url.includes("/api/personas")) return "personas";
  if (url.includes("/play-history")) return "history";
  return "now";
}

function installLiveFetchMock(initial: LiveFetchState) {
  const state: LiveFetchState = { ...initial };
  const calls: RecordedCall[] = [];
  const fn = jest.fn<typeof fetch>().mockImplementation((input, init) => {
    const url = String(input);
    const method = (init?.method ?? "GET").toUpperCase();
    const body = typeof init?.body === "string" ? (JSON.parse(init.body) as unknown) : undefined;
    calls.push({ url, method, body });

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
  return { fn, state, calls };
}

function renderLive(): ReturnType<typeof render> {
  return render(
    <>
      <LiveView timeZone="UTC" />
      <Toaster />
    </>
  );
}

// ---------------------------------------------------------------------------
// Booth-log surface (BoothLogView) — extends booth-log-page.spec.tsx's installFetchMock style
// with the persona directory and the taste-thumb POST (no "Load more" paging needed here).
// ---------------------------------------------------------------------------

interface BoothLogFetchState {
  head: MockResult;
  personas: MockResult;
  tasteThumb: MockResult;
}

function defaultBoothLogState(overrides: Partial<BoothLogFetchState> = {}): BoothLogFetchState {
  return {
    head: ok({ entries: [makeBoothLogEntry()], nextBefore: null }),
    personas: ok([makePersona()]),
    tasteThumb: ok({ alreadyRecorded: false, weight: 0.2 }),
    ...overrides,
  };
}

function endpointKeyForBoothLog(url: string): keyof BoothLogFetchState {
  if (url.includes("taste-thumb")) return "tasteThumb";
  if (url.includes("/api/personas")) return "personas";
  return "head";
}

function installBoothLogFetchMock(initial: BoothLogFetchState) {
  const state: BoothLogFetchState = { ...initial };
  const calls: RecordedCall[] = [];
  const fn = jest.fn<typeof fetch>().mockImplementation((input, init) => {
    const url = String(input);
    const method = (init?.method ?? "GET").toUpperCase();
    const body = typeof init?.body === "string" ? (JSON.parse(init.body) as unknown) : undefined;
    calls.push({ url, method, body });

    const result = state[endpointKeyForBoothLog(url)];
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

function renderBoothLog(): ReturnType<typeof render> {
  return render(
    <>
      <BoothLogView timeZone="UTC" />
      <Toaster />
    </>
  );
}

// ---------------------------------------------------------------------------
// Feature: Persona taste thumbs
// ---------------------------------------------------------------------------

describe("Feature: Persona taste thumbs", () => {
  describe("Scenario: thumbing the now-playing track", () => {
    // Arrange: now-playing with an active persona; thumb endpoints faked at the fetch seam.
    it("shows taste thumbs attributed to the active persona", async () => {
      installLiveFetchMock(defaultLiveState());

      renderLive();
      await flush();

      const card = screen.getByRole("region", { name: "Now playing" });
      expect(within(card).getByText("Nova taste")).toBeInTheDocument();
      expect(within(card).getByRole("button", { name: "Taste up for Nova" })).toBeInTheDocument();
      expect(within(card).getByRole("button", { name: "Taste down for Nova" })).toBeInTheDocument();
    });

    it("posts one thumb per tap to the taste endpoint", async () => {
      const { calls } = installLiveFetchMock(defaultLiveState());

      renderLive();
      await flush();

      const card = screen.getByRole("region", { name: "Now playing" });
      await clickAndSettle(within(card).getByRole("button", { name: "Taste up for Nova" }));

      const thumbCalls = calls.filter((call) => call.url.includes("taste-thumb"));
      expect(thumbCalls).toHaveLength(1);
      expect(thumbCalls[0]).toMatchObject({
        url: "/api/booth-log/501/taste-thumb",
        method: "POST",
        body: { direction: "up" },
      });
    });

    it("reflects the recorded direction after the round trip", async () => {
      installLiveFetchMock(defaultLiveState());

      renderLive();
      await flush();

      const card = screen.getByRole("region", { name: "Now playing" });
      const up = within(card).getByRole("button", { name: "Taste up for Nova" });
      await clickAndSettle(up);

      expect(up).toBeDisabled();
      expect(within(card).getByRole("button", { name: "Taste down for Nova" })).toBeEnabled();
    });
  });

  describe("Scenario: thumbing a booth-log row", () => {
    // Arrange: booth-log rows — one stamped with persona A, one unstamped (F84.6).
    it("offers thumbs on a persona-stamped track row", async () => {
      installBoothLogFetchMock(
        defaultBoothLogState({
          head: ok({ entries: [makeBoothLogEntry({ id: 9, personaId: 7 })], nextBefore: null }),
        })
      );

      renderBoothLog();
      await flush();

      expect(screen.getByRole("button", { name: "Taste up for Nova" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Taste down for Nova" })).toBeInTheDocument();
    });

    it("labels the thumb with the stamped persona, not the now-active one", async () => {
      installBoothLogFetchMock(
        defaultBoothLogState({
          personas: ok([makePersona({ id: 7, name: "Nova" }), makePersona({ id: 9, name: "Comet" })]),
          head: ok({ entries: [makeBoothLogEntry({ id: 3, personaId: 9 })], nextBefore: null }),
        })
      );

      renderBoothLog();
      await flush();

      // Row 3 was stamped with persona 9 (Comet) — the control attributes to Comet even though the
      // directory also knows about persona 7 (Nova), which this view never asks "is active".
      expect(screen.getByRole("button", { name: "Taste up for Comet" })).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Taste up for Nova" })).not.toBeInTheDocument();
    });
  });

  describe("Scenario: settled state survives a poll that shifts row order (remount-survival)", () => {
    it("keeps the tapped row's disabled direction after a later poll prepends a new head row", async () => {
      // Arrange: personas resolved once at mount (usePersonaDirectory never repolls), so both the
      // already-thumbed row's persona and the row about to be prepended must be known up front.
      const { state } = installBoothLogFetchMock(
        defaultBoothLogState({
          personas: ok([makePersona({ id: 7, name: "Nova" }), makePersona({ id: 9, name: "Comet" })]),
          head: ok({ entries: [makeBoothLogEntry({ id: 501, personaId: 7 })], nextBefore: null }),
        })
      );

      renderBoothLog();
      await flush();

      const down = screen.getByRole("button", { name: "Taste down for Nova" });
      await clickAndSettle(down);
      expect(down).toBeDisabled();

      // Act: exactly what happens on air every few minutes — the next head-page poll delivers a
      // NEW row (a different persona, so its own control is unambiguously distinguishable) ahead
      // of the just-thumbed row, shifting it from index 0 to index 1. BoothLogFeed must key rows
      // by `entry.id`, not occurredAt/index, or this remounts row 501's PersonaTasteThumbs and
      // resets its settled state.
      state.head = ok({
        entries: [
          makeBoothLogEntry({
            id: 777,
            personaId: 9,
            occurredAt: "2026-01-01T10:20:00.000Z",
            summary: "Started 'New Track'",
          }),
          makeBoothLogEntry({ id: 501, personaId: 7 }),
        ],
        nextBefore: null,
      });
      await advance(12000);

      expect(screen.getByText("Started 'New Track'")).toBeInTheDocument();
      // The previously-thumbed row's tapped direction is STILL disabled — it did not remount.
      expect(screen.getByRole("button", { name: "Taste down for Nova" })).toBeDisabled();
      expect(screen.getByRole("button", { name: "Taste up for Nova" })).toBeEnabled();
      // The freshly prepended row starts fully live — row 501's settled state never leaked onto it.
      expect(screen.getByRole("button", { name: "Taste up for Comet" })).toBeEnabled();
      expect(screen.getByRole("button", { name: "Taste down for Comet" })).toBeEnabled();
    });
  });

  describe("Scenario: guardrails in the UI (sad path)", () => {
    it("offers no taste thumb on an unstamped row", async () => {
      installBoothLogFetchMock(
        defaultBoothLogState({
          head: ok({
            entries: [makeBoothLogEntry({ id: 11, personaId: null, summary: "Started 'Unstamped Track'" })],
            nextBefore: null,
          }),
        })
      );

      renderBoothLog();
      await flush();

      expect(screen.getByText("Started 'Unstamped Track'")).toBeInTheDocument();
      // No control at all (F84.6) — not a disabled one, matching the house empty-state pattern
      // PlayHistoryTable already uses for a tts:* row's rating cell.
      expect(screen.queryByRole("button", { name: /Taste (up|down) for/ })).not.toBeInTheDocument();
    });

    it("offers no taste thumb on a safe-content row, even a persona-stamped one (gh-#99)", async () => {
      installBoothLogFetchMock(
        defaultBoothLogState({
          head: ok({
            entries: [
              makeBoothLogEntry({
                id: 12,
                personaId: 7,
                tasteExcluded: true,
                summary: "Started 'Please Stand By (Station Default)'",
              }),
            ],
            nextBefore: null,
          }),
        })
      );

      renderBoothLog();
      await flush();

      expect(screen.getByText("Started 'Please Stand By (Station Default)'")).toBeInTheDocument();
      // Safe-loop tracks and station IDs never accrue taste — same no-control-not-disabled
      // posture as the unstamped row above; the endpoint refuses the write independently.
      expect(screen.queryByRole("button", { name: /Taste (up|down) for/ })).not.toBeInTheDocument();
    });

    it("disables the tapped direction after recording (idempotency affordance)", async () => {
      installBoothLogFetchMock(defaultBoothLogState());

      renderBoothLog();
      await flush();

      const down = screen.getByRole("button", { name: "Taste down for Nova" });
      await clickAndSettle(down);

      expect(down).toBeDisabled();
      expect(screen.getByRole("button", { name: "Taste up for Nova" })).toBeEnabled();
    });

    it("renders the taste thumb visually distinct from the catalog vote control", () => {
      render(
        <>
          <RatingControls mediaId="101" value={{ score: 50, neverPlay: false }} onChange={() => undefined} />
          <PersonaTasteThumbs boothLogRowId={1} personaName="Nova" />
        </>
      );

      const voteUp = screen.getByRole("button", { name: "Vote up" });
      const tasteUp = screen.getByRole("button", { name: "Taste up for Nova" });

      // Distinct affordance shape (brass persona-attribution styling vs the plain vote control) —
      // never sharing a class, and the persona-attribution chip has no F33 equivalent at all.
      expect(tasteUp).toHaveClass("border-accent-2");
      expect(voteUp).not.toHaveClass("border-accent-2");
      expect(screen.getByText("Nova taste")).toBeInTheDocument();
    });
  });
});

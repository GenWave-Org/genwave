// @jest-environment jsdom
// STORY-370 — I can thumb from the booth: the station-thumb pair beside persona taste (SPEC
// F150.1, F150.8 · PLAN T369)
//
// BDD specification — jest + @testing-library/react (mock fetch). Real specs replacing the
// pending todos from PLAN T369's own scaffold. Runner/harness mirrors persona-taste-thumbs.spec.tsx
// (duplicated rather than imported — this directory's established convention, see e.g.
// catalog-rating-toolbar.spec.tsx's own header comment) with one addition: a `stationThumb` mock
// endpoint bucket alongside `tasteThumb`, since a track-started row now offers both.

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, fireEvent, within, act } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { Toaster } from "@/components/ui/toast";
import { LiveView } from "../app/(authed)/live/LiveView";
import { BoothLogView } from "../app/(authed)/booth-log/BoothLogView";
import { PersonaTasteThumbs } from "../app/(authed)/_components/PersonaTasteThumbs";
import { RatingControls } from "../app/(authed)/_components/RatingControls";
import { StationThumbs } from "../app/(authed)/_components/StationThumbs";

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

beforeEach(() => {
  jest.useFakeTimers({ now: new Date(ISO_NOW) });
});

afterEach(() => {
  jest.useRealTimers();
  jest.restoreAllMocks();
});

// ---------------------------------------------------------------------------
// Now-playing surface (LiveView)
// ---------------------------------------------------------------------------

interface TrackFixture {
  mediaId?: string;
}

function makeTrack(overrides: TrackFixture = {}) {
  return {
    stationId: "1",
    mediaId: "live:announcer-1",
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
  stationThumb: MockResult;
}

function defaultLiveState(overrides: Partial<LiveFetchState> = {}): LiveFetchState {
  return {
    now: ok(makeTrack()),
    history: ok([]),
    boothLog: ok({ entries: [makeBoothLogEntry()], nextBefore: null }),
    personas: ok([makePersona()]),
    tasteThumb: ok({ alreadyRecorded: false, weight: 0.2 }),
    stationThumb: ok({ result: "recorded" }),
    ...overrides,
  };
}

function endpointKeyForLive(url: string): keyof LiveFetchState {
  if (url.includes("station-thumb")) return "stationThumb";
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
// Booth-log surface (BoothLogView)
// ---------------------------------------------------------------------------

interface BoothLogFetchState {
  head: MockResult;
  personas: MockResult;
  tasteThumb: MockResult;
  stationThumb: MockResult;
}

function defaultBoothLogState(overrides: Partial<BoothLogFetchState> = {}): BoothLogFetchState {
  return {
    head: ok({ entries: [makeBoothLogEntry()], nextBefore: null }),
    personas: ok([makePersona()]),
    tasteThumb: ok({ alreadyRecorded: false, weight: 0.2 }),
    stationThumb: ok({ result: "recorded" }),
    ...overrides,
  };
}

function endpointKeyForBoothLog(url: string): keyof BoothLogFetchState {
  if (url.includes("station-thumb")) return "stationThumb";
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
// Feature: Station-thumb controls
// ---------------------------------------------------------------------------

describe("Feature: Station-thumb controls", () => {
  describe("Scenario: the Live now-playing card shows both thumb pairs", () => {
    it("renders a station-thumb pair with its own glyph and label", async () => {
      installLiveFetchMock(defaultLiveState());

      renderLive();
      await flush();

      const card = screen.getByRole("region", { name: "Now playing" });
      expect(within(card).getByText("Station")).toBeInTheDocument();
      expect(within(card).getByRole("button", { name: "Station thumbs up" })).toBeInTheDocument();
      expect(within(card).getByRole("button", { name: "Station thumbs down" })).toBeInTheDocument();
    });

    it("renders a persona-taste pair alongside it, both pairs present at once", async () => {
      installLiveFetchMock(defaultLiveState());

      renderLive();
      await flush();

      const card = screen.getByRole("region", { name: "Now playing" });
      expect(within(card).getByRole("button", { name: "Taste up for Nova" })).toBeInTheDocument();
      expect(within(card).getByRole("button", { name: "Station thumbs up" })).toBeInTheDocument();
    });
  });

  describe("Scenario: a booth-log track row shows both thumb pairs", () => {
    it("renders a station-thumb pair beside its persona-taste pair", async () => {
      installBoothLogFetchMock(defaultBoothLogState());

      renderBoothLog();
      await flush();

      expect(screen.getByRole("button", { name: "Taste up for Nova" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Taste down for Nova" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Station thumbs up" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Station thumbs down" })).toBeInTheDocument();
    });
  });

  describe("Scenario: the two pairs never blur together", () => {
    it("gives the station-thumb pair's glyphs and labels distinct accessible names from the persona-taste pair's", async () => {
      installBoothLogFetchMock(defaultBoothLogState());

      renderBoothLog();
      await flush();

      const tasteUp = screen.getByRole("button", { name: "Taste up for Nova" });
      const stationUp = screen.getByRole("button", { name: "Station thumbs up" });

      // Distinct accessible names (a screen reader hears the difference) and a distinct label chip
      // ("Nova taste" vs "Station") — never the same text for both pairs.
      expect(tasteUp).not.toBe(stationUp);
      expect(screen.getByText("Nova taste")).toBeInTheDocument();
      expect(screen.getByText("Station")).toBeInTheDocument();
    });

    it("shares no affordance class between the station-thumb pair and the persona-taste pair", async () => {
      installBoothLogFetchMock(defaultBoothLogState());

      renderBoothLog();
      await flush();

      const tasteUp = screen.getByRole("button", { name: "Taste up for Nova" });
      const stationUp = screen.getByRole("button", { name: "Station thumbs up" });

      // The taste pair carries the brass F84.7 distinctness treatment; the station pair
      // deliberately does not reuse it (own glyph, own label, own styling).
      expect(tasteUp).toHaveClass("border-accent-2");
      expect(stationUp).not.toHaveClass("border-accent-2");
    });

    it("renders its own dedicated glyph — distinct from vote-up/down AND taste-thumb-up/down (T369 review HIGH-1)", () => {
      // Mirrors persona-taste-thumbs.spec.tsx's own RatingControls-distinctness fact: all THREE
      // controls that can share a row/card rendered together, isolated from any page harness.
      render(
        <>
          <RatingControls mediaId="101" value={{ score: 50, neverPlay: false }} onChange={() => undefined} />
          <PersonaTasteThumbs boothLogRowId={1} personaName="Nova" />
          <StationThumbs boothLogRowId={1} />
        </>
      );

      const voteUp = screen.getByRole("button", { name: "Vote up" });
      const voteDown = screen.getByRole("button", { name: "Vote down" });
      const tasteUp = screen.getByRole("button", { name: "Taste up for Nova" });
      const tasteDown = screen.getByRole("button", { name: "Taste down for Nova" });
      const stationUp = screen.getByRole("button", { name: "Station thumbs up" });
      const stationDown = screen.getByRole("button", { name: "Station thumbs down" });

      // Accessible names never collide with either sibling's own pair.
      expect(screen.queryAllByRole("button", { name: "Station thumbs up" })).toHaveLength(1);

      // The glyph itself (the icon's own SVG markup) must differ from BOTH siblings, not just the
      // label — reusing `vote-up`/`vote-down` (the HIGH-1 defect) would make the vote pair and the
      // station pair identical; reusing `taste-thumb-up`/`down` would do the same to the pair it
      // sits directly beside.
      expect(stationUp.innerHTML).not.toEqual(voteUp.innerHTML);
      expect(stationDown.innerHTML).not.toEqual(voteDown.innerHTML);
      expect(stationUp.innerHTML).not.toEqual(tasteUp.innerHTML);
      expect(stationDown.innerHTML).not.toEqual(tasteDown.innerHTML);
    });
  });

  describe("Scenario: thumbing a station rotation signal (sad path first, then the happy path)", () => {
    it("shows the ProblemDetails detail on a 400 and marks nothing pressed", async () => {
      const { calls } = installBoothLogFetchMock(
        defaultBoothLogState({
          stationThumb: ok(
            { title: "Not thumbable.", detail: "Booth-log row 501 is a \"patter-aired\" row, not a track airing — station thumbs apply to aired tracks only (F150.8)." },
            400
          ),
        })
      );

      renderBoothLog();
      await flush();

      const stationUp = screen.getByRole("button", { name: "Station thumbs up" });
      await clickAndSettle(stationUp);

      expect(
        screen.getByText(
          'Booth-log row 501 is a "patter-aired" row, not a track airing — station thumbs apply to aired tracks only (F150.8).'
        )
      ).toBeInTheDocument();
      expect(stationUp).not.toBeDisabled();
      expect(stationUp).toHaveAttribute("aria-pressed", "false");

      const stationCalls = calls.filter((call) => call.url.includes("station-thumb"));
      expect(stationCalls).toHaveLength(1);
      expect(stationCalls[0]).toMatchObject({
        url: "/api/booth-log/501/station-thumb",
        method: "POST",
        body: { direction: "up" },
      });
    });

    it("posts to the station-thumb endpoint and marks the direction pressed on 200", async () => {
      const { calls } = installBoothLogFetchMock(defaultBoothLogState());

      renderBoothLog();
      await flush();

      const stationDown = screen.getByRole("button", { name: "Station thumbs down" });
      await clickAndSettle(stationDown);

      const stationCalls = calls.filter((call) => call.url.includes("station-thumb"));
      expect(stationCalls).toHaveLength(1);
      expect(stationCalls[0]).toMatchObject({
        url: "/api/booth-log/501/station-thumb",
        method: "POST",
        body: { direction: "down" },
      });

      expect(stationDown).toBeDisabled();
      expect(stationDown).toHaveAttribute("aria-pressed", "true");
      expect(screen.getByRole("button", { name: "Station thumbs up" })).toBeEnabled();
      // Taste pair's own state is untouched by the station pair's click.
      expect(screen.getByRole("button", { name: "Taste up for Nova" })).toBeEnabled();
      expect(screen.getByRole("button", { name: "Taste down for Nova" })).toBeEnabled();
    });
  });

  describe("Scenario: every StationThumbResponse.Result token settles correctly (T369 review LOW-7)", () => {
    it("flips: tapping the other direction re-enables the first and settles the second", async () => {
      const { calls, state } = installBoothLogFetchMock(defaultBoothLogState());

      renderBoothLog();
      await flush();

      const stationUp = screen.getByRole("button", { name: "Station thumbs up" });
      const stationDown = screen.getByRole("button", { name: "Station thumbs down" });

      await clickAndSettle(stationUp);
      expect(stationUp).toBeDisabled();
      expect(stationDown).toBeEnabled();

      // The row's OTHER direction now flips the station's own current value server-side.
      state.stationThumb = ok({ result: "flipped" });
      await clickAndSettle(stationDown);

      expect(stationDown).toBeDisabled();
      expect(stationDown).toHaveAttribute("aria-pressed", "true");
      expect(stationUp).toBeEnabled();
      expect(stationUp).toHaveAttribute("aria-pressed", "false");

      const stationCalls = calls.filter((call) => call.url.includes("station-thumb"));
      expect(stationCalls).toHaveLength(2);
      expect(stationCalls[1]).toMatchObject({ body: { direction: "down" } });
    });

    it("unchanged: settles the tapped direction and toasts 'Already recorded'", async () => {
      installBoothLogFetchMock(
        defaultBoothLogState({ stationThumb: ok({ result: "unchanged" }) })
      );

      renderBoothLog();
      await flush();

      const stationUp = screen.getByRole("button", { name: "Station thumbs up" });
      await clickAndSettle(stationUp);

      expect(screen.getByText("Already recorded")).toBeInTheDocument();
      expect(stationUp).toBeDisabled();
      expect(stationUp).toHaveAttribute("aria-pressed", "true");
    });

    it("ignored: settles NEITHER button, toasts the station-imaging explanation (T369 review MED-2)", async () => {
      installBoothLogFetchMock(
        defaultBoothLogState({ stationThumb: ok({ result: "ignored" }) })
      );

      renderBoothLog();
      await flush();

      const stationUp = screen.getByRole("button", { name: "Station thumbs up" });
      const stationDown = screen.getByRole("button", { name: "Station thumbs down" });
      await clickAndSettle(stationUp);

      expect(screen.getByText("Ignored — station imaging")).toBeInTheDocument();
      expect(stationUp).toBeEnabled();
      expect(stationUp).toHaveAttribute("aria-pressed", "false");
      expect(stationDown).toBeEnabled();
      expect(stationDown).toHaveAttribute("aria-pressed", "false");
    });

    it("401: toasts the house session-expired copy and marks nothing pressed", async () => {
      installBoothLogFetchMock(
        defaultBoothLogState({ stationThumb: ok(undefined, 401) })
      );

      renderBoothLog();
      await flush();

      const stationUp = screen.getByRole("button", { name: "Station thumbs up" });
      await clickAndSettle(stationUp);

      expect(screen.getByText("Your session has expired — sign in again.")).toBeInTheDocument();
      expect(stationUp).toBeEnabled();
      expect(stationUp).toHaveAttribute("aria-pressed", "false");
    });
  });
});

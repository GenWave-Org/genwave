// @jest-environment jsdom
// STORY-215 — The persona learns only from me, and can't spiral (UI half — SPEC F84.1, F84.6, F84.7)
//
// Runner: Jest (jsdom) + @testing-library/react. PLAN T71 implements against the booth-log surface
// (`BoothLogView`, driven like booth-log-page.spec.tsx's own harness) — mirrored here rather than
// imported, per this directory's established "duplicated rather than imported" convention (see e.g.
// catalog-rating-toolbar.spec.tsx's header comment). The taste thumb is a DIFFERENT control from
// the F33 catalog vote (curation vs character) and must never be visually confusable with it
// (F84.7) — the last scenario below pins that directly against `CatalogToolbar`'s own "Vote up"
// button. This scenario used to compare against the Live page's `RatingControls`; F203.4 retired
// that page, so it's re-pointed at the toolbar, the surviving catalog-vote surface.

jest.mock("next/navigation", () => ({
  ...jest.requireActual<typeof import("next/navigation")>("next/navigation"),
  useRouter: jest.fn(),
}));

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, fireEvent, act } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import type { useRouter } from "next/navigation";
import { Toaster } from "@/components/ui/toast";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import type { LibraryDto } from "@/lib/library";
import { BoothLogView } from "../app/(authed)/booth-log/BoothLogView";
import { PersonaTasteThumbs } from "../app/(authed)/_components/PersonaTasteThumbs";
import type { AdminMediaDto, BulkFilter } from "../app/(authed)/catalog/types";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;

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
  mockedUseRouter.mockReturnValue({ refresh: jest.fn() } as unknown as ReturnType<typeof useRouter>);
});

afterEach(() => {
  jest.useRealTimers();
  jest.restoreAllMocks();
});

/** Minimal `CatalogToolbar` fixtures — mirrors catalog-rating-toolbar.spec.tsx's own `makeRow`/
 * `LIBRARIES`/`EMPTY_FILTER`, trimmed to the one row this scenario needs to get the toolbar's
 * "Vote up" button on screen (selection mode renders unconditionally on a non-empty selection —
 * CatalogTable, not CatalogToolbar itself, is what gates visibility on selection/filter state). */
function makeToolbarRow(): AdminMediaDto {
  return {
    mediaId: "101",
    locator: "/media/101.flac",
    format: "flac",
    state: "ready",
    durationMs: 180000,
    title: "Astral Plane",
    artist: "Valerie June",
    album: "Album",
    genre: "Folk",
    year: 2024,
    bpm: null,
    trackEnergy: null,
    integratedLufs: -14,
    truePeakDbtp: -1,
    measurable: true,
    cueInSec: null,
    cueOutSec: null,
    eligible: true,
    version: "900",
    score: 50,
    neverPlay: false,
  };
}

const EMPTY_TOOLBAR_FILTER: BulkFilter = {
  state: null,
  artist: null,
  genre: null,
  libraryId: null,
  q: null,
  eligible: null,
};

const TOOLBAR_LIBRARIES: LibraryDto[] = [{ id: 1, name: "In Rotation", mediaCount: 50 }];

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
  });

  describe("Scenario: the taste thumb never blurs with the catalog vote (F84.7)", () => {
    it("renders a taste thumbs-up glyph distinct from CatalogToolbar's Vote up glyph", async () => {
      // Dynamic import (not a static top-level import): CatalogToolbar itself calls `useRouter()`,
      // and next/jest's SWC transform does not hoist `jest.mock()` above static imports (mirrors
      // grouped-navigation.spec.tsx's own header comment, and catalog-rating-toolbar.spec.tsx's own
      // `renderCatalogTable` precedent) — a static import here would load the REAL next/navigation
      // before this file's `jest.mock()` call takes effect.
      const { CatalogToolbar } = await import("../app/(authed)/catalog/CatalogToolbar");

      render(
        <ConfirmDialogProvider>
          <CatalogToolbar
            selectedMedia={[makeToolbarRow()]}
            totalMatchingRows={1}
            filter={EMPTY_TOOLBAR_FILTER}
            libraries={TOOLBAR_LIBRARIES}
            busy={false}
            onBusyChange={() => undefined}
            onOutcome={() => undefined}
          />
          <PersonaTasteThumbs boothLogRowId={1} personaName="Nova" />
        </ConfirmDialogProvider>
      );

      const voteUp = screen.getByRole("button", { name: "Vote up" });
      const tasteUp = screen.getByRole("button", { name: "Taste up for Nova" });

      expect(tasteUp.innerHTML).not.toEqual(voteUp.innerHTML);
    });
  });
});

// @jest-environment jsdom
// STORY-248 — Paint the week (SPEC F94.3, PLAN T129 — the "or bust" deliverable)
//
// Drives the REAL ScheduleEditor with @testing-library/react, a fetch mock dispatched by
// METHOD+URL (mirrors roster-page.spec.tsx/personas-page.spec.tsx's own harness), and pointer
// events that drive the PRODUCTION hit-test path — no drag library, matching the shipped
// component. Review finding F2 (T129): a prior version of this file dispatched `pointerEnter`
// directly AT each cell, which is structurally blind to F1's bug (pointer capture on the anchor
// cell retargets every subsequent event, including pointerenter/over, away from every OTHER cell —
// a real drag painted exactly one cell in Chrome). `ScheduleGrid` no longer has a per-cell
// pointerenter handler at all; it hit-tests `document.elementFromPoint` on the GRID's own
// `pointermove`. So `drag()` below dispatches `pointerdown` on the anchor cell, then one
// `pointermove` per cell the drag crosses on the GRID CONTAINER with `clientX`/`clientY` set to
// that cell's `day`/`halfHour` — exercising the exact same code path a real drag does — with
// `document.elementFromPoint` mocked (jsdom has no layout engine, so it doesn't implement the
// method at all) to resolve those coordinates back to a cell (`mockElementFromPoint` below: the
// production code never inspects what these numbers MEAN, it only forwards them verbatim to
// `elementFromPoint`, so mapping them straight onto `cell(day, halfHour)` is a faithful stand-in
// for "the browser says this cell is what's under the pointer at this coordinate," not a shortcut
// around the hit-test itself). Reverting F1 (moving capture/hit-testing back onto the per-cell
// pointerenter) makes the drag specs below fail, since the production code would no longer read
// the mocked `elementFromPoint` calls at all — verified by temporarily reverting it while writing
// these specs.
//
// `drag()` dispatches its events MANUALLY (`new Event(type, { bubbles: true })` +
// `Object.assign(event, init)`) rather than through `@testing-library/dom`'s `fireEvent.pointerX`
// helpers: this installed jsdom has no global `PointerEvent` constructor at all, so
// `fireEvent`'s own construction path falls back to a plain `Event`, whose native constructor
// silently DROPS non-standard init fields (`pointerId`, `clientX`, `clientY`) — verified
// empirically against this repo's exact jsdom version. Assigning them onto the event object
// AFTER construction (a plain own-property set) is what actually makes them readable as
// `e.pointerId`/`e.clientX`/`e.clientY` inside the component's handlers; `act()` wraps the manual
// `dispatchEvent` call so React flushes the resulting state updates before the next line runs,
// same guarantee `fireEvent`'s own internal wrapper gives every other call in this suite.
//
// Cell math: half-hour index × 30 = minutes (`schedule-grid-model.ts`'s own units) — e.g. index 12
// is 06:00. Test 4/5 deliberately use round-hour indices (12, 20) so the panel's rendered time
// range reads as an obviously-correct human time; the drag/paint tests (1-3) only assert the raw
// startMinute/endMinute integers, so any index works there.
//
// Cell GEOMETRY (rendered box height/position, whether a cell's actual rect matches its 14px grid
// row, whether `elementFromPoint` at a cell's own center resolves to that SAME cell) is browser-
// acceptance territory, not jsdom's: jsdom has no layout engine at all, so every rect here is
// zero-sized regardless of CSS — a real min-height bug that makes every cell button render 40px
// tall inside its 14px row (globals.css's base-layer touch-target floor, defeated on cells via
// `min-h-0 h-full` in `ScheduleGrid.CELL_BASE`) is invisible to any spec in this file and was only
// caught and verified against real Chrome. `mockElementFromPoint` below stands in for the hit-test
// RESULT (which cell a coordinate resolves to), not for whether the coordinates themselves are
// geometrically correct — that guarantee has to come from a real-browser check.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { act, render, screen, fireEvent, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { ScheduleEditor } from "../app/(authed)/schedule/ScheduleEditor";
import type { ScheduleEditorProps } from "../app/(authed)/schedule/ScheduleEditor";
import type { RosterPersonaDto, ScheduleWeekDto } from "../app/(authed)/schedule/types";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const REX: RosterPersonaDto = { id: 1, name: "Radio Rex" };
const NOVA: RosterPersonaDto = { id: 2, name: "Nova" };

const EMPTY_WEEK: ScheduleWeekDto = { segments: [] };

// ---------------------------------------------------------------------------
// Fetch mock — this component issues exactly one kind of request ever
// (PUT /api/schedule, on Save); the mock only needs to answer that one route.
// ---------------------------------------------------------------------------

interface PutResponseSpec {
  status: number;
  body?: unknown;
}

function makePutFetchMock(response: PutResponseSpec): jest.MockedFunction<typeof fetch> {
  const fn = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
    const method = init?.method ?? "GET";
    const url = String(input);
    if (method !== "PUT" || url !== "/api/schedule") {
      throw new Error(`Unexpected fetch in this suite: ${method} ${url}`);
    }
    return {
      ok: response.status >= 200 && response.status < 300,
      status: response.status,
      json: jest.fn<() => Promise<unknown>>().mockResolvedValue(response.body ?? {}),
      headers: new Headers(),
    } as unknown as Response;
  });
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

function lastPutBody(mockFetch: jest.MockedFunction<typeof fetch>): ScheduleWeekDto {
  const call = mockFetch.mock.calls.at(-1);
  if (call === undefined) throw new Error("PUT /api/schedule was never called");
  const init = call[1];
  return JSON.parse(String(init?.body)) as ScheduleWeekDto;
}

function renderEditor(overrides: Partial<ScheduleEditorProps> = {}): ReturnType<typeof render> {
  const props: ScheduleEditorProps = {
    initialWeek: EMPTY_WEEK,
    personas: [REX, NOVA],
    ...overrides,
  };
  return render(
    <ConfirmDialogProvider>
      <ScheduleEditor {...props} />
      <Toaster />
    </ConfirmDialogProvider>
  );
}

function cell(day: number, halfHour: number): HTMLElement {
  return screen.getByTestId(`schedule-cell-${day}-${halfHour}`);
}

function grid(): HTMLElement {
  return screen.getByRole("group", { name: "Weekly schedule" });
}

/** Stands in for the browser's real hit-test (see this file's header comment): jsdom has no
 * layout engine and doesn't implement `document.elementFromPoint` at all, so the production code's
 * `ScheduleGrid.cellFromPoint` would otherwise have nothing to call. `clientX`/`clientY` here are
 * simply the `day`/`halfHour` indices a `drag()` call means to hit — the production code only
 * forwards them to `elementFromPoint`, never interprets them, so mapping them straight onto `cell`
 * is a faithful stand-in, not a shortcut around the hit-test itself. */
function mockElementFromPoint(): void {
  document.elementFromPoint = jest.fn((x: number, y: number) => cell(x, y)) as unknown as typeof document.elementFromPoint;
}

/** Dispatches a pointer event with `init` fields actually attached — see this file's header
 * comment for why `fireEvent.pointerX` can't be used here (this jsdom has no `PointerEvent`
 * constructor, so its fallback silently drops non-standard init fields like `pointerId`/
 * `clientX`/`clientY`). `act()` gives the same "flush before returning" guarantee `fireEvent`'s
 * own wrapper gives every other dispatch in this suite. */
function dispatchPointerEvent(target: HTMLElement, type: string, init: Record<string, number>): void {
  const event = new Event(type, { bubbles: true, cancelable: true });
  Object.assign(event, init);
  act(() => {
    target.dispatchEvent(event);
  });
}

/** Drives a real drag through the PRODUCTION path: `pointerdown` on the anchor cell (which is how
 * a real press always targets its exact cell — no hit-test involved there), then one
 * `pointermove` per `path` entry on the GRID CONTAINER (where capture retargets every subsequent
 * event once F1's fix is in place — never on a cell), then `pointerup`. Requires
 * `mockElementFromPoint()` to have been called first. */
function drag(anchor: readonly [day: number, halfHour: number], path: ReadonlyArray<readonly [number, number]>): void {
  const [anchorDay, anchorHalfHour] = anchor;
  dispatchPointerEvent(cell(anchorDay, anchorHalfHour), "pointerdown", { pointerId: 1 });
  for (const [day, halfHour] of path) {
    dispatchPointerEvent(grid(), "pointermove", { pointerId: 1, clientX: day, clientY: halfHour });
  }
  dispatchPointerEvent(grid(), "pointerup", { pointerId: 1 });
}

function selectBrush(name: string): void {
  fireEvent.click(screen.getByRole("button", { name }));
}

async function clickSave(): Promise<void> {
  fireEvent.click(screen.getByRole("button", { name: /Save schedule/ }));
  // "Saving…" reverting to "Save schedule" is the one signal common to every outcome (200, 400,
  // network error) — the button's own `finally` clears `isSaving` regardless of which branch ran.
  await waitFor(() => {
    expect(screen.getByRole("button", { name: "Save schedule" })).toBeInTheDocument();
  });
}

describe("Feature: Paint the week", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
    mockElementFromPoint();
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: painting creates segments", () => {
    it("dragging across cells with a DJ selected produces one segment block on 30-minute boundaries", async () => {
      const mockFetch = makePutFetchMock({ status: 200, body: EMPTY_WEEK });
      renderEditor();

      selectBrush("Radio Rex");
      drag([1, 0], [[1, 1], [1, 2]]);

      await clickSave();

      const body = lastPutBody(mockFetch);
      expect(body.segments).toEqual([
        { id: null, day: 1, startMinute: 0, endMinute: 90, personaId: REX.id, genres: null, energyMin: null, energyMax: null, showId: null },
      ]);
    });

    it("extending a drag grows the same segment, not a second one", async () => {
      const mockFetch = makePutFetchMock({ status: 200, body: EMPTY_WEEK });
      renderEditor();

      selectBrush("Radio Rex");
      // First stroke: a single click paints one cell.
      fireEvent.pointerDown(cell(2, 4), { pointerId: 1 });
      fireEvent.pointerUp(cell(2, 4), { pointerId: 1 });
      // Second, SEPARATE stroke: a drag immediately adjacent to the first.
      drag([2, 5], [[2, 6]]);

      await clickSave();

      const body = lastPutBody(mockFetch);
      expect(body.segments).toHaveLength(1);
      expect(body.segments[0]).toMatchObject({ day: 2, startMinute: 120, endMinute: 210, personaId: REX.id });
    });

    it("a sparse pointermove jump fills the cells it skipped over, not just its endpoint", async () => {
      // Review finding (live app): a real fast flick — or Playwright's coarse `dragTo` — can jump
      // straight from one hit-tested cell to another several rows away with no event in between
      // (browsers coalesce pointermove). Painting only the two hit cells would leave a hole in the
      // middle of the stroke; `ScheduleGrid`'s interpolation must fill it. Only ONE `pointermove`
      // is dispatched here, straight from anchor cell 16 to cell 23 — `mockElementFromPoint`
      // resolves it to cell 23 alone, exactly reproducing the sparse-event gap.
      const mockFetch = makePutFetchMock({ status: 200, body: EMPTY_WEEK });
      renderEditor();

      selectBrush("Radio Rex");
      drag([1, 16], [[1, 23]]);

      await clickSave();

      const body = lastPutBody(mockFetch);
      expect(body.segments).toEqual([
        { id: null, day: 1, startMinute: 480, endMinute: 720, personaId: REX.id, genres: null, energyMin: null, energyMax: null, showId: null },
      ]);
    });

    it("the music-only brush produces a persona-less segment", async () => {
      const mockFetch = makePutFetchMock({ status: 200, body: EMPTY_WEEK });
      renderEditor();

      selectBrush("Music only");
      drag([0, 10], [[0, 11]]);

      await clickSave();

      const body = lastPutBody(mockFetch);
      expect(body.segments).toEqual([
        { id: null, day: 0, startMinute: 300, endMinute: 360, personaId: null, genres: null, energyMin: null, energyMax: null, showId: null },
      ]);
    });
  });

  describe("Scenario: blocks open the envelope panel", () => {
    const REX_BLOCK_WEEK: ScheduleWeekDto = {
      segments: [
        {
          id: 7,
          day: 3,
          startMinute: 360,
          endMinute: 600,
          personaId: REX.id,
          genres: ["rock", "pop"],
          energyMin: 0.2,
          energyMax: 0.8,
          showId: null,
        },
      ],
    };

    it("clicking a block opens the side panel with its genre/energy overrides", () => {
      renderEditor({ initialWeek: REX_BLOCK_WEEK });

      // No brush selected (the editor's default) — a plain click on a painted cell inspects it
      // rather than repainting it.
      fireEvent.pointerDown(cell(3, 15), { pointerId: 1 });
      fireEvent.pointerUp(cell(3, 15), { pointerId: 1 });

      const panel = screen.getByRole("complementary", { name: "Radio Rex block details" });
      expect(panel).toBeInTheDocument();
      expect(screen.getByText(/Wednesday/)).toBeInTheDocument();
      expect(screen.getByText(/06:00–10:00/)).toBeInTheDocument();
      expect(screen.getByLabelText(/Genres/)).toHaveValue("rock, pop");
      expect(screen.getByLabelText("Energy min")).toHaveValue(0.2);
      expect(screen.getByLabelText("Energy max")).toHaveValue(0.8);
    });

    it("blank envelope fields serialize as station-default (nulls)", async () => {
      const mockFetch = makePutFetchMock({ status: 200, body: REX_BLOCK_WEEK });
      renderEditor({ initialWeek: REX_BLOCK_WEEK });

      fireEvent.pointerDown(cell(3, 15), { pointerId: 1 });
      fireEvent.pointerUp(cell(3, 15), { pointerId: 1 });

      fireEvent.change(screen.getByLabelText(/Genres/), { target: { value: "" } });
      fireEvent.change(screen.getByLabelText("Energy min"), { target: { value: "" } });
      fireEvent.change(screen.getByLabelText("Energy max"), { target: { value: "" } });

      await clickSave();

      const body = lastPutBody(mockFetch);
      expect(body.segments).toEqual([
        { id: null, day: 3, startMinute: 360, endMinute: 600, personaId: REX.id, genres: null, energyMin: null, energyMax: null, showId: null },
      ]);
    });
  });

  describe("Scenario: save is the whole week", () => {
    const NOVA_WEEK: ScheduleWeekDto = {
      segments: [
        { id: 3, day: 5, startMinute: 0, endMinute: 60, personaId: NOVA.id, genres: null, energyMin: null, energyMax: null, showId: null },
      ],
    };

    it("save issues one PUT /api/schedule carrying the entire week document", async () => {
      const mockFetch = makePutFetchMock({ status: 200, body: NOVA_WEEK });
      renderEditor({ initialWeek: NOVA_WEEK });

      selectBrush("Radio Rex");
      fireEvent.pointerDown(cell(6, 0), { pointerId: 1 });
      fireEvent.pointerUp(cell(6, 0), { pointerId: 1 });

      await clickSave();

      expect(mockFetch).toHaveBeenCalledTimes(1);
      const [url, init] = mockFetch.mock.calls[0] ?? [];
      expect(url).toBe("/api/schedule");
      expect(init?.method).toBe("PUT");

      const body = lastPutBody(mockFetch);
      expect(body.segments).toHaveLength(2);
      expect(body.segments).toEqual(
        expect.arrayContaining([
          expect.objectContaining({ day: 5, startMinute: 0, endMinute: 60, personaId: NOVA.id }),
          expect.objectContaining({ day: 6, startMinute: 0, endMinute: 30, personaId: REX.id }),
        ])
      );
    });

    it("the grid re-renders from the PUT response", async () => {
      const serverWeek: ScheduleWeekDto = {
        segments: [
          { id: 99, day: 4, startMinute: 60, endMinute: 120, personaId: NOVA.id, genres: null, energyMin: null, energyMax: null, showId: null },
        ],
      };
      makePutFetchMock({ status: 200, body: serverWeek });
      renderEditor();

      selectBrush("Radio Rex");
      fireEvent.pointerDown(cell(2, 0), { pointerId: 1 });
      fireEvent.pointerUp(cell(2, 0), { pointerId: 1 });

      await clickSave();

      // The server's response — not the locally-painted stroke — is what the grid now shows.
      expect(cell(4, 2)).toHaveAttribute("aria-label", expect.stringContaining("Nova"));
      expect(cell(2, 0)).toHaveAttribute("aria-label", expect.stringContaining("empty"));
    });
  });

  describe("Scenario: rejections land on cells", () => {
    const CELL_ERROR_MESSAGE = "Overlaps another segment for this persona.";
    const REJECT_BODY = {
      detail: "1 segment(s) failed validation; nothing was saved.",
      cellErrors: [{ rowIndex: 0, day: 1, startMinute: 0, endMinute: 60, kind: "overlap", message: CELL_ERROR_MESSAGE }],
    };

    function paintRejectedBlock(): void {
      selectBrush("Radio Rex");
      drag([1, 0], [[1, 1]]);
    }

    it("per-cell 400 errors highlight the offending blocks in place with the error text", async () => {
      makePutFetchMock({ status: 400, body: REJECT_BODY });
      renderEditor();

      paintRejectedBlock();
      await clickSave();

      expect(cell(1, 0)).toHaveAttribute("title", CELL_ERROR_MESSAGE);
      expect(cell(1, 1)).toHaveAttribute("title", CELL_ERROR_MESSAGE);
      // gh-#255: the rejection surfaces TWICE by design — the transient toast plus the persistent
      // `role="alert"` banner (a toast alone fades while the painted grid still looks right, which
      // is exactly how a rejected save once read as "saved fine" on the demo box).
      expect(await screen.findAllByText(REJECT_BODY.detail)).not.toHaveLength(0);
      expect(screen.getByRole("alert")).toHaveTextContent(REJECT_BODY.detail);
    });

    it("a rejected save never silently drops the edit", async () => {
      makePutFetchMock({ status: 400, body: REJECT_BODY });
      renderEditor();

      paintRejectedBlock();
      await clickSave();

      // The painted block is still there — the failed save didn't revert it.
      expect(cell(1, 0)).toHaveAttribute("aria-label", expect.stringContaining("Radio Rex"));
      expect(cell(1, 1)).toHaveAttribute("aria-label", expect.stringContaining("Radio Rex"));
      expect(screen.getByText("Unsaved changes")).toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  // gh-#255 — whole-week spans save and round-trip; stale editors can't wipe.
  // -------------------------------------------------------------------------

  describe("Scenario: a block spanning every day of the week saves and round-trips (gh-#255)", () => {
    /** Echo mock faithful to the real 200 path: same segments back, ids assigned, fresh version. */
    function makeEchoFetchMock(version = "v-after"): jest.MockedFunction<typeof fetch> {
      const fn = jest.fn<typeof fetch>().mockImplementation(async (_input, init) => {
        const body = JSON.parse(String(init?.body)) as ScheduleWeekDto;
        const echoed: ScheduleWeekDto = {
          segments: body.segments.map((s, i) => ({ ...s, id: i + 1 })),
          version,
        };
        return {
          ok: true,
          status: 200,
          json: jest.fn<() => Promise<unknown>>().mockResolvedValue(echoed),
          headers: new Headers(),
        } as unknown as Response;
      });
      global.fetch = fn as unknown as typeof fetch;
      return fn;
    }

    function paintBandAcross(days: readonly number[]): void {
      selectBrush("Radio Rex");
      for (const day of days) {
        drag([day, 20], [[day, 23]]);
      }
    }

    it.each([
      ["6 days", [1, 2, 3, 4, 5, 6]],
      ["all 7 days", [0, 1, 2, 3, 4, 5, 6]],
    ])("a 2h band across %s PUTs one segment per day and the grid keeps every day after the 200", async (_label, days) => {
      const mockFetch = makeEchoFetchMock();
      renderEditor();

      paintBandAcross(days);
      await clickSave();

      const body = lastPutBody(mockFetch);
      expect(body.segments).toHaveLength(days.length);
      for (const day of days) {
        expect(body.segments).toContainEqual(
          expect.objectContaining({ day, startMinute: 600, endMinute: 720, personaId: REX.id })
        );
        // Round trip: the grid re-derived from the response still shows every painted day.
        expect(cell(day, 20)).toHaveAttribute("aria-label", expect.stringContaining("Radio Rex"));
        expect(cell(day, 23)).toHaveAttribute("aria-label", expect.stringContaining("Radio Rex"));
      }
      expect(screen.queryByText("Unsaved changes")).not.toBeInTheDocument();
    });

    it("a block wrapping the week boundary (Sat 23:00 → Sun 01:00) saves as two segments and survives", async () => {
      const mockFetch = makeEchoFetchMock();
      renderEditor();

      selectBrush("Radio Rex");
      drag([6, 46], [[6, 47]]);
      drag([0, 0], [[0, 1]]);
      await clickSave();

      const body = lastPutBody(mockFetch);
      expect(body.segments).toEqual([
        expect.objectContaining({ day: 0, startMinute: 0, endMinute: 60, personaId: REX.id }),
        expect.objectContaining({ day: 6, startMinute: 1380, endMinute: 1440, personaId: REX.id }),
      ]);
      expect(cell(6, 47)).toHaveAttribute("aria-label", expect.stringContaining("Radio Rex"));
      expect(cell(0, 0)).toHaveAttribute("aria-label", expect.stringContaining("Radio Rex"));
    });
  });

  describe("Scenario: optimistic concurrency — a stale editor cannot silently wipe (gh-#255)", () => {
    const STALE_PROBLEM = {
      detail:
        "Another tab or session saved a different week after this page loaded. Reload to see the latest schedule before saving — saving now would overwrite it.",
      conflict: "staleWeek",
    };

    it("the PUT carries the version the editor loaded, as baseVersion", async () => {
      const mockFetch = makePutFetchMock({ status: 200, body: { segments: [], version: "v-2" } });
      renderEditor({ initialWeek: { segments: [], version: "v-1" } });

      selectBrush("Radio Rex");
      drag([2, 10], [[2, 11]]);
      await clickSave();

      const body = lastPutBody(mockFetch);
      expect(body.baseVersion).toBe("v-1");
    });

    it("after a 200, the NEXT save carries the response's fresh version", async () => {
      const mockFetch = makePutFetchMock({ status: 200, body: { segments: [], version: "v-2" } });
      renderEditor({ initialWeek: { segments: [], version: "v-1" } });

      selectBrush("Radio Rex");
      drag([2, 10], [[2, 11]]);
      await clickSave();
      drag([3, 10], [[3, 11]]);
      await clickSave();

      const body = lastPutBody(mockFetch);
      expect(body.baseVersion).toBe("v-2");
    });

    it("a 409 staleWeek keeps the paint on screen and shows a persistent alert, not just a toast", async () => {
      makePutFetchMock({ status: 409, body: STALE_PROBLEM });
      renderEditor({ initialWeek: { segments: [], version: "v-1" } });

      selectBrush("Radio Rex");
      drag([4, 8], [[4, 9]]);
      await clickSave();

      // The operator's unsaved paint survives (AC5 posture), still marked dirty…
      expect(cell(4, 8)).toHaveAttribute("aria-label", expect.stringContaining("Radio Rex"));
      expect(screen.getByText("Unsaved changes")).toBeInTheDocument();
      // …and the rejection stays visible in a role="alert" banner (a fading toast alone is how a
      // failed save once read as "saved fine" on the demo box).
      const alerts = screen.getAllByRole("alert").map((el) => el.textContent ?? "");
      expect(alerts.some((text) => text.includes("Reload to see the latest"))).toBe(true);
    });

    it("a 200 whose body isn't the week document surfaces a persistent error instead of failing silently", async () => {
      const fn = jest.fn<typeof fetch>().mockImplementation(async () => {
        return {
          ok: true,
          status: 200,
          json: jest.fn<() => Promise<unknown>>().mockRejectedValue(new SyntaxError("not JSON")),
          headers: new Headers(),
        } as unknown as Response;
      });
      global.fetch = fn as unknown as typeof fetch;
      renderEditor();

      selectBrush("Radio Rex");
      drag([5, 8], [[5, 9]]);
      await clickSave();

      expect(cell(5, 8)).toHaveAttribute("aria-label", expect.stringContaining("Radio Rex"));
      expect(screen.getByText("Unsaved changes")).toBeInTheDocument();
      const alerts = screen.getAllByRole("alert").map((el) => el.textContent ?? "");
      expect(alerts.some((text) => text.includes("NOT saved"))).toBe(true);
    });
  });
});

// @jest-environment jsdom
// STORY-313 — Span-assign & imaging scope (F119.2) — grid picker half (PLAN T245)
//
// Drives the REAL ScheduleEditor with @testing-library/react, mirroring schedule-editor.spec.tsx's
// own harness (a fetch mock dispatched by METHOD+URL, ConfirmDialogProvider + Toaster wrapping).
// `POST /api/schedule/assign-show`'s F119.2 run-span algorithm itself — contiguous same-persona (and
// same-brush music-only) run, stops at interruptions, narrow-to-one — is REAL code proven against a
// REAL Postgres fixture in GenWave.MediaLibrary.Tests/Specs/Story313_ScheduleShowAssignment.cs; this
// suite scopes itself to the WIRE/UI concerns T245 owns: the picker sends exactly the CLICKED block's
// own stored id (never a client-computed run/list — the server is the one source of truth for where a
// run starts/stops), applyToRun's default/narrow values, the shows list rendering, the SAVED-grid-only
// gate (disabled while a paint edit is unsaved), and how a successful response's fresh state (version,
// current show) reaches the editor via the documented follow-up GET.
//
// Review pass (P1/P6): the show roster is no longer fetched by this component tree at all — the
// schedule PAGE loads it once, server-side, and `ScheduleEditor` receives it as a plain `shows` prop
// (`renderEditor`'s own default below). Every scenario that used to mock `GET /api/shows` supplies
// the roster via that prop instead; the sad-path scenarios at the bottom of this file pin the P1-P4
// review findings directly (an unreachable roster, a rejected assignment, a failed follow-up GET).

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { ScheduleEditor } from "../app/(authed)/schedule/ScheduleEditor";
import type { ScheduleEditorProps } from "../app/(authed)/schedule/ScheduleEditor";
import type {
  RosterPersonaDto,
  ScheduleSegmentDto,
  ScheduleShowOptionDto,
  ScheduleShowsStatus,
  ScheduleWeekDto,
} from "../app/(authed)/schedule/types";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const REX: RosterPersonaDto = { id: 1, name: "Radio Rex" };
const NOVA: RosterPersonaDto = { id: 2, name: "Nova" };

const MORNING_DRIVE: ScheduleShowOptionDto = { id: 1, name: "Morning Drive", tagline: "Wake up loud" };
const LATE_NIGHT: ScheduleShowOptionDto = { id: 2, name: "Late Night", tagline: null };
const SHOWS: ScheduleShowOptionDto[] = [MORNING_DRIVE, LATE_NIGHT];

/** `ScheduleEditor`'s own `shows` prop (STORY-313 P6) — the schedule page's resolved roster load.
 * `renderEditor`'s default; individual scenarios override it to exercise the `"error"` branch. */
const LOADED_SHOWS: ScheduleShowsStatus = { kind: "loaded", shows: SHOWS };
const ERROR_SHOWS: ScheduleShowsStatus = { kind: "error" };

// A single Rex run, day 3, 06:00-10:00 (half-hours 12-20), unnamed — the "clicking a block opens the
// side panel" fixture schedule-editor.spec.tsx already uses.
const REX_SEGMENT: ScheduleSegmentDto = {
  id: 7,
  day: 3,
  startMinute: 360,
  endMinute: 600,
  personaId: REX.id,
  genres: null,
  energyMin: null,
  energyMax: null,
  showId: null,
};
const REX_BLOCK_WEEK: ScheduleWeekDto = { segments: [REX_SEGMENT], version: "v-1" };

/** Three adjacent blocks on the same day, back to back with no gap: Rex (id 21), then music-only
 * (id 22), then Nova (id 23) — used by the "runs end at interruptions" scenario to prove the picker
 * addresses only the CLICKED block's own id, never something it merged across a neighbor itself. */
const ADJACENT_BLOCKS_WEEK: ScheduleWeekDto = {
  segments: [
    { id: 21, day: 4, startMinute: 0, endMinute: 60, personaId: REX.id, genres: null, energyMin: null, energyMax: null, showId: null },
    { id: 22, day: 4, startMinute: 60, endMinute: 120, personaId: null, genres: null, energyMin: null, energyMax: null, showId: null },
    { id: 23, day: 4, startMinute: 120, endMinute: 180, personaId: NOVA.id, genres: null, energyMin: null, energyMax: null, showId: null },
  ],
  version: "v-1",
};

/** Two SEPARATE stored rows (ids 31/32) that render as ONE visual run: same day, same persona,
 * back-to-back with no gap — `computeRuns` merges by cell VALUE alone (`schedule-grid-model`'s own
 * doc comment), so opening either half opens ONE panel spanning both, even though `findBlockId` can
 * only ever name the leftmost row's own id (31). Used by the P2 multi-stored-row scenario. */
const TWO_ROW_RUN_WEEK: ScheduleWeekDto = {
  segments: [
    { id: 31, day: 5, startMinute: 0, endMinute: 120, personaId: REX.id, genres: null, energyMin: null, energyMax: null, showId: null },
    { id: 32, day: 5, startMinute: 120, endMinute: 240, personaId: REX.id, genres: null, energyMin: null, energyMax: null, showId: null },
  ],
  version: "v-1",
};

/** T247 wire-smoke finding fixture: two SEPARATE stored rows (ids 41/42), both MUSIC-ONLY
 * (`personaId: null`), back-to-back with no gap, BOTH already carrying the SAME `showId` — exactly
 * the shape a run-wide show assignment across a merged music-only run leaves in the store (Postgres
 * confirmed `show_id` set on both rows in the live repro). Unlike {@link TWO_ROW_RUN_WEEK}, these
 * rows load with the override ALREADY set — the P2 fixture never did, which is why the merged-run
 * scenario below is the one the jest suite was missing before this fix. */
const TWO_ROW_MUSIC_RUN_WEEK: ScheduleWeekDto = {
  segments: [
    { id: 41, day: 0, startMinute: 0, endMinute: 240, personaId: null, genres: null, energyMin: null, energyMax: null, showId: LATE_NIGHT.id },
    { id: 42, day: 0, startMinute: 240, endMinute: 480, personaId: null, genres: null, energyMin: null, energyMax: null, showId: LATE_NIGHT.id },
  ],
  version: "v-1",
};

// ---------------------------------------------------------------------------
// Fetch mock — dispatched by METHOD + URL (mirrors schedule-editor.spec.tsx's own makePutFetchMock,
// generalized to the several routes this suite's flows touch: POST /api/schedule/assign-show, the
// documented follow-up GET /api/schedule, and PUT /api/schedule). A route may also be wired to THROW
// (`{ throws: true }`) — the follow-up-GET sad path needs to prove the network-error catch branch
// shows the identical copy the non-ok branch does (STORY-313 P3).
// ---------------------------------------------------------------------------

interface RouteResponseSpec {
  status: number;
  body?: unknown;
}

type RouteOutcome = RouteResponseSpec | { throws: true };

function jsonResponse(spec: RouteResponseSpec): Response {
  return {
    ok: spec.status >= 200 && spec.status < 300,
    status: spec.status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(spec.body ?? {}),
    headers: new Headers(),
  } as unknown as Response;
}

function routeKey(method: string, url: string): string {
  return `${method} ${url}`;
}

/** Every route this suite needs is a single fixed outcome — nothing here mutates server state
 * between calls, so a plain lookup table (never a queue) is enough; each test wires exactly the
 * routes its own flow reaches. Any OTHER fetch throws loudly rather than silently 404ing — the P1
 * sad-path scenario relies on this to prove a disabled Assign never even reaches the network. */
function makeRouteFetchMock(routes: Record<string, RouteOutcome>): jest.MockedFunction<typeof fetch> {
  const fn = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
    const method = init?.method ?? "GET";
    const url = String(input);
    const outcome = routes[routeKey(method, url)];
    if (outcome === undefined) {
      throw new Error(`Unexpected fetch in this suite: ${method} ${url}`);
    }
    if ("throws" in outcome) {
      throw new Error("Simulated network error");
    }
    return jsonResponse(outcome);
  });
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

/** The most recent call's own JSON body for a given method+URL — throws if that route was never
 * called, the same "fail loud, not silent" posture `lastPutBody` takes in schedule-editor.spec.tsx. */
function lastRequestBody(mockFetch: jest.MockedFunction<typeof fetch>, method: string, url: string): unknown {
  const call = [...mockFetch.mock.calls]
    .reverse()
    .find(([input, init]) => String(input) === url && (init?.method ?? "GET") === method);
  if (call === undefined) throw new Error(`${method} ${url} was never called`);
  const init = call[1];
  return init?.body === undefined ? undefined : JSON.parse(String(init.body));
}

function callCount(mockFetch: jest.MockedFunction<typeof fetch>, method: string, url: string): number {
  return mockFetch.mock.calls.filter(
    ([input, init]) => String(input) === url && (init?.method ?? "GET") === method
  ).length;
}

// ---------------------------------------------------------------------------
// Render + interaction helpers
// ---------------------------------------------------------------------------

function renderEditor(overrides: Partial<ScheduleEditorProps> = {}): ReturnType<typeof render> {
  const props: ScheduleEditorProps = {
    initialWeek: REX_BLOCK_WEEK,
    personas: [REX, NOVA],
    shows: LOADED_SHOWS,
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

/** No brush selected (the editor's default) — a plain click on a painted cell inspects it, opening
 * the side panel, exactly like schedule-editor.spec.tsx's own "clicking a block opens…" scenario. */
function openBlock(day: number, halfHour: number): void {
  fireEvent.pointerDown(cell(day, halfHour), { pointerId: 1 });
  fireEvent.pointerUp(cell(day, halfHour), { pointerId: 1 });
}

function panel(): HTMLElement {
  return screen.getByRole("complementary");
}

/** Waits for the panel's "Assign show" select to become enabled — the roster now arrives as an
 * already-resolved `shows` prop (STORY-313 P6, no more client-side fetch-on-mount), so this is really
 * just waiting out the render after `openBlock`; kept as a `waitFor` regardless, the same defensive
 * posture every other async assertion in this file already takes. */
async function showSelect(): Promise<HTMLElement> {
  return waitFor(() => {
    const el = within(panel()).getByLabelText("Assign show");
    expect(el).not.toBeDisabled();
    return el;
  });
}

async function clickAssign(): Promise<void> {
  fireEvent.click(within(panel()).getByRole("button", { name: /Assign/ }));
  await waitFor(() => {
    expect(within(panel()).getByRole("button", { name: "Assign" })).toBeInTheDocument();
  });
}

describe("Feature: Grid show picker with span-assign", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: the run rule", () => {
    it("assigning from any block applies to the whole contiguous same-persona run by default", async () => {
      const mockFetch = makeRouteFetchMock({
        [routeKey("POST", "/api/schedule/assign-show")]: {
          status: 200,
          body: { updatedBlockIds: [7], version: "v-2" },
        },
        [routeKey("GET", "/api/schedule")]: { status: 200, body: { ...REX_BLOCK_WEEK, version: "v-2" } },
      });
      renderEditor();

      openBlock(3, 15);
      const select = await showSelect();
      fireEvent.change(select, { target: { value: String(MORNING_DRIVE.id) } });
      await clickAssign();

      // Applies to the whole run by DEFAULT — the checkbox starts checked, nothing unchecked it —
      // and the request names exactly the clicked run's own stored block id, never a client-derived
      // list of ids (the server computes the run — see this file's own header comment).
      expect(lastRequestBody(mockFetch, "POST", "/api/schedule/assign-show")).toEqual({
        blockId: 7,
        showId: MORNING_DRIVE.id,
        applyToRun: true,
      });
    });

    it("the narrow checkbox limits assignment to the single clicked block", async () => {
      const mockFetch = makeRouteFetchMock({
        [routeKey("POST", "/api/schedule/assign-show")]: {
          status: 200,
          body: { updatedBlockIds: [7], version: "v-2" },
        },
        [routeKey("GET", "/api/schedule")]: { status: 200, body: { ...REX_BLOCK_WEEK, version: "v-2" } },
      });
      renderEditor();

      openBlock(3, 15);
      const select = await showSelect();
      fireEvent.change(select, { target: { value: String(MORNING_DRIVE.id) } });
      fireEvent.click(within(panel()).getByLabelText("Apply to the whole run"));
      await clickAssign();

      expect(lastRequestBody(mockFetch, "POST", "/api/schedule/assign-show")).toEqual({
        blockId: 7,
        showId: MORNING_DRIVE.id,
        applyToRun: false,
      });
    });

    it("the picker lists shows by name with a clear-none option", async () => {
      makeRouteFetchMock({});
      renderEditor();

      openBlock(3, 15);
      const select = await showSelect();

      const options = within(select).getAllByRole("option");
      expect(options.map((option) => option.textContent)).toEqual(["No show", MORNING_DRIVE.name, LATE_NIGHT.name]);
      // "No show" is the clear-none option — its value is the empty-string sentinel Assign reads as
      // a null showId; this scenario only asserts it's LISTED, not that Assign was clicked with it.
      expect(within(select).getByRole("option", { name: "No show" }).getAttribute("value")).toBe("");
    });
  });

  describe("Scenario: runs end honestly", () => {
    // "Stops at" here is proven from the CLIENT's own honest ignorance: the picker never inspects
    // neighboring blocks to decide anything — it always sends exactly the CLICKED block's own stored
    // id, so whatever the server decides is a run boundary (a music-only chain, a different persona)
    // is never something the client could accidentally widen or narrow by itself.

    it("span-assign stops at a music-only block (the pinned span rule) — the click still names only that block's own id", async () => {
      const mockFetch = makeRouteFetchMock({
        [routeKey("POST", "/api/schedule/assign-show")]: {
          status: 200,
          body: { updatedBlockIds: [22], version: "v-2" },
        },
        [routeKey("GET", "/api/schedule")]: { status: 200, body: { ...ADJACENT_BLOCKS_WEEK, version: "v-2" } },
      });
      renderEditor({ initialWeek: ADJACENT_BLOCKS_WEEK });

      // Half-hour 2 (60-90 min) is inside the music-only block (id 22), immediately after the Rex
      // block (id 21) that precedes it with no gap.
      openBlock(4, 2);
      const select = await showSelect();
      fireEvent.change(select, { target: { value: String(MORNING_DRIVE.id) } });
      await clickAssign();

      expect(lastRequestBody(mockFetch, "POST", "/api/schedule/assign-show")).toEqual({
        blockId: 22,
        showId: MORNING_DRIVE.id,
        applyToRun: true,
      });
    });

    it("span-assign stops at an other-persona block — the click still names only that block's own id", async () => {
      const mockFetch = makeRouteFetchMock({
        [routeKey("POST", "/api/schedule/assign-show")]: {
          status: 200,
          body: { updatedBlockIds: [23], version: "v-2" },
        },
        [routeKey("GET", "/api/schedule")]: { status: 200, body: { ...ADJACENT_BLOCKS_WEEK, version: "v-2" } },
      });
      renderEditor({ initialWeek: ADJACENT_BLOCKS_WEEK });

      // Half-hour 4 (120-150 min) is inside the Nova block (id 23), immediately after the music-only
      // block (id 22) that precedes it with no gap.
      openBlock(4, 4);
      const select = await showSelect();
      fireEvent.change(select, { target: { value: String(MORNING_DRIVE.id) } });
      await clickAssign();

      expect(lastRequestBody(mockFetch, "POST", "/api/schedule/assign-show")).toEqual({
        blockId: 23,
        showId: MORNING_DRIVE.id,
        applyToRun: true,
      });
    });
  });

  // -------------------------------------------------------------------------
  // T245 wire-contract decision (a): the picker acts on the SAVED grid only, and applies a
  // successful response via the documented follow-up GET.
  // -------------------------------------------------------------------------

  describe("Scenario: the picker only ever acts on the saved grid", () => {
    it("disables every control while there is an unsaved paint change, with a save-first affordance", async () => {
      makeRouteFetchMock({});
      renderEditor();

      // Dirty an UNRELATED cell (day 0, half-hour 5) so the Rex block itself is untouched — a brush
      // must be selected to paint, then deselected (clicking the active brush again) so the next
      // click on the Rex block INSPECTS it rather than repainting it.
      fireEvent.click(screen.getByRole("button", { name: "Radio Rex" }));
      fireEvent.pointerDown(cell(0, 5), { pointerId: 1 });
      fireEvent.pointerUp(cell(0, 5), { pointerId: 1 });
      fireEvent.click(screen.getByRole("button", { name: "Radio Rex" })); // deselect

      openBlock(3, 15);

      await waitFor(() => {
        expect(within(panel()).getByText(/Save your changes before assigning a show\./)).toBeInTheDocument();
        expect(within(panel()).getByLabelText("Assign show")).toBeDisabled();
        expect(within(panel()).getByLabelText("Apply to the whole run")).toBeDisabled();
        expect(within(panel()).getByRole("button", { name: "Assign" })).toBeDisabled();
      });
    });

    it("re-derives the grid's current show from a follow-up GET after a successful assignment", async () => {
      const assignedWeek: ScheduleWeekDto = {
        segments: [{ ...REX_SEGMENT, showId: MORNING_DRIVE.id }],
        version: "v-2",
      };
      makeRouteFetchMock({
        [routeKey("POST", "/api/schedule/assign-show")]: {
          status: 200,
          body: { updatedBlockIds: [7], version: "v-2" },
        },
        [routeKey("GET", "/api/schedule")]: { status: 200, body: assignedWeek },
      });
      renderEditor();

      openBlock(3, 15);
      const select = await showSelect();
      fireEvent.change(select, { target: { value: String(MORNING_DRIVE.id) } });
      await clickAssign();

      // The response itself carries no show name — only ids/version (AssignShowResponseDto) — so
      // this can only pass if the follow-up GET's fresh showId actually reached `overrides` and the
      // panel re-read it as `currentShowId`.
      await waitFor(() => {
        expect(within(panel()).getByText(`Current: ${MORNING_DRIVE.name}`)).toBeInTheDocument();
      });
    });

    it("carries the follow-up GET's fresh version into the next Save, avoiding a false 409", async () => {
      const assignedWeek: ScheduleWeekDto = {
        segments: [{ ...REX_SEGMENT, showId: MORNING_DRIVE.id }],
        version: "v-assigned",
      };
      const mockFetch = makeRouteFetchMock({
        [routeKey("POST", "/api/schedule/assign-show")]: {
          status: 200,
          body: { updatedBlockIds: [7], version: "v-assigned" },
        },
        [routeKey("GET", "/api/schedule")]: { status: 200, body: assignedWeek },
        [routeKey("PUT", "/api/schedule")]: { status: 200, body: assignedWeek },
      });
      renderEditor();

      openBlock(3, 15);
      const select = await showSelect();
      fireEvent.change(select, { target: { value: String(MORNING_DRIVE.id) } });
      await clickAssign();
      await waitFor(() => {
        expect(callCount(mockFetch, "GET", "/api/schedule")).toBe(1);
      });

      // Paint an unrelated cell so Save is enabled, then Save.
      fireEvent.click(screen.getByRole("button", { name: "Radio Rex" }));
      fireEvent.pointerDown(cell(0, 5), { pointerId: 1 });
      fireEvent.pointerUp(cell(0, 5), { pointerId: 1 });
      fireEvent.click(screen.getByRole("button", { name: "Save schedule" }));
      await waitFor(() => {
        expect(screen.getByRole("button", { name: "Save schedule" })).toBeInTheDocument();
      });

      expect(lastRequestBody(mockFetch, "PUT", "/api/schedule")).toMatchObject({ baseVersion: "v-assigned" });
    });
  });

  // -------------------------------------------------------------------------
  // STORY-313 P2 (review finding): a visual run can merge more than one STORED segment — narrowing
  // to "just this block" would only ever be able to name the leftmost of them.
  // -------------------------------------------------------------------------

  describe("Scenario: a visual run merging more than one stored row", () => {
    it("disables the narrow-to-one-block checkbox with a reason; run-wide assign still sends the leftmost id with applyToRun true", async () => {
      const mockFetch = makeRouteFetchMock({
        [routeKey("POST", "/api/schedule/assign-show")]: {
          status: 200,
          body: { updatedBlockIds: [31, 32], version: "v-2" },
        },
        [routeKey("GET", "/api/schedule")]: { status: 200, body: { ...TWO_ROW_RUN_WEEK, version: "v-2" } },
      });
      renderEditor({ initialWeek: TWO_ROW_RUN_WEEK });

      // Half-hour 1 (00:30) sits inside the merged 4-hour run — well past the leftmost stored row's
      // own end (id 31 covers only the first 2 hours).
      openBlock(5, 1);
      const select = await showSelect();
      expect(within(panel()).getByLabelText("Apply to the whole run")).toBeDisabled();
      expect(within(panel()).getByText(/merges 2 saved blocks/)).toBeInTheDocument();

      fireEvent.change(select, { target: { value: String(MORNING_DRIVE.id) } });
      await clickAssign();

      expect(lastRequestBody(mockFetch, "POST", "/api/schedule/assign-show")).toEqual({
        blockId: 31,
        showId: MORNING_DRIVE.id,
        applyToRun: true,
      });
    });
  });

  // -------------------------------------------------------------------------
  // T247 wire-smoke finding: Postgres confirmed the write path was correct (both stored rows of a
  // merged run carried the assigned show's id), but the panel's "Current:" line read "None" —
  // reproduced after both the assign-show follow-up GET AND a full page reload. Root cause:
  // `ScheduleEditor`'s mount-time `pruneOverrides` reconciliation misread two already-adjacent
  // STORED rows (loaded straight off `deriveGridFromWeek`, never painted together by an operator) as
  // a same-brush run MERGE (`schedule-grid-model`'s own case-5 rule) and dropped the override it had
  // just loaded — on the very first render, before any assign action ever ran. These two specs drive
  // the REAL `ScheduleEditor` + `deriveGridFromWeek` from an already-assigned `initialWeek`, asserting
  // the panel's own "Current:" line — never a prop-scripted `currentShowId` — for exactly the two run
  // shapes STORY-313's suspects named: a music-only run spanning more than one stored row, and a
  // persona run backed by a single row.
  // -------------------------------------------------------------------------

  describe("Scenario: the Current line for an already-assigned run, straight from load", () => {
    it("a music-only run merging two separately-stored rows with the same show id shows that show, not None", () => {
      makeRouteFetchMock({});
      renderEditor({ initialWeek: TWO_ROW_MUSIC_RUN_WEEK });

      // Half-hour 0 sits inside the leftmost stored row (id 41); the visual run merges it with the
      // second row (id 42) — both music-only, no gap between them.
      openBlock(0, 0);

      expect(within(panel()).getByText(`Current: ${LATE_NIGHT.name}`)).toBeInTheDocument();
    });

    it("a persona run backed by a single stored row shows its assigned show on the very first render", () => {
      const assignedWeek: ScheduleWeekDto = {
        segments: [{ ...REX_SEGMENT, showId: MORNING_DRIVE.id }],
        version: "v-1",
      };
      makeRouteFetchMock({});
      renderEditor({ initialWeek: assignedWeek });

      openBlock(3, 15);

      expect(within(panel()).getByText(`Current: ${MORNING_DRIVE.name}`)).toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  // STORY-313 P4 (review finding): clearing a show (submitting the "No show" option) must toast
  // "cleared", never "assigned" — the same success path used to say "assigned" regardless.
  // -------------------------------------------------------------------------

  describe("Scenario: clearing an assigned show", () => {
    it("toasts 'cleared', not 'assigned', when the picker submits the No-show option", async () => {
      const assignedWeek: ScheduleWeekDto = {
        segments: [{ ...REX_SEGMENT, showId: MORNING_DRIVE.id }],
        version: "v-1",
      };
      makeRouteFetchMock({
        [routeKey("POST", "/api/schedule/assign-show")]: {
          status: 200,
          body: { updatedBlockIds: [7], version: "v-2" },
        },
        [routeKey("GET", "/api/schedule")]: { status: 200, body: { ...assignedWeek, version: "v-2" } },
      });
      renderEditor({ initialWeek: assignedWeek });

      openBlock(3, 15);
      const select = await showSelect();
      expect(within(panel()).getByText(`Current: ${MORNING_DRIVE.name}`)).toBeInTheDocument();
      fireEvent.change(select, { target: { value: "" } });
      await clickAssign();

      await waitFor(() => {
        expect(screen.getByText("Show cleared from 1 block.")).toBeInTheDocument();
      });
    });
  });

  // -------------------------------------------------------------------------
  // STORY-313 P1 (review finding, BLOCKER): a failed `GET /api/shows` load used to leave Assign
  // ARMED — clicking it posted the block's EXISTING showId while the select still read "No show".
  // The roster now arrives as an already-settled `shows` prop (P6), so there is no load to fail
  // from this component's own perspective — only the page-supplied `"error"` state to render honestly.
  // -------------------------------------------------------------------------

  describe("Scenario: the shows list failed to load", () => {
    it("disables the select, the narrow checkbox, and Assign — a click can never reach the network", async () => {
      const mockFetch = makeRouteFetchMock({});
      renderEditor({ shows: ERROR_SHOWS });

      openBlock(3, 15);

      await waitFor(() => {
        expect(within(panel()).getByText(/Show list unavailable/)).toBeInTheDocument();
      });
      expect(within(panel()).getByLabelText("Assign show")).toBeDisabled();
      expect(within(panel()).getByLabelText("Apply to the whole run")).toBeDisabled();
      expect(within(panel()).getByRole("button", { name: "Assign" })).toBeDisabled();
      // No route is even registered for the assign POST — a stray click reaching the network would
      // throw "Unexpected fetch" inside the mock and fail this test loudly.
      expect(mockFetch).not.toHaveBeenCalled();
    });

    it("renders the Current line as terminal-honest, not a perpetual loading ellipsis, when a show IS already assigned", async () => {
      const assignedWeek: ScheduleWeekDto = {
        segments: [{ ...REX_SEGMENT, showId: MORNING_DRIVE.id }],
        version: "v-1",
      };
      makeRouteFetchMock({});
      renderEditor({ initialWeek: assignedWeek, shows: ERROR_SHOWS });

      openBlock(3, 15);

      await waitFor(() => {
        expect(within(panel()).getByText("Current: Unavailable")).toBeInTheDocument();
      });
    });
  });

  // -------------------------------------------------------------------------
  // STORY-313 P8 sad path: a rejected assignment (non-200) toasts the server's own message and
  // leaves local state untouched — `onAssigned` (and therefore the follow-up GET) only fires on 200.
  // -------------------------------------------------------------------------

  describe("Scenario: the assign POST is rejected", () => {
    it("toasts the server's error and leaves the block's current show unchanged", async () => {
      const mockFetch = makeRouteFetchMock({
        [routeKey("POST", "/api/schedule/assign-show")]: {
          status: 400,
          body: { detail: "Unknown show id 999." },
        },
      });
      renderEditor();

      openBlock(3, 15);
      const select = await showSelect();
      fireEvent.change(select, { target: { value: String(MORNING_DRIVE.id) } });
      await clickAssign();

      await waitFor(() => {
        expect(screen.getByText("Unknown show id 999.")).toBeInTheDocument();
      });
      // No route is registered for the follow-up GET — if the rejected POST wrongly triggered
      // `onAssigned` anyway, the mock would throw; this asserts it plainly never happened.
      expect(callCount(mockFetch, "GET", "/api/schedule")).toBe(0);
      expect(within(panel()).getByText("Current: None")).toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  // STORY-313 P3 (review finding): both follow-up-GET failure shapes — a non-ok response, and the
  // fetch itself throwing — must show the IDENTICAL honest recovery copy (the assignment already
  // landed server-side; only the local re-sync didn't, and reloading the page is the real fix).
  // -------------------------------------------------------------------------

  describe("Scenario: the follow-up GET fails after a successful assignment", () => {
    const RECOVERY_MESSAGE = "The assignment saved, but the grid couldn't refresh — reload the page.";

    it("a non-ok response shows the honest recovery copy", async () => {
      const mockFetch = makeRouteFetchMock({
        [routeKey("POST", "/api/schedule/assign-show")]: {
          status: 200,
          body: { updatedBlockIds: [7], version: "v-2" },
        },
        [routeKey("GET", "/api/schedule")]: { status: 500, body: {} },
      });
      renderEditor();

      openBlock(3, 15);
      const select = await showSelect();
      fireEvent.change(select, { target: { value: String(MORNING_DRIVE.id) } });
      await clickAssign();

      await waitFor(() => {
        expect(screen.getByText(RECOVERY_MESSAGE)).toBeInTheDocument();
      });
      expect(callCount(mockFetch, "GET", "/api/schedule")).toBe(1);
    });

    it("a thrown network error shows the SAME honest recovery copy, not a different message", async () => {
      makeRouteFetchMock({
        [routeKey("POST", "/api/schedule/assign-show")]: {
          status: 200,
          body: { updatedBlockIds: [7], version: "v-2" },
        },
        [routeKey("GET", "/api/schedule")]: { throws: true },
      });
      renderEditor();

      openBlock(3, 15);
      const select = await showSelect();
      fireEvent.change(select, { target: { value: String(MORNING_DRIVE.id) } });
      await clickAssign();

      await waitFor(() => {
        expect(screen.getByText(RECOVERY_MESSAGE)).toBeInTheDocument();
      });
    });
  });
});

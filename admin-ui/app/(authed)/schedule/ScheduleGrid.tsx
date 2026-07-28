"use client";

import { useEffect, useRef, type PointerEvent as ReactPointerEvent, type ReactNode } from "react";
import { cn } from "@/lib/utils";
import { MUSIC_HATCH_STYLE } from "./SchedulePalette";
import {
  cellErrorMatchesRun,
  DAY_COUNT,
  DAY_FULL_NAMES,
  DAY_LABELS,
  findRunAt,
  formatHalfHourLabel,
  formatRunTimeRange,
  HALF_HOURS_PER_DAY,
  personaSwatchClassName,
  type CellValue,
  type ScheduleRun,
} from "./schedule-grid-model";
import type { ScheduleCellErrorDto } from "./types";

export interface NowMarker {
  day: number;
  halfHour: number;
}

export interface ScheduleGridProps {
  cells: readonly (readonly CellValue[])[];
  runs: readonly ScheduleRun[];
  personaNames: ReadonlyMap<number, string>;
  hasBrushSelected: boolean;
  cellErrors: readonly ScheduleCellErrorDto[];
  nowMarker: NowMarker | null;
  /** Paints ONE cell with the currently-selected brush — the caller only ever calls this when a
   * brush IS selected (see `hasBrushSelected`); this component makes no assumption about WHAT gets
   * painted. */
  onPaintCell: (day: number, halfHour: number) => void;
  /** Opens the side panel for whatever block covers (day, halfHour) — a no-op upstream if the cell
   * is a gap. Fired for a plain click/keyboard activation while NO brush is selected. */
  onInspectCell: (day: number, halfHour: number) => void;
}

interface Stroke {
  pointerId: number;
  anchorDay: number;
  anchorHalfHour: number;
  /** The last cell the stroke actually painted — lets `handlePointerMove` skip re-painting the
   * SAME cell on every one of the many pointermove events a browser fires per pixel of travel. */
  lastDay: number;
  lastHalfHour: number;
  moved: boolean;
}

/** A cell button's day/half-hour, parsed back out of its own `data-day`/`data-half-hour`
 * attributes — the hit-test target {@link cellFromPoint} walks up to. */
interface CellCoordinates {
  day: number;
  halfHour: number;
}

/** The real hit-test a captured pointermove needs (see this module's own doc comment): pointer
 * capture retargets every pointer event to whatever holds capture, so `e.target`/`e.currentTarget`
 * during a captured drag is the GRID CONTAINER, never the cell underneath the finger/cursor —
 * `document.elementFromPoint` is the one API that still answers "what's physically at this
 * coordinate" regardless of capture. `closest` walks up from whatever's topmost there (e.g. the
 * label `<span>` skipped automatically — it's `pointer-events-none` — but walking up is a cheap,
 * honest safety net either way) to the cell `<button>` carrying both data attributes. */
function cellFromPoint(clientX: number, clientY: number): CellCoordinates | null {
  const target = document.elementFromPoint(clientX, clientY);
  const cellEl = target instanceof Element ? target.closest<HTMLElement>("[data-day][data-half-hour]") : null;
  if (cellEl === null) return null;
  const day = Number(cellEl.dataset.day);
  const halfHour = Number(cellEl.dataset.halfHour);
  if (!Number.isFinite(day) || !Number.isFinite(halfHour)) return null;
  return { day, halfHour };
}

/**
 * Every grid cell on the straight line between `from` and `to` (both inclusive), walked via
 * Bresenham's line algorithm over the two integer grid axes (`day`, `halfHour`) — NOT pixels.
 * Real pointermove events are sparse: a fast flick or a coarse automated drag (Playwright's
 * `dragTo`, verified live) can jump straight from one hit-tested cell to another several rows away
 * with no event in between, so painting only the two endpoints leaves a hole in the middle of the
 * stroke. The dominant case (a drag that stays in one day column) degenerates to a plain
 * inclusive range walk up/down `halfHour`; Bresenham handles a cross-day jump the same way,
 * without a separate code path for it.
 */
function cellsBetween(from: CellCoordinates, to: CellCoordinates): CellCoordinates[] {
  const points: CellCoordinates[] = [];
  let day = from.day;
  let halfHour = from.halfHour;
  const dayStep = from.day < to.day ? 1 : -1;
  const halfHourStep = from.halfHour < to.halfHour ? 1 : -1;
  const dayDistance = Math.abs(to.day - from.day);
  const halfHourDistance = -Math.abs(to.halfHour - from.halfHour);
  let error = dayDistance + halfHourDistance;
  for (;;) {
    points.push({ day, halfHour });
    if (day === to.day && halfHour === to.halfHour) break;
    const doubledError = 2 * error;
    if (doubledError >= halfHourDistance) {
      error += halfHourDistance;
      day += dayStep;
    }
    if (doubledError <= dayDistance) {
      error += dayDistance;
      halfHour += halfHourStep;
    }
  }
  return points;
}

// `min-h-0 h-full` defeats globals.css's base-layer `button { min-height: 2.5rem }` (the 40px
// touch-target floor, SPEC F28.13) — without it every cell button renders 40px tall inside its
// 14px grid row (a real-browser finding: rows measured 14px apart, but each button's own rect was
// 40px, so a cell's rendered box overlapped the row below/above it and `elementFromPoint` at a
// cell's own center hit the WRONG cell). `h-full` then explicitly stretches the button to fill its
// grid-row track (CSS Grid already defaults to stretch, but only once nothing else — like the
// defeated min-height — forces a taller intrinsic size).
const CELL_BASE =
  "relative h-full min-h-0 border-b border-line/40 focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-accent";

function cellLabelFor(
  day: number,
  halfHour: number,
  value: CellValue,
  personaNames: ReadonlyMap<number, string>
): string {
  const time = formatRunTimeRange(halfHour, halfHour + 1);
  const occupant = value === null ? "empty" : value === "music" ? "Music only" : personaNames.get(value) ?? "Unknown DJ";
  return `${DAY_FULL_NAMES[day]} ${time}, ${occupant}`;
}

/**
 * The 7×48 half-hour grid (STORY-248, SPEC F94.3) — plain pointer events, no drag library. Each
 * half-hour is its own focusable `<button>`; painting/inspecting is decided by
 * {@link ScheduleGridProps.hasBrushSelected}, not by anything this component tracks itself:
 *
 * - A brush selected: `onPointerDown` (on the pressed cell) and `onPointerMove` (on the GRID
 *   CONTAINER, hit-tested — see below) paint every cell the stroke touches. A plain click is a
 *   zero-length stroke — the single-cell "click-to-paint" baseline, SPEC F94.3's documented
 *   keyboard-equivalent minimum, and it happens to also cover simple mouse clicks for free.
 * - No brush selected: nothing paints. `onPointerUp` opens the panel for the ANCHOR cell if the
 *   stroke never moved to a different cell (a genuine click, not a drag) — a drag with no brush
 *   selected is a no-op both ways.
 * - Keyboard: the native `onClick` a `<button>` fires for Enter/Space calls the exact same
 *   paint-or-inspect branch a plain mouse click would (`activateCell`). A real mouse click ALSO
 *   fires this after the pointer handlers already ran — harmlessly idempotent (repainting a cell
 *   with the same value, or reopening the same panel), so there's no special-casing to skip it.
 *
 * ── Pointer capture lives on the GRID, not the cell (fixes a real Chrome bug) ────────────────────
 * Capturing on the pressed CELL was the original (broken) design: the Pointer Events spec
 * retargets EVERY subsequent event for that pointer — including `pointerover`/`pointerenter` —
 * to whatever holds capture, so once a cell captures the pointer, no OTHER cell ever sees an
 * enter/over event again for the rest of the stroke. A drag painted exactly one cell: the anchor,
 * in every real browser (verified against Chrome), touch included (touch gets the same implicit
 * capture behavior). The fix is to capture on the GRID CONTAINER instead (`gridRef`) — the one
 * element that should legitimately keep receiving every event for the stroke — and hit-test
 * manually on `pointermove` via {@link cellFromPoint} (`document.elementFromPoint`, which answers
 * "what's really at this screen coordinate" independent of capture retargeting) rather than
 * relying on the browser's own per-element enter/over events, which capture defeats by design.
 * There is no per-cell `pointerenter` handler anymore — it would never fire correctly once capture
 * is held elsewhere, so keeping it around would be a dead, misleading code path.
 *
 * Pointer capture (`setPointerCapture`/`releasePointerCapture`) is feature-detected before use —
 * jsdom (the spec harness) implements pointerdown/move/up dispatch but not the capture methods.
 * A window-level `pointerup`/`pointercancel` listener is a pure safety net so a stroke can never
 * get stuck "active" if release lands somewhere capture didn't retarget.
 */
export function ScheduleGrid({
  cells,
  runs,
  personaNames,
  hasBrushSelected,
  cellErrors,
  nowMarker,
  onPaintCell,
  onInspectCell,
}: ScheduleGridProps): ReactNode {
  const strokeRef = useRef<Stroke | null>(null);
  const gridRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    // A pure safety net: with capture correctly held by `gridRef` (below), pointerup/pointercancel
    // for this stroke's pointer bubble through the grid's own handlers regardless of where the
    // release physically lands — this window listener only matters if capture was never granted
    // at all (e.g. jsdom, which doesn't implement the capture methods), so a stroke released
    // outside the grid's DOM subtree can't get stuck "active" forever.
    function endStroke(e: PointerEvent): void {
      if (strokeRef.current !== null && strokeRef.current.pointerId === e.pointerId) {
        strokeRef.current = null;
      }
    }
    window.addEventListener("pointerup", endStroke);
    window.addEventListener("pointercancel", endStroke);
    return () => {
      window.removeEventListener("pointerup", endStroke);
      window.removeEventListener("pointercancel", endStroke);
    };
  }, []);

  function activateCell(day: number, halfHour: number): void {
    if (hasBrushSelected) {
      onPaintCell(day, halfHour);
    } else {
      onInspectCell(day, halfHour);
    }
  }

  /** A pointerdown always targets the exact cell pressed — no hit-test needed here, only on
   * `pointermove` (see this component's own doc comment for why). Capture goes on the GRID
   * container, never `e.currentTarget` (the cell) — that retargeting mistake is the bug this
   * component was rewritten to fix. */
  function handlePointerDown(e: ReactPointerEvent<HTMLButtonElement>, day: number, halfHour: number): void {
    strokeRef.current = {
      pointerId: e.pointerId,
      anchorDay: day,
      anchorHalfHour: halfHour,
      lastDay: day,
      lastHalfHour: halfHour,
      moved: false,
    };
    const grid = gridRef.current;
    if (grid !== null && typeof grid.setPointerCapture === "function") {
      try {
        grid.setPointerCapture(e.pointerId);
      } catch {
        // Some environments expose the method but reject an untracked pointer id — painting still
        // works via the pointermove hit-test below either way.
      }
    }
    if (hasBrushSelected) {
      onPaintCell(day, halfHour);
    }
  }

  /** Grid-container-level handler: with capture held by the grid, every pointermove for this
   * stroke's pointer arrives HERE regardless of what's physically under the pointer — hit-testing
   * via {@link cellFromPoint} is what tells us which cell that actually is. Skips re-painting the
   * same cell repeatedly (a pointermove fires many times per pixel of travel) but NEVER skips
   * cells BETWEEN two hits: consecutive pointermove events are not guaranteed adjacent — a fast
   * flick (the browser coalesces pointermove) or a coarse automated drag can jump several rows in
   * one event, verified live — so {@link cellsBetween} fills every cell from the last hit to this
   * one before painting, not just the new endpoint. */
  function handlePointerMove(e: ReactPointerEvent<HTMLDivElement>): void {
    const stroke = strokeRef.current;
    if (stroke === null || stroke.pointerId !== e.pointerId) return;
    const hit = cellFromPoint(e.clientX, e.clientY);
    if (hit === null || (hit.day === stroke.lastDay && hit.halfHour === stroke.lastHalfHour)) return;
    const gap = cellsBetween({ day: stroke.lastDay, halfHour: stroke.lastHalfHour }, hit);
    stroke.lastDay = hit.day;
    stroke.lastHalfHour = hit.halfHour;
    if (hit.day !== stroke.anchorDay || hit.halfHour !== stroke.anchorHalfHour) {
      stroke.moved = true;
    }
    if (hasBrushSelected) {
      // gap[0] is the PREVIOUS hit, already painted on the prior call (or on pointerdown, for the
      // anchor) — skip it so it isn't repainted a second time.
      for (const cell of gap.slice(1)) {
        onPaintCell(cell.day, cell.halfHour);
      }
    }
  }

  function releaseCapture(pointerId: number): void {
    const grid = gridRef.current;
    if (grid !== null && typeof grid.releasePointerCapture === "function") {
      try {
        grid.releasePointerCapture(pointerId);
      } catch {
        // Capture may never have been granted (guarded above) — nothing to release.
      }
    }
  }

  function handlePointerUp(e: ReactPointerEvent<HTMLDivElement>): void {
    releaseCapture(e.pointerId);
    const stroke = strokeRef.current;
    if (stroke === null || stroke.pointerId !== e.pointerId) return;
    if (!stroke.moved && !hasBrushSelected) {
      onInspectCell(stroke.anchorDay, stroke.anchorHalfHour);
    }
    strokeRef.current = null;
  }

  function handlePointerCancel(e: ReactPointerEvent<HTMLDivElement>): void {
    releaseCapture(e.pointerId);
    if (strokeRef.current !== null && strokeRef.current.pointerId === e.pointerId) {
      strokeRef.current = null;
    }
  }

  return (
    <div className="overflow-x-auto rounded-[6px] border border-line">
      {/* `role="group"`, not `role="grid"` — this is a CSS grid LAYOUT (SPEC F94.3's "7×48 CSS
          grid"), not an ARIA grid WIDGET (that pattern requires a row/gridcell descendant
          hierarchy this component doesn't build); each cell is its own properly-labeled `<button>`,
          which is what a screen reader actually needs here. `touch-none` (touch-action: none) on
          the grid: without it, the outer `overflow-x-auto` wrapper claims a touch drag as a page
          pan before this component's own pointer handling ever sees it — the stroke gets a
          `pointercancel` mid-drag, so a touch drag painted nothing past the anchor. */}
      <div
        ref={gridRef}
        role="group"
        aria-label="Weekly schedule"
        className="grid w-max touch-none grid-cols-[56px_repeat(7,minmax(96px,1fr))] grid-rows-[28px_repeat(48,14px)]"
        onPointerMove={handlePointerMove}
        onPointerUp={handlePointerUp}
        onPointerCancel={handlePointerCancel}
      >
        <div className="sticky left-0 z-10 border-b-2 border-line bg-surface-2" style={{ gridColumn: 1, gridRow: 1 }} />
        {DAY_LABELS.map((label, day) => (
          <div
            key={label}
            style={{ gridColumn: day + 2, gridRow: 1 }}
            className="flex items-center justify-center border-b-2 border-line bg-surface-2 text-[0.7rem] font-semibold uppercase tracking-[0.12em] text-accent-2"
          >
            {label}
          </div>
        ))}

        {Array.from({ length: HALF_HOURS_PER_DAY / 2 }, (_, hour) => hour * 2).map((halfHour) => (
          <div
            key={halfHour}
            style={{ gridColumn: 1, gridRow: halfHour + 2 }}
            className="sticky left-0 z-10 border-b border-line/40 bg-surface-2 pr-1.5 text-right text-[0.62rem] text-mute"
          >
            {formatHalfHourLabel(halfHour)}
          </div>
        ))}

        {Array.from({ length: DAY_COUNT }, (_, day) => day).flatMap((day) =>
          Array.from({ length: HALF_HOURS_PER_DAY }, (_, halfHour) => halfHour).map((halfHour) => {
            const value = cells[day]?.[halfHour] ?? null;
            const run = value === null ? null : findRunAt(runs, day, halfHour);
            const isLabelCell =
              run !== null && run.end - run.start >= 3 && halfHour === run.start + Math.floor((run.end - run.start) / 2);
            const matchedError = run === null ? undefined : cellErrors.find((error) => cellErrorMatchesRun(error, run));
            const isNow = nowMarker !== null && nowMarker.day === day && nowMarker.halfHour === halfHour;

            const label =
              value === null ? "" : value === "music" ? "Music" : personaNames.get(value) ?? "Unknown DJ";
            const swatchClassName =
              value === null
                ? "bg-surface hover:bg-surface-2"
                : value === "music"
                  ? "text-ink"
                  : `${personaSwatchClassName(value)} text-ink`;

            return (
              <button
                key={`${day}-${halfHour}`}
                type="button"
                data-testid={`schedule-cell-${day}-${halfHour}`}
                data-day={day}
                data-half-hour={halfHour}
                aria-label={cellLabelFor(day, halfHour, value, personaNames)}
                style={{
                  gridColumn: day + 2,
                  gridRow: halfHour + 2,
                  ...(value === "music" ? MUSIC_HATCH_STYLE : undefined),
                }}
                className={cn(
                  CELL_BASE,
                  swatchClassName,
                  matchedError !== undefined && "ring-2 ring-inset ring-danger",
                  isNow && "border-t-2 border-t-accent"
                )}
                title={matchedError?.message}
                onPointerDown={(e) => handlePointerDown(e, day, halfHour)}
                onClick={() => activateCell(day, halfHour)}
              >
                {isLabelCell && (
                  <span className="pointer-events-none absolute inset-x-0 top-1/2 block -translate-y-1/2 truncate px-1 text-[0.6rem] font-semibold leading-none">
                    {label}
                  </span>
                )}
              </button>
            );
          })
        )}
      </div>
    </div>
  );
}

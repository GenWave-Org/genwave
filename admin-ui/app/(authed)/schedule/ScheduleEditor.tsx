"use client";

import { useEffect, useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { useConfirm } from "@/components/ui/confirm-dialog";
import { toast } from "@/components/ui/toast";
import { readErrorMessage } from "@/lib/problem-details";
import { ScheduleEnvelopePanel } from "./ScheduleEnvelopePanel";
import { ScheduleGrid, type NowMarker } from "./ScheduleGrid";
import { SchedulePalette } from "./SchedulePalette";
import {
  computeRuns,
  deriveGridFromWeek,
  findRunAt,
  findRunByStart,
  HALF_HOURS_PER_DAY,
  paintedValueOf,
  personaNameMap,
  pruneOverrides,
  runKey,
  serializeWeek,
  withCellPainted,
  type BlockOverrides,
  type Brush,
  type CellValue,
} from "./schedule-grid-model";
import type { RosterPersonaDto, ScheduleCellErrorDto, ScheduleWeekDto } from "./types";

export interface ScheduleEditorProps {
  initialWeek: ScheduleWeekDto;
  personas: readonly RosterPersonaDto[];
}

interface SavedProblemBody {
  detail?: string;
  cellErrors?: ScheduleCellErrorDto[];
}

/** Today's day/half-hour, computed once at mount — a static "now" marker on the grid (SPEC F94.3:
 * "cheap and orienting", explicitly not worth a live 1s tick for an editor screen). Browser-local
 * time, the same default every clock formatter in this app already falls back to
 * (`lib/format-clock.ts`) absent a station-timezone concept anywhere in the admin UI. */
function computeNowMarker(): NowMarker {
  const now = new Date();
  const halfHour = Math.min(HALF_HOURS_PER_DAY - 1, Math.floor((now.getHours() * 60 + now.getMinutes()) / 30));
  return { day: now.getDay(), halfHour };
}

/**
 * The drag-paint editor's orchestrator (STORY-248, SPEC F94.3 — PLAN T129, "the or bust
 * deliverable"). Owns every piece of state the paint model needs; `ScheduleGrid`/
 * `SchedulePalette`/`ScheduleEnvelopePanel` are presentation over it.
 *
 * ── Paint vs. inspect ─────────────────────────────────────────────────────────────────────────
 * Exactly one thing decides whether the grid paints or opens the panel: whether a brush is
 * currently selected (`selectedBrush !== null`). With a brush selected, every pointer stroke (or a
 * single click, or a keyboard Enter/Space — `ScheduleGrid`'s own remarks) paints. With NO brush
 * selected — the default state on load, or after clicking the active brush again to deselect it —
 * a click/Enter on an occupied cell opens {@link ScheduleEnvelopePanel} instead, and paints
 * nothing. This is why clicking a freshly-loaded block opens its panel without any extra "select"
 * step: nothing is selected yet.
 *
 * ── Save is the only write ────────────────────────────────────────────────────────────────────
 * Every local mutation (paint, clear, delete-block, an override edit) only touches `cells`/
 * `overrides` in memory — nothing reaches the network until the operator clicks Save, which PUTs
 * the ENTIRE serialized week in one request (SPEC F94.3: no autosave, a schedule write flips the
 * live station). A 200 re-derives local state from the response body (ids refresh); a 400 leaves
 * `cells`/`overrides` completely untouched and instead populates `cellErrors` so the grid
 * highlights the offending blocks in place — the operator's unsaved edit is never dropped
 * (STORY-248 AC5). Save is disabled whenever there is nothing TO save (`!isDirty`), same as while
 * a save is already in flight (`isSaving`) — and a dirty, un-saved edit gets one more guard: a
 * `beforeunload` listener (mounted only while `isDirty`, torn down the instant it isn't) asks the
 * browser to confirm leaving, so a drag-painted week can't vanish on an accidental tab close.
 *
 * ── One cell mutation can paint several cells in a single React batch ───────────────────────────
 * A drag paints every cell the pointer crosses (`ScheduleGrid`'s pointermove hit-test) — several
 * `onPaintCell` calls can land inside ONE React batch (e.g. a fast drag, or a spec driving multiple
 * synthetic pointermoves back-to-back with no `await` between them). `commitCells` therefore takes
 * an UPDATER (`(prev) => next`), never a precomputed array: `setCells(update)` lets React apply
 * each queued update against the PREVIOUS update's result, so a batch of N paints keeps all N —
 * passing an already-computed `CellValue[][]` closed over the stale pre-batch `cells` would let
 * later calls in the same batch silently overwrite earlier ones (only the LAST paint would stick).
 * Reconciling `overrides` against the mutation's OWN result can't happen inside that same updater
 * (a second `setState` call from within another state updater is exactly the kind of impurity React
 * warns about) — so it runs in an effect keyed on `cells` instead, which by construction only ever
 * sees the fully-batched, post-commit value.
 */
export function ScheduleEditor({ initialWeek, personas }: ScheduleEditorProps): ReactNode {
  const initialGrid = deriveGridFromWeek(initialWeek);
  const [cells, setCells] = useState<CellValue[][]>(initialGrid.cells);
  const [overrides, setOverrides] = useState(initialGrid.overrides);
  const [selectedBrush, setSelectedBrush] = useState<Brush | null>(null);
  const [openBlockAnchor, setOpenBlockAnchor] = useState<{ day: number; start: number } | null>(null);
  const [cellErrors, setCellErrors] = useState<readonly ScheduleCellErrorDto[]>([]);
  const [isDirty, setIsDirty] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  const [nowMarker] = useState<NowMarker>(computeNowMarker);
  const confirm = useConfirm();

  const runs = computeRuns(cells);
  const personaNames = personaNameMap(personas);
  const openRun = openBlockAnchor === null ? null : findRunByStart(runs, openBlockAnchor.day, openBlockAnchor.start);

  // Reconciles `overrides` against whatever `cells` actually ended up as, every time it changes —
  // see this component's own doc comment for why this can't live inside `commitCells` itself. A
  // no-op on the initial render (the mounted `overrides` already matches the mounted `cells`, both
  // derived from the same `initialWeek` a moment ago) beyond one harmless, equal-by-value re-set.
  useEffect(() => {
    setOverrides((prev) => pruneOverrides(prev, computeRuns(cells)));
  }, [cells]);

  // A dirty, unsaved paint stroke asks before the tab closes — mounted only while there IS
  // something to lose, torn down the instant a save clears `isDirty` (or the editor unmounts).
  useEffect(() => {
    if (!isDirty) return;
    function handleBeforeUnload(e: BeforeUnloadEvent): void {
      e.preventDefault();
    }
    window.addEventListener("beforeunload", handleBeforeUnload);
    return () => {
      window.removeEventListener("beforeunload", handleBeforeUnload);
    };
  }, [isDirty]);

  /** The one path every cell-mutating action funnels through — see this component's own doc
   * comment for why `update` is an updater, not a precomputed grid. Also clears `cellErrors`: those
   * describe the LAST save attempt, and any further edit makes them stale (a highlighted block the
   * operator has since repainted, or moved past, shouldn't keep showing a prior rejection). */
  function commitCells(update: (prev: CellValue[][]) => CellValue[][]): void {
    setCells(update);
    setIsDirty(true);
    setCellErrors([]);
  }

  function handlePaintCell(day: number, halfHour: number): void {
    if (selectedBrush === null) return; // ScheduleGrid only calls this when a brush IS selected.
    const value = paintedValueOf(selectedBrush);
    commitCells((prev) => withCellPainted(prev, day, halfHour, value));
  }

  function handleInspectCell(day: number, halfHour: number): void {
    const run = findRunAt(runs, day, halfHour);
    if (run === null) return; // an empty cell has nothing to inspect.
    setOpenBlockAnchor({ day: run.day, start: run.start });
  }

  /** `end` is the open run's current end — threaded in from the caller's own `openRun` rather than
   * re-derived here, since the caller already has the exact run this edit targets. Needed to build
   * a {@link StoredOverride} entry (`schedule-grid-model`'s merge-detection anchor). */
  function handleChangeOverrides(
    day: number,
    start: number,
    end: number,
    brush: CellValue,
    patch: Partial<BlockOverrides>
  ): void {
    if (brush === null) return;
    const key = runKey(day, start, brush);
    setOverrides((prev) => {
      const next = new Map(prev);
      const existing = next.get(key)?.overrides ?? { genres: null, energyMin: null, energyMax: null };
      next.set(key, { end, overrides: { ...existing, ...patch } });
      return next;
    });
    setIsDirty(true);
  }

  async function handleDeleteBlock(day: number, start: number, end: number): Promise<void> {
    const ok = await confirm({
      title: "Delete block",
      consequence: "This clears the block from your local edit — nothing changes on air until you Save.",
      confirmLabel: "Delete",
      destructive: true,
    });
    if (!ok) return;

    commitCells((prev) =>
      prev.map((row, d) => (d === day ? row.map((value, h) => (h >= start && h < end ? null : value)) : row))
    );
    setOpenBlockAnchor(null);
  }

  async function handleSave(): Promise<void> {
    setIsSaving(true);
    const body = serializeWeek(cells, overrides);

    try {
      const resp = await fetch("/api/schedule", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });

      if (resp.status === 200) {
        const week = (await resp.json()) as ScheduleWeekDto;
        const derived = deriveGridFromWeek(week);
        setCells(derived.cells);
        setOverrides(derived.overrides);
        setCellErrors([]);
        setOpenBlockAnchor(null);
        setIsDirty(false);
        toast.success("Schedule saved.");
        return;
      }

      if (resp.status === 400) {
        const problem = (await resp.json().catch(() => ({}))) as SavedProblemBody;
        const errors = problem.cellErrors ?? [];
        setCellErrors(errors);
        toast.error(problem.detail ?? `${errors.length} segment(s) rejected — see the highlighted cells.`);
        // cells/overrides are deliberately left untouched — the rejected edit survives (AC5).
        return;
      }

      toast.error(await readErrorMessage(resp));
    } catch {
      toast.error("Network error — check your connection");
    } finally {
      setIsSaving(false);
    }
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <SchedulePalette personas={personas} selectedBrush={selectedBrush} onSelectBrush={setSelectedBrush} />
        <div className="flex items-center gap-3">
          {isDirty && (
            <span role="status" className="text-[0.78rem] text-mute">
              Unsaved changes
            </span>
          )}
          <Button
            type="button"
            onClick={() => {
              void handleSave();
            }}
            disabled={isSaving || !isDirty}
          >
            {isSaving ? "Saving…" : "Save schedule"}
          </Button>
        </div>
      </div>

      <div className="flex flex-col items-start gap-4 sm:flex-row">
        <div className="min-w-0 flex-1">
          <ScheduleGrid
            cells={cells}
            runs={runs}
            personaNames={personaNames}
            hasBrushSelected={selectedBrush !== null}
            cellErrors={cellErrors}
            nowMarker={nowMarker}
            onPaintCell={handlePaintCell}
            onInspectCell={handleInspectCell}
          />
        </div>

        {openRun !== null && (
          <ScheduleEnvelopePanel
            key={runKey(openRun.day, openRun.start, openRun.brush)}
            run={openRun}
            personaName={openRun.brush === "music" ? null : personaNames.get(openRun.brush) ?? null}
            overrides={overrides.get(runKey(openRun.day, openRun.start, openRun.brush))?.overrides ?? null}
            onChangeOverrides={(patch) =>
              handleChangeOverrides(openRun.day, openRun.start, openRun.end, openRun.brush, patch)
            }
            onDelete={() => {
              void handleDeleteBlock(openRun.day, openRun.start, openRun.end);
            }}
            onClose={() => setOpenBlockAnchor(null)}
          />
        )}
      </div>
    </div>
  );
}
